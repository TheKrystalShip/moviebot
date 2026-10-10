using System.Net;
using System.Text.Json;
using TheKrystalShip.Agent.Plugin;
using TheKrystalShip.Agent.Plugin.Testing;
using TheKrystalShip.MovieBot.Assistant;
using TheKrystalShip.MovieBot.Assistant.Api;
using TheKrystalShip.MovieBot.Assistant.Tools;

namespace TheKrystalShip.MovieBot.Tests.Assistant;

/// <summary>
/// The film database and the library as the API answers them, in an invented world: a person with the
/// same name twice, a silent film and ones that are not, early-2000s horror, announced
/// titles with and without an IMDb id, a person's history, lists and follows. It is a table of answers
/// behind the same HTTP client the tools use, so a recording of the live API replays the same way.
/// </summary>
internal static class CinemaWorld
{
    public const string Today = "2026-10-09";
    public const string Token = "a-person's-access-token";

    public static TitleHit Hit(
        string id, string title, int year, string kind = "film", string[]? genres = null, double rating = 7.2, int votes = 5400,
        bool inLibrary = false, bool watched = false, string? plot = null, string[]? madeBy = null, string[]? starring = null) =>
        new()
        {
            ImdbId = id.Length == 0 ? null : id, TmdbId = Math.Abs(HashCode.Combine(id, title)) % 100000, Kind = kind,
            Title = title, Year = year, ReleaseDate = $"{year}-03-05", Genres = [.. genres ?? ["Drama"]], Rating = rating,
            Votes = votes, Plot = plot, MadeBy = [.. madeBy ?? []], Starring = [.. starring ?? []],
            PosterUrl = id.Length == 0 ? null : $"/glances/{id}/poster.jpg", InLibrary = inLibrary, Watched = watched,
            LibraryId = inLibrary ? title.ToLowerInvariant().Replace(' ', '-') + $"-{year}" : null,
        };

    public static Credit Credit(
        string? id, string title, string? date, string status, string kind = "film", string? role = null, bool watched = false) =>
        new()
        {
            TmdbId = Math.Abs(title.GetHashCode()) % 100000, ImdbId = id, Kind = kind, Title = title,
            Year = date is null ? null : int.Parse(date[..4]), ReleaseDate = date, Status = status, Role = role, Rating = date is null ? null : 6.8,
            Votes = date is null ? 0 : 1200, PosterUrl = id is null ? null : $"/glances/{id}/poster.jpg", Watched = watched,
        };

    public static readonly TitleHit NightHarbor = Hit("tt1000001", "Night Harbor", 1995, genres: ["Crime", "Drama"], rating: 8.3, votes: 700000,
        inLibrary: true, watched: true, plot: "A detective hunts a thief across a port city.", madeBy: ["Vera Lund"], starring: ["Ada Calloway", "Theo Marsh"]);

    public static readonly TitleHit NightHarborShow = Hit("tt1000002", "Night Harbor", 2012, "show", ["Crime"], 6.9, 3100,
        plot: "Dockworkers uncover a smuggling ring.", madeBy: ["Imre Voss"], starring: ["Nell Aldous"]);

    public static readonly TitleHit LanternKeeper = Hit("tt1000003", "The Lantern Keeper", 2019, genres: ["Drama", "Mystery"], rating: 7.5, votes: 61000,
        plot: "Two keepers lose their grip on a lighthouse.", madeBy: ["Rowan Pike"], starring: ["Julian Rook", "Willa Dane"]);

    public static readonly TitleHit HollowHouse = Hit("tt1000010", "Hollow House", 2002, genres: ["Horror"], rating: 7.2, votes: 410000,
        watched: true, inLibrary: true, plot: "A family hears the walls breathe.", madeBy: ["Dara Quill"], starring: ["Mina Ford", "Cass Hale"]);

    public static readonly TitleHit QuietRoom = Hit("tt1000011", "The Quiet Room", 2003, genres: ["Horror", "Mystery"], rating: 6.8, votes: 2300,
        plot: "A nurse finds a ward that does not exist.", madeBy: ["Oto Brandt"], starring: ["Pia Lowe"]);

    public static readonly TitleHit AshRoad = Hit("tt1000012", "Ash Road", 2004, genres: ["Horror"], rating: 6.6, votes: 900,
        inLibrary: true, plot: "A hitchhiker is not what she seems.", madeBy: ["Dara Quill"], starring: ["Cass Hale"]);

    public static readonly TitleHit Unpaged = Hit("", "Untitled Harbour Project", 2027, genres: ["Drama"], rating: 0, votes: 0);

    /// <summary>A film's page, with its keywords.</summary>
    public static readonly FilmDetails LanternPage = new()
    {
        ImdbId = "tt1000003", Title = "The Lantern Keeper", Year = 2019, ReleaseDate = "2019-10-18", RuntimeMinutes = 109,
        Certification = "R", Genres = ["Drama", "Mystery"], Overview = "Two keepers lose their grip on a lighthouse.",
        Tagline = "The light is the least of it.", Keywords = ["silent film", "lighthouse", "isolation"],
        Rating = 7.5, Votes = 61000, Directors = ["Rowan Pike"], Writers = ["Rowan Pike", "Sol Pike"],
        Cast = [new() { Name = "Julian Rook", Character = "Thomas" }, new() { Name = "Willa Dane", Character = "Ephraim" }],
        OriginalLanguage = "en", Countries = ["United States"], PosterUrl = "/films/tt1000003/poster.jpg", LibraryId = null, Watched = false,
    };

    /// <summary>A film with keywords, none of them a look.</summary>
    public static readonly FilmDetails CoastalPage = new()
    {
        ImdbId = "tt1000004", Title = "Coastal Hours", Year = 2021, ReleaseDate = "2021-06-11", RuntimeMinutes = 98,
        Genres = ["Drama"], Overview = "Three summers at one cafe.", Keywords = ["friendship", "summer"],
        Rating = 6.9, Votes = 800, Directors = ["Ines Varga"], Cast = [new() { Name = "Ada Calloway", Character = "Lia" }],
        OriginalLanguage = "es", Countries = ["Spain"], PosterUrl = "/films/tt1000004/poster.jpg", LibraryId = "coastal-hours-2021", Watched = true,
    };

    /// <summary>A film whose keywords have not been asked for yet.</summary>
    public static readonly FilmDetails ColdFramePage = new()
    {
        ImdbId = "tt1000005", Title = "Cold Frame", Year = 2023, ReleaseDate = "2023-01-20", Genres = ["Thriller"], Keywords = [],
        Rating = 6.1, Votes = 400, Directors = ["Maks Ilves"], Cast = [],
    };

    public static readonly ShowDetails SaltLine = new()
    {
        ImdbId = "tt1000006", Title = "The Salt Line", FirstYear = 2020, LastYear = 2022, Status = "Ended", Genres = ["Drama"],
        Overview = "A border town runs on salt.", Keywords = ["border"], Rating = 7.9, Votes = 21000, Creators = ["Imre Voss"],
        Cast = [new() { Name = "Nell Aldous", Character = "Marta" }], Networks = ["Northlight"], PosterUrl = "/shows/tt1000006/poster.jpg",
        Seasons =
        [
            new() { Number = 1, AirDate = "2020-02-02", Episodes = [new() { Number = 1, TitleId = "salt-s01e01" }, new() { Number = 2 }] },
            new() { Number = 2, AirDate = "2021-02-02", Episodes = [new() { Number = 1 }] },
        ],
        Follow = new FollowDetails { Since = new DateTimeOffset(2026, 1, 4, 0, 0, 0, TimeSpan.Zero) },
    };

    /// <summary>The world, and the API client that asks it as one person.</summary>
    public static ReplayHandler Build()
    {
        var world = new ReplayHandler();

        world.Answer("/api/catalogue/search?q=night harbor", new SearchResult { Query = "night harbor", Titles = [NightHarbor, NightHarborShow] });
        world.Answer("/api/catalogue/search?q=night harbor&year=1995", new SearchResult { Query = "night harbor", Titles = [NightHarbor] });
        world.Answer("/api/catalogue/search?q=night harbor&kind=film", new SearchResult { Query = "night harbor", Titles = [NightHarbor] });
        world.Answer("/api/catalogue/search?q=the lantern keeper", new SearchResult { Titles = [LanternKeeper] });
        world.Answer("/api/catalogue/search?q=the lantern keeper&year=2019", new SearchResult { Titles = [LanternKeeper] });
        world.Answer("/api/catalogue/search?q=lantern", new SearchResult { Titles = [LanternKeeper] });
        world.Answer("/api/catalogue/search?q=coastal hours&year=2021",
            new SearchResult { Titles = [Hit("tt1000004", "Coastal Hours", 2021, inLibrary: true, watched: true)] });
        world.Answer("/api/catalogue/search?q=cold frame", new SearchResult { Titles = [Hit("tt1000005", "Cold Frame", 2023)] });
        world.Answer("/api/catalogue/search?q=the salt line", new SearchResult { Titles = [Hit("tt1000006", "The Salt Line", 2020, "show")] });
        world.Answer("/api/catalogue/search?q=nothing like it", new SearchResult { Query = "nothing like it", Titles = [] });
        world.Answer("/api/catalogue/search?q=harbour", new SearchResult { Titles = [Unpaged, NightHarbor] });
        world.Answer("/api/catalogue/search?q=unborn harbour", new SearchResult { Titles = [Unpaged] });
        world.Answer("/api/catalogue/search?q=down", "{\"error\":\"The film database could not be asked.\"}", 503);

        world.Answer("/api/films/tt1000001", new FilmDetails
        {
            ImdbId = "tt1000001", Title = "Night Harbor", Year = 1995, ReleaseDate = "1995-12-15", Genres = ["Crime", "Drama"],
            Keywords = ["heist"], Rating = 8.3, Votes = 700000, Directors = ["Vera Lund"], LibraryId = "night-harbor-1995", Watched = true,
        });
        world.Answer("/api/films/tt1000003", LanternPage);
        world.Answer("/api/films/tt1000004", CoastalPage);
        world.Answer("/api/films/tt1000005", ColdFramePage);
        world.Answer("/api/films/tt1000006", "{\"error\":\"The film database holds no film under tt1000006.\"}", 404);
        world.Answer("/api/shows/tt1000006", SaltLine);

        world.Answer("/api/catalogue/people?q=ada calloway", new PeopleResult
        {
            Query = "ada calloway",
            People =
            [
                new() { PersonId = 501, Name = "Ada Calloway", KnownFor = "Acting", KnownForTitles = ["Night Harbor (1995)", "Coastal Hours (2021)"] },
                new() { PersonId = 502, Name = "Ada Calloway", KnownFor = "Directing", KnownForTitles = ["Small Rooms (2011)"] },
            ],
        });
        world.Answer("/api/catalogue/people?q=julian rook", new PeopleResult
        {
            People = [new() { PersonId = 601, Name = "Julian Rook", KnownFor = "Acting", KnownForTitles = ["The Lantern Keeper (2019)"] }],
        });
        world.Answer("/api/catalogue/people?q=rook", new PeopleResult
        {
            People =
            [
                new() { PersonId = 601, Name = "Julian Rook", KnownFor = "Acting", KnownForTitles = ["The Lantern Keeper (2019)"] },
                new() { PersonId = 603, Name = "Edda Rookwood", KnownFor = "Writing", KnownForTitles = [] },
            ],
        });
        world.Answer("/api/catalogue/people?q=nobody here", new PeopleResult { Query = "nobody here", People = [] });

        world.Answer("/api/catalogue/people/501/credits?from=2026&to=2026&role=acting", new PersonCredits
        {
            PersonId = 501, Name = "Ada Calloway", KnownFor = "Acting", AsOf = Today, Total = 7,
            Credits =
            [
                Credit("tt1000030", "Mother Tongue", "2026-04-16", "released", role: "Anna"),
                Credit("tt1000031", "The Wedding Party 2", "2026-04-29", "released", role: "Lead"),
                Credit("tt1000032", "The Long Voyage", "2026-07-15", "released", role: "Helen", watched: true),
                Credit("tt1000033", "Winter Street", "2026-12-18", "upcoming", role: "Carol"),
                Credit("tt1000034", "Salt Marsh", null, "announced"),
                Credit(null, "Untitled Harbour Sting", null, "announced"),
                Credit("tt1000035", "Fear Not", null, "announced", kind: "show"),
            ],
        });
        world.Answer("/api/catalogue/people/502/credits?role=directing", new PersonCredits
        {
            PersonId = 502, Name = "Ada Calloway", KnownFor = "Directing", AsOf = Today, Total = 1,
            Credits = [Credit("tt1000040", "Small Rooms", "2011-05-05", "released", role: "Director")],
        });
        world.Answer("/api/catalogue/people/501/credits", new PersonCredits
        {
            PersonId = 501, Name = "Ada Calloway", KnownFor = "Acting", AsOf = Today, Total = 120,
            Credits = [Credit("tt1000001", "Night Harbor", "1995-03-05", "released", role: "Ruth")],
        });
        world.Answer("/api/catalogue/people/501/credits?from=2030&to=2030", new PersonCredits
        {
            PersonId = 501, Name = "Ada Calloway", KnownFor = "Acting", AsOf = Today, Total = 0, Credits = [],
        });
        world.Answer("/api/catalogue/people/501/credits?from=2027&to=2026", "{\"error\":\"from is after to.\"}", 400);
        world.Answer("/api/catalogue/people/601/credits", new PersonCredits
        {
            PersonId = 601, Name = "Julian Rook", KnownFor = "Acting", AsOf = Today, Total = 4,
            Credits =
            [
                Credit("tt1000003", "The Lantern Keeper", "2019-10-18", "released", role: "Thomas"),
                Credit("tt1000050", "The Last Ferry", "2024-03-01", "released", role: "Owen"),
                Credit("tt1000051", "Winter Quarters", null, "announced"),
                Credit("tt1000052", "Cinder", "2027-02-12", "upcoming", role: "Pell"),
            ],
        });

        world.Answer("/api/catalogue/discover?kind=film&genre=Horror&from=2000&to=2005&goodOnly=true", new DiscoverResult
        {
            Kind = "film",
            Resolved = new DiscoverResolved { Genres = ["Horror"], MinRating = 6.5, MinVotes = 200, Sort = "popular" },
            Titles = [HollowHouse, QuietRoom, AshRoad],
        });
        world.Answer("/api/catalogue/discover?kind=show&goodOnly=true&category=anime", new DiscoverResult
        {
            Kind = "show",
            Resolved = new DiscoverResolved { Genres = ["Animation"], Language = "Japanese", MinRating = 6.5, MinVotes = 200, Sort = "popular" },
            Titles = [Hit("tt1000020", "Paper Lanterns", 2019, "show", ["Animation", "Drama"]) with { Category = "anime", OriginalLanguage = "ja" }],
        });
        world.Answer("/api/catalogue/added?kind=show&category=anime&limit=20", new LibraryAdditions());
        world.Answer("/api/catalogue/discover?kind=film&genre=Horror&exclude=Crime&includeFamily=true&goodOnly=true", new DiscoverResult
        {
            Kind = "film",
            Resolved = new DiscoverResolved { Genres = ["Horror"], Excluded = ["Crime"], MinRating = 6.5, MinVotes = 200, Sort = "popular" },
            Titles = [HollowHouse],
        });
        world.Answer("/api/catalogue/discover?kind=film&genre=Horror&genre=Mystery&goodOnly=false&sort=rating", new DiscoverResult
        {
            Resolved = new DiscoverResolved { Genres = ["Horror", "Mystery"], Sort = "rating" },
            Titles = [QuietRoom],
        });
        world.Answer("/api/catalogue/discover?kind=film&about=silent film&with=Julian Rook&goodOnly=false", new DiscoverResult
        {
            Resolved = new DiscoverResolved
            {
                People = [new ResolvedPerson(601, "Julian Rook", "Acting")], Keywords = ["silent film"], Sort = "popular",
            },
            Titles = [LanternKeeper],
        });
        world.Answer("/api/catalogue/discover?kind=film&like=Night Harbor (1995)&goodOnly=true", new DiscoverResult
        {
            Resolved = new DiscoverResolved { Like = "Night Harbor (1995)", MinRating = 6.5, MinVotes = 200, Sort = "popular" },
            Titles = [QuietRoom],
        });
        world.Answer("/api/catalogue/discover?kind=film&genre=Horror&language=Korean&goodOnly=true", new DiscoverResult
        {
            Resolved = new DiscoverResolved { Genres = ["Horror"], Language = "ko", MinRating = 6.5, MinVotes = 200, Sort = "popular" },
            Titles = [],
        });
        world.Answer("/api/catalogue/discover?kind=film&genre=Spooky&goodOnly=true",
            "{\"error\":\"The database has no genre 'Spooky'. Its genres: Action, Comedy, Drama, Horror.\"}", 404);
        world.Answer("/api/catalogue/discover?kind=show&genre=Drama&goodOnly=true", new DiscoverResult
        {
            Kind = "show", Resolved = new DiscoverResolved { Genres = ["Drama"], Sort = "popular" }, Titles = [NightHarborShow],
        });

        // find_by_clues: the look alone, the plot word alone, a person's work from 2024, and the pages that settle the clues.
        world.Answer("/api/catalogue/discover?kind=film&about=silent film&goodOnly=false", new DiscoverResult
        {
            Resolved = new DiscoverResolved { Keywords = ["silent film"], Sort = "popular" },
            Titles = [LanternKeeper, Hit("tt1000060", "Moth Light", 2008, rating: 6.1, votes: 150, plot: "A projectionist chases a moth.")],
        });
        world.Answer("/api/catalogue/discover?kind=film&about=lighthouse&goodOnly=false", new DiscoverResult
        {
            Resolved = new DiscoverResolved { Keywords = ["lighthouse"], Sort = "popular" }, Titles = [LanternKeeper],
        });
        world.Answer("/api/catalogue/discover?kind=film&about=zzz&goodOnly=false", "{\"error\":\"The database has no keyword 'zzz'.\"}", 404);
        world.Answer("/api/catalogue/people/601/credits?from=2024", new PersonCredits
        {
            PersonId = 601, Name = "Julian Rook", KnownFor = "Acting", AsOf = Today, Total = 3,
            Credits =
            [
                Credit("tt1000050", "The Last Ferry", "2024-03-01", "released", role: "Owen"),
                Credit("tt1000051", "Winter Quarters", null, "announced"),
                Credit("tt1000052", "Cinder", "2027-02-12", "upcoming", role: "Pell"),
            ],
        });
        world.Answer("/api/films/tt1000050", new FilmDetails
        {
            ImdbId = "tt1000050", Title = "The Last Ferry", Year = 2024, Genres = ["Drama"], Overview = "A ferry crosses the last night of winter.",
            Keywords = ["ferry", "winter"], Votes = 900,
        });
        world.Answer("/api/films/tt1000052", new FilmDetails
        {
            ImdbId = "tt1000052", Title = "Cinder", Year = 2027, Genres = ["Thriller"], Overview = "A fire watcher on a lighthouse island.",
            Keywords = ["fire"], Votes = 0,
        });
        world.Answer("/api/films/tt1000060", new FilmDetails
        {
            ImdbId = "tt1000060", Title = "Moth Light", Year = 2008, Genres = ["Drama"], Overview = "A projectionist chases a moth.",
            Keywords = ["silent film", "projectionist"], Votes = 150,
        });

        world.Answer("/api/popular", new Popular
        {
            FetchedAt = new DateTimeOffset(2026, 10, 9, 6, 0, 0, TimeSpan.Zero),
            Films = [new() { ImdbId = "tt1000060", Title = "Rain Check", Year = 2026, Rating = 7.0, Votes = 9000, PosterUrl = "/glances/tt1000060/poster.jpg" }],
            Shows = [new() { ImdbId = "tt1000061", Title = "Doorbell", Year = 2026, PosterUrl = "/glances/tt1000061/poster.jpg" }],
        });
        world.Answer("/api/catalogue/released?kind=film", new TitleList
        {
            Kind = "film", From = "2026-08-25", To = Today, Titles = [Hit("tt1000070", "Late Bloom", 2026, rating: 6.4, votes: 300)],
        });
        world.Answer("/api/catalogue/released?kind=show", new TitleList { Kind = "show", From = "2026-08-25", To = Today, Titles = [] });
        world.Answer("/api/catalogue/upcoming?kind=film", new TitleList
        {
            Kind = "film", From = Today, To = "2027-04-07", Titles = [Hit("tt1000071", "Clay Face", 2026, rating: 0, votes: 0)],
        });
        world.Answer("/api/catalogue/upcoming?kind=show", new TitleList { Kind = "show", From = Today, To = "2027-04-07", Titles = [] });
        world.Answer("/api/catalogue/added?limit=20", new LibraryAdditions
        {
            Titles =
            [
                new()
                {
                    ImdbId = "tt1000004", Kind = "film", Title = "Coastal Hours", Year = 2021, Genres = ["Drama"], Rating = 6.9,
                    LibraryId = "coastal-hours-2021", AddedAt = new DateTimeOffset(2026, 10, 8, 12, 0, 0, TimeSpan.Zero), Watched = true,
                },
            ],
        });

        world.Answer("/api/me/history", new List<HistoryEntry>
        {
            new() { TitleId = "night-harbor-1995", Name = "Night Harbor", Finished = true, PositionSeconds = 9000, DurationSeconds = 9100,
                    LastWatchedAt = new DateTimeOffset(2026, 10, 1, 20, 0, 0, TimeSpan.Zero) },
            new() { TitleId = "coastal-hours-2021", Name = "Coastal Hours", PositionSeconds = 2000, DurationSeconds = 5900,
                    LastWatchedAt = new DateTimeOffset(2026, 9, 20, 20, 0, 0, TimeSpan.Zero) },
            new() { TitleId = "salt-s01e02", Name = "The Salt Line", Finished = true, PositionSeconds = 2500, DurationSeconds = 2600,
                    Episode = new EpisodeRef(1, 2), ShowImdbId = "tt1000006",
                    LastWatchedAt = new DateTimeOffset(2026, 9, 2, 20, 0, 0, TimeSpan.Zero) },
        });
        world.Answer("/api/me/lists", new List<ListSummary>
        {
            new() { Id = "later", Name = "Watch later", WatchLater = true, Count = 1 },
            new() { Id = "friday", Name = "Friday night", Count = 0 },
        });
        world.Answer("/api/lists/later", new ListContents
        {
            List = new ListSummary { Id = "later", Name = "Watch later", WatchLater = true, Count = 1 },
            Entries =
            [
                new() { ImdbId = "tt1000003", Kind = "film", Title = "The Lantern Keeper", Year = 2019, Rating = 7.5, Genres = ["Drama"],
                        AddedAt = new DateTimeOffset(2026, 9, 30, 0, 0, 0, TimeSpan.Zero), Note = "Rook, as Nina said", PosterUrl = "/films/tt1000003/poster.jpg" },
            ],
        });
        world.Answer("/api/lists/friday", new ListContents { List = new ListSummary { Id = "friday", Name = "Friday night" }, Entries = [] });
        world.Answer("/api/me/follows", new List<Follow>
        {
            new() { ImdbId = "tt1000006", Title = "The Salt Line", Since = new DateTimeOffset(2026, 1, 4, 0, 0, 0, TimeSpan.Zero),
                    NextEpisode = new Airing(3, 1, null, "2026-11-02") },
        });

        return world;
    }
}

/// <summary>Runs the plugin's tools as a signed-in person over a world, the way the plugin package's tool host does.</summary>
internal sealed class ToolRig
{
    public ToolRig(ReplayHandler? world = null, string? token = null)
    {
        World = world ?? CinemaWorld.Build();
        Token = token ?? CinemaWorld.Token;
        Api = new CinemaApi(new HttpClient(World) { BaseAddress = new Uri("http://api.test/") });
    }

    public ReplayHandler World { get; }
    public string Token { get; }
    public CinemaApi Api { get; }

    public SearchTitlesTool Search => new(Api);
    public TitleDetailsTool Details => new(Api);
    public PersonCreditsTool Credits => new(Api);
    public DiscoverTool Discover => new(Api);
    public WhatsNewTool WhatsNew => new(Api);
    public MyTasteTool Taste => new(Api);
    public AnticipatedTool Anticipated => new(Api);

    /// <summary>The day the world is read on; the statuses of its credits are as of it.</summary>
    public static readonly DateTimeOffset Now = new(2026, 10, 9, 12, 0, 0, TimeSpan.Zero);

    public FindByCluesTool Clues => new(Api, new Microsoft.Extensions.Time.Testing.FakeTimeProvider(Now));

    public Caller Caller => new("u-sam", "Sam", Token, null, new HashSet<string> { "cinema:watch" });

    private static readonly ToolTestOptions Options = new()
    {
        Subject = "u-sam", DisplayName = "Sam", Cards = [CardJson.Default],
    };

    /// <summary>Runs <paramref name="tool"/> with <paramref name="arguments"/> as the person.</summary>
    public Task<ToolOutcome> RunAsync(IPluginTool tool, object arguments) =>
        ToolTest.RunAsync(tool, JsonSerializer.Serialize(arguments), Caller, Options);

    /// <summary>Runs <paramref name="tool"/> for a turn with nobody signed in.</summary>
    public Task<ToolOutcome> RunSignedOutAsync(IPluginTool tool, object arguments) =>
        ToolTest.RunAsync(tool, JsonSerializer.Serialize(arguments), null, Options);

    /// <summary>The titles a result's card offers, as their ids; empty when the result has no card.</summary>
    public static string[] CardIds(ToolOutcome result) =>
        result.Card is { } card
            ? [.. card.GetProperty("titles").EnumerateArray().Select(t => t.GetProperty("imdbId").GetString()!)]
            : [];
}

internal static class ReplayHandlerExtensions
{
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    /// <summary>Answers <paramref name="target"/> (path and query) with <paramref name="body"/> as the API writes JSON.</summary>
    public static ReplayHandler Answer(this ReplayHandler world, string target, object body, int status = 200) =>
        world.Answer(target, JsonSerializer.Serialize(body, Web), status);
}
