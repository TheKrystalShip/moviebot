using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Configuration;
using TheKrystalShip.WebPush;
using TheKrystalShip.MovieBot.Acquire.Imdb;
using TheKrystalShip.MovieBot.Acquire.Search;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Data.Sqlite;
using TheKrystalShip.MovieBot.Api.Auth;
using TheKrystalShip.MovieBot.Core;
using TheKrystalShip.MovieBot.Api.Sessions;
using System.Net.Http.Json;

namespace TheKrystalShip.MovieBot.Tests;

/// <summary>
/// Hosts the API in-process over a media root built for the test, so clamping can be exercised
/// against a title that is genuinely mid-transcode rather than a mocked head.
/// </summary>
public class SessionFixture : WebApplicationFactory<Program>
{
    public string MediaRoot { get; } = Path.Combine(
        Path.GetTempPath(), "moviebot-tests", Guid.NewGuid().ToString("n"));

    /// <summary>The disk finished films are kept on, standing in for a second volume.</summary>
    public string ColdRoot { get; } = Path.Combine(
        Path.GetTempPath(), "moviebot-tests", Guid.NewGuid().ToString("n"), "cold");

    /// <summary>
    /// Where this run's rooms are journalled. Its own, because the API restores every room it finds in
    /// its journal on start: a shared one carries one run's rooms into the next, and a test that
    /// asserts a room does not exist then fails on a room some earlier run made.
    /// </summary>
    public string StateRoot { get; } = Path.Combine(
        Path.GetTempPath(), "moviebot-tests", Guid.NewGuid().ToString("n"), "state");

    /// <summary>
    /// The catalogue the API reads: <see cref="TestCatalogue"/>, with its images, the two films the library
    /// names, <see cref="NotOut"/> and <see cref="Quiet"/>.
    /// </summary>
    public string CatalogueRoot { get; } = Path.Combine(
        Path.GetTempPath(), "moviebot-tests", Guid.NewGuid().ToString("n"), "catalogue");

    /// <summary>A film the catalogue lists and the title index knows, that the tracker has no release of.</summary>
    public const string NotOut = "tt0325980";

    /// <summary>A film the catalogue lists that nothing else knows.</summary>
    public const string Quiet = "tt9000004";

    /// <summary>A title still being written: the head sits here, seeks past it are refused.</summary>
    public const string TranscodingTitle = "still-cooking";
    public const double TranscodingHead = 300;

    /// <summary>A finished title: the whole film is seekable.</summary>
    public const string ReadyTitle = "all-done";
    public const double TitleDuration = 6000;

    /// <summary>A finished title that has moved to the disk films are kept on.</summary>
    public const string SettledTitle = "put-away";

    /// <summary>Two finished films the catalogue has named, for anything that reads a library the way a person names a film.</summary>
    public const string Heat = "heat-1995";
    public const string Collateral = "collateral-2004";

    /// <summary>A finished film with two audio tracks, two subtitle tracks that can be shown and one that cannot.</summary>
    public const string Dubbed = "dubbed-1999";

    public SessionFixture()
    {
        WriteManifest(MediaRoot, TranscodingTitle, TitleStatus.Transcoding, TranscodingHead);
        WriteManifest(MediaRoot, ReadyTitle, TitleStatus.Ready, null);
        WriteManifest(MediaRoot, Heat, TitleStatus.Ready, null,
            new FilmIdentity { ImdbId = "tt0113277", Name = "Heat", Year = 1995, Starring = "Al Pacino, Robert De Niro" });
        WriteManifest(MediaRoot, Collateral, TitleStatus.Ready, null,
            new FilmIdentity { ImdbId = "tt0369339", Name = "Collateral", Year = 2004, Starring = "Tom Cruise, Jamie Foxx" });

        WriteManifest(MediaRoot, Dubbed, TitleStatus.Ready, null,
            new FilmIdentity { ImdbId = "tt0133093", Name = "Dubbed", Year = 1999 }, tracks: true);

        Directory.CreateDirectory(ColdRoot);
        File.WriteAllText(Path.Combine(ColdRoot, MediaRoots.ColdMarker), "");
        WriteManifest(ColdRoot, SettledTitle, TitleStatus.Ready, null);

        Catalogue = TestCatalogue.Write(CatalogueRoot, TimeProvider.System);
        TestCatalogue.WriteImages(CatalogueRoot);
        TestCatalogue.KeepFilm(Catalogue, TimeProvider.System, "tt0113277", 949, "Heat", "1995-12-15", ["Action", "Crime", "Drama"],
            "Michael Mann", ["Al Pacino", "Robert De Niro"], runtime: 170);
        TestCatalogue.KeepFilm(Catalogue, TimeProvider.System, "tt0369339", 1538, "Collateral", "2004-08-05", ["Crime", "Thriller"],
            "Michael Mann", ["Tom Cruise", "Jamie Foxx"]);
        TestCatalogue.KeepFilm(Catalogue, TimeProvider.System, NotOut, 22, "Pirates of the Caribbean: The Curse of the Black Pearl", "2003-07-09",
            ["Adventure", "Fantasy"], "Gore Verbinski", ["Johnny Depp", "Geoffrey Rush"]);
        TestCatalogue.KeepFilm(Catalogue, TimeProvider.System, Quiet, 9000004, "Silver Harbour", $"{DateTime.UtcNow.Year}-01-01",
            ["Drama"], "Jo Test", ["Ray Test"]);
    }

    /// <summary>The catalogue the API reads, open for the test to write.</summary>
    internal TheKrystalShip.MovieBot.Catalogue.Data.CatalogueDatabase Catalogue { get; }

    /// <summary>Keeps a show in the catalogue, as <c>moviebot-catalogue</c> would once the film database described it.</summary>
    internal void KeepShow(string imdbId, int tmdbId, string name, IReadOnlyList<ShowEpisode> episodes, string status = "Returning Series",
        ShowEpisode? next = null) =>
        TestCatalogue.KeepShow(Catalogue, TimeProvider.System, imdbId, tmdbId, name, status, episodes, next);

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (!disposing) return;

        if (Directory.Exists(MediaRoot)) Directory.Delete(MediaRoot, recursive: true);
        if (Directory.Exists(ColdRoot)) Directory.Delete(ColdRoot, recursive: true);
        if (Directory.Exists(StateRoot)) Directory.Delete(StateRoot, recursive: true);
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(CatalogueRoot)) Directory.Delete(CatalogueRoot, recursive: true);
    }

    /// <summary>The keys this service holds of its own: one for media tickets, one for machine work.</summary>
    public const string MediaTicketKey = "a-test-media-ticket-key-of-at-least-thirty-two-characters";
    public const string ServiceKey = "a-test-service-key";

    /// <summary>The identity provider the API trusts in the tests.</summary>
    public TestIssuer Issuer { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureAppConfiguration((_, config) =>
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Media:Root"] = MediaRoot,
                ["Media:ColdRoot"] = ColdRoot,
                ["Database:Path"] = Path.Combine(StateRoot, "moviebot.db"),
                ["Auth:MediaTicketKey"] = MediaTicketKey,
                ["Auth:ServiceKey"] = ServiceKey,
                ["Oidc:Issuer"] = TestIssuer.Issuer,
                ["Oidc:ActivityClientSecret"] = "a-test-activity-secret",
                ["Discord:ClientId"] = "123456789",
                ["Discord:ClientSecret"] = "a-test-discord-secret",

                // Fetching films: a tracker and a title index answered by the test, a torrent
                // client nobody is listening for.
                ["Selection:AllowedCategories:0"] = "Movies HD",
                ["Tracker:BaseUrl"] = "https://tracker.invalid/",
                ["QBittorrent:BaseUrl"] = "http://127.0.0.1:1",
                ["Download:Root"] = StateRoot,
                // The catalogue moviebot-catalogue would write, built for the test.
                ["Catalogue:Directory"] = CatalogueRoot,
            }));
        builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(MoreSettings()));

        builder.ConfigureServices(services =>
        {
            services.RemoveAll<IReleaseSearch>();
            services.AddSingleton<IReleaseSearch>(new ReleaseStubs.OneRelease());
            services.AddHttpClient<ImdbClient>().ConfigurePrimaryHttpMessageHandler(() => new ReleaseStubs.CatalogueStub());
            services.AddHttpClient<WebPushSender>().ConfigurePrimaryHttpMessageHandler(() => PushService);

            // The provider's discovery, keys and token endpoint, answered by the test issuer.
            services.Configure<JwtBearerOptions>(CinemaSchemes.Bearer, o => o.BackchannelHttpHandler = Issuer.Handler());
            services.AddHttpClient<TokenExchangeClient>().ConfigurePrimaryHttpMessageHandler(() => Issuer.Handler());
            services.AddHttpClient<Api.Discord.DiscordAuthClient>().ConfigurePrimaryHttpMessageHandler(() => Discord);
            MoreServices(services);
        });
    }

    /// <summary>Settings a fixture built on this one adds, over the ones above.</summary>
    protected virtual Dictionary<string, string?> MoreSettings() => [];

    /// <summary>Services a fixture built on this one replaces, after the ones above.</summary>
    protected virtual void MoreServices(IServiceCollection services)
    {
    }

    /// <summary>Puts a title in the library, as the hand-off would.</summary>
    public void AddTitle(string id, FilmIdentity film) => WriteManifest(MediaRoot, id, TitleStatus.Transcoding, 120, film);

    /// <summary>Puts an episode in the library, playable up to its head, as the hand-off would.</summary>
    public void AddEpisode(string id, string showImdbId, string showName, int season, int number) =>
        WriteManifest(MediaRoot, id, TitleStatus.Transcoding, 120, new FilmIdentity { ImdbId = showImdbId, Name = showName },
            new EpisodeIdentity { Season = season, Number = number });

    /// <summary>The browsers' push services, as the tests answer them.</summary>
    public PushServiceStub PushService { get; } = new();

    /// <summary>Discord's token endpoint, as the tests answer it: a code redeems for <see cref="DiscordStub.AccessToken"/>.</summary>
    public DiscordStub Discord { get; } = new();

    /// <summary>
    /// An access token the provider would have minted for this person, holding <paramref name="actions"/>
    /// (every Cinema action unless named), and carrying the bot as the acting client when
    /// <paramref name="via"/> names it.
    /// </summary>
    public string TokenFor(string userId = "u", string displayName = "Someone", IEnumerable<string>? actions = null, string? via = null) =>
        Issuer.Mint(userId, displayName, actions, actor: via);

    /// <summary>A client signed in as one person.</summary>
    public HttpClient CreatePersonClient(string userId = "u", string displayName = "Someone", IEnumerable<string>? actions = null, string? via = null)
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", TokenFor(userId, displayName, actions, via));
        return client;
    }

    /// <summary>What each Discord account linked at the provider is called; the account is the Discord id itself.</summary>
    public Dictionary<ulong, string> DiscordAccounts { get; } = new() { [4242] = "Haru", [77] = "Bea" };

    /// <summary>
    /// The API as the bot reaches it: a command's calls with the person's token, exchanged at the
    /// provider for the Discord user it runs for, and machine work on the service key.
    /// </summary>
    public MovieBotApiClient CreateBotApi()
    {
        Issuer.Exchange = form =>
            form["subject_token_type"] == TokenExchangeTypes.DiscordUserId
            && ulong.TryParse(form["subject_token"], out var id) && DiscordAccounts.TryGetValue(id, out var name)
                ? (System.Net.HttpStatusCode.OK, Issuer.Minted(Issuer.Mint(form["subject_token"], name, actor: "cinema-bot")))
                : (System.Net.HttpStatusCode.BadRequest, """{"error":"invalid_grant","account_status":"pending"}""");

        var tokens = new Bot.Api.PersonTokens(
            new TokenExchangeClient(new HttpClient(Issuer.Handler()), TimeProvider.System),
            Microsoft.Extensions.Options.Options.Create(new Bot.Api.BotOidcOptions { Issuer = TestIssuer.Issuer, BotClientSecret = "a-test-bot-secret" }),
            TimeProvider.System, Microsoft.Extensions.Logging.Abstractions.NullLogger<Bot.Api.PersonTokens>.Instance);
        return new MovieBotApiClient(new HttpClient(new Bot.Api.PersonAuthorization(tokens, ServiceKey) { InnerHandler = Server.CreateHandler() })
        {
            BaseAddress = Server.BaseAddress,
        });
    }

    /// <summary>A client that proves itself the way machine work does: the hand-off, and the bot's background passes.</summary>
    public HttpClient CreateServiceClient()
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Add("X-MovieBot-Service", ServiceKey);
        return client;
    }

    private static void WriteManifest(string root, string id, TitleStatus status, double? head, FilmIdentity? film = null,
        EpisodeIdentity? episode = null, bool tracks = false)
    {
        var manifest = new Manifest
        {
            Id = id,
            Title = id,
            Film = film,
            Kind = episode is null ? default : TitleKind.Show,
            Episode = episode,
            DurationSeconds = TitleDuration,
            Status = status,
            HeadSeconds = head,
            Video = new VideoInfo
            {
                Width = 1920,
                Height = 800,
                SourceCodec = "hevc",
                SourceHdr = "hdr10",
                Renditions = [new Rendition { Name = "800p", BitrateKbps = 9000, Width = 1920, Height = 800, Uri = "v0/index.m3u8" }]
            },
            Audio =
            [
                new AudioTrack
                {
                    Id = "a0", Kind = TrackKind.Feature, Language = "eng",
                    Label = "English", Channels = 2, Default = true, Uri = "a0/index.m3u8"
                },
                .. tracks
                    ? [new AudioTrack { Id = "a1", Kind = TrackKind.Feature, Language = "spa", Label = "Spanish", Channels = 6, Uri = "a1/index.m3u8" }]
                    : Array.Empty<AudioTrack>(),
            ],
            Subtitles = tracks
                ?
                [
                    new SubtitleTrack
                    {
                        Id = "s0", Kind = TrackKind.Feature, Language = "eng", Label = "English", Source = SubtitleSource.EmbeddedText,
                        Available = true, Uri = "s0.vtt"
                    },
                    new SubtitleTrack
                    {
                        Id = "s1", Kind = TrackKind.Feature, Language = "spa", Label = "Spanish", Source = SubtitleSource.EmbeddedText,
                        Available = true, Uri = "s1.vtt"
                    },
                    new SubtitleTrack
                    {
                        Id = "s2", Kind = TrackKind.Feature, Language = "jpn", Label = "Japanese", Source = SubtitleSource.EmbeddedText,
                        Available = false, Reason = "needs-ocr"
                    },
                ]
                : []
        };

        ManifestJson.WriteAtomic(Path.Combine(root, id, "manifest.json"), manifest);
    }

    /// <summary>
    /// A hub connection routed through the in-process test server, in the room. A room is open from the
    /// moment it is made, so anybody comes in until somebody in it closes it
    /// (<see cref="ConnectPrivateAsync"/>).
    /// </summary>
    public async Task<HubConnection> ConnectAsync(string sessionId, string userId, string displayName)
    {
        var connection = await ConnectOutsideAsync(userId, displayName);
        var reply = await connection.InvokeAsync<JoinReply>("Join", sessionId);
        if (!reply.Admitted)
            throw new InvalidOperationException($"{displayName} was not let into {sessionId}.");
        return connection;
    }

    /// <summary>The first person in a room, who closes it to anybody not let in.</summary>
    public async Task<HubConnection> ConnectPrivateAsync(string sessionId, string userId, string displayName)
    {
        var connection = await ConnectAsync(sessionId, userId, displayName);
        await connection.InvokeAsync("SetOpen", false);
        return connection;
    }

    /// <summary>
    /// A hub connection in no room yet: somebody at a room's door, signed in through the client
    /// <paramref name="via"/> names, or the site's.
    /// </summary>
    public Task<HubConnection> ConnectOutsideAsync(string userId, string displayName, string? via = null) =>
        ConnectWithAsync(() => TokenFor(userId, displayName, via: via));

    /// <summary>
    /// A hub connection signed in as a television: the TV client's token for an anchor session
    /// <paramref name="sid"/>, carrying <paramref name="deviceName"/> when given. In no room yet.
    /// </summary>
    public Task<HubConnection> ConnectScreenAsync(string userId, string sid, string? deviceName = null, string clientId = "cinema-tv") =>
        ConnectWithAsync(() => Issuer.Mint(userId, "Someone", clientId: clientId, sid: sid, deviceName: deviceName));

    /// <summary>
    /// A hub connection of a phone: the site's token for the anchor session <paramref name="sid"/>. In no
    /// room; it hears the screens that session is paired with.
    /// </summary>
    public Task<HubConnection> ConnectPhoneAsync(string userId, string sid, string displayName = "Someone") =>
        ConnectWithAsync(() => Issuer.Mint(userId, displayName, sid: sid));

    /// <summary>A client signed in as a phone: the site's token for the anchor session <paramref name="sid"/>.</summary>
    public HttpClient CreatePhoneClient(string userId, string sid, string? userAgent = null, string? clientId = null)
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue(
            "Bearer", Issuer.Mint(userId, "Someone", clientId: clientId, sid: sid));
        if (userAgent is not null) client.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", userAgent);
        return client;
    }

    /// <summary>
    /// Pairs the phone of session <paramref name="phoneSid"/> with the screen on <paramref name="screen"/>,
    /// as a scan of the code it shows would, and answers the response.
    /// </summary>
    public async Task<HttpResponseMessage> PairAsync(HubConnection screen, string userId, string phoneSid, string? userAgent = null)
    {
        var code = await screen.InvokeAsync<PairingCodeView>("PairingCode");
        return await CreatePhoneClient(userId, phoneSid, userAgent)
            .PostAsJsonAsync("/api/me/screens", new PairRequest(code.Path["/p/".Length..]));
    }

    private async Task<HubConnection> ConnectWithAsync(Func<string> token)
    {
        var connection = new HubConnectionBuilder()
            .WithUrl(new Uri(Server.BaseAddress, "/hub/session"), o =>
            {
                o.HttpMessageHandlerFactory = _ => Server.CreateHandler();
                o.Transports = Microsoft.AspNetCore.Http.Connections.HttpTransportType.LongPolling;
                o.AccessTokenProvider = () => Task.FromResult<string?>(token());
            })
            // The player reads enums as the strings the server writes, so this client does too.
            .AddJsonProtocol(o => o.PayloadSerializerOptions.Converters.Add(
                new System.Text.Json.Serialization.JsonStringEnumConverter()))
            .Build();

        await connection.StartAsync();
        return connection;
    }
}

/// <summary>Discord's OAuth2 token endpoint, redeeming any code but a refused one.</summary>
public sealed class DiscordStub : HttpMessageHandler
{
    public const string AccessToken = "a-discord-access-token";
    public const string RefusedCode = "refused";

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        var form = await request.Content!.ReadAsStringAsync(ct);
        return form.Contains($"code={RefusedCode}", StringComparison.Ordinal)
            ? new HttpResponseMessage(System.Net.HttpStatusCode.BadRequest) { Content = new StringContent("""{"error":"invalid_grant"}""") }
            : new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent($$"""{"access_token":"{{AccessToken}}","token_type":"Bearer","expires_in":604800}""",
                    System.Text.Encoding.UTF8, "application/json"),
            };
    }
}
