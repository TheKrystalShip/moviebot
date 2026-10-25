using System.Globalization;
using TheKrystalShip.MovieBot.Catalogue;

namespace TheKrystalShip.MovieBot.Api.Catalogue;

/// <summary>The days of the year a shelf is shown on, a span that may cross the new year.</summary>
public sealed record ShelfSeason(int FromMonth, int FromDay, int ToMonth, int ToDay)
{
    public bool Holds(DateOnly day)
    {
        var at = day.Month * 100 + day.Day;
        var from = FromMonth * 100 + FromDay;
        var to = ToMonth * 100 + ToDay;
        return from <= to ? at >= from && at <= to : at >= from || at <= to;
    }
}

/// <summary>A shelf of the Discover page: a name and the list query whose titles it shows, in its section.</summary>
public sealed record DiscoverShelf(string Section, string Id, string Title, string Icon, string Query)
{
    public ShelfSeason? Season { get; init; }
}

/// <summary>A shelf as the page draws its head: its list query, the first posters it holds and how many titles; a series how far the person got.</summary>
public sealed record ShelfHead
{
    public required string Id { get; init; }
    public required string Title { get; init; }
    public required string Icon { get; init; }
    public required string Query { get; init; }
    public List<string> Posters { get; init; } = [];
    public int Total { get; init; }

    /// <summary>A series' films the person watched through, and the first they have not.</summary>
    public int? Watched { get; init; }
    public TitleCard? Next { get; init; }
}

public sealed record DiscoverSection(string Id, string Label, string Icon, bool Series, int Count, List<ShelfHead> Shelves);

/// <summary>One row of the page: a shelf, or the moods, the franchises or the browse categories drawn as tiles.</summary>
public sealed record DiscoverRow(string Kind, ShelfHead? Shelf);

public sealed record DiscoverPage(string Spotlight, List<DiscoverRow> Rows, List<DiscoverSection> Sections, List<string> Genres);

/// <summary>
/// The Discover page: its rows for the asking person and its browse categories, every shelf a list
/// query over the catalogue. The shelves the same for everybody are made of the film database's own ids
/// (keywords, companies, networks and people), which the catalogue keeps, and Wikidata's items; their
/// heads (posters and counts) are read every <see cref="Every"/> in the background, so the page asks
/// nothing of the catalogue for them. The person's own shelves (what is like what they watched, what is
/// popular and ready here, the series they are partway through) are made when the page is asked for.
/// </summary>
public sealed class DiscoverIndex(CatalogueQueries queries, CatalogueState state, TimeProvider clock, ILogger<DiscoverIndex> logger) : BackgroundService
{
    private static readonly TimeSpan Every = TimeSpan.FromMinutes(30);

    /// <summary>How many posters a head carries: as many as a tile shows.</summary>
    private const int Posters = 4;

    /// <summary>How many of the series the page's row of franchises holds, and how many the category lists.</summary>
    public const int RowSeries = 24;
    public const int SeriesListed = 400;

    private const int LikeShelves = 2;

    public static readonly IReadOnlyList<(string Id, string Label, string Icon)> Sections =
    [
        ("moods", "Moods", "smile"),
        ("movies", "Movies", "trophy"),
        ("shows", "TV Shows", "tv"),
        ("genres", "Genres", "shapes"),
        ("themes", "Themes", "tags"),
        ("awards", "Awards", "award"),
        ("adaptations", "Adaptations", "book-open"),
        ("places", "Places", "map-pin"),
        ("world", "World cinema", "globe"),
        ("franchises", "Franchises", "library"),
        ("studios", "Studios", "clapperboard"),
        ("directors", "Directors", "megaphone"),
    ];

    private const string ForChildren = "notGenre=Animation,Family";

    /// <summary>The directors' shelves, by the film database's person ids, which the catalogue resolves to its own.</summary>
    private static readonly (string Id, string Name, int Person)[] Directors =
    [
        ("christopher-nolan", "Christopher Nolan", 525), ("denis-villeneuve", "Denis Villeneuve", 137427), ("quentin-tarantino", "Quentin Tarantino", 138),
        ("stanley-kubrick", "Stanley Kubrick", 240), ("hayao-miyazaki", "Hayao Miyazaki", 608), ("martin-scorsese", "Martin Scorsese", 1032),
        ("steven-spielberg", "Steven Spielberg", 488), ("david-fincher", "David Fincher", 7467), ("bong-joon-ho", "Bong Joon Ho", 21684),
        ("greta-gerwig", "Greta Gerwig", 45400), ("ridley-scott", "Ridley Scott", 578), ("wes-anderson", "Wes Anderson", 5655),
        ("pedro-almodovar", "Pedro Almodóvar", 309), ("guillermo-del-toro", "Guillermo del Toro", 10828), ("alfred-hitchcock", "Alfred Hitchcock", 2636),
        ("akira-kurosawa", "Akira Kurosawa", 5026), ("park-chan-wook", "Park Chan-wook", 10099), ("sofia-coppola", "Sofia Coppola", 1769),
        ("joel-coen", "Joel Coen", 1223), ("edgar-wright", "Edgar Wright", 11090), ("jordan-peele", "Jordan Peele", 291263),
    ];

    /// <summary>Every shelf the same for everybody but the directors' and the franchises', in the order its category shows it.</summary>
    public static readonly IReadOnlyList<DiscoverShelf> Fixed = Shelves();

    private static List<DiscoverShelf> Shelves()
    {
        var shelves = new List<DiscoverShelf>();
        void Add(string section, string id, string title, string icon, string query, ShelfSeason? season = null) =>
            shelves.Add(new DiscoverShelf(section, id, title, icon, query) { Season = season });

        Add("for-you", "spooky-season", "Spooky season", "ghost", "kind=film&keyword=k3335&minVotes=100", new ShelfSeason(10, 1, 10, 31));
        Add("for-you", "christmas", "Christmas movies", "gift", "kind=film&keyword=k207317&minVotes=200", new ShelfSeason(12, 1, 12, 26));
        Add("for-you", "valentines", "Valentine's", "heart", "kind=film&genre=Romance&minRating=7&minVotes=1000", new ShelfSeason(2, 1, 2, 14));
        Add("for-you", "summer-blockbusters", "Summer blockbusters", "sun", "kind=film&genre=Action,Adventure&minVotes=5000", new ShelfSeason(6, 21, 8, 31));
        Add("for-you", "trending-films", "Popular movies this week", "flame", "ranking=trending-films");
        Add("for-you", "trending-shows", "Popular shows this week", "flame", "ranking=trending-shows");
        Add("for-you", "in-cinemas", "In cinemas now", "ticket", "ranking=now-playing");
        Add("for-you", "coming-soon", "Coming soon", "calendar-clock", "ranking=upcoming");

        Add("moods", "sunday-chill", "Sunday chill", "coffee",
            "kind=film&genre=Comedy&notGenre=Animation,Family,Horror,Thriller,Crime,Action&minRating=6.8&minVotes=1000&maxRuntime=115");
        Add("moods", "date-night", "Date night", "heart", "kind=film&genre=Romance,Comedy&minRating=6.3&minVotes=500");
        Add("moods", "family-night", "Family movie night", "house", "kind=film&genre=Family&minRating=6.5&minVotes=1000");
        Add("moods", "mind-benders", "Mind-benders", "brain", "kind=film&anyKeyword=k326438,k157171,k374835,k12565&minRating=7&minVotes=1000");
        Add("moods", "edge-of-your-seat", "Edge of your seat", "heart-pulse", "kind=film&genre=Thriller&minRating=7&minVotes=2000");
        Add("moods", "feel-good", "Feel-good", "sun", "kind=film&genre=Comedy,Drama&notGenre=Horror,Thriller,Crime,War&minRating=7.2&minVotes=1500");
        Add("moods", "good-cry", "Have a good cry", "droplets", "kind=film&keyword=k156924&minVotes=50");
        Add("moods", "laugh-out-loud", "Laugh out loud", "laugh", $"kind=film&genre=Comedy&{ForChildren}&minRating=7&minVotes=2000");
        Add("moods", "short-and-sweet", "Short and sweet", "timer", "kind=film&minRuntime=60&maxRuntime=90&minRating=7&minVotes=1000");
        Add("moods", "epic-weekend", "Epic weekend", "hourglass", "kind=film&minRuntime=150&minRating=7.5&minVotes=2000&sort=rating");
        Add("moods", "so-bad-its-good", "So bad it's good", "thumbs-down", "kind=film&maxRating=5&minVotes=1500&sort=votes");
        Add("moods", "comfort-shows", "Comfort shows", "sofa", "kind=show&genre=Comedy&notGenre=Animation&minRating=7.5&minVotes=500");

        Add("movies", "top-rated", "Top rated of all time", "trophy", "kind=film&minVotes=5000&sort=rating");
        Add("movies", "hidden-gems", "Hidden gems", "gem", "kind=film&minRating=7.3&minVotes=300&maxVotes=1500&notGenre=Documentary,Music,Animation&sort=rating");
        Add("movies", "classics", "Classics", "film", "kind=film&to=1979&minVotes=1000&sort=rating");
        foreach (var decade in new[] { 1970, 1980, 1990, 2000, 2010, 2020 })
        {
            var name = decade < 2000 ? $"{decade % 100}s" : $"{decade}s";
            Add("movies", $"best-of-{decade}s", $"Best of the {name}", "calendar", $"kind=film&decade={decade}&minVotes=1000&sort=rating");
        }

        Add("shows", "top-rated-shows", "Top rated series", "trophy", "kind=show&minVotes=2000&sort=rating");
        Add("shows", "binge-worthy", "Binge-worthy", "list-video", "kind=show&status=Ended&notGenre=Animation&minRating=8&minVotes=500&sort=rating");
        Add("shows", "miniseries", "Miniseries", "layers", "kind=show&maxSeasons=1&status=Ended&minRating=7.5&minVotes=200&sort=rating");
        Add("shows", "crime-series", "Crime series", "siren", "kind=show&genre=Crime&notGenre=Documentary&minVotes=300");
        Add("shows", "sci-fi-fantasy-series", "Sci-fi and fantasy series", "rocket", "kind=show&anyGenre=Sci-Fi,Fantasy&notGenre=Animation&minVotes=300");
        Add("shows", "sitcoms", "Sitcoms", "laugh", "kind=show&genre=Comedy&keyword=k193171&minVotes=200");
        Add("shows", "true-crime", "True crime", "fingerprint", "kind=show&genre=Documentary&keyword=k33722&minVotes=20");
        Add("shows", "k-dramas", "K-dramas", "drama", "kind=show&language=ko&genre=Drama&minVotes=100");
        foreach (var (id, name, network, votes) in new[]
                 {
                     ("hbo", "HBO", 49, 200), ("netflix", "Netflix originals", 213, 500), ("apple-tv", "Apple TV", 2552, 100),
                     ("prime-video", "Prime Video", 1024, 100), ("disney-plus", "Disney+", 2739, 100), ("bbc-one", "BBC One", 4, 100), ("fx", "FX", 88, 100),
                 })
            Add("shows", id, name, "tv", $"kind=show&company=s{network}&minVotes={votes}");

        foreach (var (id, name, icon, genre) in new[]
                 {
                     ("action", "Action", "swords", "Action"), ("adventure", "Adventure", "compass", "Adventure"), ("comedy", "Comedy", "laugh", "Comedy"),
                     ("crime", "Crime", "siren", "Crime"), ("drama", "Drama", "drama", "Drama"), ("horror", "Horror", "skull", "Horror"),
                     ("science-fiction", "Science fiction", "rocket", "Sci-Fi"), ("thriller", "Thriller", "heart-pulse", "Thriller"),
                     ("romance", "Romance", "heart", "Romance"), ("mystery", "Mystery", "search", "Mystery"), ("fantasy", "Fantasy", "wand-sparkles", "Fantasy"),
                     ("war", "War", "shield", "War"), ("western", "Western", "mountain", "Western"), ("history", "History", "landmark", "History"),
                     ("animation", "Animation", "palette", "Animation"), ("documentary", "Documentary", "video", "Documentary"), ("music", "Music", "music", "Music"),
                 })
            Add("genres", id, name, icon, $"kind=film&genre={genre}{(genre == "Animation" ? "" : "&" + ForChildren)}&minVotes=300");

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
            Add("themes", id, name, icon, $"kind=film&keyword=k{keyword}&minVotes=200");
        Add("themes", "neo-noir", "Neo-noir", "moon", "fact=P136:Q2421031&sort=votes");
        Add("themes", "film-noir", "Film noir", "moon", "fact=P136:Q185867&sort=votes");
        Add("themes", "spaghetti-westerns", "Spaghetti westerns", "mountain", "fact=P136:Q212781&sort=votes");
        Add("themes", "disaster-films", "Disaster films", "siren", "fact=P136:Q846544&sort=votes");
        Add("themes", "world-war-ii", "World War II", "shield", "fact=P921:Q362&sort=votes");

        Add("awards", "best-picture-winners", "Best Picture winners", "trophy", "fact=P166:Q102427&sort=newest");
        Add("awards", "best-picture-nominees", "Best Picture nominees", "award", "fact=P1411:Q102427&sort=newest");
        Add("awards", "palme-dor-winners", "Palme d'Or winners", "medal", "fact=P166:Q179808&sort=newest");

        foreach (var (id, name, item) in new[]
                 {
                     ("stephen-king", "Stephen King", "Q39829"), ("jane-austen", "Jane Austen", "Q36322"), ("agatha-christie", "Agatha Christie", "Q35064"),
                     ("shakespeare", "Shakespeare", "Q692"), ("charles-dickens", "Charles Dickens", "Q5686"), ("arthur-conan-doyle", "Arthur Conan Doyle", "Q35610"),
                     ("jules-verne", "Jules Verne", "Q33977"),
                 })
            Add("adaptations", id, $"{name} adaptations", "book-open", $"fact=P144/P50:{item}&sort=newest");

        foreach (var (id, name, item) in new[] { ("paris", "Paris", "Q90"), ("new-york", "New York", "Q60"), ("london", "London", "Q84"), ("tokyo", "Tokyo", "Q1490") })
            Add("places", $"set-in-{id}", $"Set in {name}", "map-pin", $"fact=P840:{item}&sort=votes");

        Add("world", "korean", "Korean cinema", "globe", "kind=film&language=ko&minVotes=300");
        Add("world", "japanese", "Japanese cinema", "globe", "kind=film&language=ja&notGenre=Animation&minVotes=300");
        Add("world", "french", "French cinema", "globe", "kind=film&language=fr&minVotes=300");
        Add("world", "italian-classics", "Italian classics", "globe", "kind=film&language=it&to=1979&minVotes=200&sort=rating");
        Add("world", "german", "German cinema", "globe", "kind=film&language=de&minVotes=300");
        Add("world", "nordic-noir", "Nordic noir", "globe", "kind=film&language=sv,da,no,fi,is&anyGenre=Crime,Thriller&minVotes=100");
        Add("world", "bollywood", "Bollywood", "globe", "kind=film&language=hi&minVotes=100");
        Add("world", "chinese", "Chinese and Hong Kong cinema", "globe", "kind=film&language=zh,cn&minVotes=200");

        foreach (var (id, name, company) in new[]
                 {
                     ("studio-ghibli", "Studio Ghibli", 10342), ("pixar", "Pixar", 3), ("a24", "A24", 41077), ("blumhouse", "Blumhouse", 3172),
                     ("aardman", "Aardman", 297), ("marvel-studios", "Marvel Studios", 420), ("disney-animation", "Walt Disney Animation Studios", 6125),
                     ("neon", "Neon", 90733),
                 })
            Add("studios", id, name, "building", $"kind=film&company=s{company}&minVotes=50");

        return shelves;
    }

    /// <summary>
    /// The heads of the shelves the same for everybody and every franchise's titles, as last read. Only
    /// the background pass reads them: on a catalogue being filled a pass takes minutes, so a request
    /// waits for the first rather than starting its own.
    /// </summary>
    private volatile Shared? _shared;

    private readonly TaskCompletionSource<Shared> _first = new(TaskCreationOptions.RunContinuationsAsynchronously);

    private sealed record Shared(List<(DiscoverShelf Shelf, ShelfHead Head)> Shelves, List<(FranchiseCard Card, List<FranchiseEntry> Titles)> Franchises, DateTimeOffset At);

    /// <summary>What the background pass last read, or the first pass once it has.</summary>
    private Task<Shared> Current() => _shared is { } shared ? Task.FromResult(shared) : _first.Task;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                if (state.Problem() is null)
                {
                    try
                    {
                        var started = clock.GetTimestamp();
                        var read = await Task.Run(Read, stoppingToken);
                        _shared = read;
                        _first.TrySetResult(read);
                        logger.LogInformation("Discover: {Shelves} shelves and {Franchises} franchises read in {Seconds:0.0}s.",
                            read.Shelves.Count, read.Franchises.Count, clock.GetElapsedTime(started).TotalSeconds);
                    }
                    catch (Microsoft.Data.Sqlite.SqliteException ex)
                    {
                        logger.LogWarning("The Discover shelves could not be read: {Reason}", ex.Message);
                    }
                }
                await Task.Delay(Every, clock, stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
    }

    /// <summary>Every shared shelf's head, the directors' resolved to the catalogue's people, a shelf with nothing on it left out.</summary>
    private Shared Read()
    {
        var people = queries.PeopleByTmdb(Directors.Select(d => d.Person).ToList());
        var shelves = Fixed.Concat(Directors
                .Where(d => people.ContainsKey(d.Person))
                .Select(d => new DiscoverShelf("directors", d.Id, $"Directed by {d.Name}", "megaphone", $"person={people[d.Person]}&role=directing&sort=rating")))
            .Select(s => (Shelf: s, Head: HeadOf(s.Id, s.Title, s.Icon, s.Query)))
            .Where(s => s.Head is not null)
            .Select(s => (s.Shelf, s.Head!))
            .ToList();
        // A series' tile is a strip of its titles' posters in their order, and how far a person got is read
        // against the same list, so every franchise's titles are read at once.
        var entries = queries.FranchiseEntries();
        var franchises = queries.Franchises(1, SeriesListed).Franchises
            .Select(f => (f, entries.GetValueOrDefault(f.Id) ?? []))
            .ToList();
        return new Shared(shelves, franchises, clock.GetUtcNow());
    }

    /// <summary>A shelf's head: its first posters and how many titles it holds; null when it holds none.</summary>
    private ShelfHead? HeadOf(string id, string title, string icon, string query, TitleQuery? narrowed = null)
    {
        var parsed = narrowed ?? Parse(query);
        var page = queries.Titles(parsed with { Size = Posters });
        if (page.Total == 0) return null;
        return new ShelfHead
        {
            Id = id, Title = title, Icon = icon, Query = query, Total = page.Total,
            Posters = page.Titles.Select(t => t.Poster).OfType<string>().ToList(),
        };
    }

    private static TitleQuery Parse(string query)
    {
        var pairs = query.Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Select(p => p.Split('=', 2))
            .GroupBy(p => p[0], p => p.Length > 1 ? Uri.UnescapeDataString(p[1]) : "")
            .ToDictionary(g => g.Key, g => (IReadOnlyList<string>)g.ToList());
        var (parsed, refusal) = TitleQuery.Parse(name => pairs.GetValueOrDefault(name) ?? []);
        return parsed ?? throw new InvalidOperationException($"The shelf query {query} is refused: {refusal}");
    }

    /// <summary>The page for one person: their rows, the shared shelves' categories, and the genres the filter offers.</summary>
    public async Task<DiscoverPage> PageAsync(string? userId, ViewerJoin viewer)
    {
        var shared = await Current();
        var today = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);
        var shown = shared.Shelves.Where(s => s.Shelf.Season is null || s.Shelf.Season.Holds(today)).ToList();
        ShelfHead? Named(string id) => shown.FirstOrDefault(s => s.Shelf.Id == id).Head;

        var series = shared.Franchises.Select(f => SeriesHead(f, viewer)).ToList();
        var sections = new List<DiscoverSection>();
        foreach (var (id, label, icon) in Sections)
        {
            var heads = id == "franchises" ? series : shown.Where(s => s.Shelf.Section == id).Select(s => s.Head).ToList();
            if (heads.Count > 0) sections.Add(new DiscoverSection(id, label, icon, id == "franchises", heads.Count, id == "franchises" ? Partway(heads).Take(RowSeries).ToList() : heads));
        }

        var rows = new List<DiscoverRow>();
        void Row(ShelfHead? head)
        {
            if (head is not null) rows.Add(new DiscoverRow("shelf", head));
        }
        foreach (var (kind, imdb, name) in viewer.LastWatched(userId, LikeShelves))
            Row(HeadOf($"like-{imdb}", $"Because you watched {name}", "sparkles", $"similarTo={imdb}"));
        foreach (var seasonal in shown.Where(s => s.Shelf.Season is not null)) Row(seasonal.Head);
        rows.Add(new DiscoverRow("moods", null));
        Row(Named("trending-films"));
        rows.Add(new DiscoverRow("franchises", null));
        if (viewer.PopularHere(clock.GetUtcNow()) is { Count: > 0 } popular)
            Row(HeadOf("popular-here", "Popular here", "users", "here=watched", new TitleQuery { Ordered = popular }));
        Row(Named("trending-shows"));
        rows.Add(new DiscoverRow("browse", null));
        var directors = shown.Where(s => s.Shelf.Section == "directors").ToList();
        if (directors.Count > 0) Row(directors[today.DayOfYear % directors.Count].Head);
        Row(Named("in-cinemas"));
        Row(HeadOf("new-releases", "New releases", "sparkles",
            $"released={today.AddDays(-45).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}&releasedBefore={today.AddDays(1).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}&minVotes=20&sort=newest"));
        Row(Named("coming-soon"));
        if (viewer.ReadyHere() is { Count: > 0 } ready)
            Row(HeadOf("ready-here", "Ready to watch", "play", "here=ready", new TitleQuery { Ordered = ready }));

        var spotlight = shown.FirstOrDefault(s => s.Shelf.Season is not null).Shelf?.Id ?? "trending-films";
        return new DiscoverPage(spotlight, rows, sections, queries.Genres());
    }

    /// <summary>The franchises for one person, a page at a time: the series they are partway through first, the ones they finished last.</summary>
    public async Task<(List<ShelfHead> Series, int Total)> FranchisesAsync(ViewerJoin viewer, int page, int size)
    {
        var shared = await Current();
        var ordered = Partway(shared.Franchises.Select(f => SeriesHead(f, viewer)).ToList()).ToList();
        return (ordered.Skip((page - 1) * size).Take(size).ToList(), ordered.Count);
    }

    /// <summary>A series' head, with how far the person got through its films and the first they have not watched.</summary>
    private ShelfHead SeriesHead((FranchiseCard Card, List<FranchiseEntry> Titles) series, ViewerJoin viewer)
    {
        var franchise = series.Card;
        var posters = series.Titles.Select(t => t.Poster).OfType<string>().Take(Posters).ToList();
        var query = $"franchise={franchise.Id}&sort=order";
        var watched = viewer.Watched();
        ShelfHead head = new()
        {
            Id = $"franchise-{franchise.Id}", Title = franchise.Name, Icon = "library", Query = query, Total = franchise.Titles,
            Posters = posters.Count > 0 ? posters : franchise.Poster is { } poster ? [poster] : [],
        };
        if (watched.Count == 0) return head;
        var seen = series.Titles.Count(t => watched.Contains(t.ImdbId));
        if (seen == 0) return head;
        var next = series.Titles.FirstOrDefault(t => t.Kind == "film" && !watched.Contains(t.ImdbId));
        return head with { Watched = seen, Next = next is null ? null : queries.CardsOf([next.ImdbId]).FirstOrDefault() };
    }

    /// <summary>Series the person is partway through first, then the ones untouched, then the ones they finished.</summary>
    private static IEnumerable<ShelfHead> Partway(List<ShelfHead> series) =>
        series.OrderBy(s => s.Watched is null ? 1 : s.Next is null ? 2 : 0);
}
