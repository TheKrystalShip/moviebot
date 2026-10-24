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

/// <summary>
/// A studio: its name as shown, the database's companies it files under, and whether one of them is
/// called exactly the words it was found by (<c>A24</c>, where <c>blumhouse</c> is only within names).
/// </summary>
public sealed record Studio(string Name, List<int> CompanyIds, bool Exact);

public sealed record DiscoverRequest
{
    /// <summary><c>film</c> (the default), <c>show</c>, or <c>all</c> for both.</summary>
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

    /// <summary>Studios by name, each every company the database files under the name; a title made by any of them is kept.</summary>
    public IReadOnlyList<string> Studios { get; init; } = [];

    public string? Language { get; init; }

    /// <summary>Genres a title is filed under at least one of, by name; in a kind that lacks some, the others.</summary>
    public IReadOnlyList<string> AnyGenres { get; init; } = [];

    /// <summary>Original languages a title was made in one of, each by code or English name.</summary>
    public IReadOnlyList<string> Languages { get; init; } = [];

    /// <summary>Decades a title came out in one of, each by its first year (<c>1990</c>).</summary>
    public IReadOnlyList<int> Decades { get; init; } = [];

    /// <summary>Lengths a film runs one of (<see cref="FilmLengths"/>); a length is a film's alone.</summary>
    public IReadOnlyList<string> Lengths { get; init; } = [];

    public string? Like { get; init; }
    public bool GoodOnly { get; init; } = true;
    public double? MinRating { get; init; }
    public int? MinVotes { get; init; }

    /// <summary>The shortest a film runs, or a show's episode, in minutes.</summary>
    public int? MinRuntime { get; init; }

    /// <summary>The longest a film runs, or a show's episode, in minutes.</summary>
    public int? MaxRuntime { get; init; }
    public string? Sort { get; init; }

    /// <summary>The most titles the discovery lists across all its pages; a page holds at most <see cref="CatalogueSearch.ResultsListed"/>.</summary>
    public int Limit { get; init; } = CatalogueSearch.MostListed;

    /// <summary>Which page of the discovery is wanted, counting from 1.</summary>
    public int Page { get; init; } = 1;

    /// <summary><c>anime</c> keeps the titles <see cref="TitleCategories"/> counts as anime: animated, and made in Japanese.</summary>
    public string? Category { get; init; }
}

/// <summary>A discovery's page of titles; <paramref name="Next"/> is the page that follows it, absent on the last.</summary>
public sealed record DiscoverAnswer(string Kind, DiscoverResolved Resolved, List<TitleHit> Hits, int? Next);

public sealed record ListAnswer(string Kind, DateOnly From, DateOnly To, List<TitleHit> Hits, int? Next);

/// <summary>A page of titles each with its card; <paramref name="Next"/> is the page that follows it, absent on the last.</summary>
public sealed record PagedHits(List<TitleHit> Hits, int? Next);

/// <summary>A page of one person's work: their name, the titles, and the part they had on each by kind and database id.</summary>
public sealed record PersonWork(string Name, PagedHits Page, IReadOnlyDictionary<(string Kind, int TmdbId), string?> Roles);

/// <summary>A page of the film database's titles before they are named; <paramref name="Next"/> is as in <see cref="PagedHits"/>.</summary>
internal sealed record TitlePage(List<ListedTitle> Titles, int? Next);

/// <summary>
/// Which page of a list is wanted: the pages are <paramref name="Size"/> titles each, the list holds
/// at most <paramref name="Limit"/> in all, and <paramref name="Page"/> counts from 1.
/// </summary>
public readonly record struct PageAsk(int Limit, int Size, int Page)
{
    /// <summary>The titles before this page.</summary>
    public int Skip => (Page - 1) * Size;

    /// <summary>The titles this page holds: a full page, or what is left of the limit.</summary>
    public int Take => Math.Clamp(Limit - Skip, 0, Size);

    /// <summary>The page that follows when the source has titles beyond this one and the limit and the cap on pages allow it.</summary>
    public int? Next(bool sourceHasMore) =>
        sourceHasMore && Page * Size < Limit && Page < CatalogueSearch.PagesListed ? Page + 1 : null;
}

/// <summary>The films of one series, the first released first.</summary>
public sealed record CollectionAnswer(string Name, List<TitleHit> Hits);

/// <summary>
/// The film database searched: films and shows by name, people by name and everything they are
/// credited on, and a discovery by what a title is like, each joined with the library and with
/// whether the asking person has watched it.
///
/// Every question's answer is kept by <see cref="AnswerKeeper"/> and served from the copy until
/// it is old: a search or a discovery for a day, a person's credits for a day, the lists of the week
/// for six hours. What names a title (its IMDb id, who made it and who stars in it) is one call per
/// title kept for a week, so a title that turns up in many answers is asked about once. A title's
/// poster is the one its page shows, served by <see cref="TitleGlances"/>.
///
/// A question ordered by rating is ranked by IMDb's ratings and votes where the IMDb import holds
/// them (<see cref="ImdbRanking"/>), over the titles the film database listed and, for a question of
/// kind, genre and years alone, the best rated titles IMDb itself files there that the film database
/// did not list.
/// </summary>
public sealed partial class CatalogueSearch(
    AnswerKeeper keeper,
    Catalogue catalogue,
    TitleGlances glances,
    FilmStore films,
    ShowStore shows,
    LibraryListing library,
    WatchRecord record,
    TitleCategories categories,
    TitleScores scores,
    ImdbRatings imdbRatings,
    WikidataSets wikidata,
    TimeProvider clock)
{
    /// <summary>The most titles a search lists.</summary>
    public const int SearchListed = 10;

    /// <summary>The most titles a page of a discovery or a list of the week holds.</summary>
    public const int ResultsListed = 20;

    /// <summary>The most titles a page of a shelf of the Discover page holds.</summary>
    public const int ShelfListed = 40;

    /// <summary>The most pages a discovery or a shelf serves, whatever the database holds beyond.</summary>
    public const int PagesListed = 25;

    /// <summary>The most titles a discovery lists in all.</summary>
    public const int MostListed = PagesListed * ResultsListed;

    /// <summary>The most titles a shelf lists in all.</summary>
    public const int MostOnShelf = PagesListed * ShelfListed;

    /// <summary>The most credits a person's list holds; the newest are kept when there are more.</summary>
    public const int CreditsListed = 300;

    /// <summary>The rating a title needs to count as well rated, and the votes that make the rating mean something.</summary>
    public const double GoodRating = 6.5;
    public const int GoodVotes = 200;

    /// <summary>The genres a title for children is filed under, by the database's names for them.</summary>
    private static readonly HashSet<string> ChildrensGenres = new(["Animation", "Family", "Kids"], StringComparer.OrdinalIgnoreCase);

    /// <summary>The votes a sort by rating needs behind a title, so one vote of ten is not the best film there is.</summary>
    public const int RatedVotes = 25;

    /// <summary>The votes a question of a studio's work needs behind a title, the studios shelf's own floor, and no floor on its rating.</summary>
    public const int StudioVotes = 50;

    public const int RecentDays = 45;
    public const int UpcomingDays = 180;

    internal static readonly TimeSpan SearchKept = TimeSpan.FromDays(1);
    internal static readonly TimeSpan CreditsKept = TimeSpan.FromDays(1);
    internal static readonly TimeSpan DiscoverKept = TimeSpan.FromDays(1);
    internal static readonly TimeSpan ListKept = TimeSpan.FromHours(6);
    internal static readonly TimeSpan CardKept = TimeSpan.FromDays(7);
    internal static readonly TimeSpan VocabularyKept = TimeSpan.FromDays(30);
    internal static readonly TimeSpan CollectionKept = TimeSpan.FromDays(7);

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
            AnswerJson.Default.ListPersonMatch, SearchKept,
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

        var kept = await FilmographyAsync(personId, ct);
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
    /// similar to one and narrows them by the other filters. The picks (<see cref="DiscoverRequest.AnyGenres"/>,
    /// <see cref="DiscoverRequest.Languages"/>, <see cref="DiscoverRequest.Decades"/>,
    /// <see cref="DiscoverRequest.Lengths"/>) are each answered as any of their values, and a discovery the
    /// database cannot take as one question is asked as several (<see cref="DiscoverNarrowing"/>) whose
    /// answers are paged as one list.
    /// </summary>
    public async Task<Outcome<DiscoverAnswer>> DiscoverAsync(DiscoverRequest request, CancellationToken ct)
    {
        var planned = await PlanAsync(request, ct);
        if (planned.Value is not { } plan) return planned.Carry<DiscoverAnswer>();

        Outcome<TitlePage> listed = plan.Seed is { } seed
            ? await SimilarToAsync(seed.Key, plan.Queries, plan.Sort, plan.Ask, ct)
            : await ListedAsync("discover", DiscoverKept, plan.Queries, plan.Ask, ct);
        if (listed.Value is not { } page) return listed.Carry<DiscoverAnswer>();

        var hits = RatedAtLeast(await HitsAsync(page.Titles, plan.Ask.Take, ct), plan.MinRating);
        return Outcome<DiscoverAnswer>.Ok(new DiscoverAnswer(plan.Kind, plan.Resolved, hits, page.Next));
    }

    /// <summary>
    /// A discovery resolved to the database's ids: its kind (<c>film</c>, <c>show</c> or <c>all</c>),
    /// the questions it is asked as (none when nothing can match it), or the title its titles are like.
    /// </summary>
    private sealed record DiscoverPlan(string Kind, Seed? Seed, List<DiscoverQuery> Queries, DiscoverResolved Resolved, PageAsk Ask, string? Sort, double? MinRating);

    /// <summary>Both kinds at once, which the database keeps apart and a discovery asks of each.</summary>
    public const string AllKinds = "all";

    private async Task<Outcome<DiscoverPlan>> PlanAsync(DiscoverRequest request, CancellationToken ct)
    {
        var limit = Math.Clamp(request.Limit, 1, MostListed);
        if (request.Page < 1 || request.Page > PagesListed) return Invalid<DiscoverPlan>($"Page is 1 to {PagesListed}.");
        var ask = new PageAsk(limit, Math.Min(limit, ResultsListed), request.Page);
        if (request.Kind is not null && request.Kind != AllKinds && !Catalogue.IsKind(request.Kind)) return Invalid<DiscoverPlan>("Kind is film, show or all.");
        if (request.Sort is not null && !DiscoverSorts.IsSort(request.Sort)) return Invalid<DiscoverPlan>("Sort is popular, rating, newest or oldest.");
        if (request.From > request.To) return Invalid<DiscoverPlan>("From is after to.");
        if (request.MinRuntime > request.MaxRuntime) return Invalid<DiscoverPlan>("The shortest runtime is longer than the longest.");
        var today = Today();
        if (request.Decades.FirstOrDefault(d => !Decades.IsDecade(d, today)) is var decade and not 0)
            return Invalid<DiscoverPlan>($"{decade} is no decade: a decade is named by its first year, from {Decades.Earliest} to this one.");
        if (request.Lengths.FirstOrDefault(l => FilmLengths.Of(l) is null) is { } length)
            return Invalid<DiscoverPlan>($"\"{length}\" is no length; a length is {string.Join(", ", FilmLengths.All)}.");
        if (request.Lengths.Count > 0 && request.Kind == "show") return Invalid<DiscoverPlan>("A length is a film's; a show is not picked by one.");
        if (!string.IsNullOrWhiteSpace(request.Like) && (request.MinRuntime is not null || request.MaxRuntime is not null || request.Lengths.Count > 0))
            return Invalid<DiscoverPlan>("A discovery like another title cannot be narrowed by runtime; the titles it starts from carry none.");
        if (request.Category is not null)
        {
            if (TitleCategories.Parse(request.Category) != TitleCategory.Anime)
                return Invalid<DiscoverPlan>("A discovery is narrowed to anime alone; leave category out for everything.");
            if ((string?[])[request.Language, .. request.Languages] is var named
                && named.Any(l => !string.IsNullOrWhiteSpace(l)
                    && !string.Equals(l.Trim(), TitleCategories.AnimeLanguage, StringComparison.OrdinalIgnoreCase)
                    && !string.Equals(l.Trim(), "japanese", StringComparison.OrdinalIgnoreCase)))
                return Invalid<DiscoverPlan>("Anime is made in Japanese; a discovery of anime takes no other language.");
            request = request with
            {
                Genres = [.. request.Genres, TitleCategories.AnimeGenre],
                Language = TitleCategories.AnimeLanguage,
                Languages = [],
            };
        }

        var kind = request.Kind;
        Seed? seed = null;
        if (!string.IsNullOrWhiteSpace(request.Like))
        {
            var found = await SeedAsync(request.Like, kind == AllKinds ? null : kind, ct);
            if (found.Value is not { } resolvedSeed) return found.Carry<DiscoverPlan>();
            if (kind is not null && kind != AllKinds && kind != resolvedSeed.Key.Kind)
                return Invalid<DiscoverPlan>($"{resolvedSeed.Name} is a {resolvedSeed.Key.Kind}, not a {kind}.");
            seed = resolvedSeed;
            kind = resolvedSeed.Key.Kind;
        }
        kind ??= "film";
        if (kind == "show" && request.With.Count > 0)
            return Invalid<DiscoverPlan>("The film database cannot find a show by the people in it; ask for the person's credits with kind=show.");

        // A show is found by neither the people in it nor its length, so both kinds asked with either are films.
        List<string> kinds = kind == AllKinds
            ? request.With.Count > 0 || request.Lengths.Count > 0 ? ["film"] : ["film", "show"]
            : [kind];

        var people = new List<PersonMatch>();
        foreach (var name in request.With.Where(w => !string.IsNullOrWhiteSpace(w)))
        {
            var matches = await PeopleAsync(name, ct);
            if (matches.Value is not { } found) return matches.Carry<DiscoverPlan>();
            if (PickPerson(found, name) is not { } person)
                return NotFound<DiscoverPlan>($"The film database has nobody called \"{name.Trim()}\".");
            if (people.All(p => p.TmdbId != person.TmdbId)) people.Add(person);
        }

        var keywords = new List<KeywordMatch>();
        foreach (var phrase in request.About.Where(a => !string.IsNullOrWhiteSpace(a)))
        {
            var matches = await KeywordsAsync(phrase, ct);
            if (matches.Value is not { } found) return matches.Carry<DiscoverPlan>();
            if (PickKeyword(found, phrase) is not { } keyword)
                return NotFound<DiscoverPlan>($"The film database has no keyword matching \"{phrase.Trim()}\".");
            if (keywords.All(k => k.Id != keyword.Id)) keywords.Add(keyword);
        }

        var studios = new List<Studio>();
        foreach (var name in request.Studios.Where(s => !string.IsNullOrWhiteSpace(s)))
        {
            var found = await StudioAsync(name, ct);
            if (found.Value is not { } studio) return found.Carry<DiscoverPlan>();
            studios.Add(studio);
        }
        if (seed is not null && studios.Count > 0)
            return Invalid<DiscoverPlan>("The film database finds what is like a title without asking who made it; leave the studio out or the title.");

        LanguageName? language = null;
        if (!string.IsNullOrWhiteSpace(request.Language))
        {
            var named = await LanguageAsync(request.Language, ct);
            if (named.Value is not { } resolvedLanguage) return named.Carry<DiscoverPlan>();
            language = resolvedLanguage;
        }
        var languages = new List<LanguageName>();
        foreach (var text in request.Languages.Where(l => !string.IsNullOrWhiteSpace(l)))
        {
            var named = await LanguageAsync(text, ct);
            if (named.Value is not { } picked) return named.Carry<DiscoverPlan>();
            if (languages.All(l => l.Code != picked.Code)) languages.Add(picked);
        }

        var sort = request.Sort ?? (seed is null ? DiscoverSorts.Popular : null);
        var minRating = request.MinRating ?? (request.GoodOnly ? GoodRating : null);
        var minVotes = request.MinVotes ?? (request.GoodOnly ? GoodVotes : null);
        if (sort == DiscoverSorts.Rating) minVotes ??= RatedVotes;

        var windows = DiscoverNarrowing.Windows(request.Decades);
        var runtimes = DiscoverNarrowing.Runtimes(request.Lengths);
        var queries = new List<DiscoverQuery>();
        var genreNames = new List<string>();
        var anyGenreNames = new List<string>();
        var excludedNames = new List<string>();
        string? missing = null;
        foreach (var asked in kinds)
        {
            var genres = await KindGenresAsync(asked, request, ct);
            if (genres.Value is not { } resolved)
            {
                if (genres.Problem != Problem.NotFound || kinds.Count == 1) return genres.Carry<DiscoverPlan>();
                missing ??= genres.Message;
                continue;
            }
            genreNames.AddRange(resolved.Genres.Select(g => g.Name).Where(n => !genreNames.Contains(n)));
            anyGenreNames.AddRange(resolved.AnyGenres.Select(g => g.Name).Where(n => !anyGenreNames.Contains(n)));
            excludedNames.AddRange(resolved.Excluded.Select(g => g.Name).Where(n => !excludedNames.Contains(n)));

            var query = new DiscoverQuery
            {
                Kind = asked,
                GenreIds = [.. resolved.Genres.Select(g => g.Id)],
                WithoutGenreIds = [.. resolved.Excluded.Select(g => g.Id)],
                From = request.From is { } from ? new DateOnly(from, 1, 1) : null,
                To = request.To is { } to ? new DateOnly(to, 12, 31) : sort == DiscoverSorts.Newest ? today : null,
                PersonIds = [.. people.Select(p => p.TmdbId)],
                KeywordIds = [.. keywords.Select(k => k.Id)],
                CompanyIds = [.. studios.SelectMany(s => s.CompanyIds).Distinct()],
                Language = language?.Code,
                MinRating = minRating,
                MinVotes = minVotes,
                MinRuntime = request.MinRuntime,
                MaxRuntime = request.MaxRuntime,
                Sort = sort ?? DiscoverSorts.Popular,
            };
            queries.AddRange(DiscoverNarrowing.Narrow(query, new Narrowing
            {
                AnyGenreIds = [.. resolved.AnyGenres.Select(g => g.Id)],
                Windows = windows,
                Runtimes = runtimes,
                Languages = [.. languages.Select(l => l.Code)],
            }));
        }
        if (missing is not null && genreNames.Count + anyGenreNames.Count == 0 && queries.Count == 0)
            return NotFound<DiscoverPlan>(missing);
        if (queries.Count > DiscoverNarrowing.MostQuestions)
            return Invalid<DiscoverPlan>($"These picks ask the film database {queries.Count} questions at once; at most {DiscoverNarrowing.MostQuestions} are asked. Pick fewer decades, lengths or genres.");

        var resolvedView = new DiscoverResolved
        {
            Genres = genreNames,
            AnyGenres = anyGenreNames,
            Excluded = excludedNames,
            People = [.. people.Select(p => new ResolvedPerson(p.TmdbId, p.Name, p.KnownFor))],
            Keywords = [.. keywords.Select(k => k.Name)],
            Studios = [.. studios.Select(s => s.Name)],
            Language = language?.Name ?? (languages.Count == 1 ? languages[0].Name : null),
            Languages = [.. languages.Select(l => l.Name)],
            Decades = [.. request.Decades.Distinct().OrderDescending()],
            Lengths = [.. FilmLengths.All.Where(request.Lengths.Contains)],
            Like = seed?.Name,
            MinRating = minRating,
            MinVotes = minVotes,
            Sort = sort ?? "similar",
        };
        return Outcome<DiscoverPlan>.Ok(new DiscoverPlan(kind, seed, queries, resolvedView, ask, sort, minRating));
    }

    /// <summary>A discovery's genres in one kind's terms: those every title is filed under, those any is, and those none is.</summary>
    private sealed record KindGenres(List<CatalogueGenre> Genres, List<CatalogueGenre> AnyGenres, List<CatalogueGenre> Excluded);

    /// <summary>
    /// The genres a discovery names, matched to one kind's. A genre the kind does not have is a
    /// refusal for <see cref="DiscoverRequest.Genres"/> and <see cref="DiscoverRequest.Exclude"/>; of the
    /// picks, it is left out while one of them is the kind's, and a refusal when none is.
    /// </summary>
    private async Task<Outcome<KindGenres>> KindGenresAsync(string kind, DiscoverRequest request, CancellationToken ct)
    {
        var named = request.Genres.Concat(request.AnyGenres).Concat(request.Exclude).Any(g => !string.IsNullOrWhiteSpace(g));
        if (!named) return Outcome<KindGenres>.Ok(new KindGenres([], [], []));
        var all = await catalogue.GenresAsync(kind, ct);
        if (all is null) return Unavailable<KindGenres>("The film database could not be asked about its genres.");
        string Refusal(string name) => $"The film database has no {kind} genre called \"{name.Trim()}\"; its genres are {string.Join(", ", all.Select(g => g.Name))}.";

        var genres = new List<CatalogueGenre>();
        foreach (var name in request.Genres.Where(g => !string.IsNullOrWhiteSpace(g)))
        {
            if (MatchGenre(all, name) is not { } genre) return NotFound<KindGenres>(Refusal(name));
            if (genres.All(g => g.Id != genre.Id)) genres.Add(genre);
        }

        var picks = request.AnyGenres.Where(g => !string.IsNullOrWhiteSpace(g)).ToList();
        var anyGenres = picks.Select(name => MatchGenre(all, name)).OfType<CatalogueGenre>().DistinctBy(g => g.Id).ToList();
        if (picks.Count > 0 && anyGenres.Count == 0) return NotFound<KindGenres>(Refusal(picks[0]));

        var excluded = new List<CatalogueGenre>();
        foreach (var name in request.Exclude.Where(g => !string.IsNullOrWhiteSpace(g)))
        {
            if (MatchGenre(all, name) is not { } genre) return NotFound<KindGenres>(Refusal(name));
            if (genres.Concat(anyGenres).Any(g => g.Id == genre.Id))
                return Invalid<KindGenres>($"{genre.Name} is both asked for and left out.");
            if (excluded.All(g => g.Id != genre.Id)) excluded.Add(genre);
        }

        // A genre search is about its genre: children's titles are left out unless they were asked for.
        if (genres.Count + anyGenres.Count > 0 && !request.IncludeFamily && !genres.Concat(anyGenres).Any(g => ChildrensGenres.Contains(g.Name)))
            excluded.AddRange(all.Where(g => ChildrensGenres.Contains(g.Name) && excluded.All(e => e.Id != g.Id)));
        return Outcome<KindGenres>.Ok(new KindGenres(genres, anyGenres, excluded));
    }

    /// <summary>
    /// A page of the titles a question put by the site rather than a person finds, each with its
    /// card: the question names the database's own ids, so nothing in it needs resolving.
    /// </summary>
    public Task<Outcome<PagedHits>> ShelfAsync(DiscoverQuery query, PageAsk ask, CancellationToken ct) => ShelfAsync([query], ask, ct);

    /// <summary>A page of a shelf's question narrowed by a filter's picks, asked as the questions <see cref="NarrowedAsync"/> makes of it.</summary>
    public async Task<Outcome<PagedHits>> ShelfAsync(IReadOnlyList<DiscoverQuery> queries, PageAsk ask, CancellationToken ct)
    {
        var listed = await ListedAsync("discover", DiscoverKept, queries, ask, ct);
        var named = await NamedPageAsync(listed, ask, ct);
        var floor = queries.Count == 0 ? null : queries.Max(q => q.MinRating);
        return named.Value is { } paged ? Outcome<PagedHits>.Ok(paged with { Hits = RatedAtLeast(paged.Hits, floor) }) : named;
    }

    /// <summary>
    /// The hits whose shown rating (<see cref="TitleScores.Serve"/>) is at least
    /// <paramref name="floor"/>. The database holds a question to a floor on its own rating, and a
    /// title it passes is left out when IMDb, whose rating is the one shown, rates it under the floor.
    /// </summary>
    private List<TitleHit> RatedAtLeast(List<TitleHit> hits, double? floor) =>
        floor is not { } least ? hits
            : [.. hits.Where(h => scores.Serve(h.Card?.ImdbId, h.Listed.TmdbId, h.Listed.Kind, h.Listed.Rating, h.Listed.Votes).Rating is not { } shown || shown >= least)];

    /// <summary>
    /// A page of one of the database's own lists, kept as the lists of the week are. With a
    /// <paramref name="sort"/>, the list's first <see cref="TmdbClient.RatingPoolSize"/> titles are put
    /// in that order (<see cref="OrderedAsync"/>) and kept as one answer every page is a slice of.
    /// </summary>
    public async Task<Outcome<PagedHits>> ChartAsync(TitleChart chart, PageAsk ask, string? sort, CancellationToken ct)
    {
        if (ask.Take <= 0) return Outcome<PagedHits>.Ok(new PagedHits([], null));
        if (sort is not null)
        {
            var pool = await keeper.KeptAsync($"charts/{chart}-{sort}", AnswerJson.Default.ListListedTitle, ListKept,
                async (tmdb, token) => (await OrderedAsync(await ChartPoolAsync(tmdb, chart, token), sort, token)).ToList(), null, ct);
            if (pool is null) return Unavailable<PagedHits>("The film database could not be asked what is on.");
            var ordered = pool.Value ?? [];
            return Outcome<PagedHits>.Ok(new PagedHits(await HitsAsync([.. ordered.Skip(ask.Skip).Take(ask.Take)], ask.Take, ct),
                ask.Next(ordered.Count > ask.Skip + ask.Size)));
        }
        var kept = await keeper.KeptAsync($"charts/{chart}-{ask.Size}-p{ask.Page}", AnswerJson.Default.ListedPage, ListKept,
            async (tmdb, token) => await tmdb.ChartPageAsync(chart, ask.Page, ask.Size, token), null, ct);
        if (kept is null) return Unavailable<PagedHits>("The film database could not be asked what is on.");
        var page = kept.Value ?? new ListedPage();
        return Outcome<PagedHits>.Ok(new PagedHits(await HitsAsync([.. page.Titles.Take(ask.Take)], ask.Take, ct), ask.Next(ask.Page < page.Pages)));
    }

    /// <summary>The first titles of one of the database's own lists, as many as a ranking reads.</summary>
    private static async Task<List<ListedTitle>> ChartPoolAsync(TmdbClient tmdb, TitleChart chart, CancellationToken ct)
    {
        var pool = new List<ListedTitle>();
        var seen = new HashSet<(string, int)>();
        for (var page = 1; pool.Count < TmdbClient.RatingPoolSize; page++)
        {
            var listed = await tmdb.ChartPageAsync(chart, page, ShelfListed, ct);
            pool.AddRange(listed.Titles.Where(t => seen.Add((t.Kind, t.TmdbId))));
            if (page >= listed.Pages || listed.Titles.Count == 0) break;
        }
        return [.. pool.Take(TmdbClient.RatingPoolSize)];
    }

    /// <summary>The films of one series, the first released first or in the order a <paramref name="sort"/> names, kept a week.</summary>
    public async Task<Outcome<CollectionAnswer>> CollectionAsync(int collectionId, CancellationToken ct, string? sort = null)
    {
        var kept = await keeper.KeptAsync($"collections/{collectionId}", AnswerJson.Default.TitleCollection, CollectionKept,
            (tmdb, token) => tmdb.CollectionAsync(collectionId, token), null, ct);
        if (kept is null) return Unavailable<CollectionAnswer>("The film database could not be asked about this series.");
        if (kept.Value is not { } collection) return NotFound<CollectionAnswer>("The film database has no series under that id.");
        var hits = await HitsAsync(await OrderedAsync(collection.Films, sort, ct), ShelfListed, ct);
        return Outcome<CollectionAnswer>.Ok(new CollectionAnswer(collection.Name, hits));
    }

    /// <summary>
    /// A page of the films a person directed that are out, the most voted on first or in the order a
    /// <paramref name="sort"/> names, read from the same kept credits as everything else they did.
    /// </summary>
    public async Task<Outcome<PagedHits>> DirectedAsync(int personId, PageAsk ask, CancellationToken ct, string? sort = null)
    {
        var work = await DirectedWorkAsync(personId, ask, ct, sort);
        return work.Value is { } found ? Outcome<PagedHits>.Ok(found.Page) : work.Carry<PagedHits>();
    }

    /// <summary>
    /// A page of the films a person directed that are out, as <see cref="DirectedAsync"/> reads them,
    /// with the person's name and <c>Director</c> as the part on each.
    /// </summary>
    public Task<Outcome<PersonWork>> DirectedWorkAsync(int personId, PageAsk ask, CancellationToken ct, string? sort = null) =>
        PersonWorkAsync(personId, ask, ct, sort, (c, today) => c.Kind == "film" && c.Directing && c.Released is { } day && day <= today, _ => "Director");

    /// <summary>
    /// A page of the acting work of a person that is out, the most voted on first or in the order a
    /// <paramref name="sort"/> names: films and shows, leaving out an appearance as oneself, a part
    /// only ever uncredited and a show they played in for fewer than <see cref="StarringEpisodes"/>
    /// episodes (a show whose episodes the database does not count is left out). Each title carries
    /// the part played.
    /// </summary>
    public Task<Outcome<PersonWork>> StarringAsync(int personId, PageAsk ask, CancellationToken ct, string? sort = null) =>
        PersonWorkAsync(personId, ask, ct, sort,
            (c, today) => c.Acting && !c.Appearance && !OnlyUncredited(c.Role) && c.Released is { } day && day <= today
                          && (c.Kind != "show" || c.Episodes >= StarringEpisodes),
            c => c.Role);

    /// <summary>How many episodes of a show a person plays in for it to count as their work.</summary>
    public const int StarringEpisodes = 3;

    /// <summary>Whether every part of a role is credited as uncredited.</summary>
    private static bool OnlyUncredited(string? role) =>
        !string.IsNullOrWhiteSpace(role)
        && role.Split(" · ", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .All(part => part.EndsWith("(uncredited)", StringComparison.OrdinalIgnoreCase));

    private async Task<Outcome<PersonWork>> PersonWorkAsync(int personId, PageAsk ask, CancellationToken ct, string? sort,
        Func<FilmCredit, DateOnly, bool> keep, Func<FilmCredit, string?> roleOf)
    {
        var kept = await FilmographyAsync(personId, ct);
        if (kept is null) return Unavailable<PersonWork>("The film database could not be asked about this person.");
        if (kept.Value is not { } person) return NotFound<PersonWork>("The film database has nobody under that id.");

        var today = Today();
        var credits = person.Credits.Where(c => keep(c, today)).OrderByDescending(c => c.Votes).ToList();
        var roles = credits.ToDictionary(c => (c.Kind, c.TmdbId), roleOf);
        var ordered = await OrderedAsync([.. credits.Select(c => c.AsListed())], sort, ct);
        var page = ordered.Skip(ask.Skip).Take(ask.Take).ToList();
        var hits = new PagedHits(await HitsAsync(page, ask.Take, ct), ask.Next(ordered.Count > ask.Skip + ask.Size));
        return Outcome<PersonWork>.Ok(new PersonWork(person.Name, hits, roles));
    }

    /// <summary>
    /// The films or shows released in the last <paramref name="days"/> days, the most looked at, newest
    /// first; with <paramref name="anime"/>, the anime among them; with a <paramref name="sort"/>, the
    /// window's titles in that order.
    /// </summary>
    public Task<Outcome<ListAnswer>> RecentAsync(string kind, int days, bool anime, CancellationToken ct, int page = 1, int size = ResultsListed, string? sort = null)
    {
        var today = Today();
        return WindowAsync("released", kind, today.AddDays(-days), today, newestFirst: true, anime, sort, new PageAsk(MostOnShelf, size, page), ct);
    }

    /// <summary>
    /// The films or shows with a date in the next <paramref name="days"/> days, the most looked at,
    /// soonest first; with <paramref name="anime"/>, the anime among them; with a <paramref name="sort"/>,
    /// the window's titles in that order.
    /// </summary>
    public Task<Outcome<ListAnswer>> UpcomingAsync(string kind, int days, bool anime, CancellationToken ct, int page = 1, int size = ResultsListed, string? sort = null)
    {
        var today = Today();
        return WindowAsync("upcoming", kind, today.AddDays(1), today.AddDays(days), newestFirst: false, anime, sort, new PageAsk(MostOnShelf, size, page), ct);
    }

    /// <summary>
    /// The library's newest films and shows, newest first, described from what is kept; with
    /// <paramref name="category"/>, those of that category alone.
    /// </summary>
    public LibraryAdditionsView Added(string? kind, TitleCategory? category, int limit, string? viewerId)
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
                    Rating = scores.Serve(film.Film?.ImdbId, facts?.TmdbId, "film", facts?.Rating, facts?.Votes ?? 0).Rating,
                    Plot = OneLine(facts?.Overview),
                    PosterUrl = film.Film?.ImdbId is { } imdb && films.HasImage(imdb, FilmStore.Poster) ? FilmViews.ImageUrl(imdb, FilmStore.Poster) : null,
                    LibraryId = film.Id,
                    AddedAt = film.AddedAt!.Value,
                    Watched = shelf.Saw("film", film.Film?.ImdbId),
                    Category = categories.Of("film", film.Film?.ImdbId),
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
                    Rating = scores.Serve(imdb, facts?.TmdbId, "show", facts?.Rating, facts?.Votes ?? 0).Rating,
                    Plot = OneLine(facts?.Overview),
                    PosterUrl = imdb is not null && shows.HasImage(imdb, ShowStore.Poster) ? ShowViews.ImageUrl(imdb, ShowStore.Poster) : null,
                    LibraryId = first.Id,
                    AddedAt = show.Max(t => t.AddedAt)!.Value,
                    Category = categories.Of("show", imdb),
                });
            }

        // A title nothing describes yet is shelved with its kind, so it counts as general here too.
        return new LibraryAdditionsView
        {
            Titles = [.. additions
                .Where(a => category is null || (a.Category ?? TitleCategory.General) == category)
                .OrderByDescending(a => a.AddedAt).Take(Math.Clamp(limit, 1, 50))],
        };
    }

    // Views.

    /// <summary>What the library holds and the asking person watched; <paramref name="viewerId"/> is null for the service key.</summary>
    public LibraryShelf ShelfFor(string? viewerId) => LibraryShelf.Of(library.Titles(), record, viewerId);

    public TitleHitView View(TitleHit hit, LibraryShelf shelf)
    {
        var (listed, card) = (hit.Listed, hit.Card);
        var imdb = card?.ImdbId;
        var released = listed.Released ?? card?.Released;
        var (rating, votes, ratedBy) = scores.Serve(imdb, listed.TmdbId, listed.Kind, listed.Rating, listed.Votes);
        return new TitleHitView
        {
            ImdbId = imdb,
            TmdbId = listed.TmdbId,
            Kind = listed.Kind,
            Title = listed.Title,
            Year = released?.Year,
            ReleaseDate = released is { } day ? Iso(day) : null,
            Genres = [.. card?.Genres ?? []],
            Rating = rating,
            Votes = votes,
            RatedBy = ratedBy,
            Plot = OneLine(listed.Overview),
            MadeBy = [.. card?.MadeBy ?? []],
            Starring = [.. card?.Starring ?? []],
            OriginalLanguage = listed.OriginalLanguage ?? card?.OriginalLanguage,
            PosterUrl = imdb is null ? null : glances.PosterUrl(imdb, Glance(listed)),
            InLibrary = shelf.Holds(listed.Kind, imdb),
            LibraryId = shelf.LibraryIdOf(listed.Kind, imdb),
            Watched = shelf.Saw(listed.Kind, imdb),
            Category = categories.Of(listed.Kind, imdb, card?.Genres, listed.OriginalLanguage ?? card?.OriginalLanguage),
        };
    }

    public CreditView View(CreditHit hit, LibraryShelf shelf)
    {
        var (credit, card) = (hit.Credit, hit.Card);
        var imdb = card?.ImdbId;
        var (rating, votes, ratedBy) = scores.Serve(imdb, credit.TmdbId, credit.Kind, credit.Rating, credit.Votes);
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
            Rating = rating,
            Votes = votes,
            RatedBy = ratedBy,
            PosterUrl = imdb is null ? null : glances.PosterUrl(imdb, Glance(credit.AsListed())),
            InLibrary = shelf.Holds(credit.Kind, imdb),
            LibraryId = shelf.LibraryIdOf(credit.Kind, imdb),
            Watched = shelf.Saw(credit.Kind, imdb),
            Category = categories.Of(credit.Kind, imdb),
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
            AnswerJson.Default.ListListedTitle, SearchKept,
            async (tmdb, token) => (await tmdb.SearchTitlesAsync(wanted, kind, 2, token)).ToList(), null, ct);
        return kept is null
            ? Unavailable<List<ListedTitle>>("The film database could not be asked about this name.")
            : Outcome<List<ListedTitle>>.Ok(kept.Value ?? []);
    }

    /// <summary>
    /// A page of a discovery, each page a distinct answer kept under its own name. A sort by rating
    /// ranks the most voted titles of the whole discovery, so that ranked list is the one answer kept
    /// and every page is a slice of it: page two continues page one's order whenever it is asked.
    /// </summary>
    private async Task<Outcome<TitlePage>> DiscoveredAsync(string family, TimeSpan keep, DiscoverQuery query, PageAsk ask, CancellationToken ct)
    {
        if (ask.Take <= 0) return Outcome<TitlePage>.Ok(new TitlePage([], null));

        if (query.Sort == DiscoverSorts.Rating)
        {
            if (await RatingPoolAsync(family, keep, query, ct) is not { } pool) return Unavailable<TitlePage>("The film database could not be asked to discover.");
            IReadOnlyList<ListedTitle> ranked = pool;
            if (imdbRatings.Edition is { } edition)
            {
                var byImdb = await keeper.KeptAsync(
                    AnswerKeeper.QueryName(family, Canonical(query, TmdbClient.RatingPoolSize, 0) + "|imdb|" + edition + "|" + ImdbRanking.Breadth),
                    AnswerJson.Default.ListListedTitle, keep,
                    async (_, token) => await RankedByImdbAsync(query, ranked, token), null, ct);
                if (byImdb?.Value is { Count: > 0 } better) ranked = better;
            }
            return Outcome<TitlePage>.Ok(new TitlePage([.. ranked.Skip(ask.Skip).Take(ask.Take)], ask.Next(ranked.Count > ask.Skip + ask.Size)));
        }

        var kept = await keeper.KeptAsync(AnswerKeeper.QueryName(family, Canonical(query, ask.Size, ask.Page)),
            AnswerJson.Default.ListedPage, keep,
            async (tmdb, token) => await tmdb.DiscoverPageAsync(query, ask.Page, ask.Size, token), null, ct);
        if (kept is null) return Unavailable<TitlePage>("The film database could not be asked to discover.");
        var page = kept.Value ?? new ListedPage();
        return Outcome<TitlePage>.Ok(new TitlePage([.. page.Titles.Take(ask.Take)], ask.Next(ask.Page < page.Pages)));
    }

    /// <summary>The most voted titles of a discovery, the pool a sort by rating ranks; null when the database cannot be asked.</summary>
    private async Task<List<ListedTitle>?> RatingPoolAsync(string family, TimeSpan keep, DiscoverQuery query, CancellationToken ct)
    {
        var pool = await keeper.KeptAsync(AnswerKeeper.QueryName(family, Canonical(query, TmdbClient.RatingPoolSize, 0)),
            AnswerJson.Default.ListListedTitle, keep,
            async (tmdb, token) => (await tmdb.DiscoverAsync(query, TmdbClient.RatingPoolSize, token)).ToList(), null, ct);
        return pool is null ? null : pool.Value ?? [];
    }

    /// <summary>A page of a discovery asked as <paramref name="queries"/>: nothing for none, the one question's own page, or the union's (<see cref="MergedAsync"/>).</summary>
    private async Task<Outcome<TitlePage>> ListedAsync(string family, TimeSpan keep, IReadOnlyList<DiscoverQuery> queries, PageAsk ask, CancellationToken ct) => queries.Count switch
    {
        0 => Outcome<TitlePage>.Ok(new TitlePage([], null)),
        1 => await DiscoveredAsync(family, keep, queries[0], ask, ct),
        _ => await MergedAsync(family, keep, queries, ask, ct),
    };

    /// <summary>
    /// A page of the union of several questions in their one sort, each title once. The first
    /// page-times-size titles of the union are among the first page-times-size of each question, so
    /// each is read to the page asked and no further, every page of it kept as its own answer; a sort
    /// by rating ranks the union of their pools of the most voted.
    /// </summary>
    private async Task<Outcome<TitlePage>> MergedAsync(string family, TimeSpan keep, IReadOnlyList<DiscoverQuery> queries, PageAsk ask, CancellationToken ct)
    {
        if (ask.Take <= 0) return Outcome<TitlePage>.Ok(new TitlePage([], null));
        var sort = queries[0].Sort;
        if (sort == DiscoverSorts.Rating)
        {
            var pools = await Task.WhenAll(queries.Select(q => RatingPoolAsync(family, keep, q, ct)));
            if (pools.Any(p => p is null)) return Unavailable<TitlePage>("The film database could not be asked to discover.");
            var ranked = await RatedAsync([.. pools.SelectMany(p => p!).DistinctBy(t => (t.Kind, t.TmdbId))], ct);
            return Outcome<TitlePage>.Ok(new TitlePage([.. ranked.Skip(ask.Skip).Take(ask.Take)], ask.Next(ranked.Count > ask.Skip + ask.Size)));
        }

        var read = await Task.WhenAll(queries.Select(q => ReadToAsync(family, keep, q, ask, ct)));
        if (read.Any(r => r is null)) return Unavailable<TitlePage>("The film database could not be asked to discover.");
        var merged = await OrderedAsync([.. read.SelectMany(r => r!.Value.Titles).DistinctBy(t => (t.Kind, t.TmdbId))], sort, ct);
        var more = merged.Count > ask.Skip + ask.Size || read.Any(r => r!.Value.More);
        return Outcome<TitlePage>.Ok(new TitlePage([.. merged.Skip(ask.Skip).Take(ask.Take)], ask.Next(more)));
    }

    /// <summary>A question's pages from the first to the one asked, and whether it goes on past them; null when the database cannot be asked.</summary>
    private async Task<(List<ListedTitle> Titles, bool More)?> ReadToAsync(string family, TimeSpan keep, DiscoverQuery query, PageAsk ask, CancellationToken ct)
    {
        var titles = new List<ListedTitle>();
        for (var page = 1; page <= ask.Page; page++)
        {
            var listed = await DiscoveredAsync(family, keep, query, ask with { Page = page }, ct);
            if (listed.Value is not { } found) return null;
            titles.AddRange(found.Titles);
            if (found.Next is null) return (titles, false);
        }
        return (titles, true);
    }

    private async Task<Outcome<PagedHits>> NamedPageAsync(Outcome<TitlePage> listed, PageAsk ask, CancellationToken ct) =>
        listed.Value is { } page
            ? Outcome<PagedHits>.Ok(new PagedHits(await HitsAsync(page.Titles, ask.Take, ct), page.Next))
            : listed.Carry<PagedHits>();

    /// <summary>
    /// The released and upcoming lists: the most looked at titles of a window of days, each page in
    /// date order. With a <paramref name="sort"/>, the window's <see cref="TmdbClient.RatingPoolSize"/>
    /// most looked at are put in that order (<see cref="OrderedAsync"/>) and paged as one list: asked
    /// of the database by date, a window is led by titles nobody has looked at.
    /// </summary>
    private async Task<Outcome<ListAnswer>> WindowAsync(string family, string kind, DateOnly from, DateOnly to, bool newestFirst, bool anime, string? sort, PageAsk ask, CancellationToken ct)
    {
        if (!Catalogue.IsKind(kind)) return Invalid<ListAnswer>("Kind is film or show.");

        var query = new DiscoverQuery { Kind = kind, From = from, To = to, Sort = DiscoverSorts.Popular };
        if (anime)
        {
            var genres = await catalogue.GenresAsync(kind, ct);
            if (genres is null) return Unavailable<ListAnswer>("The film database could not be asked about its genres.");
            if (MatchGenre(genres, TitleCategories.AnimeGenre) is not { } animation)
                return NotFound<ListAnswer>($"The film database has no {kind} genre called {TitleCategories.AnimeGenre}.");
            query = query with { GenreIds = [animation.Id], Language = TitleCategories.AnimeLanguage };
        }
        if (sort is not null)
        {
            var pool = new List<ListedTitle>();
            for (var p = 1; pool.Count < TmdbClient.RatingPoolSize; p++)
            {
                var band = await DiscoveredAsync(family, ListKept, query, new PageAsk(TmdbClient.RatingPoolSize, ShelfListed, p), ct);
                if (band.Value is not { } listedBand) return band.Carry<ListAnswer>();
                pool.AddRange(listedBand.Titles);
                if (listedBand.Next is null) break;
            }
            var whole = await OrderedAsync([.. pool.DistinctBy(t => (t.Kind, t.TmdbId))], sort, ct);
            var sorted = await HitsAsync([.. whole.Skip(ask.Skip).Take(ask.Take)], ask.Take, ct);
            return Outcome<ListAnswer>.Ok(new ListAnswer(kind, from, to, sorted, ask.Next(whole.Count > ask.Skip + ask.Size)));
        }

        var listed = await DiscoveredAsync(family, ListKept, query, ask, ct);
        if (listed.Value is not { } page) return listed.Carry<ListAnswer>();

        var ordered = page.Titles
            .OrderBy(t => t.Released is null);
        var dated = newestFirst
            ? ordered.ThenByDescending(t => t.Released)
            : ordered.ThenBy(t => t.Released);
        var hits = await HitsAsync(dated.ToList(), ask.Take, ct);
        return Outcome<ListAnswer>.Ok(new ListAnswer(kind, from, to, hits, page.Next));
    }

    /// <summary>
    /// The titles the database counts as similar to a seed, narrowed by the discovery's other filters.
    /// The database filters nothing of its own here, so the filters are read off each candidate, and a
    /// person or a keyword is looked for in what each candidate is tagged with and credited to.
    /// </summary>
    private async Task<Outcome<TitlePage>> SimilarToAsync(TitleKey seed, IReadOnlyList<DiscoverQuery> queries, string? sort, PageAsk ask, CancellationToken ct)
    {
        if (queries.Count == 0) return Outcome<TitlePage>.Ok(new TitlePage([], null));
        var space = seed.Kind == "film" ? "movie" : "tv";
        var kept = await keeper.KeptAsync($"similar/{space}-{seed.TmdbId}", AnswerJson.Default.ListListedTitle, DiscoverKept,
            async (tmdb, token) => (await tmdb.SimilarAsync(seed.Kind, seed.TmdbId, 2, token)).ToList(), null, ct);
        if (kept is null) return Unavailable<TitlePage>("The film database could not be asked what is similar.");

        // The people and keywords are the same in every question; the picks are what tell them apart.
        var query = queries[0];
        var narrowed = (kept.Value ?? []).Where(t => queries.Any(q => DiscoverNarrowing.Holds(t, q))).ToList();

        var ordered = await OrderedAsync(narrowed, sort, ct);
        if (query.PersonIds.Count == 0 && query.KeywordIds.Count == 0)
        {
            var all = ordered.ToList();
            return Outcome<TitlePage>.Ok(new TitlePage([.. all.Skip(ask.Skip).Take(ask.Take)], ask.Next(all.Count > ask.Skip + ask.Size)));
        }

        // A title is asked about only as far as the answer needs: eight at a time until the page is
        // full and one title beyond it shows there is a page to follow.
        var matched = new List<ListedTitle>();
        foreach (var chunk in ordered.Chunk(CardsAtOnce))
        {
            var traits = new TitleTraits?[chunk.Length];
            await Parallel.ForEachAsync(Enumerable.Range(0, chunk.Length), new ParallelOptions { MaxDegreeOfParallelism = CardsAtOnce, CancellationToken = ct },
                async (i, token) =>
                {
                    var title = chunk[i];
                    var traitsKept = await keeper.KeptAsync($"traits/{(title.Kind == "film" ? "movie" : "tv")}-{title.TmdbId}",
                        AnswerJson.Default.TitleTraits, VocabularyKept,
                        (tmdb, t) => tmdb.TraitsAsync(title.Kind, title.TmdbId, t), null, token);
                    traits[i] = traitsKept?.Value;
                });
            for (var i = 0; i < chunk.Length; i++)
                if (traits[i] is { } t
                    && query.KeywordIds.All(t.Keywords.Contains)
                    && query.PersonIds.All(t.People.Contains))
                    matched.Add(chunk[i]);
            if (matched.Count > ask.Skip + ask.Size) break;
        }
        return Outcome<TitlePage>.Ok(new TitlePage([.. matched.Skip(ask.Skip).Take(ask.Take)], ask.Next(matched.Count > ask.Skip + ask.Size)));
    }

    /// <summary>
    /// Titles held whole in the order a sort names: the most looked at first, the best rated first
    /// (<see cref="RatedAsync"/>), or the newest or oldest release first, an undated title last; any
    /// other sort leaves them as they are.
    /// </summary>
    private async Task<IReadOnlyList<ListedTitle>> OrderedAsync(IReadOnlyList<ListedTitle> titles, string? sort, CancellationToken ct) => sort switch
    {
        DiscoverSorts.Popular => [.. titles.OrderByDescending(t => t.Popularity)],
        DiscoverSorts.Rating => await RatedAsync(titles, ct),
        DiscoverSorts.Newest => [.. titles.OrderBy(t => t.Released is null).ThenByDescending(t => t.Released)],
        DiscoverSorts.Oldest => [.. titles.OrderBy(t => t.Released is null).ThenBy(t => t.Released)],
        _ => titles,
    };

    /// <summary>The titles best rated first: by IMDb where the import holds it, else by the film database's weighted rating.</summary>
    private async Task<IReadOnlyList<ListedTitle>> RatedAsync(IReadOnlyList<ListedTitle> titles, CancellationToken ct)
    {
        if (imdbRatings.Edition is null) return WeightedRating.Order(titles);
        try
        {
            return await RankedByImdbAsync(null, titles, ct);
        }
        catch (HttpRequestException)
        {
            return WeightedRating.Order(titles);
        }
    }

    /// <summary>The most a title may be short of its card before the ranking is left unmade rather than kept without it.</summary>
    private static int CardsMayBeMissing(int titles) => Math.Max(2, titles / 4);

    /// <summary>
    /// The listed titles, and with a <paramref name="query"/> of kind, genre and years alone the best
    /// rated titles IMDb files there that were not listed, ranked by <see cref="ImdbRanking"/>. Each
    /// listed title is matched to IMDb through its card, which names its IMDb id. Throws
    /// <see cref="HttpRequestException"/> when too many cards could not be had for the ranking to stand.
    /// </summary>
    private async Task<List<ListedTitle>> RankedByImdbAsync(DiscoverQuery? query, IReadOnlyList<ListedTitle> listed, CancellationToken ct)
    {
        var cards = await CardsAsync(listed.Select(t => (t.Kind, t.TmdbId)), ct);
        var missing = listed.Count(t => cards.GetValueOrDefault((t.Kind, t.TmdbId)) is null);
        if (missing > CardsMayBeMissing(listed.Count))
            throw new HttpRequestException($"{missing} of {listed.Count} titles could not be named by the film database.");

        var entries = listed
            .Select(t => new ImdbRanking.Entry(t, imdbRatings.Of(cards.GetValueOrDefault((t.Kind, t.TmdbId))?.ImdbId)))
            .ToList();

        var spec = query is null ? null : await ImdbSpecAsync(query, ct);
        var mostVoted = spec is null ? [] : imdbRatings.MostVoted(spec, ImdbRanking.Breadth);
        var floor = mostVoted.Count > 0
            ? ImdbRanking.FloorFor(mostVoted)
            : ImdbRanking.FloorFor([.. entries.Where(e => e.Imdb is not null).Select(e => e.Imdb!.Value.Votes).OrderDescending()]);

        if (spec is not null && query is not null)
        {
            var present = listed.Select(t => cards.GetValueOrDefault((t.Kind, t.TmdbId))?.ImdbId).OfType<string>().ToHashSet(StringComparer.Ordinal);
            var found = imdbRatings.Candidates(spec, Math.Max(floor, query.MinVotes ?? 0), query.MinRating, ImdbRanking.Widened + present.Count)
                .Where(c => !present.Contains(c.ImdbId))
                .Take(ImdbRanking.Widened)
                .ToList();
            if (found.Count > 0)
            {
                var glanced = await glances.ForAsync([.. found.Select(c => c.ImdbId)], ct, WideningWait);
                var taken = listed.Select(t => (t.Kind, t.TmdbId)).ToHashSet();
                foreach (var candidate in found)
                {
                    if (!glanced.TryGetValue(candidate.ImdbId, out var glance) || glance.Kind != spec.Kind || !taken.Add((glance.Kind, glance.TmdbId)))
                        continue;
                    entries.Add(new ImdbRanking.Entry(new ListedTitle
                    {
                        TmdbId = glance.TmdbId,
                        Kind = glance.Kind,
                        Title = candidate.Title,
                        Released = glance.Released,
                        Rating = glance.Rating,
                        Votes = glance.Votes,
                        Overview = glance.Overview,
                        PosterPath = glance.PosterPath,
                    }, new ImdbRatings.Rating(candidate.Rating, candidate.Votes)));
                }
            }
        }

        return ImdbRanking.Order(entries, floor);
    }

    /// <summary>How long a ranking waits for the film database to name the titles IMDb adds to it.</summary>
    private static readonly TimeSpan WideningWait = TimeSpan.FromSeconds(30);

    /// <summary>
    /// The question in IMDb's terms, or null when it asks for anything IMDb's two files cannot answer
    /// (people, keywords, a language, a country, companies, networks, a runtime, a ceiling on rating or
    /// votes, a certificate, a show's airing, status or type) or names a genre IMDb has no name for.
    /// </summary>
    private async Task<ImdbSpec?> ImdbSpecAsync(DiscoverQuery q, CancellationToken ct)
    {
        if (q.PersonIds.Count + q.KeywordIds.Count + q.AnyGenreIds.Count + q.AnyKeywordIds.Count + q.AnyLanguages.Count
            + q.CompanyIds.Count + q.NetworkIds.Count > 0
            || !string.IsNullOrWhiteSpace(q.Language) || !string.IsNullOrWhiteSpace(q.OriginCountry)
            || q.MinRuntime is not null || q.MaxRuntime is not null || q.MaxRating is not null || q.MaxVotes is not null
            || !string.IsNullOrWhiteSpace(q.MaxCertification) || q.AiringFrom is not null || q.AiringTo is not null
            || q.ShowStatus is not null || q.ShowType is not null)
            return null;

        var all = q.GenreIds.Count + q.WithoutGenreIds.Count > 0 ? await catalogue.GenresAsync(q.Kind, ct) : [];
        if (all is null) return null;

        var allOf = new List<IReadOnlyList<string>>();
        foreach (var id in q.GenreIds)
        {
            if (all.FirstOrDefault(g => g.Id == id) is not { } genre || ImdbGenres.Of(genre.Name) is not { } names) return null;
            allOf.Add(names);
        }

        var noneOf = new List<string>();
        foreach (var id in q.WithoutGenreIds)
        {
            if (all.FirstOrDefault(g => g.Id == id) is not { } genre || ImdbGenres.Of(genre.Name) is not { } names) return null;
            noneOf.AddRange(names);
        }
        if (q.Kind == "show" && q.GenreIds.Count == 0) noneOf.AddRange(ImdbGenres.Unscripted);
        // IMDb files a documentary about a genre under that genre too; it is a documentary's answer only when asked.
        if (!allOf.Any(names => names.Contains("Documentary"))) noneOf.Add("Documentary");

        return new ImdbSpec(q.Kind, allOf, noneOf, q.From?.Year, q.To?.Year);
    }

    /// <summary>Everything a person is credited on, kept a day, their portrait kept with it.</summary>
    private Task<KeptAnswer<Filmography>?> FilmographyAsync(int personId, CancellationToken ct) =>
        keeper.KeptAsync($"credits/{personId}", AnswerJson.Default.Filmography, CreditsKept,
            async (tmdb, token) =>
            {
                var person = await tmdb.FilmographyAsync(personId, token);
                if (person is not null) await catalogue.FetchPortraitAsync(tmdb, person.TmdbId, person.ProfilePath, token);
                return person;
            }, null, ct);

    private async Task<Outcome<List<KeywordMatch>>> KeywordsAsync(string phrase, CancellationToken ct)
    {
        var wanted = phrase.Trim();
        var kept = await keeper.KeptAsync(AnswerKeeper.QueryName("keywords", wanted.ToLowerInvariant()),
            AnswerJson.Default.ListKeywordMatch, VocabularyKept,
            async (tmdb, token) => (await tmdb.SearchKeywordsAsync(wanted, token)).ToList(), null, ct);
        return kept is null
            ? Unavailable<List<KeywordMatch>>("The film database could not be asked about its keywords.")
            : Outcome<List<KeywordMatch>>.Ok(kept.Value ?? []);
    }

    /// <summary>The film database's production companies matching some words, kept.</summary>
    private async Task<Outcome<List<CompanyMatch>>> CompaniesAsync(string words, CancellationToken ct)
    {
        var wanted = words.Trim();
        var kept = await keeper.KeptAsync(AnswerKeeper.QueryName("companies", wanted.ToLowerInvariant()),
            AnswerJson.Default.ListCompanyMatch, VocabularyKept,
            async (tmdb, token) => (await tmdb.SearchCompaniesAsync(wanted, token)).ToList(), null, ct);
        return kept is null
            ? Unavailable<List<CompanyMatch>>("The film database could not be asked about its companies.")
            : Outcome<List<CompanyMatch>>.Ok(kept.Value ?? []);
    }

    /// <summary>A studio by name (<see cref="MatchStudio"/>), or why there is none.</summary>
    private async Task<Outcome<Studio>> StudioAsync(string words, CancellationToken ct)
    {
        var companies = await CompaniesAsync(words, ct);
        if (companies.Value is not { } found) return companies.Carry<Studio>();
        return MatchStudio(found, words) is { } studio
            ? Outcome<Studio>.Ok(studio)
            : NotFound<Studio>($"The film database has no studio called \"{words.Trim()}\".");
    }

    /// <summary>
    /// A studio as the database files it: every company whose name holds the words as whole words
    /// (<c>blumhouse</c> is Blumhouse Productions, Blumhouse Television and Blumhouse Atomic Monster), and
    /// the words as the first such name spells them, and whether a company's name is the words and nothing
    /// else. Null when no company's name holds them.
    /// </summary>
    internal static Studio? MatchStudio(IReadOnlyList<CompanyMatch> companies, string words)
    {
        var wanted = words.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (wanted.Length == 0) return null;
        string? shown = null;
        var exact = false;
        var ids = new List<int>();
        foreach (var company in companies)
        {
            var name = company.Name.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var at = Enumerable.Range(0, Math.Max(0, name.Length - wanted.Length + 1))
                .FirstOrDefault(i => wanted.Select((w, j) => string.Equals(name[i + j], w, StringComparison.OrdinalIgnoreCase)).All(same => same), -1);
            if (at < 0) continue;
            ids.Add(company.Id);
            shown ??= string.Join(' ', name[at..(at + wanted.Length)]);
            exact |= name.Length == wanted.Length;
        }
        return shown is null ? null : new Studio(shown, ids, exact);
    }

    /// <summary>The film database's languages with their English names, kept.</summary>
    private async Task<Outcome<IReadOnlyList<LanguageName>>> LanguagesAsync(CancellationToken ct)
    {
        var kept = await keeper.KeptAsync("languages", AnswerJson.Default.ListLanguageName, VocabularyKept,
            async (tmdb, token) => (await tmdb.LanguagesAsync(token)).ToList() is { Count: > 0 } list ? list : null, null, ct);
        return kept is null
            ? Unavailable<IReadOnlyList<LanguageName>>("The film database could not be asked about its languages.")
            : Outcome<IReadOnlyList<LanguageName>>.Ok(kept.Value ?? []);
    }

    /// <summary>A language by its code or its English name.</summary>
    private async Task<Outcome<LanguageName>> LanguageAsync(string text, CancellationToken ct)
    {
        var languages = await LanguagesAsync(ct);
        if (languages.Value is not { } known) return languages.Carry<LanguageName>();

        var wanted = text.Trim();
        var language = known.FirstOrDefault(l => string.Equals(l.Code, wanted, StringComparison.OrdinalIgnoreCase))
                       ?? known.FirstOrDefault(l => string.Equals(l.Name, wanted, StringComparison.OrdinalIgnoreCase));
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
            var found = await keeper.KeptAsync($"finds/{tag}", AnswerJson.Default.TitleKey, VocabularyKept,
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
                    AnswerJson.Default.TitleCard, CardKept,
                    (tmdb, t) => tmdb.CardAsync(key.Kind, key.Id, t), null, token);
                cards[key] = kept?.Value;
            });
        return new Dictionary<(string Kind, int Id), TitleCard?>(cards);
    }

    /// <summary>The titles a description recommends, each with its card and joined with the library as <paramref name="shelf"/> holds it.</summary>
    public async Task<List<TitleHitView>> RecommendedAsync(IReadOnlyList<ListedTitle> titles, LibraryShelf shelf, CancellationToken ct) =>
        [.. (await HitsAsync(titles, ResultsListed, ct)).Where(h => h.Card?.ImdbId is not null).Select(h => View(h, shelf))];

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
    private static string Canonical(DiscoverQuery q, int limit, int page) => string.Join('|',
        q.Kind, string.Join(',', q.GenreIds), string.Join(',', q.WithoutGenreIds), q.From?.ToString("O"), q.To?.ToString("O"), string.Join(',', q.PersonIds),
        string.Join(',', q.KeywordIds), q.Language, q.MinRating?.ToString(CultureInfo.InvariantCulture),
        q.MinVotes?.ToString(CultureInfo.InvariantCulture), q.Sort, limit, page,
        string.Join(',', q.AnyGenreIds), string.Join(',', q.AnyKeywordIds), string.Join(',', q.AnyLanguages), q.OriginCountry,
        string.Join(',', q.CompanyIds), string.Join(',', q.NetworkIds), q.MinRuntime, q.MaxRuntime,
        q.MaxRating?.ToString(CultureInfo.InvariantCulture), q.MaxVotes, q.MaxCertification,
        q.AiringFrom?.ToString("O"), q.AiringTo?.ToString("O"), q.ShowStatus, q.ShowType);

    private static Outcome<T> Invalid<T>(string message) where T : class => Outcome<T>.Failed(Problem.Invalid, message);

    private static Outcome<T> NotFound<T>(string message) where T : class => Outcome<T>.Failed(Problem.NotFound, message);

    private static Outcome<T> Unavailable<T>(string message) where T : class => Outcome<T>.Failed(Problem.Unavailable, message);
}
