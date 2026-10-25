using System.Globalization;
using Microsoft.AspNetCore.Mvc;
using TheKrystalShip.MovieBot.Acquire.Tmdb;
using TheKrystalShip.MovieBot.Api.Auth;
using TheKrystalShip.MovieBot.Api.Films;
using TheKrystalShip.MovieBot.Api.People;
using TheKrystalShip.MovieBot.Api.Sessions;
using TheKrystalShip.MovieBot.Api.Shows;
using TheKrystalShip.MovieBot.Api.Wants;
using TheKrystalShip.MovieBot.Core;

namespace TheKrystalShip.MovieBot.Api.Library;

/// <summary>One title on a page of the library: a film, or a show with its episodes grouped.</summary>
public sealed record LibraryItemView
{
    public TitleSummary? Film { get; init; }
    public ShowSummary? Show { get; init; }
}

/// <summary>
/// A page of the library or of the person's wants as it is shown: the titles that answer the search and
/// the picks, in the order asked; how many the page holds before either; and the facets of the whole,
/// with the orders it is served in.
/// </summary>
public sealed record LibraryPageView
{
    public required List<LibraryItemView> Items { get; init; }
    public required int Total { get; init; }
    public required CatalogueFacetsView Facets { get; init; }
}

/// <summary>One entry of a person's history, with the library's title it names.</summary>
public sealed record HistoryItemView
{
    public required HistoryEntry Entry { get; init; }
    public TitleSummary? Title { get; init; }
}

/// <inheritdoc cref="LibraryPageView"/>
public sealed record HistoryPageView
{
    public required List<HistoryItemView> Entries { get; init; }
    public required int Total { get; init; }
    public required CatalogueFacetsView Facets { get; init; }
}

/// <inheritdoc cref="LibraryPageView"/>
public sealed record WantsPageView
{
    public required List<WantView> Wants { get; init; }
    public required int Total { get; init; }
    public required CatalogueFacetsView Facets { get; init; }
}

/// <summary>
/// One order a list held whole is served in: its name as a route's <c>sort</c> takes it, and how two
/// titles compare in it. A tie keeps the list's own order.
/// </summary>
public sealed record ListedOrder<T>(string Name, Comparison<T> Compare);

/// <summary>
/// The library's shelves and the person's wants as their pages show them: searched by name, narrowed by
/// a filter's picks and by where each title stands, and ordered, all here, so every surface that shows a
/// page shows the same titles in the same order. A page holds the whole shelf in one answer; the
/// library is listed from memory (<see cref="LibraryListing"/>), so an answer never waits on a disk or
/// on the network.
/// </summary>
public static class ListedPages
{
    /// <summary>The section a page's states are picked in, beside the filter's.</summary>
    public const string State = "state";

    /// <summary>The shelves beyond the kind a library page can be of: <c>anime</c>, <c>general</c> for the rest, or absent for both.</summary>
    public const string AnimeShelf = "anime";
    public const string GeneralShelf = "general";

    private static readonly StringComparer Names = StringComparer.Create(CultureInfo.InvariantCulture, CompareOptions.IgnoreCase);

    /// <summary>Each order in both directions, the given one first: the second is its opposite.</summary>
    private static IEnumerable<ListedOrder<T>> Both<T>(string first, string second, Comparison<T> ascending, bool firstDescends) =>
        firstDescends
            ? [new(first, (a, b) => ascending(b, a)), new(second, ascending)]
            : [new(first, ascending), new(second, (a, b) => ascending(b, a))];

    private static DateTimeOffset AddedAt(LibraryItemView i) => (i.Film?.AddedAt ?? i.Show?.AddedAt) ?? DateTimeOffset.MinValue;
    private static double RatingOf(LibraryItemView i) => (i.Film?.Details ?? i.Show?.Details)?.Rating ?? 0;
    private static int YearOf(LibraryItemView i) => (i.Film is { } f ? f.Film?.Year : i.Show?.FirstYear) ?? 0;

    /// <summary>A film's name as its card shows it, its year beside it; a show's title.</summary>
    public static string NameOf(LibraryItemView i) => i.Film is { } f
        ? f.Film?.Name is { } name ? f.Film.Year is { } year ? $"{name} ({year})" : name : f.Title
        : i.Show!.Title;

    /// <summary>The orders a library page is served in, the one it opens in first; a page of films alone is also ordered by length.</summary>
    public static IReadOnlyList<ListedOrder<LibraryItemView>> LibraryOrders(string scope) =>
    [
        .. Both<LibraryItemView>("added", "first-added", (a, b) => AddedAt(a).CompareTo(AddedAt(b)), true),
        .. Both<LibraryItemView>("name", "name-desc", (a, b) => Names.Compare(NameOf(a), NameOf(b)), false),
        .. Both<LibraryItemView>(DiscoverSorts.Rating, ReversedSorts.Lowest, (a, b) => RatingOf(a).CompareTo(RatingOf(b)), true),
        .. Both<LibraryItemView>(DiscoverSorts.Newest, DiscoverSorts.Oldest, (a, b) => YearOf(a).CompareTo(YearOf(b)), true),
        .. scope == Films
            ? Both<LibraryItemView>("shortest", "longest", (a, b) => (a.Film?.DurationSeconds ?? 0).CompareTo(b.Film?.DurationSeconds ?? 0), false)
            : [],
    ];

    /// <summary>The orders the wants are served in, the one they open in first.</summary>
    public static readonly IReadOnlyList<ListedOrder<WantView>> WantOrders =
    [
        .. Both<WantView>("added", "first-added", (a, b) => a.Since.CompareTo(b.Since), true),
        .. Both<WantView>("name", "name-desc", (a, b) => Names.Compare(a.Title, b.Title), false),
        .. Both<WantView>(DiscoverSorts.Rating, ReversedSorts.Lowest, (a, b) => (a.Rating ?? 0).CompareTo(b.Rating ?? 0), true),
        .. Both<WantView>(DiscoverSorts.Newest, DiscoverSorts.Oldest, (a, b) => (a.Year ?? 0).CompareTo(b.Year ?? 0), true),
        .. Both<WantView>("most-wanted", "least-wanted", (a, b) => a.Others.CompareTo(b.Others), true),
    ];

    /// <summary>The scopes a library page is of: its films, its shows, or both together.</summary>
    public const string Films = "films";
    public const string Shows = "shows";
    public const string All = "all";

    /// <summary>The states a film can stand in, by the words a pick names them with.</summary>
    private static readonly (string Value, TitleStatus Status)[] FilmStates =
        [("ready", TitleStatus.Ready), ("preparing", TitleStatus.Transcoding), ("failed", TitleStatus.Failed)];

    /// <summary>The states a want can stand in, in the order they are offered.</summary>
    private static readonly string[] WantStatesOffered = [WantStates.Ready, WantStates.Fetching, WantStates.Available, WantStates.Waiting];

    private static FacetTraits TraitsOf(LibraryItemView i) => i.Film is { } f
        ? new FacetTraits(FacetSections.Film, f.Category == TitleCategory.Anime, CinemaGenres.Of(f.Details?.Genres ?? []), f.Film?.Year,
            f.Details?.RuntimeMinutes ?? (f.DurationSeconds > 0 ? (int)Math.Round(f.DurationSeconds / 60) : null),
            f.Details?.Rating, f.Details?.OriginalLanguage)
        : new FacetTraits(FacetSections.Show, i.Show!.Category == TitleCategory.Anime, CinemaGenres.Of(i.Show.Details?.Genres ?? []),
            i.Show.FirstYear, null, i.Show.Details?.Rating, i.Show.Details?.OriginalLanguage);

    private static FacetTraits TraitsOf(WantView w) => new(w.Kind, w.Category == "anime", CinemaGenres.Of(w.Genres), w.Year,
        w.Kind == FacetSections.Film ? w.RuntimeMinutes : null, w.Rating, w.OriginalLanguage);

    private static string? StateOf(LibraryItemView i) => i.Film is { } f ? FilmStates.First(s => s.Status == f.Status).Value : null;

    /// <summary>
    /// A page of a list held whole: <paramref name="all"/> narrowed by the states picked, the filter's
    /// picks and the words searched, in <paramref name="order"/>; the facets of the whole with the states
    /// picked on it and the words not, offering what the whole holds whatever is picked, each state the
    /// whole holds counted with the picks on it; and the orders served.
    /// </summary>
    private static (List<T> Shown, CatalogueFacetsView Facets) Page<T>(
        IReadOnlyList<T> all, Func<T, FacetTraits> traits, Func<T, string?> state, IReadOnlyList<string> states, IReadOnlyList<string> offered,
        DiscoverPicks picks, IReadOnlySet<string> fixedBy, Func<string, string?> languageName,
        Func<T, string> name, string? q, ListedOrder<T> order, IEnumerable<string> orders)
    {
        var stated = states.Count == 0 ? all : [.. all.Where(t => state(t) is { } s && states.Contains(s))];
        var facets = WholeFacets.Facets([.. stated.Select(traits)], picks, fixedBy, languageName, [.. all.Select(traits)]);
        var picked = WholeFacets.Filter(all, traits, picks);
        var held = offered.Where(s => all.Any(t => state(t) == s))
            .Select(s => picked.Count(t => state(t) == s) is var count and > 0 ? new FacetOptionView(s, Count: count) : new FacetOptionView(s, Count: 0, Empty: true)).ToList();
        var sections = held.Count > 0 ? [.. facets.Sections, new FacetSectionView(State, held)] : facets.Sections;

        var shown = WholeFacets.Filter(stated, traits, picks)
            .Where(t => Holds(name(t), q))
            .Order(Comparer<T>.Create(order.Compare))
            .ToList();
        return (shown, new CatalogueFacetsView { Sections = sections, Sorts = [.. orders] });
    }

    public static void MapListedPages(this WebApplication app)
    {
        // A library shelf's page: its films, its shows or both, of the anime shelf or of the rest,
        // searched, narrowed and ordered, with the facets of the whole shelf and the orders it is served in.
        app.MapGet("/api/library/{scope}", async (string scope, string? shelf, string? q, string? sort, [FromQuery] string[]? state, [FromQuery] string[]? held,
            string? kind, string? category, [FromQuery] string[]? anyGenre, [FromQuery] string[]? decade, [FromQuery] string[]? length,
            double? minRating, [FromQuery] string[]? language,
            HttpContext context, LibraryListing library, FilmStore films, ShowStore shows, TitleCategories categories, TitlePosters posters,
            WatchRecord record, PersonDirectory people, SessionStore sessions, CatalogueSearch search, TimeProvider clock, CancellationToken ct) =>
        {
            if (scope is not (Films or Shows or All)) return Error("There is no such page of the library.", StatusCodes.Status404NotFound);
            if (shelf is not null and not (AnimeShelf or GeneralShelf)) return Error("A library page's shelf is anime or general, or none for both.", StatusCodes.Status400BadRequest);
            if (CatalogueSearchEndpoints.Picks(kind, category, anyGenre, decade, length, minRating, language) is not { } picks)
                return CatalogueSearchEndpoints.BadDecade();
            if (WholeFacets.Refusal(picks, DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime)) is { } refusal) return Error(refusal, StatusCodes.Status400BadRequest);
            if (scope != All && (picks.Kind is not null || picks.Category is not null))
                return Error("This page is of one kind, so a filter does not pick one.", StatusCodes.Status400BadRequest);
            var orders = LibraryOrders(scope);
            if (Ordered(orders, sort) is not { } order) return Error($"Sort is {string.Join(", ", orders.Select(o => o.Name))}.", StatusCodes.Status400BadRequest);
            var states = Words(state);
            if (states.FirstOrDefault(s => scope == Shows || FilmStates.All(f => f.Value != s)) is { } unknown)
                return Error($"\"{unknown}\" is no state a title of this page stands in.", StatusCodes.Status400BadRequest);

            bool OnShelf(TitleCategory? category) => shelf is null || (category == TitleCategory.Anime) == (shelf == AnimeShelf);
            var listed = Listed(context, library, films, shows, categories, posters, record, people, sessions, clock);
            var all = new List<LibraryItemView>();
            if (scope != Shows)
                all.AddRange(listed.Where(t => t.Kind != TitleKind.Show && OnShelf(t.Category)).Select(t => new LibraryItemView { Film = t }));
            if (scope != Films)
                all.AddRange(shows.Summaries(listed).Select(s => s with { Category = categories.Of("show", s.ImdbId) })
                    .Where(s => OnShelf(s.Category)).Select(s => new LibraryItemView { Show = s }));
            var total = all.Count;
            // The titles the asking device holds, which only it knows: a film by its title id, a show by its IMDb id.
            var only = Words(held);
            if (only.Count > 0) all = [.. all.Where(i => only.Contains(i.Film?.Id ?? i.Show?.ImdbId ?? ""))];

            var (shown, facets) = Page(all, TraitsOf, StateOf, states, scope == Shows ? [] : [.. FilmStates.Select(s => s.Value)], picks,
                scope == All ? new HashSet<string>() : new HashSet<string> { FacetSections.Kind }, await search.LanguageNamesAsync(ct),
                NameOf, q, order, orders.Select(o => o.Name));
            return Results.Ok(new LibraryPageView { Items = shown, Total = total, Facets = facets });
        }).RequireAction(CinemaActions.Watch);

        // What the person watched as their History shows it: newest first, searched by name and
        // narrowed by what each entry's title is, with the facets of the whole history.
        app.MapGet("/api/me/history/page", async (string? q, string? kind, string? category, [FromQuery] string[]? anyGenre,
            [FromQuery] string[]? decade, [FromQuery] string[]? length, double? minRating, [FromQuery] string[]? language,
            HttpContext context, TitleLibrary titles, LibraryListing library, FilmStore films, ShowStore shows, TitleCategories categories,
            TitlePosters posters, WatchRecord record, PersonDirectory people, SessionStore sessions, CatalogueSearch search,
            TimeProvider clock, CancellationToken ct) =>
        {
            if (PeopleEndpoints.Person(context) is not { } person) return Error("This belongs to a person; sign in as one.", StatusCodes.Status403Forbidden);
            if (CatalogueSearchEndpoints.Picks(kind, category, anyGenre, decade, length, minRating, language) is not { } picks)
                return CatalogueSearchEndpoints.BadDecade();
            if (WholeFacets.Refusal(picks, DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime)) is { } refusal) return Error(refusal, StatusCodes.Status400BadRequest);

            var byId = Listed(context, library, films, shows, categories, posters, record, people, sessions, clock).ToDictionary(t => t.Id, StringComparer.Ordinal);
            var all = PeopleEndpoints.HistoryOf(person.UserId, record, sessions, titles, clock)
                .Select(e => new HistoryItemView { Entry = e, Title = byId.GetValueOrDefault(e.TitleId) }).ToList();
            var (shown, facets) = Page(all, TraitsOf, _ => null, [], [], picks, new HashSet<string>(), await search.LanguageNamesAsync(ct),
                h => h.Entry.Name, q, new ListedOrder<HistoryItemView>("watched", (_, _) => 0), []);
            return Results.Ok(new HistoryPageView { Entries = shown, Total = all.Count, Facets = facets });
        }).RequireAction(CinemaActions.ProfileRead);

        // The person's wants as their page shows them: searched, narrowed and ordered, with the facets
        // of every want and the orders they are served in.
        app.MapGet("/api/me/wants/page", async (string? q, string? sort, [FromQuery] string[]? state,
            string? kind, string? category, [FromQuery] string[]? anyGenre, [FromQuery] string[]? decade, [FromQuery] string[]? length,
            double? minRating, [FromQuery] string[]? language,
            HttpContext context, WantStore wants, WantPresenter present, CatalogueSearch search, TimeProvider clock, CancellationToken ct) =>
        {
            if (PeopleEndpoints.Person(context) is not { } person) return Error("This belongs to a person; sign in as one.", StatusCodes.Status403Forbidden);
            if (CatalogueSearchEndpoints.Picks(kind, category, anyGenre, decade, length, minRating, language) is not { } picks)
                return CatalogueSearchEndpoints.BadDecade();
            if (WholeFacets.Refusal(picks, DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime)) is { } refusal) return Error(refusal, StatusCodes.Status400BadRequest);
            if (Ordered(WantOrders, sort) is not { } order) return Error($"Sort is {string.Join(", ", WantOrders.Select(o => o.Name))}.", StatusCodes.Status400BadRequest);
            var states = Words(state);
            if (states.FirstOrDefault(s => !WantStatesOffered.Contains(s)) is { } unknown)
                return Error($"\"{unknown}\" is no state a want stands in.", StatusCodes.Status400BadRequest);

            var all = await present.ViewsAsync(person.UserId, wants.For(person.UserId), ct);
            var (shown, facets) = Page(all, TraitsOf, w => w.State, states, WantStatesOffered, picks, new HashSet<string>(),
                await search.LanguageNamesAsync(ct), w => w.Title, q, order, WantOrders.Select(o => o.Name));
            return Results.Ok(new WantsPageView { Wants = shown, Total = all.Count, Facets = facets });
        }).RequireAction(CinemaActions.ProfileRead);
    }

    /// <summary>What a history entry is, by the library's title it names: an episode is its show's.</summary>
    private static FacetTraits TraitsOf(HistoryItemView h) => h.Title is { } t
        ? t.Kind == TitleKind.Show
            ? new FacetTraits(FacetSections.Show, t.Category == TitleCategory.Anime, CinemaGenres.Of(t.Details?.Genres ?? []), null, null, t.Details?.Rating, t.Details?.OriginalLanguage)
            : TraitsOf(new LibraryItemView { Film = t })
        : new FacetTraits(h.Entry.Episode is null ? FacetSections.Film : FacetSections.Show, false, [], null, null, null, null);

    /// <summary>
    /// Each library title as <c>/api/titles</c> lists it: what the film database said about it, from what
    /// is kept, its shelf, and the asking person's progress and when it was last played.
    /// </summary>
    public static List<TitleSummary> Listed(HttpContext context, LibraryListing library, FilmStore films, ShowStore shows,
        TitleCategories categories, TitlePosters posters, WatchRecord record, PersonDirectory people, SessionStore sessions, TimeProvider clock)
    {
        var person = PeopleEndpoints.Person(context);
        var viewing = sessions.Viewing();
        var now = clock.GetUtcNow();
        // A show's episodes share its description, its shelf and its poster, so each is read once per
        // title id, and once for the listing.
        var described = new Dictionary<(TitleKind, string?), (FilmFacts? Details, TitleCategory? Category, string? Poster, string? Version)>();
        return library.Titles()
            .Select(t =>
            {
                var key = (t.Kind, t.Film?.ImdbId);
                if (!described.TryGetValue(key, out var d))
                {
                    // A title with no kept poster keeps its own, which is no other title's.
                    var posted = posters.Listed(t);
                    var kept = !ReferenceEquals(posted, t);
                    // An episode is described by its show.
                    d = (t.Kind == TitleKind.Show ? shows.Facts(t.Film?.ImdbId) : films.Facts(t.Film?.ImdbId),
                        categories.Of(t.Kind, t.Film?.ImdbId), kept ? posted.Poster : null, kept ? posted.PosterVersion : null);
                    if (t.Film?.ImdbId is not null) described[key] = d;
                    else return posted with { Details = d.Details, Category = d.Category };
                }
                return d.Poster is null
                    ? t with { Details = d.Details, Category = d.Category }
                    : t with { Details = d.Details, Category = d.Category, Poster = d.Poster, PosterVersion = d.Version };
            })
            .WithPlays(person, record, people, viewing, now);
    }

    /// <summary>The order a route's <c>sort</c> names, the first of <paramref name="orders"/> when it names none; null for a name none has.</summary>
    private static ListedOrder<T>? Ordered<T>(IReadOnlyList<ListedOrder<T>> orders, string? sort) =>
        string.IsNullOrWhiteSpace(sort) ? orders[0] : orders.FirstOrDefault(o => o.Name == sort.Trim());

    /// <summary>Whether a name holds every word of <paramref name="q"/>, in any case; any name holds no words.</summary>
    public static bool Holds(string? name, string? q) =>
        (q ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .All(word => (name ?? "").Contains(word, StringComparison.OrdinalIgnoreCase));

    private static List<string> Words(string[]? values) =>
        [.. (values ?? []).SelectMany(v => v.Split(',')).Select(v => v.Trim()).Where(v => v.Length > 0).Distinct(StringComparer.Ordinal)];

    private static IResult Error(string message, int status) =>
        Results.Json(new ErrorReply(message), ApiJsonContext.Default.ErrorReply, statusCode: status);
}
