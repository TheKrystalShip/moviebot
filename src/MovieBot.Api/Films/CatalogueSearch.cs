using System.Collections.Concurrent;
using System.Globalization;
using System.Text.RegularExpressions;
using TheKrystalShip.MovieBot.Acquire;
using TheKrystalShip.MovieBot.Acquire.Tmdb;
using TheKrystalShip.MovieBot.Api.Library;
using TheKrystalShip.MovieBot.Api.People;
using TheKrystalShip.MovieBot.Api.Shows;
using TheKrystalShip.MovieBot.Core;

namespace TheKrystalShip.MovieBot.Api.Films;

/// <summary>Why a question of the catalogue went unanswered.</summary>
public enum Problem
{
    None,

    /// <summary>The film database cannot be asked and nothing is kept.</summary>
    Unavailable,

    /// <summary>The database has nothing under a name, an id or a word the question used.</summary>
    NotFound,

    /// <summary>The question asks for something that cannot be answered.</summary>
    Invalid,
}

/// <summary>An answer, or the reason there is none.</summary>
public sealed record Outcome<T>(T? Value, Problem Problem, string? Message) where T : class
{
    public static Outcome<T> Ok(T value) => new(value, Problem.None, null);

    public static Outcome<T> Failed(Problem problem, string message) => new(null, problem, message);

    /// <summary>The same failure, for the answer of another type that was waiting on this one.</summary>
    public Outcome<U> Carry<U>() where U : class => new(null, Problem, Message);
}

/// <summary>A film or a show a question found: its entry in the list that named it, and its card where the database gave one.</summary>
public sealed record TitleHit(ListedTitle Listed, TitleCard? Card);

/// <summary>One credit of a person and where it stands.</summary>
public sealed record CreditHit(FilmCredit Credit, TitleCard? Card, string Status);

public sealed record CreditsRequest(int? From, int? To, string Role, string? Kind, bool Appearances);

public sealed record CreditsAnswer(Filmography Person, DateOnly AsOf, int Total, List<CreditHit> Credits);

public sealed record DiscoverRequest
{
    public string? Kind { get; init; }
    public IReadOnlyList<string> Genres { get; init; } = [];

    /// <summary>Genres to leave out, besides the animation and family titles a genre search leaves out unless it asked for them.</summary>
    public IReadOnlyList<string> Exclude { get; init; } = [];

    /// <summary>Whether animation and family titles stay in a genre search that did not name them.</summary>
    public bool IncludeFamily { get; init; }

    public int? From { get; init; }
    public int? To { get; init; }
    public IReadOnlyList<string> With { get; init; } = [];
    public IReadOnlyList<string> About { get; init; } = [];
    public string? Language { get; init; }
    public string? Like { get; init; }
    public bool GoodOnly { get; init; } = true;
    public double? MinRating { get; init; }
    public int? MinVotes { get; init; }
    public string? Sort { get; init; }
    public int Limit { get; init; } = CatalogueSearch.ResultsListed;
}

public sealed record DiscoverAnswer(string Kind, DiscoverResolved Resolved, List<TitleHit> Hits);

public sealed record ListAnswer(string Kind, DateOnly From, DateOnly To, List<TitleHit> Hits);

/// <summary>
/// The film database searched: films and shows by name, people by name and everything they are
/// credited on, and a discovery by what a title is like, each joined with the library and with
/// whether the asking person has watched it.
///
/// Every question's answer is kept on disk by <see cref="AnswerKeeper"/> and served from the copy until
/// it is old: a search or a discovery for a day, a person's credits for a day, the lists of the week
/// for six hours. What names a title (its IMDb id, who made it and who stars in it) is one call per
/// title kept for a week, so a title that turns up in many answers is asked about once. A title's
/// poster is the one its page shows, served by <see cref="TitleGlances"/>.
/// </summary>
public sealed partial class CatalogueSearch(
    AnswerKeeper keeper,
    Catalogue catalogue,
    TitleGlances glances,
    FilmStore films,
    ShowStore shows,
    LibraryListing library,
    WatchRecord record,
    TimeProvider clock)
{
    /// <summary>The most titles a search lists.</summary>
    public const int SearchListed = 10;

    /// <summary>The most titles a discovery or a list of the week lists.</summary>
    public const int ResultsListed = 20;

    /// <summary>The most credits a person's list holds; the newest are kept when there are more.</summary>
    public const int CreditsListed = 300;

    /// <summary>The rating a title needs to count as well rated, and the votes that make the rating mean something.</summary>
    public const double GoodRating = 6.5;
    public const int GoodVotes = 200;

    /// <summary>The genres a title for children is filed under, by the database's names for them.</summary>
    private static readonly HashSet<string> ChildrensGenres = new(["Animation", "Family", "Kids"], StringComparer.OrdinalIgnoreCase);

    /// <summary>The votes a sort by rating needs behind a title, so one vote of ten is not the best film there is.</summary>
    public const int RatedVotes = 25;

    public const int RecentDays = 45;
    public const int UpcomingDays = 180;

    internal static readonly TimeSpan SearchKept = TimeSpan.FromDays(1);
    internal static readonly TimeSpan CreditsKept = TimeSpan.FromDays(1);
    internal static readonly TimeSpan DiscoverKept = TimeSpan.FromDays(1);
    internal static readonly TimeSpan ListKept = TimeSpan.FromHours(6);
    internal static readonly TimeSpan CardKept = TimeSpan.FromDays(7);
    internal static readonly TimeSpan VocabularyKept = TimeSpan.FromDays(30);

    /// <summary>How many titles are named at once.</summary>
    private const int CardsAtOnce = 8;

    /// <summary>How many more titles than a list holds are named, so a few that cannot be named do not shorten it.</summary>
    private const int NamedSlack = 2;

    private static readonly string[] UnreleasedStatuses = ["Rumored", "Planned", "Canceled"];

    // The people and kinds of the question are in words; the answer is in ids the database keeps.

    /// <summary>
    /// Films and shows by name or words from it, best first, at most <see cref="SearchListed"/>.
    /// <paramref name="year"/> keeps the titles released in it or the year either side.
    /// </summary>
    public async Task<Outcome<List<TitleHit>>> SearchAsync(string query, string? kind, int? year, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(query)) return Invalid<List<TitleHit>>("Say what to search for.");
        if (kind is not null && !Catalogue.IsKind(kind)) return Invalid<List<TitleHit>>("Kind is film or show.");

        var found = await SearchedAsync(query, kind, ct);
        if (found.Value is not { } listed) return found.Carry<List<TitleHit>>();

        var wanted = query.Trim();
        var candidates = listed
            .Where(t => year is null || (t.Released is { } day && Math.Abs(day.Year - year.Value) <= 1))
            .OrderBy(t => !string.Equals(t.Title, wanted, StringComparison.OrdinalIgnoreCase))
            .Take(SearchListed + NamedSlack)
            .ToList();

        var hits = await HitsAsync(candidates, SearchListed, ct);
        return Outcome<List<TitleHit>>.Ok(hits);
    }

    /// <summary>The people a name could mean, best match first.</summary>
    public async Task<Outcome<List<PersonMatch>>> PeopleAsync(string query, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(query)) return Invalid<List<PersonMatch>>("Say whose name to search for.");

        var wanted = query.Trim();
        var kept = await keeper.KeptAsync(AnswerKeeper.QueryName("people", wanted.ToLowerInvariant()),
            KeptAnswerJson.Default.KeptAnswerListPersonMatch, SearchKept,
            async (tmdb, token) => (await tmdb.SearchPeopleAsync(wanted, token)).ToList(), null, ct);
        return kept is null
            ? Unavailable<List<PersonMatch>>("The film database could not be asked about this name.")
            : Outcome<List<PersonMatch>>.Ok(kept.Value ?? []);
    }

    /// <summary>
    /// A person's credits, cast and crew, films and shows, oldest first, with the status of each as of
    /// today. <see cref="CreditsRequest.Role"/> is <c>acting</c>, <c>directing</c>, <c>writing</c> or
    /// <c>any</c>. A credit with no date is an announcement, and is listed whenever the years asked for
    /// reach this year or later.
    /// </summary>
    public async Task<Outcome<CreditsAnswer>> CreditsAsync(int personId, CreditsRequest request, CancellationToken ct)
    {
        if (request.Role is not ("acting" or "directing" or "writing" or "any"))
            return Invalid<CreditsAnswer>("Role is acting, directing, writing or any.");
        if (request.Kind is not null && !Catalogue.IsKind(request.Kind)) return Invalid<CreditsAnswer>("Kind is film or show.");
        if (request.From is { } from && request.To is { } to && from > to) return Invalid<CreditsAnswer>("From is after to.");

        var kept = await keeper.KeptAsync($"credits/{personId}", KeptAnswerJson.Default.KeptAnswerFilmography, CreditsKept,
            async (tmdb, token) =>
            {
                var person = await tmdb.FilmographyAsync(personId, token);
                if (person is not null) await catalogue.FetchPortraitAsync(tmdb, person.TmdbId, person.ProfilePath, token);
                return person;
            }, null, ct);
        if (kept is null) return Unavailable<CreditsAnswer>("The film database could not be asked about this person.");
        if (kept.Value is not { } person) return NotFound<CreditsAnswer>("The film database has nobody under that id.");

        var today = Today();
        var matching = person.Credits
            .Where(c => request.Kind is null || c.Kind == request.Kind)
            .Where(c => Does(c, request.Role, request.Appearances))
            .Where(c => InYears(c.Released, request.From, request.To, today))
            .OrderBy(c => c.Released is null)
            .ThenBy(c => c.Released)
            .ThenBy(c => c.Title, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var listed = matching.Count > CreditsListed ? matching[^CreditsListed..] : matching;

        var cards = await CardsAsync(listed.Select(c => (c.Kind, c.TmdbId)), ct);
        var credits = listed
            .Select(c =>
            {
                cards.TryGetValue((c.Kind, c.TmdbId), out var card);
                if (card?.ImdbId is { } imdb) glances.Offer(imdb, Glance(c.AsListed()));
                return new CreditHit(c, card, StatusOf(c.Released, card, today));
            })
            .ToList();
        return Outcome<CreditsAnswer>.Ok(new CreditsAnswer(person, today, matching.Count, credits));
    }

    /// <summary>
    /// Films or shows found by what they are like. Every filter is optional and they combine; each
    /// word that names a genre, a person or a keyword is matched to the database's and the match is
    /// reported. <see cref="DiscoverRequest.Like"/> starts from the titles the database counts as
    /// similar to one and narrows them by the other filters.
    /// </summary>
    public async Task<Outcome<DiscoverAnswer>> DiscoverAsync(DiscoverRequest request, CancellationToken ct)
    {
        var limit = Math.Clamp(request.Limit, 1, ResultsListed);
        if (request.Kind is not null && !Catalogue.IsKind(request.Kind)) return Invalid<DiscoverAnswer>("Kind is film or show.");
        if (request.Sort is not null && !DiscoverSorts.IsSort(request.Sort)) return Invalid<DiscoverAnswer>("Sort is popular, rating, newest or oldest.");
        if (request.From > request.To) return Invalid<DiscoverAnswer>("From is after to.");

        var kind = request.Kind;
        Seed? seed = null;
        if (!string.IsNullOrWhiteSpace(request.Like))
        {
            var found = await SeedAsync(request.Like, kind, ct);
            if (found.Value is not { } resolvedSeed) return found.Carry<DiscoverAnswer>();
            if (kind is not null && kind != resolvedSeed.Key.Kind)
                return Invalid<DiscoverAnswer>($"{resolvedSeed.Name} is a {resolvedSeed.Key.Kind}, not a {kind}.");
            seed = resolvedSeed;
            kind = resolvedSeed.Key.Kind;
        }
        kind ??= "film";
        if (kind == "show" && request.With.Count > 0)
            return Invalid<DiscoverAnswer>("The film database cannot find a show by the people in it; ask for the person's credits with kind=show.");

        var genres = new List<CatalogueGenre>();
        if (request.Genres.Count > 0)
        {
            var all = await catalogue.GenresAsync(kind, ct);
            if (all is null) return Unavailable<DiscoverAnswer>("The film database could not be asked about its genres.");
            foreach (var name in request.Genres.Where(g => !string.IsNullOrWhiteSpace(g)))
            {
                if (MatchGenre(all, name) is not { } genre)
                    return NotFound<DiscoverAnswer>($"The film database has no {kind} genre called \"{name.Trim()}\"; its genres are {string.Join(", ", all.Select(g => g.Name))}.");
                if (genres.All(g => g.Id != genre.Id)) genres.Add(genre);
            }
        }

        var excluded = new List<CatalogueGenre>();
        if (request.Exclude.Any(g => !string.IsNullOrWhiteSpace(g)) || (request.Genres.Count > 0 && !request.IncludeFamily))
        {
            var all = await catalogue.GenresAsync(kind, ct);
            if (all is null) return Unavailable<DiscoverAnswer>("The film database could not be asked about its genres.");
            foreach (var name in request.Exclude.Where(g => !string.IsNullOrWhiteSpace(g)))
            {
                if (MatchGenre(all, name) is not { } genre)
                    return NotFound<DiscoverAnswer>($"The film database has no {kind} genre called \"{name.Trim()}\"; its genres are {string.Join(", ", all.Select(g => g.Name))}.");
                if (genres.Any(g => g.Id == genre.Id))
                    return Invalid<DiscoverAnswer>($"{genre.Name} is both asked for and left out.");
                if (excluded.All(g => g.Id != genre.Id)) excluded.Add(genre);
            }

            // A genre search is about its genre: children's titles are left out unless they were asked for.
            if (!request.IncludeFamily && !genres.Any(g => ChildrensGenres.Contains(g.Name)))
                excluded.AddRange(all.Where(g => ChildrensGenres.Contains(g.Name) && excluded.All(e => e.Id != g.Id)));
        }

        var people = new List<PersonMatch>();
        foreach (var name in request.With.Where(w => !string.IsNullOrWhiteSpace(w)))
        {
            var matches = await PeopleAsync(name, ct);
            if (matches.Value is not { } found) return matches.Carry<DiscoverAnswer>();
            if (PickPerson(found, name) is not { } person)
                return NotFound<DiscoverAnswer>($"The film database has nobody called \"{name.Trim()}\".");
            if (people.All(p => p.TmdbId != person.TmdbId)) people.Add(person);
        }

        var keywords = new List<KeywordMatch>();
        foreach (var phrase in request.About.Where(a => !string.IsNullOrWhiteSpace(a)))
        {
            var matches = await KeywordsAsync(phrase, ct);
            if (matches.Value is not { } found) return matches.Carry<DiscoverAnswer>();
            if (PickKeyword(found, phrase) is not { } keyword)
                return NotFound<DiscoverAnswer>($"The film database has no keyword matching \"{phrase.Trim()}\".");
            if (keywords.All(k => k.Id != keyword.Id)) keywords.Add(keyword);
        }

        LanguageName? language = null;
        if (!string.IsNullOrWhiteSpace(request.Language))
        {
            var named = await LanguageAsync(request.Language, ct);
            if (named.Value is not { } resolvedLanguage) return named.Carry<DiscoverAnswer>();
            language = resolvedLanguage;
        }

        var sort = request.Sort ?? (seed is null ? DiscoverSorts.Popular : null);
        var minRating = request.MinRating ?? (request.GoodOnly ? GoodRating : null);
        var minVotes = request.MinVotes ?? (request.GoodOnly ? GoodVotes : null);
        if (sort == DiscoverSorts.Rating) minVotes ??= RatedVotes;

        var today = Today();
        var query = new DiscoverQuery
        {
            Kind = kind,
            GenreIds = [.. genres.Select(g => g.Id)],
            WithoutGenreIds = [.. excluded.Select(g => g.Id)],
            From = request.From is { } from ? new DateOnly(from, 1, 1) : null,
            To = request.To is { } to ? new DateOnly(to, 12, 31) : sort == DiscoverSorts.Newest ? today : null,
            PersonIds = [.. people.Select(p => p.TmdbId)],
            KeywordIds = [.. keywords.Select(k => k.Id)],
            Language = language?.Code,
            MinRating = minRating,
            MinVotes = minVotes,
            Sort = sort ?? DiscoverSorts.Popular,
        };

        Outcome<List<ListedTitle>> listed = seed is null
            ? await DiscoveredAsync(query, limit, ct)
            : await SimilarToAsync(seed.Key, query, sort, limit, ct);
        if (listed.Value is not { } titles) return listed.Carry<DiscoverAnswer>();

        var hits = await HitsAsync(titles, limit, ct);
        var resolved = new DiscoverResolved
        {
            Genres = [.. genres.Select(g => g.Name)],
            Excluded = [.. excluded.Select(g => g.Name)],
            People = [.. people.Select(p => new ResolvedPerson(p.TmdbId, p.Name, p.KnownFor))],
            Keywords = [.. keywords.Select(k => k.Name)],
            Language = language?.Name,
            Like = seed?.Name,
            MinRating = minRating,
            MinVotes = minVotes,
            Sort = sort ?? "similar",
        };
        return Outcome<DiscoverAnswer>.Ok(new DiscoverAnswer(kind, resolved, hits));
    }

    /// <summary>The films or shows released in the last <paramref name="days"/> days, the most looked at, newest first.</summary>
    public Task<Outcome<ListAnswer>> RecentAsync(string kind, int days, CancellationToken ct)
    {
        var today = Today();
        return WindowAsync("released", kind, today.AddDays(-days), today, newestFirst: true, ct);
    }

    /// <summary>The films or shows with a date in the next <paramref name="days"/> days, the most looked at, soonest first.</summary>
    public Task<Outcome<ListAnswer>> UpcomingAsync(string kind, int days, CancellationToken ct)
    {
        var today = Today();
        return WindowAsync("upcoming", kind, today.AddDays(1), today.AddDays(days), newestFirst: false, ct);
    }

    /// <summary>The library's newest films and shows, newest first, described from what is kept.</summary>
    public LibraryAdditionsView Added(string? kind, int limit, string? viewerId)
    {
        var titles = library.Titles();
        var shelf = LibraryShelf.Of(titles, record, viewerId);
        var additions = new List<LibraryAdditionView>();

        if (kind is null or "film")
            foreach (var film in titles.Where(t => t.Kind == TitleKind.Film && t.AddedAt is not null))
            {
                var facts = films.Held(film.Film?.ImdbId ?? "")?.Details;
                additions.Add(new LibraryAdditionView
                {
                    ImdbId = film.Film?.ImdbId,
                    Kind = "film",
                    Title = facts?.Title ?? film.Film?.Name ?? film.Title,
                    Year = facts?.Year ?? film.Film?.Year,
                    Genres = [.. facts?.Genres ?? []],
                    Rating = facts?.Rating,
                    Plot = OneLine(facts?.Overview),
                    PosterUrl = film.Film?.ImdbId is { } imdb && films.HasImage(imdb, FilmStore.Poster) ? FilmViews.ImageUrl(imdb, FilmStore.Poster) : null,
                    LibraryId = film.Id,
                    AddedAt = film.AddedAt!.Value,
                    Watched = shelf.Saw("film", film.Film?.ImdbId),
                });
            }

        if (kind is null or "show")
            foreach (var show in titles.Where(t => t.Kind == TitleKind.Show && t.AddedAt is not null)
                         .GroupBy(t => t.Film?.ImdbId ?? t.Film?.Name ?? t.Title))
            {
                var first = show.OrderBy(t => t.Episode?.Season).ThenBy(t => t.Episode?.Number).First();
                var imdb = first.Film?.ImdbId;
                var facts = imdb is null ? null : shows.Held(imdb)?.Details;
                additions.Add(new LibraryAdditionView
                {
                    ImdbId = imdb,
                    Kind = "show",
                    Title = facts?.Title ?? first.Film?.Name ?? first.Title,
                    Year = facts?.FirstYear,
                    Genres = [.. facts?.Genres ?? []],
                    Rating = facts?.Rating,
                    Plot = OneLine(facts?.Overview),
                    PosterUrl = imdb is not null && shows.HasImage(imdb, ShowStore.Poster) ? ShowViews.ImageUrl(imdb, ShowStore.Poster) : null,
                    LibraryId = first.Id,
                    AddedAt = show.Max(t => t.AddedAt)!.Value,
                });
            }

        return new LibraryAdditionsView { Titles = [.. additions.OrderByDescending(a => a.AddedAt).Take(Math.Clamp(limit, 1, 50))] };
    }

    // Views.

    /// <summary>What the library holds and the asking person watched; <paramref name="viewerId"/> is null for the service key.</summary>
    public LibraryShelf ShelfFor(string? viewerId) => LibraryShelf.Of(library.Titles(), record, viewerId);

    public TitleHitView View(TitleHit hit, LibraryShelf shelf)
    {
        var (listed, card) = (hit.Listed, hit.Card);
        var imdb = card?.ImdbId;
        var released = listed.Released ?? card?.Released;
        return new TitleHitView
        {
            ImdbId = imdb,
            TmdbId = listed.TmdbId,
            Kind = listed.Kind,
            Title = listed.Title,
            Year = released?.Year,
            ReleaseDate = released is { } day ? Iso(day) : null,
            Genres = [.. card?.Genres ?? []],
            Rating = listed.Rating,
            Votes = listed.Votes,
            Plot = OneLine(listed.Overview),
            MadeBy = [.. card?.MadeBy ?? []],
            Starring = [.. card?.Starring ?? []],
            OriginalLanguage = listed.OriginalLanguage ?? card?.OriginalLanguage,
            PosterUrl = imdb is null ? null : glances.PosterUrl(imdb, Glance(listed)),
            InLibrary = shelf.Holds(listed.Kind, imdb),
            LibraryId = shelf.LibraryIdOf(listed.Kind, imdb),
            Watched = shelf.Saw(listed.Kind, imdb),
        };
    }

    public CreditView View(CreditHit hit, LibraryShelf shelf)
    {
        var (credit, card) = (hit.Credit, hit.Card);
        var imdb = card?.ImdbId;
        return new CreditView
        {
            TmdbId = credit.TmdbId,
            ImdbId = imdb,
            Kind = credit.Kind,
            Title = credit.Title,
            Year = credit.Released?.Year,
            ReleaseDate = credit.Released is { } day ? Iso(day) : null,
            Status = hit.Status,
            Role = credit.Role,
            Rating = credit.Rating,
            Votes = credit.Votes,
            PosterUrl = imdb is null ? null : glances.PosterUrl(imdb, Glance(credit.AsListed())),
            InLibrary = shelf.Holds(credit.Kind, imdb),
            LibraryId = shelf.LibraryIdOf(credit.Kind, imdb),
            Watched = shelf.Saw(credit.Kind, imdb),
        };
    }

    public PersonMatchView View(PersonMatch person) => new()
    {
        PersonId = person.TmdbId,
        Name = person.Name,
        KnownFor = person.KnownFor,
        PortraitUrl = catalogue.PortraitUrlIfKept(person.TmdbId),
        KnownForTitles = [.. person.KnownForTitles],
    };

    public PersonCreditsView View(CreditsAnswer answer, LibraryShelf shelf) => new()
    {
        PersonId = answer.Person.TmdbId,
        Name = answer.Person.Name,
        KnownFor = answer.Person.KnownFor,
        PortraitUrl = catalogue.PortraitUrlIfKept(answer.Person.TmdbId),
        AsOf = Iso(answer.AsOf),
        Total = answer.Total,
        Credits = [.. answer.Credits.Select(c => View(c, shelf))],
    };

    // Questions put to the database.

    private async Task<Outcome<List<ListedTitle>>> SearchedAsync(string query, string? kind, CancellationToken ct)
    {
        var wanted = query.Trim();
        var kept = await keeper.KeptAsync(AnswerKeeper.QueryName("search", $"{kind}|{wanted.ToLowerInvariant()}"),
            KeptAnswerJson.Default.KeptAnswerListListedTitle, SearchKept,
            async (tmdb, token) => (await tmdb.SearchTitlesAsync(wanted, kind, 2, token)).ToList(), null, ct);
        return kept is null
            ? Unavailable<List<ListedTitle>>("The film database could not be asked about this name.")
            : Outcome<List<ListedTitle>>.Ok(kept.Value ?? []);
    }

    private async Task<Outcome<List<ListedTitle>>> DiscoveredAsync(DiscoverQuery query, int limit, CancellationToken ct)
    {
        var kept = await keeper.KeptAsync(AnswerKeeper.QueryName("discover", Canonical(query, limit)),
            KeptAnswerJson.Default.KeptAnswerListListedTitle, DiscoverKept,
            async (tmdb, token) => (await tmdb.DiscoverAsync(query, limit, token)).ToList(), null, ct);
        return kept is null
            ? Unavailable<List<ListedTitle>>("The film database could not be asked to discover.")
            : Outcome<List<ListedTitle>>.Ok(kept.Value ?? []);
    }

    /// <summary>The released and upcoming lists: the most looked at titles of a window of days.</summary>
    private async Task<Outcome<ListAnswer>> WindowAsync(string family, string kind, DateOnly from, DateOnly to, bool newestFirst, CancellationToken ct)
    {
        if (!Catalogue.IsKind(kind)) return Invalid<ListAnswer>("Kind is film or show.");

        var query = new DiscoverQuery { Kind = kind, From = from, To = to, Sort = DiscoverSorts.Popular };
        var kept = await keeper.KeptAsync(AnswerKeeper.QueryName(family, Canonical(query, ResultsListed)),
            KeptAnswerJson.Default.KeptAnswerListListedTitle, ListKept,
            async (tmdb, token) => (await tmdb.DiscoverAsync(query, ResultsListed, token)).ToList(), null, ct);
        if (kept is null) return Unavailable<ListAnswer>("The film database could not be asked what is on.");

        var ordered = (kept.Value ?? [])
            .OrderBy(t => t.Released is null);
        var dated = newestFirst
            ? ordered.ThenByDescending(t => t.Released)
            : ordered.ThenBy(t => t.Released);
        var hits = await HitsAsync(dated.ToList(), ResultsListed, ct);
        return Outcome<ListAnswer>.Ok(new ListAnswer(kind, from, to, hits));
    }

    /// <summary>
    /// The titles the database counts as similar to a seed, narrowed by the discovery's other filters.
    /// The database filters nothing of its own here, so the filters are read off each candidate, and a
    /// person or a keyword is looked for in what each candidate is tagged with and credited to.
    /// </summary>
    private async Task<Outcome<List<ListedTitle>>> SimilarToAsync(TitleKey seed, DiscoverQuery query, string? sort, int limit, CancellationToken ct)
    {
        var space = seed.Kind == "film" ? "movie" : "tv";
        var kept = await keeper.KeptAsync($"similar/{space}-{seed.TmdbId}", KeptAnswerJson.Default.KeptAnswerListListedTitle, DiscoverKept,
            async (tmdb, token) => (await tmdb.SimilarAsync(seed.Kind, seed.TmdbId, 2, token)).ToList(), null, ct);
        if (kept is null) return Unavailable<List<ListedTitle>>("The film database could not be asked what is similar.");

        var narrowed = (kept.Value ?? [])
            .Where(t => query.GenreIds.All(id => t.GenreIds.Contains(id)))
            .Where(t => query.WithoutGenreIds.All(id => !t.GenreIds.Contains(id)))
            .Where(t => query.From is null || t.Released >= query.From)
            .Where(t => query.To is null || t.Released <= query.To)
            .Where(t => query.Language is null || string.Equals(t.OriginalLanguage, query.Language, StringComparison.OrdinalIgnoreCase))
            .Where(t => query.MinRating is null || t.Rating >= query.MinRating)
            .Where(t => query.MinVotes is null || t.Votes >= query.MinVotes)
            .ToList();

        IEnumerable<ListedTitle> ordered = sort switch
        {
            DiscoverSorts.Popular => narrowed.OrderByDescending(t => t.Popularity),
            DiscoverSorts.Rating => WeightedRating.Order(narrowed),
            DiscoverSorts.Newest => narrowed.OrderBy(t => t.Released is null).ThenByDescending(t => t.Released),
            DiscoverSorts.Oldest => narrowed.OrderBy(t => t.Released is null).ThenBy(t => t.Released),
            _ => narrowed,
        };
        if (query.PersonIds.Count == 0 && query.KeywordIds.Count == 0) return Outcome<List<ListedTitle>>.Ok([.. ordered.Take(limit + NamedSlack)]);

        // A title is asked about only as far as the answer needs: eight at a time until there are enough.
        var matched = new List<ListedTitle>();
        foreach (var chunk in ordered.Chunk(CardsAtOnce))
        {
            var traits = new TitleTraits?[chunk.Length];
            await Parallel.ForEachAsync(Enumerable.Range(0, chunk.Length), new ParallelOptions { MaxDegreeOfParallelism = CardsAtOnce, CancellationToken = ct },
                async (i, token) =>
                {
                    var title = chunk[i];
                    var traitsKept = await keeper.KeptAsync($"traits/{(title.Kind == "film" ? "movie" : "tv")}-{title.TmdbId}",
                        KeptAnswerJson.Default.KeptAnswerTitleTraits, VocabularyKept,
                        (tmdb, t) => tmdb.TraitsAsync(title.Kind, title.TmdbId, t), null, token);
                    traits[i] = traitsKept?.Value;
                });
            for (var i = 0; i < chunk.Length; i++)
                if (traits[i] is { } t
                    && query.KeywordIds.All(t.Keywords.Contains)
                    && query.PersonIds.All(t.People.Contains))
                    matched.Add(chunk[i]);
            if (matched.Count >= limit + NamedSlack) break;
        }
        return Outcome<List<ListedTitle>>.Ok(matched);
    }

    private async Task<Outcome<List<KeywordMatch>>> KeywordsAsync(string phrase, CancellationToken ct)
    {
        var wanted = phrase.Trim();
        var kept = await keeper.KeptAsync(AnswerKeeper.QueryName("keywords", wanted.ToLowerInvariant()),
            KeptAnswerJson.Default.KeptAnswerListKeywordMatch, VocabularyKept,
            async (tmdb, token) => (await tmdb.SearchKeywordsAsync(wanted, token)).ToList(), null, ct);
        return kept is null
            ? Unavailable<List<KeywordMatch>>("The film database could not be asked about its keywords.")
            : Outcome<List<KeywordMatch>>.Ok(kept.Value ?? []);
    }

    /// <summary>A language by its code or its English name.</summary>
    private async Task<Outcome<LanguageName>> LanguageAsync(string text, CancellationToken ct)
    {
        var kept = await keeper.KeptAsync("languages", KeptAnswerJson.Default.KeptAnswerListLanguageName, VocabularyKept,
            async (tmdb, token) => (await tmdb.LanguagesAsync(token)).ToList() is { Count: > 0 } list ? list : null, null, ct);
        if (kept is null) return Unavailable<LanguageName>("The film database could not be asked about its languages.");

        var wanted = text.Trim();
        var language = (kept.Value ?? []).FirstOrDefault(l => string.Equals(l.Code, wanted, StringComparison.OrdinalIgnoreCase))
                       ?? (kept.Value ?? []).FirstOrDefault(l => string.Equals(l.Name, wanted, StringComparison.OrdinalIgnoreCase));
        return language is null
            ? NotFound<LanguageName>($"The film database has no language called \"{wanted}\".")
            : Outcome<LanguageName>.Ok(language);
    }

    private sealed record Seed(TitleKey Key, string Name);

    /// <summary>The title a discovery is like: an IMDb id, or a name with the year in brackets when it is needed.</summary>
    private async Task<Outcome<Seed>> SeedAsync(string like, string? kind, CancellationToken ct)
    {
        var text = like.Trim();
        if (ImdbId.ToTag(ImdbId.ToNumber(text)) is { } tag && text.StartsWith("tt", StringComparison.OrdinalIgnoreCase))
        {
            var found = await keeper.KeptAsync($"finds/{tag}", KeptAnswerJson.Default.KeptAnswerTitleKey, VocabularyKept,
                (tmdb, token) => tmdb.FindAsync(tag, token), null, ct);
            if (found is null) return Unavailable<Seed>("The film database could not be asked about this title.");
            if (found.Value is not { } key) return NotFound<Seed>($"The film database has no title under {tag}.");

            var cards = await CardsAsync([(key.Kind, key.TmdbId)], ct);
            var card = cards.GetValueOrDefault((key.Kind, key.TmdbId));
            return Outcome<Seed>.Ok(new Seed(key, Named(card?.Title ?? tag, card?.Released?.Year)));
        }

        int? year = null;
        if (BracketedYear().Match(text) is { Success: true } match)
        {
            text = match.Groups[1].Value;
            year = int.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture);
        }

        var searched = await SearchedAsync(text, kind, ct);
        if (searched.Value is not { } listed) return searched.Carry<Seed>();
        var title = listed
            .Where(t => year is null || t.Released?.Year == year)
            .OrderBy(t => !string.Equals(t.Title, text, StringComparison.OrdinalIgnoreCase))
            .FirstOrDefault();
        return title is null
            ? NotFound<Seed>($"The film database has no title called \"{like.Trim()}\".")
            : Outcome<Seed>.Ok(new Seed(new TitleKey { Kind = title.Kind, TmdbId = title.TmdbId }, Named(title.Title, title.Released?.Year)));
    }

    [GeneratedRegex(@"^(.*\S)\s*\((\d{4})\)$")]
    private static partial Regex BracketedYear();

    /// <summary>
    /// The card of each title asked for, a week kept, eight asked at once. A title whose card the
    /// database could not give is absent from the answer.
    /// </summary>
    private async Task<Dictionary<(string Kind, int Id), TitleCard?>> CardsAsync(IEnumerable<(string Kind, int Id)> keys, CancellationToken ct)
    {
        var cards = new ConcurrentDictionary<(string Kind, int Id), TitleCard?>();
        await Parallel.ForEachAsync(keys.Distinct(), new ParallelOptions { MaxDegreeOfParallelism = CardsAtOnce, CancellationToken = ct },
            async (key, token) =>
            {
                var kept = await keeper.KeptAsync($"cards/{(key.Kind == "film" ? "movie" : "tv")}-{key.Id}",
                    KeptAnswerJson.Default.KeptAnswerTitleCard, CardKept,
                    (tmdb, t) => tmdb.CardAsync(key.Kind, key.Id, t), null, token);
                cards[key] = kept?.Value;
            });
        return new Dictionary<(string Kind, int Id), TitleCard?>(cards);
    }

    /// <summary>The first <paramref name="limit"/> of the titles, each with its card.</summary>
    private async Task<List<TitleHit>> HitsAsync(IReadOnlyList<ListedTitle> titles, int limit, CancellationToken ct)
    {
        var named = titles.Take(limit + NamedSlack).ToList();
        var cards = await CardsAsync(named.Select(t => (t.Kind, t.TmdbId)), ct);
        var hits = new List<TitleHit>();
        foreach (var title in named)
        {
            cards.TryGetValue((title.Kind, title.TmdbId), out var card);
            if (card?.ImdbId is { } imdb) glances.Offer(imdb, Glance(title));
            hits.Add(new TitleHit(title, card));
        }
        return [.. hits.Take(limit)];
    }

    // Reading the question's words.

    /// <summary>A genre by its name: exactly, else the one genre a word of whose name it is (<c>action</c> for <c>Action &amp; Adventure</c>).</summary>
    internal static CatalogueGenre? MatchGenre(IReadOnlyList<CatalogueGenre> genres, string name)
    {
        var wanted = name.Trim();
        if (genres.FirstOrDefault(g => string.Equals(g.Name, wanted, StringComparison.OrdinalIgnoreCase)) is { } exact) return exact;
        var partial = genres
            .Where(g => g.Name.Split([' ', '&', ',', '-'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Contains(wanted, StringComparer.OrdinalIgnoreCase))
            .ToList();
        return partial.Count == 1 ? partial[0] : null;
    }

    /// <summary>The person a name means: of those called exactly that, the one the database counts most looked at; else its first match.</summary>
    internal static PersonMatch? PickPerson(IReadOnlyList<PersonMatch> matches, string name)
    {
        var wanted = name.Trim();
        return matches.Where(p => string.Equals(p.Name, wanted, StringComparison.OrdinalIgnoreCase))
                   .OrderByDescending(p => p.Popularity).FirstOrDefault()
               ?? matches.FirstOrDefault();
    }

    /// <summary>The keyword some words mean: the one named exactly that, else the database's best match.</summary>
    internal static KeywordMatch? PickKeyword(IReadOnlyList<KeywordMatch> matches, string phrase) =>
        matches.FirstOrDefault(k => string.Equals(k.Name, phrase.Trim(), StringComparison.OrdinalIgnoreCase))
        ?? matches.FirstOrDefault();

    /// <summary>Whether the credit is something the person did, by the role asked about.</summary>
    internal static bool Does(FilmCredit credit, string role, bool appearances)
    {
        if (credit.Appearance) return appearances && role is "acting" or "any";
        return role switch
        {
            "acting" => credit.Acting,
            "directing" => credit.Directing,
            "writing" => credit.Writing,
            _ => true,
        };
    }

    /// <summary>
    /// Whether a release falls in the years asked for. A title with no date is an announcement, and is
    /// in whenever the years reach this year or later, or end nowhere.
    /// </summary>
    internal static bool InYears(DateOnly? released, int? from, int? to, DateOnly today)
    {
        if (released is not { } day) return to is null || to >= today.Year;
        return (from is null || day.Year >= from) && (to is null || day.Year <= to);
    }

    /// <summary>
    /// Where a credit stands today: released once its date has come; upcoming with a date still ahead;
    /// announced with no date, or one the database files as only planned or rumoured.
    /// </summary>
    internal static string StatusOf(DateOnly? released, TitleCard? card, DateOnly today)
    {
        if (released is not { } day) return ReleaseStatuses.Announced;
        if (day <= today) return ReleaseStatuses.Released;
        return card?.Status is { } status && UnreleasedStatuses.Contains(status, StringComparer.OrdinalIgnoreCase)
            ? ReleaseStatuses.Announced
            : ReleaseStatuses.Upcoming;
    }

    /// <summary>The first sentence of a text, cut at 200 characters on a word.</summary>
    internal static string? OneLine(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var line = text.Trim().ReplaceLineEndings(" ");
        var end = line.IndexOfAny(['.', '!', '?']);
        while (end >= 0 && end + 1 < line.Length && line[end + 1] != ' ') end = line.IndexOfAny(['.', '!', '?'], end + 1);
        if (end >= 0 && end < 200) return line[..(end + 1)];
        if (line.Length <= 200) return line;
        var cut = line.LastIndexOf(' ', 199);
        return line[..(cut > 100 ? cut : 200)].TrimEnd(',', ';', ':', ' ') + "…";
    }

    private static string Named(string title, int? year) => year is { } y ? $"{title} ({y})" : title;

    private DateOnly Today() => DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);

    private static string Iso(DateOnly day) => day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static TitleGlance Glance(ListedTitle title) => new()
    {
        TmdbId = title.TmdbId,
        Kind = title.Kind,
        Rating = title.Rating,
        Votes = title.Votes,
        Overview = title.Overview,
        Released = title.Released,
        PosterPath = title.PosterPath,
    };

    /// <summary>One question as one string, which is what its answer is kept under.</summary>
    private static string Canonical(DiscoverQuery q, int limit) => string.Join('|',
        q.Kind, string.Join(',', q.GenreIds), string.Join(',', q.WithoutGenreIds), q.From?.ToString("O"), q.To?.ToString("O"), string.Join(',', q.PersonIds),
        string.Join(',', q.KeywordIds), q.Language, q.MinRating?.ToString(CultureInfo.InvariantCulture),
        q.MinVotes?.ToString(CultureInfo.InvariantCulture), q.Sort, limit);

    private static Outcome<T> Invalid<T>(string message) where T : class => Outcome<T>.Failed(Problem.Invalid, message);

    private static Outcome<T> NotFound<T>(string message) where T : class => Outcome<T>.Failed(Problem.NotFound, message);

    private static Outcome<T> Unavailable<T>(string message) where T : class => Outcome<T>.Failed(Problem.Unavailable, message);
}
