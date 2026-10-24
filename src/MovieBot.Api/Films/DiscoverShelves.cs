using System.Collections.Concurrent;
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

    /// <summary>A set of films Wikidata lists (an award, an author's adaptations, a place, a subgenre), as the weekly cycle kept it.</summary>
    Wikidata,
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

    /// <summary>The <see cref="WikidataSet.Key"/> a <see cref="ShelfSource.Wikidata"/> shelf lists.</summary>
    public string? WikidataKey { get; init; }

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
    /// <summary>Whose <see cref="Rating"/> is, <c>imdb</c> or <c>tmdb</c>; absent with no rating (<see cref="TitleScores.Serve"/>).</summary>
    public string? RatedBy { get; init; }

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

/// <summary>A shelf as Discover lists it, before its titles are asked for.</summary>
public sealed record ShelfHeadView
{
    public required string Id { get; init; }
    public required string Title { get; init; }
    public required string Icon { get; init; }

    /// <summary><c>film</c> or <c>show</c>; absent for a shelf of both.</summary>
    public string? Kind { get; init; }

    /// <summary>For a shelf drawn as a tile: the posters of its first titles, in its order, served from here.</summary>
    public List<string>? Posters { get; init; }

    /// <summary>For a shelf drawn as a tile: how many titles are on its first page.</summary>
    public int? Total { get; init; }

    /// <summary>For a shelf drawn as a tile: true when it goes on past its first page, so <see cref="Total"/> is a floor.</summary>
    public bool? More { get; init; }

    /// <summary>For a series: how many of its films the asking person watched through.</summary>
    public int? Watched { get; init; }

    /// <summary>For a series some of whose films the person watched: the first they have not, by the part of its name that tells it from the series.</summary>
    public string? Next { get; init; }
}

/// <summary>A browse category of the Discover page and the shelves in it, in order.</summary>
public sealed record ShelfSectionView
{
    public required string Id { get; init; }
    public required string Label { get; init; }
    public required string Icon { get; init; }

    /// <summary>What is in it, in a few words: how many shelves.</summary>
    public required string Caption { get; init; }

    /// <summary>Three posters from its first shelves, for its tile.</summary>
    public required List<string> Posters { get; init; }

    /// <summary>Whether its shelves are series, drawn as a grid of tiles rather than rows.</summary>
    public bool Series { get; init; }

    public required List<ShelfHeadView> Shelves { get; init; }
}

/// <summary>
/// One row of Discover's page with nothing typed: <c>shelf</c> (its <c>shelf</c>), <c>moods</c> and
/// <c>franchises</c> (those categories' shelves as tiles), or <c>browse</c> (the categories as tiles).
/// </summary>
public sealed record DiscoverRowView
{
    public required string Kind { get; init; }
    public ShelfHeadView? Shelf { get; init; }
}

/// <summary>A language the Discover page's filter offers: its ISO 639-1 code and its English name.</summary>
public sealed record LanguageOptionView(string Code, string Name);

/// <summary>What <c>/api/discover</c> answers: the page's rows and categories for the viewer, and what the filter picks from.</summary>
public sealed record DiscoverIndexView
{
    /// <summary>The shelf the page's spotlight draws from: the season's, else the week's trending films.</summary>
    public required string Spotlight { get; init; }

    public required List<DiscoverRowView> Rows { get; init; }

    /// <summary>The browse categories, in order.</summary>
    public required List<ShelfSectionView> Sections { get; init; }

    /// <summary>The database's genres for films, by name.</summary>
    public required List<string> FilmGenres { get; init; }

    /// <summary>The database's genres for shows, by name, without talk, news and reality.</summary>
    public required List<string> ShowGenres { get; init; }

    public required List<LanguageOptionView> Languages { get; init; }

    /// <summary>
    /// The spotlight's shelf and the first rows' shelves, each as <c>/api/discover/shelves/{id}</c>
    /// answers it, those answered in time; a page asks for any other by that route.
    /// </summary>
    public List<ShelfView> Shelves { get; init; } = new();

    /// <summary>
    /// The spotlight's films, its shelf's first films the viewer has not watched, each as
    /// <c>/api/films/{imdbId}</c> answers it, those described in time; a page asks for any other by that route.
    /// </summary>
    public List<FilmView> Films { get; init; } = new();
}

/// <summary>What <c>/api/discover/shelves/{id}</c> answers: one shelf and its titles, in its order.</summary>
public sealed record ShelfView
{
    public required string Id { get; init; }
    public required string Title { get; init; }
    public required string Icon { get; init; }

    /// <summary>The category it is in, by its label.</summary>
    public required string Section { get; init; }

    public string? Kind { get; init; }
    public required List<ShelfTitleView> Titles { get; init; }

    /// <summary>The page that follows this one; absent when the shelf ends here.</summary>
    public int? Next { get; init; }
}

/// <summary>A page of a shelf's titles and the page that follows it, absent on the last.</summary>
internal sealed record ShelfPage(List<ShelfTitleView> Titles, int? Next);

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
/// database. A shelf of awards, an author's adaptations, a place or a subgenre is a set of films
/// Wikidata lists, read from what <see cref="WikidataWarmer"/> kept and absent until it is kept. A title the database holds no IMDb id for has no page, and is on no
/// shelf.
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
    WikidataSets wikidata,
    TimeProvider clock)
{
    /// <summary>How many shelves of what a viewer watched their tab shows.</summary>
    private const int LikeShelves = 2;

    /// <summary>How far back what the people here watched is read.</summary>
    internal static readonly TimeSpan PopularHereWindow = TimeSpan.FromDays(30);

    /// <summary>Where every shelf belongs: the page's own rows (<c>for-you</c>), then the browse categories in order.</summary>
    public static readonly IReadOnlyList<ShelfSection> Sections =
    [
        new("for-you", "For you", "sparkles"),
        new("moods", "Moods", "smile"),
        new("movies", "Movies", "trophy"),
        new("shows", "TV Shows", "tv"),
        new("genres", "Genres", "shapes"),
        new("themes", "Themes", "tags"),
        new("awards", "Awards", "award"),
        new("adaptations", "Adaptations", "book-open"),
        new("places", "Places", "map-pin"),
        new("world", "World cinema", "globe"),
        new("franchises", "Franchises", "library"),
        new("studios", "Studios", "clapperboard"),
        new("directors", "Directors", "megaphone"),
    ];

    /// <summary>The category whose shelves are series, drawn as strips that follow a person through them.</summary>
    private const string SeriesSection = "franchises";

    /// <summary>How many posters a tile is handed: as many as a strip can show.</summary>
    private const int TilePosters = 4;

    /// <summary>The languages the filter offers, besides any.</summary>
    public static readonly IReadOnlyList<LanguageOptionView> FilterLanguages =
    [
        new("en", "English"), new("zh", "Chinese"), new("da", "Danish"), new("fr", "French"), new("de", "German"),
        new("hi", "Hindi"), new("it", "Italian"), new("ja", "Japanese"), new("ko", "Korean"), new("no", "Norwegian"),
        new("pt", "Portuguese"), new("es", "Spanish"), new("sv", "Swedish"), new("th", "Thai"), new("tr", "Turkish"),
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

    /// <summary>
    /// The page's rows and the browse categories for one viewer; <paramref name="viewerId"/> is null for
    /// the service key. A shelf drawn as a tile (a mood, a series, a category) carries its posters, read
    /// from what was last answered for it, and a series how far the viewer got through it.
    /// </summary>
    public async Task<DiscoverIndexView> IndexAsync(string? viewerId, CancellationToken ct)
    {
        var today = Today();
        var titles = library.Titles();
        var held = LibraryShelf.Of(titles, record, viewerId);
        var personal = Personal(viewerId, titles);
        var shown = Fixed.Where(s => (s.Season is null || s.Season.Holds(today)) && Kept(s)).ToList();
        var seasonal = shown.Where(s => s.Season is not null).ToList();

        // What a tile draws: every mood and series, and the first shelves of each category.
        var drawn = shown.Where(s => s.Section is "moods" or SeriesSection).ToList();
        foreach (var section in Sections.Skip(1))
            drawn.AddRange(shown.Where(s => s.Section == section.Id).Take(3));
        await KnownAsync(drawn.Distinct(), titles, ct);

        ShelfHeadView Drawn(Shelf shelf)
        {
            var known = _known.GetValueOrDefault(shelf.Id);
            var listed = known?.Titles ?? [];
            var head = Head(shelf) with
            {
                Posters = [.. listed.Where(t => t.PosterUrl is not null).Take(TilePosters).Select(t => t.PosterUrl!)],
                Total = listed.Count,
                More = known?.Next is not null ? true : null,
            };
            if (shelf.Section != SeriesSection) return head;
            var watched = listed.Count(t => held.Saw(t.Kind, t.ImdbId));
            var next = listed.FirstOrDefault(t => !held.Saw(t.Kind, t.ImdbId));
            return head with
            {
                Watched = watched,
                Next = watched > 0 && next is not null ? Shortened(next.Title, shelf.Title) : null,
            };
        }

        var sections = new List<ShelfSectionView>();
        foreach (var section in Sections.Skip(1))
        {
            var shelves = shown.Where(s => s.Section == section.Id).ToList();
            if (shelves.Count == 0) continue;
            var firsts = shelves.Take(3).Select(s => _known.GetValueOrDefault(s.Id)?.Titles ?? []).ToList();
            sections.Add(new ShelfSectionView
            {
                Id = section.Id,
                Label = section.Label,
                Icon = section.Icon,
                Caption = Caption(shelves),
                Posters = [.. Spread(firsts).Take(3)],
                Series = section.Id == SeriesSection,
                Shelves = [.. shelves.Select(s => s.Section is "moods" or SeriesSection ? Drawn(s) : Head(s))],
            });
        }

        // The likes of what the viewer watched and the season's first, then the moods and what is
        // popular, the series, what is popular here, the categories, the day's director, what is out and
        // coming, and what plays now.
        Shelf Named(string id) => ById[id];
        DiscoverRowView Row(Shelf shelf) => new() { Kind = "shelf", Shelf = Head(shelf) };
        var rows = new List<DiscoverRowView>();
        rows.AddRange(personal.Where(s => s.Source == ShelfSource.Like).Select(Row));
        rows.AddRange(seasonal.Select(Row));
        rows.Add(new DiscoverRowView { Kind = "moods" });
        rows.Add(Row(Named("trending-today")));
        rows.Add(new DiscoverRowView { Kind = "franchises" });
        rows.AddRange(personal.Where(s => s.Source == ShelfSource.PopularHere).Select(Row));
        rows.Add(Row(Named("trending-films")));
        rows.Add(Row(Named("trending-shows")));
        rows.Add(new DiscoverRowView { Kind = "browse" });
        rows.Add(Row(Directors[today.DayOfYear % Directors.Count]));
        rows.Add(Row(Named("in-cinemas")));
        rows.Add(Row(Named("new-releases")));
        rows.Add(Row(Named("coming-soon")));
        rows.AddRange(personal.Where(s => s.Source == ShelfSource.ReadyHere).Select(Row));

        return new DiscoverIndexView
        {
            Spotlight = seasonal.FirstOrDefault()?.Id ?? "trending-films",
            Rows = rows,
            Sections = sections,
            FilmGenres = [.. (await catalogue.GenresAsync("film", ct) ?? []).Select(g => g.Name)],
            ShowGenres = [.. (await catalogue.GenresAsync("show", ct) ?? []).Where(g => !UnscriptedShowGenres.Contains(g.Id)).Select(g => g.Name)],
            Languages = [.. FilterLanguages],
        };
    }

    /// <summary>
    /// The first page last answered for each shelf the same for everybody, as nobody in particular
    /// sees it: what a tile draws its posters, its count and a series its films from. The warmer keeps
    /// every one.
    /// </summary>
    private readonly ConcurrentDictionary<string, ShelfPage> _known = new(StringComparer.Ordinal);

    /// <summary>Answers each of the shelves nothing is known of yet, a few at a time.</summary>
    private async Task KnownAsync(IEnumerable<Shelf> shelves, IReadOnlyList<TitleSummary> titles, CancellationToken ct)
    {
        var missing = shelves.Where(s => s.Shared && !_known.ContainsKey(s.Id)).ToList();
        await Parallel.ForEachAsync(missing, new ParallelOptions { MaxDegreeOfParallelism = 4, CancellationToken = ct },
            async (shelf, token) => await TitlesAsync(shelf, null, titles, 1, null, token));
    }

    /// <summary>Whether a shelf has something to show: every shelf but a Wikidata one always does, and that one once its set is kept.</summary>
    private bool Kept(Shelf shelf) =>
        shelf.Source != ShelfSource.Wikidata || (WikidataSets.Find(shelf.WikidataKey!) is { } set && wikidata.Kept(set) is { Count: > 0 });

    /// <summary>A category's caption: how many shelves it holds.</summary>
    private static string Caption(IReadOnlyList<Shelf> shelves) => shelves.Count == 1 ? "1 shelf" : $"{shelves.Count} shelves";

    /// <summary>The first poster of each list, then the second of each, and so on, each once.</summary>
    private static IEnumerable<string> Spread(IReadOnlyList<List<ShelfTitleView>> lists)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < 10; i++)
            foreach (var list in lists)
                if (i < list.Count && list[i].PosterUrl is { } poster && seen.Add(poster))
                    yield return poster;
    }

    /// <summary>
    /// A film's name as told apart from its series: what follows the series' name and any joining word,
    /// <c>Part II</c> of <c>Back to the Future</c>, <c>Chamber of Secrets</c> of <c>Harry Potter</c>;
    /// its whole name when it does not begin with the series'.
    /// </summary>
    internal static string Shortened(string title, string series)
    {
        var name = series.StartsWith("The ", StringComparison.Ordinal) && !title.StartsWith("The ", StringComparison.Ordinal) ? series[4..] : series;
        if (!title.StartsWith(name, StringComparison.OrdinalIgnoreCase) || title.Length == name.Length || char.IsLetterOrDigit(title[name.Length])) return title;
        var rest = title[name.Length..].TrimStart(' ', ':', '-', '–', '—', '.', ',');
        foreach (var joint in new[] { "and the ", "and ", "of the " })
            if (rest.StartsWith(joint, StringComparison.OrdinalIgnoreCase)) rest = rest[joint.Length..];
        return rest.Length > 0 ? rest : title;
    }

    /// <summary>
    /// One page of a shelf and its titles for a viewer, by its id, in the shelf's own order or the one
    /// <paramref name="sort"/> names (<see cref="Sorts"/>); NotFound for an id that names no shelf. A
    /// shelf made of the library or of a list held whole ends on its first page.
    /// </summary>
    public async Task<Outcome<ShelfView>> ShelfAsync(string id, string? viewerId, int page, CancellationToken ct, string? sort = null)
    {
        if (page < 1 || page > CatalogueSearch.PagesListed) return Outcome<ShelfView>.Failed(Problem.Invalid, $"Page is 1 to {CatalogueSearch.PagesListed}.");
        if (sort is not null && !Sorts.Contains(sort)) return Outcome<ShelfView>.Failed(Problem.Invalid, "Sort is rating, newest or oldest.");
        var titles = library.Titles();
        var shelf = ById.GetValueOrDefault(id) ?? Personal(viewerId, titles).FirstOrDefault(s => s.Id == id) ?? LikeShelf(id, titles);
        if (shelf is null) return Outcome<ShelfView>.Failed(Problem.NotFound, "There is no shelf by that name.");

        var listed = await TitlesAsync(shelf, viewerId, titles, page, sort, ct);
        if (listed.Value is not { } found) return listed.Carry<ShelfView>();
        return Outcome<ShelfView>.Ok(new ShelfView
        {
            Id = shelf.Id,
            Title = shelf.Title,
            Icon = shelf.Icon,
            Section = Sections.First(s => s.Id == shelf.Section).Label,
            Kind = shelf.Kind,
            Titles = found.Titles,
            Next = found.Next,
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
            if ((await TitlesAsync(shelf, null, titles, 1, null, ct)).Value is null) unanswered++;
        }
        return unanswered;
    }

    private static ShelfHeadView Head(Shelf shelf) => new() { Id = shelf.Id, Title = shelf.Title, Icon = shelf.Icon, Kind = shelf.Kind };

    /// <summary>
    /// The orders a shelf is served in besides its own. Each is the order of the whole shelf, made
    /// where its titles come from, so every page continues the one before it.
    /// </summary>
    public static readonly IReadOnlySet<string> Sorts = new HashSet<string>([DiscoverSorts.Rating, DiscoverSorts.Newest, DiscoverSorts.Oldest], StringComparer.Ordinal);

    private async Task<Outcome<ShelfPage>> TitlesAsync(Shelf shelf, string? viewerId, IReadOnlyList<TitleSummary> titles, int page, string? sort, CancellationToken ct)
    {
        var answer = await AnsweredAsync(shelf, viewerId, titles, page, sort, ct);
        if (page == 1 && sort is null && shelf.Shared && answer.Value is { } found) _known[shelf.Id] = found;
        return answer;
    }

    /// <summary>
    /// A shelf's page. What the database pages (a discovery, a chart, a window of dates, the titles
    /// like another) and the films a person directed are paged; a series, the week's trending list and
    /// the library's shelves are whole in one answer, and any later page of them is empty. A
    /// <paramref name="sort"/> orders the whole shelf before it is paged: a discovery and the titles
    /// like another are asked in that order; the first titles of a chart or of a window of dates, a
    /// series, a director's films and a Wikidata set are ordered as held; and a shelf whole in one
    /// answer is ordered as it is answered.
    /// </summary>
    private async Task<Outcome<ShelfPage>> AnsweredAsync(Shelf shelf, string? viewerId, IReadOnlyList<TitleSummary> titles, int page, string? sort, CancellationToken ct)
    {
        var held = LibraryShelf.Of(titles, record, viewerId);
        var ask = new PageAsk(CatalogueSearch.MostOnShelf, CatalogueSearch.ShelfListed, page);
        if (page > 1 && shelf.Source is ShelfSource.Collection or ShelfSource.TrendingWeek or ShelfSource.PopularHere or ShelfSource.ReadyHere)
            return Ok([]);
        switch (shelf.Source)
        {
            case ShelfSource.Query:
            {
                var query = shelf.Query!;
                if (query.To is null && query.AiringTo is null) query = query with { To = Today() };
                if (sort is not null) query = query with { Sort = sort };
                return Hits(await search.ShelfAsync(query, ask, ct), held);
            }
            case ShelfSource.Chart:
                return Hits(await search.ChartAsync(shelf.Chart!.Value, ask, sort, ct), held);
            case ShelfSource.Recent:
            {
                var answer = await search.RecentAsync(shelf.Kind!, CatalogueSearch.RecentDays, false, ct, page, ask.Size, sort);
                return answer.Value is { } list ? Ok(Views(list.Hits, held), list.Next) : Failed(answer);
            }
            case ShelfSource.Upcoming:
            {
                var answer = await search.UpcomingAsync(shelf.Kind!, CatalogueSearch.UpcomingDays, false, ct, page, ask.Size, sort);
                return answer.Value is { } list ? Ok(Views(list.Hits, held), list.Next) : Failed(answer);
            }
            case ShelfSource.Collection:
            {
                var answer = await search.CollectionAsync(shelf.DatabaseId!.Value, ct, sort);
                return answer.Value is { } series ? Ok(Views(series.Hits, held)) : Failed(answer);
            }
            case ShelfSource.Director:
                return Hits(await search.DirectedAsync(shelf.DatabaseId!.Value, ask, ct, sort), held);
            case ShelfSource.Like:
            {
                var answer = await search.DiscoverAsync(new DiscoverRequest { Like = shelf.Seed, Kind = shelf.Kind, Page = page, Sort = sort }, ct);
                return answer.Value is { } similar ? Ok(Views(similar.Hits, held), similar.Next) : Failed(answer);
            }
            case ShelfSource.TrendingWeek:
            {
                var view = popular.View();
                var kind = shelf.Kind!;
                return Ok(Sorted([.. (kind == "film" ? view.Films : view.Shows).Select(t => new ShelfTitleView
                {
                    ImdbId = t.ImdbId,
                    Kind = kind,
                    Title = t.Title,
                    Year = t.Year,
                    Rating = t.Rating,
                    Votes = t.Votes,
                    RatedBy = t.RatedBy,
                    PosterUrl = t.PosterUrl,
                    InLibrary = held.Holds(kind, t.ImdbId),
                    LibraryId = held.LibraryIdOf(kind, t.ImdbId),
                    Watched = held.Saw(kind, t.ImdbId),
                    Category = categories.Of(kind, t.ImdbId),
                })], sort));
            }
            case ShelfSource.Wikidata:
                return Hits(await search.WikidataShelfAsync(WikidataSets.Find(shelf.WikidataKey!)!, ask, ct, sort), held);
            case ShelfSource.PopularHere:
                return Ok(Sorted(PopularHere(titles, held), sort));
            default:
                return Ok(Sorted(ReadyHere(titles, held), sort));
        }
    }

    /// <summary>
    /// A shelf whole in one answer in the order a sort names: the best rated first as a question by
    /// rating ranks them (<see cref="ImdbRanking"/>), or the newest or oldest year first, an undated
    /// title last; without a sort, in its own order.
    /// </summary>
    internal static List<ShelfTitleView> Sorted(List<ShelfTitleView> views, string? sort)
    {
        switch (sort)
        {
            case DiscoverSorts.Rating:
                var entries = views
                    .Select((v, i) => new ImdbRanking.Entry(
                        new ListedTitle { TmdbId = i, Kind = v.Kind, Title = v.Title, Rating = v.Rating, Votes = v.Votes },
                        v.RatedBy == TitleScores.ByImdb && v.Rating is { } rating ? new ImdbRatings.Rating(rating, v.Votes) : null))
                    .ToList();
                var floor = ImdbRanking.FloorFor([.. entries.Where(e => e.Imdb is not null).Select(e => e.Imdb!.Value.Votes).OrderDescending()]);
                return [.. ImdbRanking.Order(entries, floor).Select(t => views[t.TmdbId])];
            case DiscoverSorts.Newest:
                return [.. views.OrderBy(v => v.Year is null).ThenByDescending(v => v.Year)];
            case DiscoverSorts.Oldest:
                return [.. views.OrderBy(v => v.Year is null).ThenBy(v => v.Year)];
            default:
                return views;
        }
    }

    private Outcome<ShelfPage> Hits(Outcome<PagedHits> answer, LibraryShelf held) =>
        answer.Value is { } paged ? Ok(Views(paged.Hits, held), paged.Next) : Failed(answer);

    /// <summary>
    /// A shelf whose question the database could not answer. One it has nothing under (a series or a
    /// person it no longer holds) is a shelf with nothing on it; the shelf itself is still there.
    /// </summary>
    private static Outcome<ShelfPage> Failed<T>(Outcome<T> answer) where T : class =>
        answer.Problem == Problem.NotFound ? Ok([]) : answer.Carry<ShelfPage>();

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
                RatedBy = view.RatedBy,
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

    private static Outcome<ShelfPage> Ok(List<ShelfTitleView> titles, int? next = null) => Outcome<ShelfPage>.Ok(new ShelfPage(titles, next));

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
            var (showRating, showVotes, showRatedBy) = shows.Scores.Serve(imdb, show?.TmdbId, "show", show?.Rating, show?.Votes ?? 0);
            return new ShelfTitleView
            {
                ImdbId = imdb,
                Kind = kind,
                Title = show?.Title ?? title.Film?.Name ?? title.Title,
                Year = show?.FirstYear,
                Rating = showRating,
                Votes = showVotes,
                RatedBy = showRatedBy,
                PosterUrl = shows.HasImage(imdb, ShowStore.Poster) ? ShowViews.ImageUrl(imdb, ShowStore.Poster) : null,
                Starring = Starring(show?.Cast),
                InLibrary = true,
                Category = categories.Of(kind, imdb),
            };
        }

        var film = films.Held(imdb)?.Details;
        var (rating, votes, ratedBy) = films.Scores.Serve(imdb, film?.TmdbId, "film", film?.Rating, film?.Votes ?? 0);
        return new ShelfTitleView
        {
            ImdbId = imdb,
            Kind = kind,
            Title = film?.Title ?? title.Film?.Name ?? title.Title,
            Year = film?.Year ?? title.Film?.Year,
            Rating = rating,
            Votes = votes,
            RatedBy = ratedBy,
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

        // Awards, adaptations, places and subgenres: sets of films Wikidata lists, kept weekly.
        shelves.AddRange(WikidataSets.Fixed.Select(set => new Shelf
        {
            Section = set.Section, Id = set.Key, Title = set.Title, Icon = set.Icon, Source = ShelfSource.Wikidata, WikidataKey = set.Key,
        }));

        // World cinema, by the language a film was made in.
        shelves.AddRange(
        [
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
            shelves.Add(Film("studios", id, name, "building", new DiscoverQuery { Kind = "film", CompanyIds = [company], MinVotes = 50 }));
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
            shelves.Add(new Shelf { Section = "directors", Id = id, Title = $"Directed by {name}", Icon = "megaphone", Source = ShelfSource.Director, DatabaseId = person });

        return shelves;
    }
}
