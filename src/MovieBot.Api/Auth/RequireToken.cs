using Microsoft.Extensions.Options;

namespace TheKrystalShip.MovieBot.Api.Auth;

/// <summary>
/// Closes everything that is not the way in.
///
/// Discord is the only surface. The Activity signs a person in and carries the token it was given
/// on every request it makes; a browser pointed at the same hostname has no way to obtain one, so
/// the library, the films and the rooms answer it with a 401.
///
/// Two things stay open on purpose. The sign-in itself has to be reachable before anyone has a
/// token, and cover art is fetched by Discord's own servers when they render an embed, which
/// carry nothing — a film poster is not the film.
/// </summary>
public sealed class RequireTokenMiddleware(
    RequestDelegate next,
    IOptions<AuthOptions> options,
    TimeProvider clock,
    ILogger<RequireTokenMiddleware> logger)
{
    private const string ServiceHeader = "X-MovieBot-Service";

    public async Task InvokeAsync(HttpContext context)
    {
        if (IsOpen(context.Request.Path))
        {
            await next(context);
            return;
        }

        var settings = options.Value;

        // The bot has no Discord user to be. It proves itself with a key only it and this server
        // know, and may do the few things a server does on somebody's behalf.
        if (context.Request.Headers.TryGetValue(ServiceHeader, out var offered)
            && settings.ServiceKey.Length > 0
            && System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(
                System.Text.Encoding.UTF8.GetBytes(offered.ToString()),
                System.Text.Encoding.UTF8.GetBytes(settings.ServiceKey)))
        {
            await next(context);
            return;
        }

        var payload = RoomToken.Validate(BearerFrom(context.Request), settings, clock.GetUtcNow());
        if (payload is null)
        {
            logger.LogDebug("Refused {Method} {Path}: no usable token", context.Request.Method, context.Request.Path);
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            context.Response.Headers.WWWAuthenticate = "Bearer";
            await context.Response.WriteAsJsonAsync(new
            {
                error = "This is opened from Discord. Ask the bot for a film with /watch."
            });
            return;
        }

        context.Items["room-token"] = payload;
        await next(context);
    }

    private static bool IsOpen(PathString path) =>
        // The page itself: an unauthenticated visitor should be told where to go, not 401'd at a
        // blank screen. Everything it then asks for is closed.
        !path.StartsWithSegments("/api")
            && !path.StartsWithSegments("/media")
            && !path.StartsWithSegments("/hub")
        || path.StartsWithSegments("/api/config")
        || path.StartsWithSegments("/api/auth")
        || path.StartsWithSegments("/health")
        // Discord's servers fetch the poster to render an embed and carry no token of ours.
        || (path.StartsWithSegments("/media") && path.Value?.EndsWith("/poster.jpg", StringComparison.OrdinalIgnoreCase) == true);

    /// <summary>
    /// SignalR's WebSocket transport cannot set headers, so its token arrives on the query string
    /// the way the protocol expects. Everything else uses Authorization.
    /// </summary>
    private static string? BearerFrom(HttpRequest request)
    {
        var header = request.Headers.Authorization.ToString();
        if (header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            return header["Bearer ".Length..].Trim();

        if (request.Path.StartsWithSegments("/hub") && request.Query.TryGetValue("access_token", out var queryToken))
            return queryToken.ToString();

        return null;
    }
}
