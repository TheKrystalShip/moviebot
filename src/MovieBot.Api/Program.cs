using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Options;
using TheKrystalShip.MovieBot.Api.Auth;
using TheKrystalShip.MovieBot.Api.Discord;
using TheKrystalShip.MovieBot.Api.Library;
using TheKrystalShip.MovieBot.Api.Subtitles;
using TheKrystalShip.MovieBot.Acquire.Configuration;
using TheKrystalShip.MovieBot.Acquire.Subtitles;
using TheKrystalShip.MovieBot.Api.Media;
using TheKrystalShip.MovieBot.Api.Sessions;
using TheKrystalShip.MovieBot.Core;

var builder = WebApplication.CreateBuilder(args);

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
builder.Services.AddSingleton(sp => new TitleLibrary(
    sp.GetRequiredService<IConfiguration>()["Media:Root"]
        ?? Path.Combine(Directory.GetCurrentDirectory(), "media"),
    sp.GetRequiredService<SubtitleStore>(),
    sp.GetRequiredService<PinStore>(),
    sp.GetRequiredService<ILogger<TitleLibrary>>()));
builder.Services.AddSingleton<SessionStore>();

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

builder.Services.AddOptions<DiscordAuthOptions>()
    .Bind(builder.Configuration.GetSection(DiscordAuthOptions.Section));
builder.Services.AddHttpClient<DiscordAuthClient>(http => http.Timeout = TimeSpan.FromSeconds(10));

builder.Services.AddSignalR()
    .AddJsonProtocol(o =>
    {
        o.PayloadSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
        o.PayloadSerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
        o.PayloadSerializerOptions.Converters.Add(new JsonStringEnumConverter());
    });

builder.Services.ConfigureHttpJsonOptions(o =>
{
    o.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
    o.SerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
    o.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
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

// Before anything can serve a request. A room restored after the first client has joined is a
// room that client was already told did not exist.
app.Services.GetRequiredService<SessionKeeper>().Restore();

app.UseCors();

// Everything below this line is closed unless the caller came through Discord.
app.UseMiddleware<RequireTokenMiddleware>();

// The player is served from this origin, which is what lets a Discord Activity reach the page,
// the API, the media and the hub through a single declared URL mapping instead of a set of them.
// Static files sit under wwwroot and never shadow /api, /media or /hub, which are explicit routes.
app.UseDefaultFiles();
app.UseStaticFiles();

// Occupancy, and no names: it is open, so what it may say is how many rooms are being watched and
// nothing about who is in them. It is what makes a restart something that can be looked at first
// rather than something to find out about afterwards.
app.MapGet("/health", (SessionStore sessions) =>
{
    var rooms = sessions.Occupied();
    return Results.Ok(new
    {
        status = "ok",
        rooms = rooms.Count,
        watching = rooms.Sum(r => r.Participants)
    });
});

// The application id is not a secret — it is in every invite link and in the Activity's own URL.
// Serving it means the player needs no build-time configuration and no rebuild when it changes.
// The public address rides along for the same reason: it is where Discord's own servers fetch a
// poster from, which the page cannot learn from an origin that is Discord's proxy.
app.MapGet("/api/config", (IOptions<DiscordAuthOptions> discord, IConfiguration configuration) =>
    Results.Ok(new
    {
        discordClientId = discord.Value.ClientId,
        publicBaseUrl = configuration["Api:PublicBaseUrl"] is { Length: > 0 } url ? url.TrimEnd('/') : null
    }));

app.MapGet("/api/titles", (TitleLibrary library) => Results.Ok(library.List()));

app.MapGet("/api/titles/{id}", (string id, TitleLibrary library) =>
    library.Get(id) is { } manifest ? Results.Ok(manifest) : Results.NotFound());

app.MapSubtitles();

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

// Puts a film into a room from outside it.
//
// The bot knows which film was asked for and cannot tell the player directly: an Activity is
// launched by Discord, from a URL the bot never writes, so nothing can be handed over in a query
// string the way a browser link does it. Setting the title on the session instead means the
// Activity finds the film already loaded when it joins, whichever door the viewer came through.
app.MapPost("/api/sessions/{sessionId}/title", async (
    string sessionId,
    SetTitleRequest request,
    SessionStore sessions,
    TitleLibrary library,
    IHubContext<SessionHub> hub,
    CancellationToken ct) =>
{
    if (library.Get(request.TitleId) is null) return Results.NotFound();

    var actor = new Actor(request.UserId ?? "bot", request.DisplayName ?? "MovieBot");
    var result = sessions.LoadTitle(sessionId, request.TitleId, actor);

    var push = new SessionStatePush { State = result.State, ServerTime = DateTimeOffset.UtcNow };
    await hub.Clients.Group(sessionId).SendAsync("StateChanged", push, ct);

    return Results.Ok(push);
});

// The Activity's authenticate() handshake. The player sends the code it was given; the secret
// needed to redeem it stays here, because a secret shipped to a browser is not a secret.
app.MapPost("/api/auth/discord/callback", async (
    DiscordCodeRequest request,
    IOptions<DiscordAuthOptions> options,
    DiscordAuthClient discord,
    IOptions<AuthOptions> authOptions,
    CancellationToken ct) =>
{
    if (!options.Value.IsConfigured)
        return Results.Problem("Discord OAuth is not configured on this server.", statusCode: 503);

    if (string.IsNullOrWhiteSpace(request.Code))
        return Results.BadRequest(new { error = "A code is required." });

    var accessToken = await discord.ExchangeCodeAsync(request.Code, options.Value, request.RedirectUri, ct);
    if (accessToken is null)
        return Results.BadRequest(new { error = "Discord refused the code." });

    var user = await discord.GetUserAsync(accessToken, ct);
    if (user is null)
        return Results.BadRequest(new { error = "Discord issued a token it then would not answer for." });

    var auth = authOptions.Value;
    var roomToken = RoomToken.Issue(new RoomTokenPayload
    {
        UserId = user.Id,
        DisplayName = user.DisplayName,
        RoomId = request.SessionId ?? "",
        ExpiresAtUnix = DateTimeOffset.UtcNow.Add(auth.TokenLifetime).ToUnixTimeSeconds()
    }, auth);

    // The access token goes back so the SDK can complete authenticate(); it is the browser's own
    // token and is scoped to the Activity, unlike the secret that redeemed the code.
    return Results.Ok(new
    {
        accessToken,
        roomToken,
        user = new { user.Id, user.Username, displayName = user.DisplayName }
    });
});

// For the bot and for the browser checks: a caller that already proves itself with the service
// key can be handed a token, because there is no Discord user for it to be.
app.MapPost("/api/auth/service-token", (
    ServiceTokenRequest request,
    HttpContext context,
    IOptions<AuthOptions> authOptions) =>
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

    return Results.Ok(new
    {
        roomToken = RoomToken.Issue(new RoomTokenPayload
        {
            UserId = request.UserId,
            DisplayName = request.DisplayName,
            RoomId = request.RoomId,
            ExpiresAtUnix = DateTimeOffset.UtcNow.Add(auth.TokenLifetime).ToUnixTimeSeconds()
        }, auth)
    });
});

app.MapMedia();
app.MapHub<SessionHub>("/hub/session");

app.Logger.LogInformation("Serving media from {MediaRoot}",
    app.Services.GetRequiredService<TitleLibrary>().MediaRoot);

app.Run();

/// <summary>What the Activity posts after Discord hands it an authorization code.</summary>
public sealed record DiscordCodeRequest(string Code, string? RedirectUri, string? SessionId);

/// <summary>A token for a caller that has no Discord user of its own.</summary>
public sealed record ServiceTokenRequest(string RoomId, string UserId, string DisplayName);

/// <summary>Exposed so the test project can drive the app in-process.</summary>
public partial class Program;
