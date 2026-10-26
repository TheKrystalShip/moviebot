using System.Net;
using System.Text.Json;
using TheKrystalShip.Agent.Plugin;
using TheKrystalShip.Agent.Plugin.Testing;
using TheKrystalShip.MovieBot.Assistant;
using TheKrystalShip.MovieBot.Assistant.Api;
using TheKrystalShip.MovieBot.Assistant.Tools;
using TheKrystalShip.MovieBot.Catalogue;
using TheKrystalShip.MovieBot.Core;

namespace TheKrystalShip.MovieBot.Tests.Assistant;

/// <summary>
/// The catalogue and the library as the API answers them, in an invented world: a person with the
/// same name twice, a silent film and ones that are not, early-2000s horror, announced
/// titles with no date, a person's history, lists and follows. It is a table of answers
/// behind the same HTTP client the tools use, so a recording of the live API replays the same way.
/// </summary>
internal static class CinemaWorld
{
    public const string Today = "2026-10-09";
    public const string Token = "a-person's-access-token";

    public static TitleCard Card(
        string id, string title, int? year, string kind = "film", string[]? genres = null, double rating = 7.2, int votes = 5400,
        bool inLibrary = false, bool watched = false, string? plot = null, string[]? madeBy = null, string[]? starring = null,
        string? released = "", string? role = null, string department = "cast", string category = "general", string? language = "en") =>
        new()
        {
            Id = id, Kind = kind, Title = title, Year = year,
            Released = released == "" ? (year is { } y ? $"{y}-03-05" : null) : released,
            Genres = [.. genres ?? ["Drama"]], Rating = votes == 0 ? null : new Rating(votes < 25 ? null : rating, votes, "imdb"),
            Plot = plot, MadeBy = [.. madeBy ?? []], Starring = [.. starring ?? []], Poster = $"/images/w500/{id}.jpg",
            Category = category, Language = language,
            Credit = role is null && department == "cast" ? null : new Credit(department, role ?? ""),
            You = new Viewer(inLibrary, inLibrary ? title.ToLowerInvariant().Replace(' ', '-') + $"-{year}" : null, watched, false),
        };

    /// <summary>A credit on a list filtered by one person: its date (or none) and their part in it.</summary>
    public static TitleCard Credit(
        string id, string title, string? date, string kind = "film", string? role = null, bool watched = false, int votes = 1200) =>
        Card(id, title, date is null ? null : int.Parse(date[..4]), kind, rating: 6.8, votes: date is null ? 0 : votes, watched: watched,
            released: date, role: role);

    public static CardPage Page(params TitleCard[] titles) => new([.. titles], titles.Length, null);

    public static readonly TitleCard NightHarbor = Card("tt1000001", "Night Harbor", 1995, genres: ["Crime", "Drama"], rating: 8.3, votes: 700000,
        inLibrary: true, watched: true, plot: "A detective hunts a thief across a port city.", madeBy: ["Vera Lund"], starring: ["Ada Calloway", "Theo Marsh"]);

    public static readonly TitleCard NightHarborShow = Card("tt1000002", "Night Harbor", 2012, "show", ["Crime"], 6.9, 3100,
        plot: "Dockworkers uncover a smuggling ring.", madeBy: ["Imre Voss"], starring: ["Nell Aldous"]);

    public static readonly TitleCard LanternKeeper = Card("tt1000003", "The Lantern Keeper", 2019, genres: ["Drama", "Mystery"], rating: 7.5, votes: 61000,
        plot: "Two keepers lose their grip on a lighthouse.", madeBy: ["Rowan Pike"], starring: ["Julian Rook", "Willa Dane"], released: "2019-10-18");

    public static readonly TitleCard HollowHouse = Card("tt1000010", "Hollow House", 2002, genres: ["Horror"], rating: 7.2, votes: 410000,
        watched: true, inLibrary: true, plot: "A family hears the walls breathe.", madeBy: ["Dara Quill"], starring: ["Mina Ford", "Cass Hale"]);

    public static readonly TitleCard QuietRoom = Card("tt1000011", "The Quiet Room", 2003, genres: ["Horror", "Mystery"], rating: 6.8, votes: 2300,
        plot: "A nurse finds a ward that does not exist.", madeBy: ["Oto Brandt"], starring: ["Pia Lowe"]);

    public static readonly TitleCard AshRoad = Card("tt1000012", "Ash Road", 2004, genres: ["Horror"], rating: 6.6, votes: 900,
        inLibrary: true, plot: "A hitchhiker is not what she seems.", madeBy: ["Dara Quill"], starring: ["Cass Hale"]);

    public static readonly TitleCard CoastalHours = Card("tt1000004", "Coastal Hours", 2021, rating: 6.9, votes: 800, inLibrary: true, watched: true,
        released: "2021-06-11", language: "es");

    public static readonly TitleCard SaltLineCard = Card("tt1000006", "The Salt Line", 2020, "show", rating: 7.9, votes: 21000, inLibrary: true);

    /// <summary>A film's page, from its card.</summary>
    public static TitlePage PageOf(TitleCard card) => new()
    {
        Id = card.Id, Kind = card.Kind, Title = card.Title, Year = card.Year, Released = card.Released, Genres = card.Genres,
        Rating = card.Rating, Poster = card.Poster, Plot = card.Plot, Category = card.Category, Language = card.Language,
        MadeBy = card.MadeBy, Starring = card.Starring, You = card.You, Ratings = new SourceRatings(card.Rating, null), Facts = new(),
    };

    /// <summary>A film's page, with its keywords.</summary>
    public static readonly TitlePage LanternPage = PageOf(LanternKeeper) with
    {
        Runtime = 109, Certificates = new() { ["US"] = "R" }, Overview = "Two keepers lose their grip on a lighthouse.",
        Tagline = "The light is the least of it.",
        Keywords = [new("k77", "silent film"), new("k78", "lighthouse"), new("k80", "isolation")],
        Crew = [new(9001, "Rowan Pike", "Director"), new(9001, "Rowan Pike", "Screenplay"), new(9002, "Sol Pike", "Writer")],
        Cast = [new(601, "Julian Rook", "Thomas", null), new(9003, "Willa Dane", "Ephraim", null)],
        Countries = ["US"],
    };

    /// <summary>A film with keywords, none of them a look.</summary>
    public static readonly TitlePage CoastalPage = PageOf(CoastalHours) with
    {
        Runtime = 98, Overview = "Three summers at one cafe.", Keywords = [new("k81", "friendship"), new("k82", "summer")],
        Crew = [new(9004, "Ines Varga", "Director")], Cast = [new(501, "Ada Calloway", "Lia", null)], Countries = ["ES"],
    };

    /// <summary>A film whose keywords have not been asked for yet.</summary>
    public static readonly TitlePage ColdFramePage = PageOf(Card("tt1000005", "Cold Frame", 2023, genres: ["Thriller"], rating: 6.1, votes: 400,
        released: "2023-01-20")) with { Crew = [new(9005, "Maks Ilves", "Director")] };

    public static readonly TitlePage SaltLine = PageOf(SaltLineCard) with
    {
        EndYear = 2022, Status = "Ended", Overview = "A border town runs on salt.", Keywords = [new("k83", "border")],
        Crew = [new(9006, "Imre Voss", "Creator")], Cast = [new(801, "Nell Aldous", "Marta", null)], Networks = [new("s12", "Northlight")],
        Seasons = [new(1, null, "2020-02-02", null, 2), new(2, null, "2021-02-02", null, 1)],
    };

    /// <summary>The catalogue's genres and its most held original languages.</summary>
    public static readonly Facets Vocabulary = new(0,
        [
            new("genre", [.. new[] { "Action", "Animation", "Comedy", "Crime", "Drama", "Family", "Horror", "Mystery", "Sci-Fi", "Thriller" }
                .Select(g => new FacetOption(g, null, 10))]),
            new("language", [new("en", "English", 100), new("ja", "Japanese", 20), new("ko", "Korean", 10)]),
        ]);

    private static List<Term> People(params (int Id, string Name, string Department)[] people) =>
        [.. people.Select(p => new Term("person", p.Id.ToString(), p.Name, p.Department))];

    private static List<Term> Keyword(string id, string name) => [new("keyword", id, name, null)];

    /// <summary>The world, and the API client that asks it as one person.</summary>
    public static ReplayHandler Build()
    {
        var world = new ReplayHandler();

        world.Answer("/api/catalogue/titles?q=night harbor&size=10", Page(NightHarbor, NightHarborShow));
        world.Answer("/api/catalogue/titles?q=night harbor&from=1994&to=1996&size=10", Page(NightHarbor));
        world.Answer("/api/catalogue/titles?q=night harbor&kind=film&size=10", Page(NightHarbor));
        world.Answer("/api/catalogue/titles?q=night harbor&from=1995&to=1995&size=1", Page(NightHarbor));
        world.Answer("/api/catalogue/titles?q=the lantern keeper&size=10", Page(LanternKeeper));
        world.Answer("/api/catalogue/titles?q=the lantern keeper&from=2018&to=2020&size=10", Page(LanternKeeper));
        world.Answer("/api/catalogue/titles?q=coastal hours&from=2020&to=2022&size=10", Page(CoastalHours));
        world.Answer("/api/catalogue/titles?q=cold frame&size=10", Page(ColdFramePage));
        world.Answer("/api/catalogue/titles?q=the salt line&size=10", Page(SaltLineCard));
        world.Answer("/api/catalogue/titles?q=nothing like it&size=10", Page());
        world.Answer("/api/catalogue/titles?q=unborn harbour&size=10", Page());
        world.Answer("/api/catalogue/titles?q=down&size=10", "{\"error\":\"The catalogue is being rebuilt.\"}", 503);

        world.Answer("/api/catalogue/titles/tt1000001", PageOf(NightHarbor) with
        {
            Released = "1995-12-15", Keywords = [new("k84", "heist")], Crew = [new(9007, "Vera Lund", "Director")],
        });
        world.Answer("/api/catalogue/titles/tt1000003", LanternPage with { You = new Viewer(false, null, false, true) });
        world.Answer("/api/titles/tt1000003/wanters", new List<WanterView>
        {
            new() { UserId = "u-ana", Name = "Ana Ruiz", Since = new DateTimeOffset(2026, 9, 2, 0, 0, 0, TimeSpan.Zero) },
            new() { UserId = "u-marc", Name = "Marc", Since = new DateTimeOffset(2026, 9, 1, 0, 0, 0, TimeSpan.Zero) },
        });
        world.Answer("/api/titles/tt1000004/wanters", new List<WanterView>());
        world.Answer("/api/titles/tt1000006/wanters", new List<WanterView>());
        world.Answer("/api/catalogue/titles/tt1000004", CoastalPage);
        world.Answer("/api/catalogue/titles/tt1000005", ColdFramePage);
        world.Answer("/api/catalogue/titles/tt1000006", SaltLine);
        world.Answer("/api/me/shows/tt1000006", new PersonShowView
        {
            ImdbId = "tt1000006", Seasons = [], Follow = new ShowFollow(false, new DateTimeOffset(2026, 1, 4, 0, 0, 0, TimeSpan.Zero)),
        });

        world.Answer("/api/catalogue/terms?q=ada calloway", People((501, "Ada Calloway", "Acting"), (502, "Ada Calloway", "Directing")));
        world.Answer("/api/catalogue/titles?person=501&sort=votes&size=3", Page(NightHarbor, CoastalHours));
        world.Answer("/api/catalogue/titles?person=502&sort=votes&size=3", Page(Card("tt1000040", "Small Rooms", 2011)));
        world.Answer("/api/catalogue/titles?person=501&q=small rooms&size=1", Page());
        world.Answer("/api/catalogue/titles?person=502&q=small rooms&size=1", Page(Card("tt1000040", "Small Rooms", 2011)));
        world.Answer("/api/catalogue/terms?q=julian rook", People((601, "Julian Rook", "Acting")));
        world.Answer("/api/catalogue/terms?q=rook", People((601, "Julian Rook", "Acting"), (603, "Edda Rookwood", "Writing")));
        world.Answer("/api/catalogue/titles?person=601&sort=votes&size=3", Page(LanternKeeper));
        world.Answer("/api/catalogue/titles?person=603&sort=votes&size=3", Page());
        world.Answer("/api/catalogue/terms?q=nell aldous", People((801, "Nell Aldous", "Acting")));
        world.Answer("/api/catalogue/terms?q=nobody here", new List<Term>());

        world.Answer("/api/catalogue/titles?person=501&role=cast&from=2026&to=2026&sort=oldest&size=100", Page(
            Credit("tt1000030", "Mother Tongue", "2026-04-16", role: "Anna"),
            Credit("tt1000031", "The Wedding Party 2", "2026-04-29", role: "Lead"),
            Credit("tt1000032", "The Long Voyage", "2026-07-15", role: "Helen", watched: true),
            Credit("tt1000033", "Winter Street", "2026-12-18", role: "Carol"),
            Credit("tt1000034", "Salt Marsh", null),
            Credit("tt1000036", "Harbour Sting", null),
            Credit("tt1000035", "Fear Not", null, kind: "show")));
        world.Answer("/api/catalogue/titles?person=502&role=directing&sort=oldest&size=100", Page(
            Card("tt1000040", "Small Rooms", 2011, rating: 6.8, votes: 1200, released: "2011-05-05", role: "Director", department: "Directing")));
        world.Answer("/api/catalogue/titles?person=501&sort=oldest&size=100", new CardPage([.. Enumerable.Range(1, 100).Select(n => Credit($"tt30{n:0000}", $"Early {n:000}", $"{1900 + n}-01-01", role: $"Part {n}"))], 120, null));
        world.Answer("/api/catalogue/titles?person=501&sort=oldest&page=2&size=100", new CardPage([.. Enumerable.Range(101, 20).Select(n => Credit($"tt30{n:0000}", $"Late {n:000}", $"{1900 + n}-01-01", role: $"Part {n}"))], 120, null));
        world.Answer("/api/catalogue/titles?person=501&from=2030&to=2030&sort=oldest&size=100", Page());
        world.Answer("/api/catalogue/titles?person=601&sort=oldest&size=100", Page(
            Credit("tt1000003", "The Lantern Keeper", "2019-10-18", role: "Thomas") with { Rating = LanternKeeper.Rating, Plot = LanternKeeper.Plot },
            Credit("tt1000050", "The Last Ferry", "2024-03-01", role: "Owen"),
            Credit("tt1000052", "Cinder", "2027-02-12", role: "Pell"),
            Credit("tt1000051", "Winter Quarters", null)));

        world.Answer("/api/catalogue/facets", Vocabulary);
        world.Answer("/api/catalogue/terms?q=silent film", Keyword("k77", "silent film"));
        world.Answer("/api/catalogue/terms?q=lighthouse", Keyword("k78", "lighthouse"));
        world.Answer("/api/catalogue/terms?q=ghost story", Keyword("k90", "ghost story"));
        world.Answer("/api/catalogue/terms?q=zzz", new List<Term>());

        world.Answer("/api/catalogue/titles?kind=film&genre=Horror&notGenre=Animation&notGenre=Family&from=2000&to=2005&minRating=6.5&minVotes=200",
            Page(HollowHouse, QuietRoom, AshRoad));
        world.Answer("/api/catalogue/titles?kind=show&category=anime&minRating=6.5&minVotes=200",
            Page(Card("tt1000020", "Paper Lanterns", 2019, "show", ["Animation", "Drama"], category: "anime", language: "ja")));
        world.Answer("/api/catalogue/titles?kind=show&category=anime&here=ready&size=20", Page());
        world.Answer("/api/catalogue/titles?kind=film&genre=Horror&notGenre=Crime&minRating=6.5&minVotes=200", Page(HollowHouse));
        world.Answer("/api/catalogue/titles?kind=film&genre=Horror&genre=Mystery&notGenre=Animation&notGenre=Family&sort=rating", Page(QuietRoom));
        world.Answer("/api/catalogue/titles?kind=film&keyword=k77&person=601", Page(LanternKeeper));
        world.Answer("/api/catalogue/titles?kind=film&similarTo=tt1000001&minRating=6.5&minVotes=200", Page(QuietRoom));
        world.Answer("/api/catalogue/titles?kind=film&genre=Horror&notGenre=Animation&notGenre=Family&language=ko&minRating=6.5&minVotes=200", Page());
        world.Answer("/api/catalogue/titles?kind=show&genre=Drama&notGenre=Animation&notGenre=Family&minRating=6.5&minVotes=200", Page(NightHarborShow));
        world.Answer("/api/catalogue/titles?kind=show&person=801&minRating=6.5&minVotes=200", Page(NightHarborShow));

        // find_by_clues: the look alone, the plot word alone, a person's work from 2024, and the pages that settle the clues.
        world.Answer("/api/catalogue/titles?kind=film&keyword=k77", Page(
            LanternKeeper, Card("tt1000060", "Moth Light", 2008, rating: 6.1, votes: 150, plot: "A projectionist chases a moth.")));
        world.Answer("/api/catalogue/titles?kind=film&keyword=k78", Page(LanternKeeper));
        world.Answer("/api/catalogue/titles?kind=film&keyword=k78&person=601", Page(LanternKeeper));
        world.Answer("/api/catalogue/titles?person=601&from=2024&sort=oldest&size=100", Page(
            Credit("tt1000050", "The Last Ferry", "2024-03-01", role: "Owen"),
            Credit("tt1000052", "Cinder", "2027-02-12", role: "Pell"),
            Credit("tt1000051", "Winter Quarters", null)));
        world.Answer("/api/catalogue/titles/tt1000050", PageOf(Card("tt1000050", "The Last Ferry", 2024, votes: 900)) with
        {
            Overview = "A ferry crosses the last night of winter.", Keywords = [new("k85", "ferry"), new("k86", "winter")],
        });
        world.Answer("/api/catalogue/titles/tt1000052", PageOf(Card("tt1000052", "Cinder", 2027, genres: ["Thriller"], votes: 0)) with
        {
            Overview = "A fire watcher on a lighthouse island.", Keywords = [new("k87", "fire")],
        });
        world.Answer("/api/catalogue/titles/tt1000060", PageOf(Card("tt1000060", "Moth Light", 2008, votes: 150)) with
        {
            Overview = "A projectionist chases a moth.", Keywords = [new("k77", "silent film"), new("k88", "projectionist")],
        });

        world.Answer("/api/catalogue/titles?ranking=trending-films&size=20", Page(
            Card("tt1000060", "Rain Check", 2026, rating: 7.0, votes: 9000)));
        world.Answer("/api/catalogue/titles?ranking=trending-shows&size=20", Page(Card("tt1000061", "Doorbell", 2026, "show", votes: 0)));
        world.Answer("/api/catalogue/titles?kind=film&released=2026-08-25&releasedBefore=2026-10-10&sort=popular&size=20", Page(
            Card("tt1000072", "Old Lamp", 2026, rating: 6.0, votes: 120, released: "2026-09-01"),
            Card("tt1000070", "Late Bloom", 2026, rating: 6.4, votes: 300, released: "2026-09-30")));
        world.Answer("/api/catalogue/titles?kind=show&released=2026-08-25&releasedBefore=2026-10-10&sort=popular&size=20", Page());
        world.Answer("/api/catalogue/titles?kind=film&released=2026-10-09&releasedBefore=2027-04-08&sort=popular&size=20", Page(
            Card("tt1000071", "Clay Face", 2026, votes: 0, released: "2026-12-01")));
        world.Answer("/api/catalogue/titles?kind=show&released=2026-10-09&releasedBefore=2027-04-08&sort=popular&size=20", Page());
        world.Answer("/api/catalogue/titles?here=ready&size=20", Page(CoastalHours));

        world.Answer("/api/me/history", new List<HistoryEntry>
        {
            new("night-harbor-1995", "Night Harbor", 9000, 9100, true, new DateTimeOffset(2026, 10, 1, 20, 0, 0, TimeSpan.Zero)),
            new("coastal-hours-2021", "Coastal Hours", 2000, 5900, false, new DateTimeOffset(2026, 9, 20, 20, 0, 0, TimeSpan.Zero)),
            new("salt-s01e02", "The Salt Line", 2500, 2600, true, new DateTimeOffset(2026, 9, 2, 20, 0, 0, TimeSpan.Zero),
                new EpisodeIdentity { Season = 1, Number = 2 }, "tt1000006"),
        });
        world.Answer("/api/me/wants", new List<WantView>
        {
            new() { ImdbId = "tt1000003", Kind = "film", Title = "The Lantern Keeper", Card = LanternKeeper, Visible = true,
                    Since = new DateTimeOffset(2026, 9, 30, 0, 0, 0, TimeSpan.Zero), State = "waiting" },
            new() { ImdbId = "tt1000006", Kind = "show", Title = "The Salt Line", Visible = true, Since = new DateTimeOffset(2026, 9, 1, 0, 0, 0, TimeSpan.Zero),
                    InLibrary = true, State = "ready", NextEpisode = new WantEpisode { Season = 1, Episode = 3 } },
        });
        world.Answer("/api/me/follows", new List<FollowView>
        {
            new() { ImdbId = "tt1000006", Title = "The Salt Line", Card = SaltLineCard, Since = new DateTimeOffset(2026, 1, 4, 0, 0, 0, TimeSpan.Zero),
                    NextEpisode = new AiringView(3, 1, null, "2026-11-02") },
        });

        return world;
    }
}

/// <summary>Runs the plugin's tools as a signed-in person over a world, the way the plugin package's tool host does.</summary>
internal sealed class ToolRig
{
    public ToolRig(ReplayHandler? world = null, string? token = null, string? timeZone = null)
    {
        World = world ?? CinemaWorld.Build();
        Token = token ?? CinemaWorld.Token;
        TimeZone = timeZone;
        Sent = new BodyTap(World);
        Api = new CinemaApi(new MovieBotApiClient(new HttpClient(Sent) { BaseAddress = new Uri("http://api.test/") }));
    }

    public ReplayHandler World { get; }

    /// <summary>The bodies the tools sent, by request.</summary>
    public BodyTap Sent { get; }

    /// <summary>The time zone the person's browser names; null for none.</summary>
    public string? TimeZone { get; }
    public string Token { get; }
    public CinemaApi Api { get; }

    public SearchTitlesTool Search => new(Api);
    public TitleDetailsTool Details => new(Api);
    public PersonCreditsTool Credits => new(Api, Clock);
    public DiscoverTool Discover => new(Api);
    public WhatsNewTool WhatsNew => new(Api, Clock);
    public MyTasteTool Taste => new(Api);
    public AnticipatedTool Anticipated => new(Api);

    /// <summary>The day the world is read on; the statuses of its credits are as of it.</summary>
    public static readonly DateTimeOffset Now = new(2026, 10, 9, 12, 0, 0, TimeSpan.Zero);

    public static TimeProvider Clock => new Microsoft.Extensions.Time.Testing.FakeTimeProvider(Now);

    public FindByCluesTool Clues => new(Api, Clock);
    public WantedTogetherTool Together => new(Api);
    public MyPlansTool Plans => new(Api, Clock);
    public WantTool Want => new(Api);
    public MakePlanTool MakePlan => new(Api, Clock);
    public AnswerPlanTool AnswerPlan => new(Api, Clock);
    public ChangePlanTool ChangePlan => new(Api, Clock);

    public Caller Caller => new("u-sam", "Sam", Token, null, new HashSet<string> { "cinema:watch" }, TimeZone);

    /// <summary>The person, as the plugin resolves them when they confirm a proposal.</summary>
    public PluginPerson Person => new("u-sam", "Sam", Caller);

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

/// <summary>Keeps the body of every request on its way to the world, which answers by method and target alone.</summary>
internal sealed class BodyTap(HttpMessageHandler world) : DelegatingHandler(world)
{
    public List<(string Request, string Body)> Bodies { get; } = [];

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        if (request.Content is { } content)
            Bodies.Add(($"{request.Method} {Uri.UnescapeDataString(request.RequestUri!.PathAndQuery)}", await content.ReadAsStringAsync(ct)));
        return await base.SendAsync(request, ct);
    }
}

internal static class ReplayHandlerExtensions
{
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    /// <summary>Answers <paramref name="method"/> <paramref name="target"/> with <paramref name="body"/> as the API writes JSON.</summary>
    public static ReplayHandler Answer(this ReplayHandler world, string method, string target, object body, int status = 200) =>
        world.Add(new RecordedAnswer(method, target, status, body as string ?? JsonSerializer.Serialize(body, Web)));

    /// <summary>Answers <paramref name="target"/> (path and query) with <paramref name="body"/> as the API writes JSON.</summary>
    public static ReplayHandler Answer(this ReplayHandler world, string target, object body, int status = 200) =>
        world.Answer(target, JsonSerializer.Serialize(body, Web), status);
}
