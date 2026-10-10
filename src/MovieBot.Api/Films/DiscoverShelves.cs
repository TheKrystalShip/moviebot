using TheKrystalShip.MovieBot.Acquire.Tmdb;
using TheKrystalShip.MovieBot.Api.Library;
using TheKrystalShip.MovieBot.Api.People;
using TheKrystalShip.MovieBot.Api.Shows;
using TheKrystalShip.MovieBot.Core;

namespace TheKrystalShip.MovieBot.Api.Films;

/// <summary>Where a shelf's titles come from.</summary>
public enum ShelfSource
{
    /// <summary>A discovery the site asks, by the database's own ids.</summary>
    Query,

    /// <summary>One of the database's own lists.</summary>
    Chart,

    /// <summary>The week's trending films or shows, as <see cref="PopularNow"/> keeps them.</summary>
    TrendingWeek,

    /// <summary>What came out lately.</summary>
    Recent,

    /// <summary>What has a date coming.</summary>
    Upcoming,

    /// <summary>The films of one series.</summary>
    Collection,

    /// <summary>The films one person directed.</summary>
    Director,

    /// <summary>What the database counts as similar to a title the viewer watched.</summary>
    Like,

    /// <summary>What the people of this server watched lately.</summary>
    PopularHere,

    /// <summary>The library's films the viewer has not watched, which play at once.</summary>
    ReadyHere,
}

/// <summary>The days of the year a seasonal shelf is shown on, both ends included.</summary>
public sealed record ShelfSeason(int FromMonth, int FromDay, int ToMonth, int ToDay)
{
    public bool Holds(DateOnly day)
    {
        var at = day.Month * 100 + day.Day;
        return at >= FromMonth * 100 + FromDay && at <= ToMonth * 100 + ToDay;
    }
}

/// <summary>One shelf of the Discover page: its id, its heading, the tab it is on, and the question that fills it.</summary>
public sealed record Shelf
{
    public required string Id { get; init; }
    public required string Title { get; init; }

    /// <summary>A Lucide icon name.</summary>
    public required string Icon { get; init; }

    public required string Section { get; init; }

    /// <summary><c>film</c> or <c>show</c>; null for a shelf of both.</summary>
    public string? Kind { get; init; } = "film";

    public ShelfSource Source { get; init; } = ShelfSource.Query;

    public DiscoverQuery? Query { get; init; }

    public TitleChart? Chart { get; init; }

    /// <summary>A collection's or a person's id in the database.</summary>
    public int? DatabaseId { get; init; }

    /// <summary>The IMDb id a <see cref="ShelfSource.Like"/> shelf starts from.</summary>
    public string? Seed { get; init; }

    /// <summary>The days a seasonal shelf is shown on; null for every day.</summary>
    public ShelfSeason? Season { get; init; }

    /// <summary>Whether it reads the same for everybody, which is what is kept fresh ahead of anybody asking.</summary>
    public bool Shared => Source is not (ShelfSource.Like or ShelfSource.PopularHere or ShelfSource.ReadyHere);
}

/// <summary>A tab of the Discover page.</summary>
public sealed record ShelfSection(string Id, string Label, string Icon);

/// <summary>A title on a shelf, by its poster, joined with the library and with whether the viewer watched it.</summary>
public sealed record ShelfTitleView
{
    public required string ImdbId { get; init; }

    /// <summary><c>film</c> or <c>show</c>.</summary>
    public required string Kind { get; init; }

    public required string Title { get; init; }
    public int? Year { get; init; }
    public double? Rating { get; init; }
    public int Votes { get; init; }

    /// <summary>The poster its page shows, served from here; absent when none is kept.</summary>
    public string? PosterUrl { get; init; }

    /// <summary>The two top-billed actors, joined by a comma; absent when nothing names them.</summary>
    public string? Starring { get; init; }

    /// <summary>Whether the library holds the film, or any episode of the show.</summary>
    public bool InLibrary { get; init; }

    /// <summary>For a film the library holds, the title that plays it.</summary>
    public string? LibraryId { get; init; }

    /// <summary>Whether the viewer watched the film through; never set for a show.</summary>
    public bool Watched { get; init; }

    public TitleCategory? Category { get; init; }
}

/// <summary>A shelf as its tab lists it, before its titles are asked for.</summary>
public sealed record ShelfHeadView
{
    public required string Id { get; init; }
    public required string Title { get; init; }
    public required string Icon { get; init; }

    /// <summary><c>film</c> or <c>show</c>; absent for a shelf of both.</summary>
    public string? Kind { get; init; }
}

/// <summary>A tab of the Discover page and the shelves on it, in order.</summary>
public sealed record ShelfSectionView
{
    public required string Id { get; init; }
    public required string Label { get; init; }
    public required string Icon { get; init; }
    public required List<ShelfHeadView> Shelves { get; init; }
}

/// <summary>A language the Discover page's filter offers: its ISO 639-1 code and its English name.</summary>
public sealed record LanguageOptionView(string Code, string Name);

/// <summary>What <c>/api/discover</c> answers: the tabs and their shelves for the viewer, and what the filter picks from.</summary>
public sealed record DiscoverIndexView
{
    public required List<ShelfSectionView> Sections { get; init; }

    /// <summary>The database's genres for films, by name.</summary>
    public required List<string> FilmGenres { get; init; }

    /// <summary>The database's genres for shows, by name, without talk, news and reality.</summary>
    public required List<string> ShowGenres { get; init; }

    public required List<LanguageOptionView> Languages { get; init; }
}

/// <summary>What <c>/api/discover/shelves/{id}</c> answers: one shelf and its titles, in its order.</summary>
public sealed record ShelfView
{
    public required string Id { get; init; }
    public required string Title { get; init; }
    public required string Icon { get; init; }

    /// <summary>The tab it is on, by its label.</summary>
    public required string Section { get; init; }

    public string? Kind { get; init; }
    public required List<ShelfTitleView> Titles { get; init; }
}

/// <summary>
/// The Discover page's shelves: what is new and what is popular, moods, the acclaimed, decades,
/// genres and themes, world cinema, series, studios and directors, and what follows from what the
/// viewer and the people of this server watched.
///
/// Every shelf the same for everybody is a fixed question of the film database by its own ids,
/// answered through <see cref="CatalogueSearch"/> and kept as its answers are, and
/// <see cref="DiscoverWarmer"/> asks each again before anybody does, so a page reads what is kept.
/// A shelf that follows from a person (what they watched, what is here for them) is answered when
/// it is asked for, and one of the library's from the library as listed, never waiting on the
/// database. A title the database holds no IMDb id for has no page, and is on no shelf.
/// </summary>
public sealed class DiscoverShelves(
    CatalogueSearch search,
    Catalogue catalogue,
    PopularNow popular,
    LibraryListing library,
    WatchRecord record,
    FilmStore films,
    ShowStore shows,
    TitleCategories categories,
    TimeProvider clock)
{
    /// <summary>How many shelves of what a viewer watched their tab shows.</summary>
    private const int LikeShelves = 2;

    /// <summary>How far back what the people here watched is read.</summary>
    internal static readonly TimeSpan PopularHereWindow = TimeSpan.FromDays(30);

    /// <summary>The tabs, in the order they are shown; the first is the page's own.</summary>
    public static readonly IReadOnlyList<ShelfSection> Sections =
    [
        new("for-you", "For you", "sparkles"),
        new("moods", "Moods", "smile"),
        new("movies", "Movies", "clapperboard"),
        new("shows", "TV Shows", "tv"),
        new("genres", "Genres", "shapes"),
        new("themes", "Themes", "tags"),
        new("world", "World cinema", "globe"),
        new("franchises", "Franchises", "library"),
        new("makers", "Studios and directors", "megaphone"),
    ];

    /// <summary>The languages the filter offers, besides any.</summary>
    public static readonly IReadOnlyList<LanguageOptionView> FilterLanguages =
    [
        new("en", "English"), new("es", "Spanish"), new("ca", "Catalan"), new("fr", "French"), new("it", "Italian"),
        new("de", "German"), new("pt", "Portuguese"), new("ja", "Japanese"), new("ko", "Korean"), new("zh", "Chinese"),
        new("hi", "Hindi"), new("sv", "Swedish"), new("da", "Danish"), new("no", "Norwegian"), new("tr", "Turkish"),
        new("th", "Thai"),
    ];

    /// <summary>The database's genres for talk, news and reality shows, which no shelf or filter offers.</summary>
    private static readonly int[] UnscriptedShowGenres = [10767, 10763, 10764];

    /// <summary>The genres a title for children is filed under, which a genre's shelf leaves out unless it is theirs.</summary>
    private static readonly int[] ForChildren = [Animation, Family];

    /// <summary>Every shelf that is the same for everybody, in the order its tab shows it.</summary>
    public static readonly IReadOnlyList<Shelf> Fixed = Shelves();

    private static readonly Dictionary<string, Shelf> ById = Fixed.ToDictionary(s => s.Id, StringComparer.Ordinal);

    /// <summary>The directors a spotlight on the first tab turns through, a different one each day.</summary>
    private static readonly IReadOnlyList<Shelf> Directors = [.. Fixed.Where(s => s.Source == ShelfSource.Director)];

    /// <summary>The tabs and their shelves for one viewer; <paramref name="viewerId"/> is null for the service key.</summary>
    public async Task<DiscoverIndexView> IndexAsync(string? viewerId, CancellationToken ct)
    {
        var today = Today();
        var titles = library.Titles();
        var personal = Personal(viewerId, titles);

        var sections = new List<ShelfSectionView>();
        foreach (var section in Sections)
        {
            var shelves = Fixed.Where(s => s.Section == section.Id && (s.Season is null || s.Season.Holds(today))).ToList();
            if (section.Id == "for-you")
            {
                // The season's first, then what follows from the viewer's own watching, what is popular
                // everywhere and here, the day's director, what is out and coming, and what plays now.
                var spotlight = Directors[today.DayOfYear % Directors.Count];
                Shelf Named(string id) => shelves.First(s => s.Id == id);
                shelves =
                [
                    .. shelves.Where(s => s.Season is not null),
                    .. personal.Where(s => s.Source == ShelfSource.Like),
                    Named("trending-today"), Named("trending-films"), Named("trending-shows"),
                    .. personal.Where(s => s.Source == ShelfSource.PopularHere),
                    spotlight,
                    Named("in-cinemas"), Named("new-releases"), Named("coming-soon"),
                    .. personal.Where(s => s.Source == ShelfSource.ReadyHere),
                ];
            }
            sections.Add(new ShelfSectionView
            {
                Id = section.Id,
                Label = section.Label,
                Icon = section.Icon,
                Shelves = [.. shelves.Select(Head)],
            });
        }

        return new DiscoverIndexView
        {
            Sections = sections,
            FilmGenres = [.. (await catalogue.GenresAsync("film", ct) ?? []).Select(g => g.Name)],
            ShowGenres = [.. (await catalogue.GenresAsync("show", ct) ?? []).Where(g => !UnscriptedShowGenres.Contains(g.Id)).Select(g => g.Name)],
            Languages = [.. FilterLanguages],
        };
    }

    /// <summary>One shelf and its titles for a viewer, by its id; NotFound for an id that names no shelf.</summary>
    public async Task<Outcome<ShelfView>> ShelfAsync(string id, string? viewerId, CancellationToken ct)
    {
        var titles = library.Titles();
        var shelf = ById.GetValueOrDefault(id) ?? Personal(viewerId, titles).FirstOrDefault(s => s.Id == id) ?? LikeShelf(id, titles);
        if (shelf is null) return Outcome<ShelfView>.Failed(Problem.NotFound, "There is no shelf by that name.");

        var listed = await TitlesAsync(shelf, viewerId, titles, ct);
        if (listed.Value is not { } found) return listed.Carry<ShelfView>();
        return Outcome<ShelfView>.Ok(new ShelfView
        {
            Id = shelf.Id,
            Title = shelf.Title,
            Icon = shelf.Icon,
            Section = Sections.First(s => s.Id == shelf.Section).Label,
            Kind = shelf.Kind,
            Titles = found,
        });
    }

    /// <summary>
    /// Asks every shelf the same for everybody, one after another, which asks the database only for
    /// what is due. The number of shelves that could not be answered.
    /// </summary>
    public async Task<int> WarmAsync(CancellationToken ct)
    {
        var unanswered = 0;
        var titles = library.Titles();
        foreach (var shelf in Fixed)
        {
            ct.ThrowIfCancellationRequested();
            if ((await TitlesAsync(shelf, null, titles, ct)).Value is null) unanswered++;
        }
        return unanswered;
    }

    private static ShelfHeadView Head(Shelf shelf) => new() { Id = shelf.Id, Title = shelf.Title, Icon = shelf.Icon, Kind = shelf.Kind };

    private async Task<Outcome<List<ShelfTitleView>>> TitlesAsync(Shelf shelf, string? viewerId, IReadOnlyList<TitleSummary> titles, CancellationToken ct)
    {
        var held = LibraryShelf.Of(titles, record, viewerId);
        var limit = CatalogueSearch.ShelfListed;
        switch (shelf.Source)
        {
            case ShelfSource.Query:
            {
                var query = shelf.Query!;
                if (query.To is null && query.AiringTo is null) query = query with { To = Today() };
                return Hits(await search.ShelfAsync(query, limit, ct), held);
            }
            case ShelfSource.Chart:
                return Hits(await search.ChartAsync(shelf.Chart!.Value, limit, ct), held);
            case ShelfSource.Recent:
            {
                var answer = await search.RecentAsync(shelf.Kind!, CatalogueSearch.RecentDays, false, ct);
                return answer.Value is { } list ? Ok(Views(list.Hits, held)) : Failed(answer);
            }
            case ShelfSource.Upcoming:
            {
                var answer = await search.UpcomingAsync(shelf.Kind!, CatalogueSearch.UpcomingDays, false, ct);
                return answer.Value is { } list ? Ok(Views(list.Hits, held)) : Failed(answer);
            }
            case ShelfSource.Collection:
            {
                var answer = await search.CollectionAsync(shelf.DatabaseId!.Value, ct);
                return answer.Value is { } series ? Ok(Views(series.Hits, held)) : Failed(answer);
            }
            case ShelfSource.Director:
                return Hits(await search.DirectedAsync(shelf.DatabaseId!.Value, limit, ct), held);
            case ShelfSource.Like:
            {
                var answer = await search.DiscoverAsync(new DiscoverRequest { Like = shelf.Seed, Kind = shelf.Kind }, ct);
                return answer.Value is { } similar ? Ok(Views(similar.Hits, held)) : Failed(answer);
            }
            case ShelfSource.TrendingWeek:
            {
                var view = popular.View();
                var kind = shelf.Kind!;
                return Ok([.. (kind == "film" ? view.Films : view.Shows).Select(t => new ShelfTitleView
                {
                    ImdbId = t.ImdbId,
                    Kind = kind,
                    Title = t.Title,
                    Year = t.Year,
                    Rating = t.Rating,
                    Votes = t.Votes,
                    PosterUrl = t.PosterUrl,
                    InLibrary = held.Holds(kind, t.ImdbId),
                    LibraryId = held.LibraryIdOf(kind, t.ImdbId),
                    Watched = held.Saw(kind, t.ImdbId),
                    Category = categories.Of(kind, t.ImdbId),
                })]);
            }
            case ShelfSource.PopularHere:
                return Ok(PopularHere(titles, held));
            default:
                return Ok(ReadyHere(titles, held));
        }
    }

    private Outcome<List<ShelfTitleView>> Hits(Outcome<List<TitleHit>> answer, LibraryShelf held) =>
        answer.Value is { } hits ? Ok(Views(hits, held)) : Failed(answer);

    /// <summary>
    /// A shelf whose question the database could not answer. One it has nothing under (a series or a
    /// person it no longer holds) is a shelf with nothing on it; the shelf itself is still there.
    /// </summary>
    private static Outcome<List<ShelfTitleView>> Failed<T>(Outcome<T> answer) where T : class =>
        answer.Problem == Problem.NotFound ? Ok([]) : answer.Carry<List<ShelfTitleView>>();

    private List<ShelfTitleView> Views(IEnumerable<TitleHit> hits, LibraryShelf held)
    {
        var views = new List<ShelfTitleView>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var hit in hits)
        {
            var view = search.View(hit, held);
            if (view.ImdbId is not { } imdb || !seen.Add(view.Kind + imdb)) continue;
            views.Add(new ShelfTitleView
            {
                ImdbId = imdb,
                Kind = view.Kind,
                Title = view.Title,
                Year = view.Year,
                Rating = view.Rating,
                Votes = view.Votes,
                PosterUrl = view.PosterUrl,
                Starring = view.Starring.Count > 0 ? string.Join(", ", view.Starring) : null,
                InLibrary = view.InLibrary,
                LibraryId = view.LibraryId,
                Watched = view.Watched,
                Category = view.Category,
            });
        }
        return views;
    }

    private static Outcome<List<ShelfTitleView>> Ok(List<ShelfTitleView> titles) => Outcome<List<ShelfTitleView>>.Ok(titles);

    // The shelves that follow from the viewer and from the people here.

    /// <summary>
    /// The viewer's own shelves for the first tab: one of what is like each of the last titles they
    /// finished, what the people here watched lately, and the library's films they have not watched,
    /// each only when it has something on it.
    /// </summary>
    private List<Shelf> Personal(string? viewerId, IReadOnlyList<TitleSummary> titles)
    {
        var shelves = new List<Shelf>();
        var byId = titles.ToDictionary(t => t.Id, StringComparer.Ordinal);
        if (viewerId is not null)
        {
            var seeds = new HashSet<string>(StringComparer.Ordinal);
            foreach (var (titleId, entry) in record.Entries(viewerId))
            {
                if (seeds.Count == LikeShelves) break;
                if (!entry.Finished || !byId.TryGetValue(titleId, out var title) || title.Film?.ImdbId is not { } imdb || !seeds.Add(imdb)) continue;
                shelves.Add(Like(title.Kind == TitleKind.Show ? "show" : "film", imdb, NameOf(title)));
            }
        }

        if (record.WatchedSince(clock.GetUtcNow() - PopularHereWindow).Any(w => byId.ContainsKey(w.TitleId)))
            shelves.Add(new Shelf { Id = "popular-here", Title = "Popular here", Icon = "users", Section = "for-you", Kind = null, Source = ShelfSource.PopularHere });
        shelves.Add(new Shelf { Id = "ready-here", Title = "Ready to watch", Icon = "play", Section = "for-you", Source = ShelfSource.ReadyHere });
        return shelves;
    }

    /// <summary>A shelf of what is like a title, by the id <see cref="Like(string, string, string)"/> gives it.</summary>
    private Shelf? LikeShelf(string id, IReadOnlyList<TitleSummary> titles)
    {
        var parts = id.Split('-', 3);
        if (parts is not ["like", "film" or "show", var imdb] || !FilmStore.IsImdbId(imdb)) return null;
        var kind = parts[1];
        var held = titles.FirstOrDefault(t => t.Film?.ImdbId == imdb && (t.Kind == TitleKind.Show) == (kind == "show"));
        return Like(kind, imdb, held is null ? null : NameOf(held));
    }

    private static Shelf Like(string kind, string imdb, string? name) => new()
    {
        Id = $"like-{kind}-{imdb}",
        Title = name is null ? "More like this" : $"Because you watched {name}",
        Icon = "wand-sparkles",
        Section = "for-you",
        Kind = kind,
        Source = ShelfSource.Like,
        Seed = imdb,
    };

    /// <summary>A library title's name: its film's or show's as described, else as the library knows it.</summary>
    private string NameOf(TitleSummary title)
    {
        var imdb = title.Film?.ImdbId;
        if (title.Kind == TitleKind.Show)
            return (imdb is null ? null : shows.Held(imdb)?.Details?.Title) ?? title.Film?.Name ?? title.Title;
        return (imdb is null ? null : films.Held(imdb)?.Details?.Title) ?? title.Film?.Name ?? title.Title;
    }

    /// <summary>
    /// The films and shows the most people here watched in the last <see cref="PopularHereWindow"/>,
    /// the most recently watched first among as many. Nothing says who, or how many.
    /// </summary>
    private List<ShelfTitleView> PopularHere(IReadOnlyList<TitleSummary> titles, LibraryShelf held)
    {
        var byId = titles.ToDictionary(t => t.Id, StringComparer.Ordinal);
        var watched = new Dictionary<(string Kind, string ImdbId), (HashSet<string> People, DateTimeOffset Last, TitleSummary Title)>();
        foreach (var (userId, titleId, at) in record.WatchedSince(clock.GetUtcNow() - PopularHereWindow))
        {
            if (!byId.TryGetValue(titleId, out var title) || title.Film?.ImdbId is not { } imdb) continue;
            var key = (title.Kind == TitleKind.Show ? "show" : "film", imdb);
            if (!watched.TryGetValue(key, out var entry)) entry = ([], at, title);
            entry.People.Add(userId);
            watched[key] = (entry.People, at > entry.Last ? at : entry.Last, entry.Title);
        }
        return [.. watched
            .OrderByDescending(w => w.Value.People.Count)
            .ThenByDescending(w => w.Value.Last)
            .Take(CatalogueSearch.ResultsListed)
            .Select(w => Held(w.Key.Kind, w.Key.ImdbId, w.Value.Title, held))];
    }

    /// <summary>The library's films that play now and the viewer has not watched through, the best rated first.</summary>
    private List<ShelfTitleView> ReadyHere(IReadOnlyList<TitleSummary> titles, LibraryShelf held) =>
    [
        .. titles
            .Where(t => t.Kind == TitleKind.Film && t.Status == TitleStatus.Ready && t.Film?.ImdbId is { } imdb && !held.Saw("film", imdb))
            .GroupBy(t => t.Film!.ImdbId!)
            .Select(g => Held("film", g.Key, g.First(), held))
            .OrderByDescending(v => v.Rating ?? 0)
            .ThenBy(v => v.Title, StringComparer.OrdinalIgnoreCase)
            .Take(CatalogueSearch.ShelfListed),
    ];

    /// <summary>A title the library holds, described from what is kept, so it never waits on the database.</summary>
    private ShelfTitleView Held(string kind, string imdb, TitleSummary title, LibraryShelf held)
    {
        if (kind == "show")
        {
            var show = shows.Held(imdb)?.Details;
            return new ShelfTitleView
            {
                ImdbId = imdb,
                Kind = kind,
                Title = show?.Title ?? title.Film?.Name ?? title.Title,
                Year = show?.FirstYear,
                Rating = show?.Rating,
                PosterUrl = shows.HasImage(imdb, ShowStore.Poster) ? ShowViews.ImageUrl(imdb, ShowStore.Poster) : null,
                Starring = Starring(show?.Cast),
                InLibrary = true,
                Category = categories.Of(kind, imdb),
            };
        }

        var film = films.Held(imdb)?.Details;
        return new ShelfTitleView
        {
            ImdbId = imdb,
            Kind = kind,
            Title = film?.Title ?? title.Film?.Name ?? title.Title,
            Year = film?.Year ?? title.Film?.Year,
            Rating = film?.Rating,
            PosterUrl = films.HasImage(imdb, FilmStore.Poster) ? FilmViews.ImageUrl(imdb, FilmStore.Poster) : null,
            Starring = Starring(film?.Cast),
            InLibrary = true,
            LibraryId = held.LibraryIdOf(kind, imdb),
            Watched = held.Saw(kind, imdb),
            Category = categories.Of(kind, imdb),
        };
    }

    private static string? Starring(IReadOnlyList<CastMember>? cast) =>
        cast is { Count: > 0 } ? string.Join(", ", cast.Take(2).Select(c => c.Name)) : null;

    private DateOnly Today() => DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);

    // The shelves the same for everybody. Every id below is the film database's own: a genre, a
    // keyword, a company, a network, a collection or a person, as it numbers them.

    private const int Action = 28, Adventure = 12, Animation = 16, Comedy = 35, Crime = 80, Documentary = 99, Drama = 18,
        Family = 10751, Fantasy = 14, History = 36, Horror = 27, Music = 10402, Mystery = 9648, Romance = 10749,
        ScienceFiction = 878, TvMovie = 10770, Thriller = 53, War = 10752, Western = 37, SciFiFantasyShows = 10765;

    private static List<Shelf> Shelves()
    {
        var shelves = new List<Shelf>();

        Shelf Film(string section, string id, string title, string icon, DiscoverQuery query) =>
            new() { Section = section, Id = id, Title = title, Icon = icon, Kind = query.Kind, Query = query };

        // For you: what is new and what is popular, and the season's.
        shelves.AddRange(
        [
            Film("for-you", "spooky-season", "Spooky season", "ghost",
                new DiscoverQuery { Kind = "film", AnyKeywordIds = [3335], MinVotes = 100 }) with { Season = new ShelfSeason(10, 1, 10, 31) },
            Film("for-you", "christmas", "Christmas movies", "gift",
                new DiscoverQuery { Kind = "film", AnyKeywordIds = [207317], MinVotes = 200 }) with { Season = new ShelfSeason(12, 1, 12, 26) },
            Film("for-you", "valentines", "Valentine's", "heart",
                new DiscoverQuery { Kind = "film", GenreIds = [Romance], MinRating = 7, MinVotes = 1000 }) with { Season = new ShelfSeason(2, 1, 2, 14) },
            Film("for-you", "summer-blockbusters", "Summer blockbusters", "sun",
                new DiscoverQuery { Kind = "film", GenreIds = [Action, Adventure], MinVotes = 5000 }) with { Season = new ShelfSeason(6, 21, 8, 31) },
            new Shelf { Section = "for-you", Id = "trending-today", Title = "Trending today", Icon = "flame", Source = ShelfSource.Chart, Chart = TitleChart.FilmsTrendingToday },
            new Shelf { Section = "for-you", Id = "trending-films", Title = "Popular movies this week", Icon = "flame", Source = ShelfSource.TrendingWeek },
            new Shelf { Section = "for-you", Id = "trending-shows", Title = "Popular shows this week", Icon = "flame", Kind = "show", Source = ShelfSource.TrendingWeek },
            new Shelf { Section = "for-you", Id = "in-cinemas", Title = "In cinemas now", Icon = "ticket", Source = ShelfSource.Chart, Chart = TitleChart.InCinemas },
            new Shelf { Section = "for-you", Id = "new-releases", Title = "New releases", Icon = "sparkles", Source = ShelfSource.Recent },
            new Shelf { Section = "for-you", Id = "coming-soon", Title = "Coming soon", Icon = "calendar-clock", Source = ShelfSource.Upcoming },
        ]);

        // Moods: what to put on for an evening of a kind.
        shelves.AddRange(
        [
            Film("moods", "sunday-chill", "Sunday chill", "coffee", new DiscoverQuery
            {
                Kind = "film", GenreIds = [Comedy], WithoutGenreIds = [Animation, Family, Horror, Thriller, Crime, Action],
                MinRating = 6.8, MinVotes = 1000, MaxRuntime = 115,
            }),
            Film("moods", "date-night", "Date night", "heart", new DiscoverQuery { Kind = "film", GenreIds = [Romance, Comedy], MinRating = 6.3, MinVotes = 500 }),
            Film("moods", "family-night", "Family movie night", "house", new DiscoverQuery
            {
                Kind = "film", GenreIds = [Family], MaxCertification = "PG", MinRating = 6.5, MinVotes = 1000,
            }),
            Film("moods", "mind-benders", "Mind-benders", "brain", new DiscoverQuery
            {
                Kind = "film", AnyKeywordIds = [326438, 157171, 374835, 12565], MinRating = 7, MinVotes = 1000,
            }),
            Film("moods", "edge-of-your-seat", "Edge of your seat", "heart-pulse", new DiscoverQuery { Kind = "film", GenreIds = [Thriller], MinRating = 7, MinVotes = 2000 }),
            Film("moods", "feel-good", "Feel-good", "sun", new DiscoverQuery
            {
                Kind = "film", GenreIds = [Comedy, Drama], WithoutGenreIds = [Horror, Thriller, Crime, War], MinRating = 7.2, MinVotes = 1500,
            }),
            Film("moods", "good-cry", "Have a good cry", "droplets", new DiscoverQuery { Kind = "film", AnyKeywordIds = [156924], MinVotes = 50 }),
            Film("moods", "laugh-out-loud", "Laugh out loud", "laugh", new DiscoverQuery
            {
                Kind = "film", GenreIds = [Comedy], WithoutGenreIds = ForChildren, MinRating = 7, MinVotes = 2000,
            }),
            Film("moods", "short-and-sweet", "Short and sweet", "timer", new DiscoverQuery
            {
                Kind = "film", MinRuntime = 60, MaxRuntime = 90, MinRating = 7, MinVotes = 1000,
            }),
            Film("moods", "epic-weekend", "Epic weekend", "hourglass", new DiscoverQuery
            {
                Kind = "film", MinRuntime = 150, MinRating = 7.5, MinVotes = 2000, Sort = DiscoverSorts.Rating,
            }),
            Film("moods", "so-bad-its-good", "So bad it's good", "thumbs-down", new DiscoverQuery { Kind = "film", MaxRating = 5, MinVotes = 1500 }),
            Film("moods", "comfort-shows", "Comfort shows", "sofa", new DiscoverQuery
            {
                Kind = "show", GenreIds = [Comedy], WithoutGenreIds = [Animation], MinRating = 7.5, MinVotes = 500,
            }),
        ]);

        // Movies: the acclaimed, and each decade's best.
        shelves.AddRange(
        [
            Film("movies", "top-rated", "Top rated of all time", "trophy", new DiscoverQuery { Kind = "film", MinVotes = 5000, Sort = DiscoverSorts.Rating }),
            Film("movies", "hidden-gems", "Hidden gems", "gem", new DiscoverQuery
            {
                Kind = "film", MinRating = 7.3, MinVotes = 300, MaxVotes = 1500, WithoutGenreIds = [Documentary, Music, TvMovie, Animation],
                Sort = DiscoverSorts.Rating,
            }),
            Film("movies", "classics", "Classics", "film", new DiscoverQuery
            {
                Kind = "film", To = new DateOnly(1979, 12, 31), MinVotes = 1000, Sort = DiscoverSorts.Rating,
            }),
        ]);
        foreach (var decade in new[] { 1970, 1980, 1990, 2000, 2010 })
        {
            var name = decade < 2000 ? $"{decade % 100}s" : $"{decade}s";
            shelves.Add(Film("movies", $"best-of-{decade}s", $"Best of the {name}", "calendar", new DiscoverQuery
            {
                Kind = "film", From = new DateOnly(decade, 1, 1), To = new DateOnly(decade + 9, 12, 31), MinVotes = 1000, Sort = DiscoverSorts.Rating,
            }));
        }

        // TV shows.
        shelves.AddRange(
        [
            new Shelf { Section = "shows", Id = "trending-shows-today", Title = "Trending shows today", Icon = "flame", Kind = "show", Source = ShelfSource.Chart, Chart = TitleChart.ShowsTrendingToday },
            Film("shows", "top-rated-shows", "Top rated series", "trophy", new DiscoverQuery { Kind = "show", MinVotes = 2000, Sort = DiscoverSorts.Rating }),
            Film("shows", "binge-worthy", "Binge-worthy", "list-video", new DiscoverQuery
            {
                Kind = "show", ShowStatus = ShowStatuses.Ended, WithoutGenreIds = [Animation], MinRating = 8, MinVotes = 500, Sort = DiscoverSorts.Rating,
            }),
            Film("shows", "miniseries", "Miniseries", "layers", new DiscoverQuery
            {
                Kind = "show", ShowType = ShowTypes.Miniseries, MinRating = 7.5, MinVotes = 200, Sort = DiscoverSorts.Rating,
            }),
            Film("shows", "crime-series", "Crime series", "siren", new DiscoverQuery { Kind = "show", GenreIds = [Crime], WithoutGenreIds = [Documentary], MinVotes = 300 }),
            Film("shows", "sci-fi-fantasy-series", "Sci-fi and fantasy series", "rocket", new DiscoverQuery
            {
                Kind = "show", GenreIds = [SciFiFantasyShows], WithoutGenreIds = [Animation], MinVotes = 300,
            }),
            Film("shows", "sitcoms", "Sitcoms", "laugh", new DiscoverQuery { Kind = "show", GenreIds = [Comedy], AnyKeywordIds = [193171], MinVotes = 200 }),
            Film("shows", "true-crime", "True crime", "fingerprint", new DiscoverQuery { Kind = "show", GenreIds = [Documentary], AnyKeywordIds = [33722], MinVotes = 20 }),
            Film("shows", "k-dramas", "K-dramas", "drama", new DiscoverQuery { Kind = "show", Language = "ko", GenreIds = [Drama], MinVotes = 100 }),
        ]);
        foreach (var (id, name, network, votes) in new[]
                 {
                     ("hbo", "HBO", 49, 200), ("netflix", "Netflix originals", 213, 500), ("apple-tv", "Apple TV", 2552, 100),
                     ("prime-video", "Prime Video", 1024, 100), ("disney-plus", "Disney+", 2739, 100), ("bbc-one", "BBC One", 4, 100),
                     ("fx", "FX", 88, 100),
                 })
            shelves.Add(Film("shows", id, name, "tv", new DiscoverQuery { Kind = "show", NetworkIds = [network], MinVotes = votes }));

        // Genres.
        foreach (var (id, name, icon, genre) in new[]
                 {
                     ("action", "Action", "swords", Action), ("adventure", "Adventure", "compass", Adventure), ("comedy", "Comedy", "laugh", Comedy),
                     ("crime", "Crime", "siren", Crime), ("drama", "Drama", "drama", Drama), ("horror", "Horror", "skull", Horror),
                     ("science-fiction", "Science fiction", "rocket", ScienceFiction), ("thriller", "Thriller", "heart-pulse", Thriller),
                     ("romance", "Romance", "heart", Romance), ("mystery", "Mystery", "search", Mystery), ("fantasy", "Fantasy", "wand-sparkles", Fantasy),
                     ("war", "War", "shield", War), ("western", "Western", "mountain", Western), ("history", "History", "landmark", History),
                     ("animation", "Animation", "palette", Animation), ("documentary", "Documentary", "video", Documentary), ("music", "Music", "music", Music),
                 })
        {
            shelves.Add(Film("genres", id, name, icon, new DiscoverQuery
            {
                Kind = "film", GenreIds = [genre], WithoutGenreIds = genre == Animation ? [] : ForChildren, MinVotes = 300,
            }));
        }

        // Themes: what a film is about, as the database's keywords tag it.
        foreach (var (id, name, icon, keyword) in new[]
                 {
                     ("heists", "Heists", "vault", 10051), ("time-travel", "Time travel", "clock", 4379), ("slashers", "Slashers", "skull", 12339),
                     ("zombies", "Zombies", "biohazard", 12377), ("found-footage", "Found footage", "camera", 163053), ("whodunits", "Whodunits", "search", 12570),
                     ("courtroom", "Courtroom dramas", "scale", 33519), ("survival", "Survival", "flame-kindling", 10349), ("space", "Space", "orbit", 9882),
                     ("cyberpunk", "Cyberpunk", "cpu", 12190), ("dystopias", "Dystopias", "building-2", 4565), ("post-apocalyptic", "Post-apocalyptic", "radiation", 4458),
                     ("revenge", "Revenge", "crosshair", 9748), ("true-stories", "True stories", "book-open", 9672), ("coming-of-age", "Coming of age", "sprout", 10683),
                     ("road-trips", "Road trips", "car", 7312), ("martial-arts", "Martial arts", "swords", 779), ("sports", "Sports", "trophy", 6075),
                     ("musicals", "Musicals", "music", 4344), ("superheroes", "Superheroes", "zap", 9715), ("mockumentaries", "Mockumentaries", "video", 11800),
                     ("spies", "Spies", "eye", 470),
                 })
            shelves.Add(Film("themes", id, name, icon, new DiscoverQuery { Kind = "film", AnyKeywordIds = [keyword], MinVotes = 200 }));

        // World cinema, by the language a film was made in.
        shelves.AddRange(
        [
            Film("world", "spanish", "Spanish cinema", "globe", new DiscoverQuery { Kind = "film", Language = "es", OriginCountry = "ES", MinVotes = 200 }),
            Film("world", "catalan", "Catalan cinema", "globe", new DiscoverQuery { Kind = "film", Language = "ca", MinVotes = 20 }),
            Film("world", "korean", "Korean cinema", "globe", new DiscoverQuery { Kind = "film", Language = "ko", MinVotes = 300 }),
            Film("world", "japanese", "Japanese cinema", "globe", new DiscoverQuery { Kind = "film", Language = "ja", WithoutGenreIds = [Animation], MinVotes = 300 }),
            Film("world", "french", "French cinema", "globe", new DiscoverQuery { Kind = "film", Language = "fr", MinVotes = 300 }),
            Film("world", "italian-classics", "Italian classics", "globe", new DiscoverQuery
            {
                Kind = "film", Language = "it", To = new DateOnly(1979, 12, 31), MinVotes = 200, Sort = DiscoverSorts.Rating,
            }),
            Film("world", "german", "German cinema", "globe", new DiscoverQuery { Kind = "film", Language = "de", MinVotes = 300 }),
            Film("world", "nordic-noir", "Nordic noir", "globe", new DiscoverQuery
            {
                Kind = "film", AnyLanguages = ["sv", "da", "no", "fi", "is"], AnyGenreIds = [Crime, Thriller], MinVotes = 100,
            }),
            Film("world", "bollywood", "Bollywood", "globe", new DiscoverQuery { Kind = "film", Language = "hi", MinVotes = 100 }),
            Film("world", "chinese", "Chinese and Hong Kong cinema", "globe", new DiscoverQuery { Kind = "film", AnyLanguages = ["zh", "cn"], MinVotes = 200 }),
        ]);

        // Franchises: each series' films, in the order they came out.
        foreach (var (id, name, collection) in new[]
                 {
                     ("star-wars", "Star Wars", 10), ("lord-of-the-rings", "The Lord of the Rings", 119), ("harry-potter", "Harry Potter", 1241),
                     ("james-bond", "James Bond", 645), ("mission-impossible", "Mission: Impossible", 87359), ("indiana-jones", "Indiana Jones", 84),
                     ("back-to-the-future", "Back to the Future", 264), ("the-godfather", "The Godfather", 230), ("alien", "Alien", 8091),
                     ("the-matrix", "The Matrix", 2344), ("dune", "Dune", 726871), ("jurassic-park", "Jurassic Park", 328),
                     ("john-wick", "John Wick", 404609), ("toy-story", "Toy Story", 10194), ("pirates-of-the-caribbean", "Pirates of the Caribbean", 295),
                     ("mad-max", "Mad Max", 8945), ("the-dark-knight", "The Dark Knight", 263), ("rocky", "Rocky", 1575),
                     ("fast-and-furious", "Fast and Furious", 9485), ("oceans", "Ocean's", 304), ("before-trilogy", "The Before trilogy", 123800),
                     ("avengers", "The Avengers", 86311),
                 })
            shelves.Add(new Shelf { Section = "franchises", Id = id, Title = name, Icon = "library", Source = ShelfSource.Collection, DatabaseId = collection });

        // Studios, and directors.
        foreach (var (id, name, company) in new[]
                 {
                     ("studio-ghibli", "Studio Ghibli", 10342), ("pixar", "Pixar", 3), ("a24", "A24", 41077), ("blumhouse", "Blumhouse", 3172),
                     ("aardman", "Aardman", 297), ("marvel-studios", "Marvel Studios", 420), ("disney-animation", "Walt Disney Animation Studios", 6125),
                     ("neon", "Neon", 90733),
                 })
            shelves.Add(Film("makers", id, name, "building", new DiscoverQuery { Kind = "film", CompanyIds = [company], MinVotes = 50 }));
        foreach (var (id, name, person) in new[]
                 {
                     ("christopher-nolan", "Christopher Nolan", 525), ("denis-villeneuve", "Denis Villeneuve", 137427), ("quentin-tarantino", "Quentin Tarantino", 138),
                     ("stanley-kubrick", "Stanley Kubrick", 240), ("hayao-miyazaki", "Hayao Miyazaki", 608), ("martin-scorsese", "Martin Scorsese", 1032),
                     ("steven-spielberg", "Steven Spielberg", 488), ("david-fincher", "David Fincher", 7467), ("bong-joon-ho", "Bong Joon Ho", 21684),
                     ("greta-gerwig", "Greta Gerwig", 45400), ("ridley-scott", "Ridley Scott", 578), ("wes-anderson", "Wes Anderson", 5655),
                     ("pedro-almodovar", "Pedro Almodóvar", 309), ("guillermo-del-toro", "Guillermo del Toro", 10828), ("alfred-hitchcock", "Alfred Hitchcock", 2636),
                     ("akira-kurosawa", "Akira Kurosawa", 5026), ("park-chan-wook", "Park Chan-wook", 10099), ("sofia-coppola", "Sofia Coppola", 1769),
                     ("joel-coen", "Joel Coen", 1223), ("edgar-wright", "Edgar Wright", 11090), ("jordan-peele", "Jordan Peele", 291263),
                 })
            shelves.Add(new Shelf { Section = "makers", Id = id, Title = $"Directed by {name}", Icon = "megaphone", Source = ShelfSource.Director, DatabaseId = person });

        return shelves;
    }
}
