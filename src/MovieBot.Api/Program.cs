using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Options;
using TheKrystalShip.MovieBot.Acquire;
using TheKrystalShip.MovieBot.Api;
using TheKrystalShip.MovieBot.Api.Acquisition;
using TheKrystalShip.MovieBot.Api.Auth;
using TheKrystalShip.MovieBot.Api.Discord;
using TheKrystalShip.MovieBot.Acquire.Tmdb;
using TheKrystalShip.MovieBot.Api.Films;
using TheKrystalShip.MovieBot.Api.Library;
using TheKrystalShip.MovieBot.Api.People;
using TheKrystalShip.MovieBot.Api.Subtitles;
using TheKrystalShip.MovieBot.Acquire.Configuration;
using TheKrystalShip.MovieBot.Acquire.Subtitles;
using TheKrystalShip.MovieBot.Api.Media;
using TheKrystalShip.MovieBot.Api.Sessions;
using TheKrystalShip.MovieBot.Core;

var builder = WebApplication.CreateBuilder(args);
builder.Configuration.AddMovieBotSettings();

builder.Services.AddSingleton(TimeProvider.System);

// Configuration is resolved when the library is first asked for, not while the builder is still
// being assembled: a source added after this line — as a test host does — would otherwise be
// read too late and the service would quietly point somewhere else.
builder.Services.AddSingleton(sp => new SubtitleStore(
    sp.GetRequiredService<IConfiguration>()["Subtitles:Root"]
        ?? Path.Combine(Directory.GetCurrentDirectory(), "subtitles"),
    sp.GetRequiredService<ILogger<SubtitleStore>>()));
builder.Services.AddSingleton(sp => new PinStore(
    sp.GetRequiredService<IConfiguration>()["Subtitles:Root"]
        ?? Path.Combine(Directory.GetCurrentDirectory(), "subtitles"),
    sp.GetRequiredService<ILogger<PinStore>>()));
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
builder.Services.AddSingleton<SessionStore>();

// Every change to a room, whichever door it arrived by: the hub for a player in the room, HTTP for
// the bot and for a spoken command.
builder.Services.AddSingleton<RoomControls>();

// What a room is watching and where it has got to exists nowhere but in memory, so a restart used
// to take the film out from under everybody in it. The journal is the only copy.
builder.Services.AddSingleton(sp => new SessionJournal(
    sp.GetRequiredService<IConfiguration>()["Rooms:Journal"]
        // The directory systemd makes for this service and hands over in the environment. A unit
        // that names one is the only reason anything here may write outside the media root.
        ?? Path.Combine(
            Environment.GetEnvironmentVariable("STATE_DIRECTORY")
                ?? Directory.GetCurrentDirectory(), "rooms.json"),
    sp.GetRequiredService<ILogger<SessionJournal>>()));
builder.Services.AddSingleton<SessionKeeper>();

// How each person watches, which follows them from the site into the Activity and back.
builder.Services.AddSingleton(sp => new PreferenceStore(
    StatePath(sp.GetRequiredService<IConfiguration>()["People:Preferences"], "preferences.json"),
    sp.GetRequiredService<ILogger<PreferenceStore>>()));

// What each Discord id is called, so an id on a record can be shown as a person.
builder.Services.AddSingleton(sp => new PersonDirectory(
    StatePath(sp.GetRequiredService<IConfiguration>()["People:Directory"], "people.json"),
    sp.GetRequiredService<ILogger<PersonDirectory>>()));

// What each person has watched and when each film was last played, which the library's clock reads.
builder.Services.AddSingleton(sp => new WatchRecord(
    StatePath(sp.GetRequiredService<IConfiguration>()["People:Watch"], "watch.json"),
    sp.GetRequiredService<ILogger<WatchRecord>>()));
builder.Services.AddSingleton<IPlayHistory>(sp => sp.GetRequiredService<WatchRecord>());
// Written from the rooms as they change: the room is where a film's position lives.
builder.Services.AddSingleton<WatchRecorder>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<WatchRecorder>());

// The devices people are signed in on, which is what lets one be signed out from another.
builder.Services.AddSingleton(sp => new DeviceSessions(
    StatePath(sp.GetRequiredService<IConfiguration>()["People:Devices"], "devices.json"),
    sp.GetRequiredService<TimeProvider>(),
    sp.GetRequiredService<ILogger<DeviceSessions>>()));

// What a film is, from the film database, kept on disk with its artwork and served from here.
builder.Services.Configure<TmdbOptions>(builder.Configuration.GetSection(TmdbOptions.Section));
builder.Services.AddOptions<FilmStoreOptions>().Bind(builder.Configuration.GetSection(TmdbOptions.Section));
builder.Services.AddHttpClient<TmdbClient>((sp, http) =>
    http.Timeout = TimeSpan.FromSeconds(sp.GetRequiredService<IOptions<TmdbOptions>>().Value.TimeoutSeconds));
builder.Services.AddSingleton(sp => new FilmStore(
    StatePath(sp.GetRequiredService<IOptions<FilmStoreOptions>>().Value.Directory, "films"),
    sp.GetRequiredService<IOptions<FilmStoreOptions>>(),
    sp.GetRequiredService<IServiceScopeFactory>(),
    TimeProvider.System,
    sp.GetRequiredService<ILogger<FilmStore>>()));
builder.Services.AddHostedService<FilmWarmer>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<SessionKeeper>());

// The subtitle index. Searching costs nothing and only a download spends the day's allowance, so
// the client is a plain singleton with its own pacing rather than anything rationed here.
builder.Services.AddOptions<OpenSubtitlesOptions>()
    .Bind(builder.Configuration.GetSection(OpenSubtitlesOptions.Section));
builder.Services.AddHttpClient<OpenSubtitlesClient>();
builder.Services.AddOptions<RoomOptions>().Bind(builder.Configuration.GetSection(RoomOptions.Section));

// Without a signing key nothing can be minted or checked, and the films would be served to
// anybody who knows the hostname. Refusing to start is the only safe reading of a missing key.
builder.Services.AddOptions<AuthOptions>()
    .Bind(builder.Configuration.GetSection(AuthOptions.Section))
    .Validate(o => o.SigningKey.Length >= 32,
        "Auth:SigningKey must be set to at least 32 characters. Discord is the only way in, and "
        + "that is enforced with a signed token.")
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

// Who keeps which film, and the rule for when a settled film that nobody keeps leaves. Both are
// answered here and acted on by the hand-off, which is the one process that writes the media roots.
builder.Services.AddOptions<LibraryOptions>().Bind(builder.Configuration.GetSection(LibraryOptions.Section));
builder.Services.AddSingleton(sp => new KeepStore(
    StatePath(sp.GetRequiredService<IConfiguration>()["Library:Keeps"], "keeps.json"),
    sp.GetRequiredService<ILogger<KeepStore>>()));
// A keeper is shown by the name this gives, and by their id where it gives none.
builder.Services.AddSingleton<KeeperNames>(sp => sp.GetRequiredService<PersonDirectory>().NameOf);
builder.Services.AddSingleton<LibraryClock>();

// The client id is the application id, which the settings file names once for the bot and the
// API together.
builder.Services.AddOptions<DiscordAuthOptions>()
    .Bind(builder.Configuration.GetSection(DiscordAuthOptions.Section))
    .PostConfigure<IConfiguration>((options, configuration) =>
    {
        if (options.ClientId.Length == 0 && configuration["Discord:ApplicationId"] is { Length: > 0 } id)
            options.ClientId = id;
    });
builder.Services.AddHttpClient<DiscordAuthClient>(http => http.Timeout = TimeSpan.FromSeconds(10));

// Every type that crosses the wire is named in a serializer context, and the two contexts are the
// only resolver the hub and the endpoints have: nothing is reached by reflection, so a type left
// out fails in the JIT build the tests run exactly as it would in the native one.
builder.Services.AddSignalR()
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

// Before anything can serve a request. A room restored after the first client has joined is a
// room that client was already told did not exist.
app.Services.GetRequiredService<SessionKeeper>().Restore();

app.UseCors();

// Everything below this line is closed unless the caller came through Discord.
app.UseMiddleware<RequireTokenMiddleware>();

// Krystal Cinema, whose player is also the Discord Activity, is served from this origin at its
// root, which is what lets an Activity reach the page, the API, the media and the hub through a
// single declared URL mapping instead of a set of them. Static files sit under wwwroot and never shadow
// /api, /media or /hub, which are explicit routes.
app.UseDefaultFiles();
app.UseStaticFiles();

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
});

// The application id is not a secret — it is in every invite link and in the Activity's own URL.
// Serving it means the player needs no build-time configuration and no rebuild when it changes.
// The public address rides along for the same reason: it is where Discord's own servers fetch a
// poster from, which the page cannot learn from an origin that is Discord's proxy.
app.MapGet("/api/config", (IOptions<DiscordAuthOptions> discord, IConfiguration configuration) =>
    Results.Ok(new ClientConfig(
        discord.Value.ClientId,
        configuration["Api:PublicBaseUrl"] is { Length: > 0 } url ? url.TrimEnd('/') : null)));

// Each title with what the film database said about it, from what is kept: a listing never waits
// on the network, and a film not described yet is listed without it. Each carries the asking
// person's progress and when the film was last played, and a caller that is nobody gets no progress.
app.MapGet("/api/titles", (HttpContext context, TitleLibrary library, FilmStore films,
    WatchRecord record, PersonDirectory people, SessionStore sessions, TimeProvider clock) =>
{
    var person = PeopleEndpoints.Person(context);
    var viewing = sessions.Viewing();
    var now = clock.GetUtcNow();
    return Results.Ok(library.List()
        .Select(t => (t with { Details = films.Facts(t.Film?.ImdbId) }).WithPlays(person, record, people, viewing, now))
        .ToList());
});

app.MapGet("/api/titles/{id}", (string id, TitleLibrary library) =>
    library.Get(id) is { } manifest ? Results.Ok(manifest) : Results.NotFound());

// What a player then carries on the film's own bytes, in place of who it is. Minted per request
// rather than written into the manifest: the manifest is a file on disk that outlives any key.
app.MapGet("/api/titles/{id}/ticket", (
    string id, TitleLibrary library, IOptions<AuthOptions> auth, TimeProvider clock) =>
{
    if (library.Get(id) is null) return Results.NotFound();

    var now = clock.GetUtcNow();
    return Results.Ok(new MediaTicketReply(
        MediaTicket.Issue(id, auth.Value, now), MediaTicket.ExpiresAt(now)));
});

app.MapSubtitles();
app.MapAcquisition();
app.MapPeople();
app.MapDevices();
app.MapFilms();

// Every room, for the bot: it holds no state of its own, so what to say about rooms it reads
// from here each time it is about to say it.
app.MapGet("/api/sessions", (SessionStore sessions) => Results.Ok(sessions.Summaries()));

app.MapGet("/api/sessions/{sessionId}", (string sessionId, SessionStore sessions) =>
    Results.Ok(new SessionStatePush
    {
        State = sessions.GetOrCreate(sessionId),
        ServerTime = DateTimeOffset.UtcNow
    }));

app.MapGet("/api/sessions/{sessionId}/participants", (string sessionId, SessionStore sessions) =>
    Results.Ok(sessions.Participants(sessionId)));

app.MapRooms();

// The Activity's authenticate() handshake. The player sends the code it was given; the secret
// needed to redeem it stays here, because a secret shipped to a browser is not a secret.
app.MapPost("/api/auth/discord/callback", async (
    DiscordCodeRequest request,
    IOptions<DiscordAuthOptions> options,
    DiscordAuthClient discord,
    IOptions<AuthOptions> authOptions,
    PersonDirectory people,
    DeviceSessions devices,
    HttpContext context,
    CancellationToken ct) =>
{
    if (!options.Value.IsConfigured)
        return Results.Problem("Discord OAuth is not configured on this server.", statusCode: 503);

    if (string.IsNullOrWhiteSpace(request.Code))
        return Results.BadRequest(new ErrorReply("A code is required."));

    var accessToken = await discord.ExchangeCodeAsync(request.Code, options.Value, request.RedirectUri, ct);
    if (accessToken is null)
        return Results.BadRequest(new ErrorReply("Discord refused the code."));

    var user = await discord.GetUserAsync(accessToken, ct);
    if (user is null)
        return Results.BadRequest(new ErrorReply("Discord issued a token it then would not answer for."));

    // A sign-in through the redirect is the site's, reachable by anybody with a Discord account, so
    // it is for members of the server alone. The Activity sends no redirect: it only opens inside
    // that server.
    var member = false;
    if (!string.IsNullOrWhiteSpace(request.RedirectUri))
    {
        if (options.Value.GuildIds.Length == 0)
            return Results.Json(new ErrorReply("No server is configured, so nobody can sign in here."),
                ApiJsonContext.Default.ErrorReply, statusCode: StatusCodes.Status503ServiceUnavailable);

        switch (await discord.IsMemberOfAnyAsync(accessToken, options.Value.GuildIds, ct))
        {
            case null:
                return Results.Json(new ErrorReply("Discord could not say whether you are in the server. Try again."),
                    ApiJsonContext.Default.ErrorReply, statusCode: StatusCodes.Status502BadGateway);
            case false:
                return Results.Json(new ErrorReply("Krystal Cinema is for members of the server MovieBot plays films in."),
                    ApiJsonContext.Default.ErrorReply, statusCode: StatusCodes.Status403Forbidden);
        }

        member = true;
    }

    people.Remember(user.Id, user.DisplayName);

    var auth = authOptions.Value;
    var expiresAt = DateTimeOffset.UtcNow.Add(auth.TokenLifetime);
    var tokenId = RoomToken.NewTokenId();
    var roomToken = RoomToken.Issue(new RoomTokenPayload
    {
        UserId = user.Id,
        DisplayName = user.DisplayName,
        RoomId = request.SessionId ?? "",
        ExpiresAtUnix = expiresAt.ToUnixTimeSeconds(),
        Member = member,
        TokenId = tokenId,
    }, auth.SigningKey);

    // The Activity's sign-in sends no redirect and the site's always does; the Activity's browser
    // is Discord's own, so it is named for what it is.
    devices.Record(tokenId, user.Id,
        string.IsNullOrWhiteSpace(request.RedirectUri)
            ? DeviceName.Activity
            : DeviceName.FromUserAgent(context.Request.Headers.UserAgent.ToString()),
        expiresAt);

    // The access token goes back so the SDK can complete authenticate(); it is the browser's own
    // token and is scoped to the Activity, unlike the secret that redeemed the code.
    return Results.Ok(new DiscordSignIn(
        accessToken, roomToken, new SignedInUser(user.Id, user.Username, user.DisplayName)));
});

// For the bot and for the browser checks: a caller that already proves itself with the service
// key can be handed a token, because there is no Discord user for it to be.
app.MapPost("/api/auth/service-token", (
    ServiceTokenRequest request,
    HttpContext context,
    IOptions<AuthOptions> authOptions,
    PersonDirectory people,
    DeviceSessions devices) =>

{
    var auth = authOptions.Value;
    var offered = context.Request.Headers["X-MovieBot-Service"].ToString();

    if (auth.ServiceKey.Length == 0
        || !System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(
               System.Text.Encoding.UTF8.GetBytes(offered),
               System.Text.Encoding.UTF8.GetBytes(auth.ServiceKey)))
    {
        return Results.Unauthorized();
    }

    people.Remember(request.UserId, request.DisplayName);
    var expiresAt = DateTimeOffset.UtcNow.Add(auth.TokenLifetime);
    var tokenId = RoomToken.NewTokenId();
    devices.Record(tokenId, request.UserId, DeviceName.Service, expiresAt);

    return Results.Ok(new ServiceTokenReply(RoomToken.Issue(new RoomTokenPayload
    {
        UserId = request.UserId,
        DisplayName = request.DisplayName,
        RoomId = request.RoomId,
        ExpiresAtUnix = expiresAt.ToUnixTimeSeconds(),
        Member = request.Member,
        TokenId = tokenId,
    }, auth.SigningKey)));
});

app.MapMedia();
app.MapHub<SessionHub>("/hub/session");

// The site names its pages by path, so a film's page and a room can be linked to and reloaded. A GET
// for a path that names no file, anywhere but under the API's, the media's and the hub's prefixes,
// is a page and is answered with the site's index. The fallback answers it, because an endpoint
// matched ahead of the static files means they never see the request. Anything under the API's own
// prefixes that matched nothing stays a 404.
app.MapFallback(async (HttpContext context, IWebHostEnvironment environment, IConfiguration configuration,
    SessionStore sessions, TitleLibrary library, FilmStore films) =>
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
    // one, which is where whatever unfurls the link fetches it from.
    if (RoomPreview.RoomOf(request.Path) is { } room)
    {
        var baseUrl = configuration["Api:PublicBaseUrl"] is { Length: > 0 } configured
            ? configured.TrimEnd('/')
            : $"{request.Scheme}://{request.Host}";
        var tags = RoomPreview.Tags(room, request.Query["title"].FirstOrDefault(), sessions, library, films,
            baseUrl, baseUrl + request.Path + request.QueryString);
        var page = await File.ReadAllTextAsync(index.PhysicalPath);
        var at = page.IndexOf("</head>", StringComparison.OrdinalIgnoreCase);
        return Results.Content(at < 0 ? page : page.Insert(at, tags), "text/html; charset=utf-8");
    }

    return Results.File(index.PhysicalPath, "text/html; charset=utf-8");
});

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

/// <summary>What the Activity posts after Discord hands it an authorization code.</summary>
public sealed record DiscordCodeRequest(string Code, string? RedirectUri, string? SessionId);

/// <summary>
/// A token for a caller that has no Discord user of its own. <see cref="Member"/> is the service's to
/// vouch for, as everything else in the request is.
/// </summary>
public sealed record ServiceTokenRequest(string RoomId, string UserId, string DisplayName, bool Member = false);

/// <summary>Exposed so the test project can drive the app in-process.</summary>
public partial class Program;
