using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Options;
using TheKrystalShip.MovieBot.Acquire;
using TheKrystalShip.MovieBot.Api;
using TheKrystalShip.MovieBot.Api.Acquisition;
using TheKrystalShip.MovieBot.Api.Auth;
using TheKrystalShip.MovieBot.Api.Data;
using TheKrystalShip.MovieBot.Api.Discord;
using TheKrystalShip.MovieBot.Acquire.Tmdb;
using TheKrystalShip.MovieBot.Api.Films;
using TheKrystalShip.MovieBot.Api.Library;
using TheKrystalShip.MovieBot.Api.Plans;
using TheKrystalShip.MovieBot.Api.Wants;
using TheKrystalShip.MovieBot.Api.People;
using TheKrystalShip.MovieBot.Api.Subtitles;
using TheKrystalShip.MovieBot.Acquire.Configuration;
using TheKrystalShip.MovieBot.Acquire.Subtitles;
using TheKrystalShip.MovieBot.Api.Media;
using TheKrystalShip.MovieBot.Api.Notifications;
using TheKrystalShip.MovieBot.Api.Offline;
using TheKrystalShip.MovieBot.Api.Sessions;
using TheKrystalShip.MovieBot.Api.Shows;
using TheKrystalShip.MovieBot.Api.Transcode;
using TheKrystalShip.MovieBot.Core;

var builder = WebApplication.CreateBuilder(args);
builder.Configuration.AddMovieBotSettings();

builder.Services.AddSingleton(TimeProvider.System);

// Everything this service keeps, in one file in the state directory systemd makes for it.
builder.Services.AddSingleton(sp => new CinemaDatabase(
    StatePath(sp.GetRequiredService<IConfiguration>()["Database:Path"], CinemaDatabase.FileName)));

// Configuration is resolved when the library is first asked for, not while the builder is still
// being assembled: a source added after this line — as a test host does — would otherwise be
// read too late and the service would quietly point somewhere else.
builder.Services.AddSingleton(sp => new SubtitleStore(
    sp.GetRequiredService<IConfiguration>()["Subtitles:Root"]
        ?? Path.Combine(Directory.GetCurrentDirectory(), "subtitles"),
    sp.GetRequiredService<CinemaDatabase>()));
builder.Services.AddSingleton<PinStore>();
// The disk films are made on, and the one they are kept on. A cold root is an upgrade: with
// none configured, or with its volume gone, the library is what the media root holds.
builder.Services.AddSingleton(sp => new MediaRoots(
    sp.GetRequiredService<IConfiguration>()["Media:Root"]
        ?? Path.Combine(Directory.GetCurrentDirectory(), "media"),
    sp.GetRequiredService<IConfiguration>()["Media:ColdRoot"]));
builder.Services.AddSingleton(sp => new TitleLibrary(
    sp.GetRequiredService<MediaRoots>(),
    sp.GetRequiredService<SubtitleStore>(),
    sp.GetRequiredService<PinStore>(),
    sp.GetRequiredService<ILogger<TitleLibrary>>()));
builder.Services.AddSingleton<LibraryListing>();
builder.Services.AddSingleton<TitlePosters>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<LibraryListing>());
builder.Services.AddSingleton<SessionStore>();

// What each connected viewer would watch at, for the transcode planner. In memory and per
// connection: a viewer's quality never joins a room's state.
builder.Services.AddSingleton<ViewerQuality>();
builder.Services.AddSingleton<PlayerTrouble>();

// Every change to a room, whichever door it arrived by: the hub for a player in the room, HTTP for
// the bot and for a spoken command.
builder.Services.AddSingleton<RoomControls>();
builder.Services.AddSingleton<RoomDoors>();

// The television screens each person has: connected or not, the room each is in and what it holds,
// and the phones paired with each.
builder.Services.AddSingleton<ScreenPresence>();
builder.Services.AddSingleton<ScreenPairings>();
builder.Services.AddSingleton<PairingCodes>();

// The guests watching each room on its link, and the pass that sends them away once the link has run
// out or nobody with an account is left in the room with them.
builder.Services.AddSingleton<RoomGuests>();
builder.Services.AddSingleton<GuestKeeper>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<GuestKeeper>());

// What a room is watching and where it has got to exists nowhere but in memory; what is kept of it
// is what a restart puts back.
builder.Services.AddSingleton<RoomStore>();
builder.Services.AddSingleton<SessionKeeper>();

// How each person watches, which follows them from the site into the Activity and back.
builder.Services.AddSingleton<PreferenceStore>();
builder.Services.AddSingleton<SortStore>();
builder.Services.AddSingleton<TrackStore>();
builder.Services.AddSingleton<ViewerPush>();

// What each account is called, so an id on a record can be shown as a person.
builder.Services.AddSingleton<PersonDirectory>();

// What each person has watched and when each film was last played, which the library's clock reads.
builder.Services.AddSingleton<WatchRecord>();
builder.Services.AddSingleton<IPlayHistory>(sp => sp.GetRequiredService<WatchRecord>());
// Written from the rooms as they change: the room is where a film's position lives.
builder.Services.AddSingleton<WatchRecorder>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<WatchRecorder>());

// What a film is, from the film database, kept with its artwork and served from here.
builder.Services.Configure<TmdbOptions>(builder.Configuration.GetSection(TmdbOptions.Section));
builder.Services.AddOptions<FilmStoreOptions>().Bind(builder.Configuration.GetSection(TmdbOptions.Section));
builder.Services.AddHttpClient<TmdbClient>((sp, http) =>
    http.Timeout = TimeSpan.FromSeconds(sp.GetRequiredService<IOptions<TmdbOptions>>().Value.TimeoutSeconds))
    .KeepConnectionsOpen();

// IMDb's datasets, imported daily into the database: they rank a question asked by rating and give
// every title its IMDb rating. A host where the import has not run has none and does without.
builder.Services.AddOptions<ImdbOptions>().Bind(builder.Configuration.GetSection(ImdbOptions.Section));
builder.Services.AddHttpClient<ImdbDatasets>(http => http.Timeout = Timeout.InfiniteTimeSpan);
builder.Services.AddSingleton<ImdbRatings>();
builder.Services.AddHostedService<ImdbImporter>();
builder.Services.AddSingleton<PosterPins>();
builder.Services.AddSingleton<TitleScores>();
builder.Services.AddSingleton<KeptImages>();
builder.Services.AddSingleton(sp => new FilmStore(
    StatePath(sp.GetRequiredService<IOptions<FilmStoreOptions>>().Value.Directory, "films"),
    sp.GetRequiredService<IOptions<FilmStoreOptions>>(),
    sp.GetRequiredService<IServiceScopeFactory>(),
    sp.GetRequiredService<PosterPins>(),
    sp.GetRequiredService<KeptImages>(),
    sp.GetRequiredService<CinemaDatabase>(),
    sp.GetRequiredService<TitleScores>(),
    TimeProvider.System,
    sp.GetRequiredService<ILogger<FilmStore>>()));
builder.Services.AddSingleton<EpisodeNumbering>();
builder.Services.AddHostedService<EpisodeNumberingWarmer>();
builder.Services.AddSingleton(sp => new ShowStore(
    StatePath(sp.GetRequiredService<IOptions<FilmStoreOptions>>().Value.ShowsDirectory, "shows"),
    sp.GetRequiredService<IOptions<FilmStoreOptions>>(),
    sp.GetRequiredService<IServiceScopeFactory>(),
    sp.GetRequiredService<PosterPins>(),
    sp.GetRequiredService<KeptImages>(),
    sp.GetRequiredService<CinemaDatabase>(),
    sp.GetRequiredService<TitleScores>(),
    sp.GetRequiredService<EpisodeNumbering>(),
    sp.GetRequiredService<TimeProvider>(),
    sp.GetRequiredService<ILogger<ShowStore>>()));
// Which shelf each title is on beyond its kind: anime, or everything else.
builder.Services.AddSingleton<TitleCategories>();
builder.Services.AddSingleton(sp => new TitleGlances(
    StatePath(sp.GetRequiredService<IOptions<FilmStoreOptions>>().Value.GlancesDirectory, "glances"),
    sp.GetRequiredService<FilmStore>(),
    sp.GetRequiredService<ShowStore>(),
    sp.GetRequiredService<PosterPins>(),
    sp.GetRequiredService<KeptImages>(),
    sp.GetRequiredService<CinemaDatabase>(),
    sp.GetRequiredService<TitleScores>(),
    sp.GetRequiredService<IServiceScopeFactory>(),
    sp.GetRequiredService<TimeProvider>(),
    sp.GetRequiredService<ILogger<TitleGlances>>()));
builder.Services.AddSingleton<AnswerKeeper>();

// Wikidata, asked in the background only: the sets of films the Discover shelves are made of and each
// film's awards and sources, kept like any answer. A host that cannot reach it does without.
builder.Services.AddOptions<WikidataOptions>().Bind(builder.Configuration.GetSection(WikidataOptions.Section));
builder.Services.AddSingleton<WikidataGate>();
builder.Services.AddHttpClient<WikidataClient>((sp, http) =>
    http.Timeout = TimeSpan.FromSeconds(sp.GetRequiredService<IOptions<WikidataOptions>>().Value.TimeoutSeconds));
builder.Services.AddSingleton<WikidataSets>();
builder.Services.AddSingleton<WikidataFacts>();
builder.Services.AddHostedService<WikidataWarmer>();
builder.Services.AddSingleton(sp => new Catalogue(
    StatePath(sp.GetRequiredService<IOptions<FilmStoreOptions>>().Value.CatalogueDirectory, "catalogue"),
    sp.GetRequiredService<AnswerKeeper>(),
    sp.GetRequiredService<TitleGlances>(),
    sp.GetRequiredService<TitleScores>(),
    sp.GetRequiredService<KeptImages>()));
builder.Services.AddSingleton<CatalogueSearch>();
builder.Services.AddSingleton<TitleSearch>();
builder.Services.AddHostedService<FilmWarmer>();
builder.Services.AddSingleton(sp => new PopularNow(
    StatePath(sp.GetRequiredService<IOptions<FilmStoreOptions>>().Value.PopularDirectory, "popular"),
    sp.GetRequiredService<IServiceScopeFactory>(),
    sp.GetRequiredService<PosterPins>(),
    sp.GetRequiredService<KeptImages>(),
    sp.GetRequiredService<CinemaDatabase>(),
    sp.GetRequiredService<TitleScores>(),
    sp.GetRequiredService<TimeProvider>(),
    sp.GetRequiredService<ILogger<PopularNow>>()));
builder.Services.AddHostedService(sp => sp.GetRequiredService<PopularNow>());
builder.Services.AddSingleton<DiscoverShelves>();
builder.Services.AddHostedService<DiscoverWarmer>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<SessionKeeper>());

// The subtitle index. Searching costs nothing and only a download spends the day's allowance, so
// the client is a plain singleton with its own pacing rather than anything rationed here.
builder.Services.AddOptions<OpenSubtitlesOptions>()
    .Bind(builder.Configuration.GetSection(OpenSubtitlesOptions.Section));
builder.Services.AddHttpClient<OpenSubtitlesClient>();
// Transient as the typed client it holds is.
builder.Services.AddTransient<SubtitleFetcher>();
builder.Services.AddOptions<RoomOptions>().Bind(builder.Configuration.GetSection(RoomOptions.Section));

// Cinema signs nobody in. Every person arrives with an access token the identity provider minted,
// checked against the provider's keys, and is let do what its actions claim lists.
builder.Services.AddCinemaAuthentication();

// Without the ticket key no film's bytes can be opened. Refusing to start says so once, where a
// player that loads every page and plays nothing would say it to everybody.
builder.Services.AddOptions<AuthOptions>()
    .Bind(builder.Configuration.GetSection(AuthOptions.Section))
    .Validate(o => o.MediaTicketKey.Length >= 32,
        "Auth:MediaTicketKey must be set to at least 32 characters. It signs the tickets a film's "
        + "bytes are opened with.")
    .ValidateOnStart();
builder.Services.AddHostedService<SessionReaper>();

// What a film gains while people are watching it reaches them down the connection they hold.
builder.Services.AddSingleton<TitleChanges>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<TitleChanges>());

// Fetching films: the tracker, the torrent client and the title index, and what this service does
// with them on somebody's behalf. The API is the one place a download is started, a wish is made or a
// film is kept, so the bot's commands and the assistant's tools are the same act whichever asks.
builder.Services.AddAcquire(builder.Configuration);
builder.Services.AddSingleton<OfferedReleases>();
builder.Services.AddSingleton<Downloads>();
builder.Services.AddOptions<NotifyOptions>().Bind(builder.Configuration.GetSection(NotifyOptions.Section));
builder.Services.AddSingleton<WishList>();
builder.Services.AddSingleton<Wishes>();
builder.Services.AddSingleton<WishSweep>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<WishSweep>());

// Watch now: a film fetched and played in a site room as soon as it can be opened.
builder.Services.AddOptions<WatchOptions>().Bind(builder.Configuration.GetSection(WatchOptions.Section));
builder.Services.AddSingleton<ShowOfferCache>();
builder.Services.AddSingleton<FilmOfferCache>();
builder.Services.AddSingleton<WatchNow>();
builder.Services.AddSingleton<RoomEpisodes>();
// The episode after the one a room is watching, fetched so next has something to play.
builder.Services.AddHostedService<NextEpisodes>();
builder.Services.AddHostedService<WatchNowLoader>();

// The rule for when a settled film leaves the library. It is answered here and acted on by the
// hand-off, which is the one process that writes the media roots.
builder.Services.AddOptions<LibraryOptions>().Bind(builder.Configuration.GetSection(LibraryOptions.Section));
builder.Services.AddSingleton<LibraryClock>();

// What people want to watch is fetched ahead of them when nobody is watching and the disk has room, and
// a title somebody wants and has not watched stays in the library.
builder.Services.AddOptions<WantOptions>().Bind(builder.Configuration.GetSection(WantOptions.Section));
builder.Services.AddSingleton<IWantHolds, WantHolds>();
builder.Services.AddSingleton<WantFetcher>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<WantFetcher>());

// What the GPU should be doing, published for the hand-off to carry out. The quality each viewer
// wants is what their players report to the hub, and each output's head is the manifest's.
builder.Services.AddOptions<TranscodeOptions>().Bind(builder.Configuration.GetSection(TranscodeOptions.Section));
builder.Services.AddSingleton<IViewerQuality>(sp => sp.GetRequiredService<ViewerQuality>());
builder.Services.AddSingleton<IManifestHeads, ManifestHeads>();
builder.Services.AddSingleton<TranscodeCatalog>();
builder.Services.AddSingleton<TranscodeStatusStore>();
builder.Services.AddSingleton<TranscodePlanService>();

// Telling people things: the bell and the pushes to their browsers, kept by the shared library in a
// file beside the database.
builder.Services.AddCinemaNotifications(builder.Configuration,
    () => Path.GetDirectoryName(StatePath(builder.Configuration["Database:Path"], CinemaDatabase.FileName)));
builder.Services.AddHostedService<ReadyNotices>();

// Following a show: who follows what, and the watcher that tells them when an episode is out.
builder.Services.AddOptions<FollowOptions>().Bind(builder.Configuration.GetSection(FollowOptions.Section));
builder.Services.AddSingleton<FollowStore>();
builder.Services.AddSingleton<FollowWatcher>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<FollowWatcher>());

// What each person wants to watch, and what turns a want into what a page shows.
builder.Services.AddSingleton<WantStore>();
builder.Services.AddSingleton<WantMatcher>();
builder.Services.AddSingleton<WantPresenter>();
builder.Services.AddHostedService<TogetherDigest>();

// Plans to watch something together: the record, what tells the people in a plan, the presenter that
// measures its title's readiness, and the keeper that reminds them and makes the room ahead of time.
builder.Services.AddOptions<PlanOptions>().Bind(builder.Configuration.GetSection(PlanOptions.Section));
builder.Services.AddSingleton<PlanStore>();
builder.Services.AddSingleton<PlanTitles>();
builder.Services.AddSingleton<PlanNotices>();
builder.Services.AddSingleton<PlanPush>();
builder.Services.AddSingleton<PlanService>();
builder.Services.AddSingleton<PlanPresenter>();
builder.Services.AddSingleton<PlanKeeper>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<PlanKeeper>());

// Save offline: what each person saved onto which device or asked one to download, the tracks
// measured for it, and the transfers under way, which yield to the rooms.
builder.Services.AddOptions<OfflineOptions>().Bind(builder.Configuration.GetSection(OfflineOptions.Section));
builder.Services.AddSingleton<OfflineSaves>();
builder.Services.AddSingleton<ISaveHolds>(sp => sp.GetRequiredService<OfflineSaves>());
builder.Services.AddSingleton<OfflineWanting>();
builder.Services.AddSingleton<OfflineEstimates>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<OfflineEstimates>());
builder.Services.AddSingleton<TrackIndexes>();
builder.Services.AddSingleton<Transfers>();

// The Activity's way in: its SDK code is redeemed with Cinema's own Discord application, whose client
// id is the application id the settings file names once for the bot and the API together, and the
// Discord token it buys is exchanged at the provider for one of the provider's.
builder.Services.AddOptions<DiscordAuthOptions>()
    .Bind(builder.Configuration.GetSection(DiscordAuthOptions.Section))
    .PostConfigure<IConfiguration>((options, configuration) =>
    {
        if (options.ClientId.Length == 0 && configuration["Discord:ApplicationId"] is { Length: > 0 } id)
            options.ClientId = id;
    });
builder.Services.AddHttpClient<DiscordAuthClient>(http => http.Timeout = TimeSpan.FromSeconds(10));
builder.Services.AddHttpClient<TokenExchangeClient>(http => http.Timeout = TimeSpan.FromSeconds(10));

// Every type that crosses the wire is named in a serializer context, and the two contexts are the
// only resolver the hub and the endpoints have: nothing is reached by reflection, so a type left
// out fails in the JIT build the tests run exactly as it would in the native one.
// A guest's connection may call only the hub methods marked for one, and a remote's only those marked for it.
builder.Services.AddSignalR(o => { o.AddFilter<GuestGate>(); o.AddFilter<RemoteGate>(); })
    .AddJsonProtocol(o =>
    {
        o.PayloadSerializerOptions.TypeInfoResolver = ApiJson.Resolver;
        o.PayloadSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
        o.PayloadSerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
    });

builder.Services.ConfigureHttpJsonOptions(o =>
{
    o.SerializerOptions.TypeInfoResolver = ApiJson.Resolver;
    o.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
    o.SerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
});

// The Activity is served from Discord's proxy and the standalone page from wherever it is
// hosted, so the origin is never known ahead of time. Reflect it rather than enumerating a
// list that will be wrong the first time a front door moves; credentials are not used, the
// content is not secret, and an enumerated list fails invisibly at the preflight.
builder.Services.AddCors(o => o.AddDefaultPolicy(p => p
    .SetIsOriginAllowed(_ => true)
    .AllowAnyHeader()
    .AllowAnyMethod()
    .AllowCredentials()));

var app = builder.Build();
app.Logger.LogInformation("Settings: {Files}", MovieBotSettings.Describe());

// Every record here is keyed by an account the provider named. Another provider's accounts are
// strangers to every one of them, so the API refuses to start against one it was not told to adopt.
var oidc = app.Services.GetRequiredService<IOptions<OidcOptions>>().Value;
IssuerRecord.Enforce(oidc, app.Services.GetRequiredService<CinemaDatabase>(), app.Logger);

// Before anything can serve a request. A room restored after the first client has joined is a
// room that client was already told did not exist.
app.Services.GetRequiredService<SessionKeeper>().Restore();

app.UseCors();

// Cinema, whose player is also the Discord Activity, is served from this origin at its
// root, which is what lets an Activity reach the page, the API, the media and the hub through a
// single declared URL mapping instead of a set of them. Static files sit under wwwroot, open to
// anybody, and never shadow /api, /media or /hub, which are explicit routes. They are read from disk
// on every request, so a site installed there is served at once, without restarting this; only a
// wwwroot that exists when this starts is served at all.
app.UseDefaultFiles();
app.UseStaticFiles();

// Every route below names what it needs, and one that names nothing admits nobody.
app.UseAuthentication();
app.UseAuthorization();

// Occupancy, and no names: it is open, so what it may say is how many rooms are being watched and
// nothing about who is in them. It is what makes a restart something that can be looked at first
// rather than something to find out about afterwards.
app.MapGet("/health", (SessionStore sessions, MediaRoots roots) =>
{
    var rooms = sessions.Occupied();

    // The cold volume is named only when something is wrong with it. A library short of every
    // film that has settled looks like films disappearing, and this is the one place somebody
    // checking on the service would find out why.
    return Results.Ok(new HealthReport(
        "ok", rooms.Count, rooms.Sum(r => r.Participants), roots.ColdState.Problem));
}).AllowAnonymous();

// What the page needs before it can sign anybody in, none of it a secret: the provider, the client
// the site signs in with and the one a television signs in with, the claim a person's actions arrive in, and the Discord application id,
// which is in every invite link and in the Activity's own URL. Serving it means the page needs no
// build-time configuration and no rebuild when any of it changes. The public address rides along
// for the same reason: it is where Discord's own servers fetch a poster from, which the page cannot
// learn from an origin that is Discord's proxy.
app.MapGet("/api/config", (IOptions<DiscordAuthOptions> discord, IOptions<OidcOptions> oidc, IConfiguration configuration) =>
    Results.Ok(new ClientConfig(
        discord.Value.ClientId is { Length: > 0 } clientId ? clientId : null,
        configuration["Api:PublicBaseUrl"] is { Length: > 0 } url ? url.TrimEnd('/') : null,
        oidc.Value.Issuer,
        oidc.Value.SiteClientId,
        oidc.Value.TvClientId,
        oidc.Value.ActionsClaim)))
    .AllowAnonymous();

// The actions Cinema checks, for the provider to read, written from the same declarations every
// route's check names.
app.MapGet("/.well-known/tks-actions.json", () => Results.Ok(CinemaActions.Manifest())).AllowAnonymous();

// The television app built for this site (`tv/`), which a TV fetches with Downloader from an address
// short enough to type with a remote. It is kept apart from the site's files, `Tv:App` or `tv.apk` in
// the state directory, so deploying either leaves the other in place. A host without one answers 404.
// It goes without a Content-Disposition: the address already names the file, and Downloader stalls on
// one that carries the RFC 5987 `filename*` form beside `filename`.
app.MapMethods("/tv.apk", [HttpMethods.Get, HttpMethods.Head], (IConfiguration configuration, HttpContext context) =>
{
    var path = StatePath(configuration["Tv:App"], "tv.apk");
    if (!File.Exists(path)) return Results.NotFound();
    context.Response.Headers.CacheControl = "no-cache";
    return Results.File(path, "application/vnd.android.package-archive", enableRangeProcessing: true);
}).AllowAnonymous();

// The phone app built for this site (`phone/`), which downloads films with the screen off. Kept apart
// as the television's is, `Phone:App` or `phone.apk` in the state directory; a host without one answers 404.
app.MapMethods("/phone.apk", [HttpMethods.Get, HttpMethods.Head], (IConfiguration configuration, HttpContext context) =>
{
    var path = StatePath(configuration["Phone:App"], "phone.apk");
    if (!File.Exists(path)) return Results.NotFound();
    context.Response.Headers.CacheControl = "no-cache";
    return Results.File(path, "application/vnd.android.package-archive", enableRangeProcessing: true);
}).AllowAnonymous();

// Each title with what the film database said about it, from what is kept: a listing never waits
// on the network, and a film not described yet is listed without it. Each carries the asking
// person's progress and when the film was last played, and a caller that is nobody gets no progress.
app.MapGet("/api/titles", (HttpContext context, LibraryListing library, FilmStore films, ShowStore shows,
    TitleCategories categories, TitlePosters posters, WatchRecord record, PersonDirectory people, SessionStore sessions, TimeProvider clock) =>
{
    var person = PeopleEndpoints.Person(context);
    var viewing = sessions.Viewing();
    var now = clock.GetUtcNow();
    return Results.Ok(library.Titles()
        .Select(t => (posters.Listed(t) with
        {
            // An episode is described by its show.
            Details = t.Kind == TitleKind.Show ? shows.Facts(t.Film?.ImdbId) : films.Facts(t.Film?.ImdbId),
            Category = categories.Of(t.Kind, t.Film?.ImdbId),
        }).WithPlays(person, record, people, viewing, now))
        .ToList());
}).RequireActionOrService(CinemaActions.Watch);

// A guest reads the film their room holds, and no other.
app.MapGet("/api/titles/{id}", (string id, TitleLibrary library) =>
    library.Get(id) is { } manifest ? Results.Ok(manifest) : Results.NotFound())
    .RequireActionServiceOrGuest(GuestReach.RoomTitle, CinemaActions.Watch);

// What a player then carries on the film's own bytes, in place of who it is. Minted per request
// rather than written into the manifest: the manifest is a file on disk that outlives any key. A
// guest is handed the ticket of the film their room holds.
app.MapGet("/api/titles/{id}/ticket", (
    string id, TitleLibrary library, IOptions<AuthOptions> auth, TimeProvider clock) =>
{
    if (library.Get(id) is null) return Results.NotFound();

    var now = clock.GetUtcNow();
    var expires = MediaTicket.ExpiresAt(now);
    return Results.Ok(new MediaTicketReply(
        MediaTicket.Issue(id, auth.Value, now), expires, (long)(expires - now).TotalSeconds));
}).RequireActionOrGuest(GuestReach.RoomTitle, CinemaActions.Watch);

app.MapSubtitles();
app.MapAcquisition();
app.MapTranscode();
app.MapPeople();
app.MapFilms();
app.MapCatalogueSearch();
app.MapDiscover();
app.MapShows();
app.MapFollows();
app.MapWants();
app.MapPlans();
app.MapCinemaNotifications();
app.MapInvites();
app.MapOffline();

// Every room, for the bot as much as the site: it holds no state of its own, so what to say about
// rooms it reads from here each time it is about to say it. A person is listed the rooms they may
// enter, so a private room is not in anybody else's list.
app.MapGet("/api/sessions", (HttpContext context, SessionStore sessions) =>
    Results.Ok(sessions.Summaries(visibleTo: CinemaPerson.Of(context)?.UserId)))
    .RequireActionOrService(CinemaActions.Watch);

app.MapGet("/api/sessions/{sessionId}", (string sessionId, SessionStore sessions) =>
    Results.Ok(new SessionStatePush
    {
        State = sessions.GetOrCreate(sessionId),
        ServerTime = DateTimeOffset.UtcNow
    }))
    .RequireActionOrService(CinemaActions.Watch)
    .RequireRoomEntry();

app.MapGet("/api/sessions/{sessionId}/participants", (string sessionId, SessionStore sessions) =>
    Results.Ok(sessions.Participants(sessionId)))
    .RequireAction(CinemaActions.Watch)
    .RequireRoomEntry();

// The episodes either side of what the room holds, which a player's previous and next name; an
// episode the library lacks is offered only to somebody who may fetch it.
app.MapGet("/api/sessions/{sessionId}/around", (string sessionId, HttpContext context, RoomEpisodes episodes) =>
    Results.Ok(episodes.Around(sessionId, CinemaPerson.Of(context)?.Holds(CinemaActions.Download) == true)))
    .RequireAction(CinemaActions.Watch)
    .RequireRoomEntry();

app.MapRooms();
app.MapGuests();
app.MapPairings();
app.MapActivitySignIn();

app.MapMedia();

// A room's connection is admitted when it opens, on the token it carries then, or on a room's guest
// link, which the hub holds to watching.
app.MapHub<SessionHub>("/hub/session").RequireActionOrGuest(GuestReach.Hub, CinemaActions.Watch);

// The site names its pages by path, so a film's page and a room can be linked to and reloaded. A GET
// for a path that names no file, anywhere but under the API's, the media's and the hub's prefixes,
// is a page and is answered with the site's index. The fallback answers it, because an endpoint
// matched ahead of the static files means they never see the request. Anything under the API's own
// prefixes that matched nothing stays a 404.
app.MapFallback(async (HttpContext context, IWebHostEnvironment environment, IConfiguration configuration,
    SessionStore sessions, TitleLibrary library, FilmStore films, GuestCodes codes) =>
{
    var request = context.Request;
    if (!HttpMethods.IsGet(request.Method) && !HttpMethods.IsHead(request.Method)) return Results.NotFound();
    if (request.Path.StartsWithSegments("/api") || request.Path.StartsWithSegments("/media")
        || request.Path.StartsWithSegments("/hub") || Path.HasExtension(request.Path.Value))
        return Results.NotFound();

    var index = environment.WebRootFileProvider.GetFileInfo("index.html");
    if (!index.Exists || index.PhysicalPath is null) return Results.NotFound();

    // The index names the build's hashed assets, so a stale copy would load the last release's.
    context.Response.Headers.CacheControl = "no-cache";

    // A room's page carries the tags a chat client unfurls a link with, written here because a
    // crawler does not run the page's script. The image has to be an absolute address on the public
    // one, which is where whatever unfurls the link fetches it from. A good guest link unfurls with
    // its room's film whether or not the room is open.
    var guestRoom = RoomPreview.GuestCodeOf(request.Path) is { } code && codes.PassIdOf(code) is { } passId
        ? sessions.RoomOfGuests(passId)
        : null;
    if ((RoomPreview.RoomOf(request.Path) ?? guestRoom) is { } room)
    {
        var baseUrl = configuration["Api:PublicBaseUrl"] is { Length: > 0 } configured
            ? configured.TrimEnd('/')
            : $"{request.Scheme}://{request.Host}";
        var tags = RoomPreview.Tags(room, request.Query["title"].FirstOrDefault(), sessions, library, films,
            baseUrl, baseUrl + request.Path + request.QueryString, invited: guestRoom is not null);
        var page = await File.ReadAllTextAsync(index.PhysicalPath);
        var at = page.IndexOf("</head>", StringComparison.OrdinalIgnoreCase);
        return Results.Content(at < 0 ? page : page.Insert(at, tags), "text/html; charset=utf-8");
    }

    return Results.File(index.PhysicalPath, "text/html; charset=utf-8");
}).AllowAnonymous();

var roots = app.Services.GetRequiredService<MediaRoots>();
app.Logger.LogInformation("Serving media from {MediaRoot}", roots.Hot);

if (roots.Cold is { } coldRoot)
{
    if (roots.ColdState.Ready)
    {
        app.Logger.LogInformation("Films that have settled are served from {ColdRoot}.", coldRoot);
    }
    else
    {
        // Reported and carried on with. The cold volume holds capacity rather than the service:
        // every film on the media root is served exactly as it would be with no cold root at
        // all, and refusing to start would take those down too over a disk that is only an
        // upgrade. What has settled is missing from the library until the volume is back.
        app.Logger.LogError(
            "Cold storage is unusable: {Problem} Films kept there are out of the library until "
            + "it is back; everything under {MediaRoot} is served as usual.",
            roots.ColdState.Problem, roots.Hot);
    }
}

app.Run();

// A path a setting names, or the name under the directory systemd makes for this service and hands
// over in the environment (the first, when it names several), and the working directory without one.
// An empty setting is the default, which is what the shipped settings file spells it as.
static string StatePath(string? configured, string name) =>
    !string.IsNullOrWhiteSpace(configured)
        ? Path.GetFullPath(configured)
        : Path.Combine(
            Environment.GetEnvironmentVariable("STATE_DIRECTORY")?.Split(':').FirstOrDefault() is { Length: > 0 } state
                ? state
                : Directory.GetCurrentDirectory(),
            name);

/// <summary>Exposed so the test project can drive the app in-process.</summary>
public partial class Program;
