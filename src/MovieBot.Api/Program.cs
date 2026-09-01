using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;
using TheKrystalShip.MovieBot.Api.Discord;
using TheKrystalShip.MovieBot.Api.Library;
using TheKrystalShip.MovieBot.Api.Media;
using TheKrystalShip.MovieBot.Api.Sessions;
using TheKrystalShip.MovieBot.Core;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSingleton(TimeProvider.System);

// Configuration is resolved when the library is first asked for, not while the builder is still
// being assembled: a source added after this line — as a test host does — would otherwise be
// read too late and the service would quietly point somewhere else.
builder.Services.AddSingleton(sp => new TitleLibrary(
    sp.GetRequiredService<IConfiguration>()["Media:Root"]
        ?? Path.Combine(Directory.GetCurrentDirectory(), "media"),
    sp.GetRequiredService<ILogger<TitleLibrary>>()));
builder.Services.AddSingleton<SessionStore>();

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

app.UseCors();

// The player is served from this origin, which is what lets a Discord Activity reach the page,
// the API, the media and the hub through a single declared URL mapping instead of a set of them.
// Static files sit under wwwroot and never shadow /api, /media or /hub, which are explicit routes.
app.UseDefaultFiles();
app.UseStaticFiles();

app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

// The application id is not a secret — it is in every invite link and in the Activity's own URL.
// Serving it means the player needs no build-time configuration and no rebuild when it changes.
app.MapGet("/api/config", (IOptions<DiscordAuthOptions> discord) =>
    Results.Ok(new { discordClientId = discord.Value.ClientId }));

app.MapGet("/api/titles", (TitleLibrary library) => Results.Ok(library.List()));

app.MapGet("/api/titles/{id}", (string id, TitleLibrary library) =>
    library.Get(id) is { } manifest ? Results.Ok(manifest) : Results.NotFound());

app.MapGet("/api/sessions/{sessionId}", (string sessionId, SessionStore sessions) =>
    Results.Ok(new SessionStatePush
    {
        State = sessions.GetOrCreate(sessionId),
        ServerTime = DateTimeOffset.UtcNow
    }));

app.MapGet("/api/sessions/{sessionId}/participants", (string sessionId, SessionStore sessions) =>
    Results.Ok(sessions.Participants(sessionId)));

// The Activity's authenticate() handshake. The player sends the code it was given; the secret
// needed to redeem it stays here, because a secret shipped to a browser is not a secret.
app.MapPost("/api/auth/discord/callback", async (
    DiscordCodeRequest request,
    IOptions<DiscordAuthOptions> options,
    DiscordAuthClient discord,
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

    // The access token goes back so the SDK can complete authenticate(); it is the browser's own
    // token and is scoped to the Activity, unlike the secret that redeemed the code.
    return Results.Ok(new
    {
        accessToken,
        user = new { user.Id, user.Username, displayName = user.DisplayName }
    });
});

app.MapMedia();
app.MapHub<SessionHub>("/hub/session");

app.Logger.LogInformation("Serving media from {MediaRoot}",
    app.Services.GetRequiredService<TitleLibrary>().MediaRoot);

app.Run();

/// <summary>What the Activity posts after Discord hands it an authorization code.</summary>
public sealed record DiscordCodeRequest(string Code, string? RedirectUri);

/// <summary>Exposed so the test project can drive the app in-process.</summary>
public partial class Program;
