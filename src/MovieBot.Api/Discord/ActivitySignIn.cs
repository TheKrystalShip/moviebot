using Microsoft.Extensions.Options;
using TheKrystalShip.MovieBot.Api.Auth;
using TheKrystalShip.MovieBot.Core;

namespace TheKrystalShip.MovieBot.Api.Discord;

/// <summary>
/// What the Activity posts: the code the SDK's <c>authorize</c> handed it, or, to be given a fresh token
/// before the last one runs out, the Discord access token the first answer carried.
/// </summary>
public sealed record ActivitySignInRequest(string? Code, string? AccessToken);

/// <summary>
/// What the Activity is answered: Discord's access token, for <c>sdk.commands.authenticate</c>, and the
/// provider's token for this person, which is what the API and the hub take, with how long it lives.
/// </summary>
public sealed record ActivitySignInReply(string AccessToken, string Token, long ExpiresIn);

/// <summary>A refused sign-in, with the provider's word for the account when the refusal is about it.</summary>
public sealed record ActivitySignInRefusal(string Error, string? AccountStatus);

/// <summary>
/// How the Activity is signed in. It has no browser of its own to redirect, and nothing in it may reach
/// the provider, which Discord's URL mapping would not allow, so its sign-in runs through here.
/// </summary>
/// <remarks>
/// <para>
/// <b>Discord proves who it is, and the provider says who that is.</b> The SDK's code is redeemed with
/// Cinema's own Discord application, as only the holder of its secret can, and the Discord access token
/// it buys is exchanged at the provider (RFC 8693) by the API as a confidential client. The provider asks
/// Discord whose token it is and which application issued it, finds the account the Discord identity is
/// linked to, and mints a normal token for Cinema's audience. This API mints nothing.
/// </para>
/// <para>
/// <b>No refresh token.</b> The Activity already holds Discord's access token, so it asks again with it
/// before its token runs out, and each answer is a fresh exchange.
/// </para>
/// </remarks>
public static class ActivitySignIn
{
    public static void MapActivitySignIn(this WebApplication app)
    {
        app.MapPost("/api/auth/discord/callback", async (
            ActivitySignInRequest request,
            IOptions<DiscordAuthOptions> discord,
            IOptions<OidcOptions> oidc,
            DiscordAuthClient discordClient,
            TokenExchangeClient exchange,
            TimeProvider clock,
            ILoggerFactory loggers,
            CancellationToken ct) =>
        {
            if (!discord.Value.IsConfigured || !oidc.Value.ExchangesForActivity)
                return Refuse("The Activity's sign-in is not configured on this server.", null, StatusCodes.Status503ServiceUnavailable);

            var discordToken = request.AccessToken is { Length: > 0 } held
                ? held
                : request.Code is { Length: > 0 } code
                    ? await discordClient.ExchangeCodeAsync(code, discord.Value, ct)
                    : null;
            if (discordToken is null)
                return Refuse(request.Code is null && request.AccessToken is null
                    ? "A code or an access token is required."
                    : "Discord refused the code.", null, StatusCodes.Status400BadRequest);

            ExchangeResult result;
            try
            {
                result = await exchange.ExchangeAsync(oidc.Value.Issuer, oidc.Value.ActivityClientId,
                    oidc.Value.ActivityClientSecret, discordToken, TokenExchangeTypes.DiscordAccessToken, ct);
            }
            catch (TokenExchangeUnavailableException ex)
            {
                loggers.CreateLogger(typeof(ActivitySignIn)).LogWarning(ex, "The Activity's sign-in could not reach the identity provider.");
                return Refuse("The sign-in service is not answering. Try again in a moment.", null, StatusCodes.Status502BadGateway);
            }

            if (result.Token is { } token)
                return Results.Ok(new ActivitySignInReply(discordToken, token.AccessToken,
                    Math.Max(0, (long)(token.ExpiresAt - clock.GetUtcNow()).TotalSeconds)));

            loggers.CreateLogger(typeof(ActivitySignIn)).LogInformation("The identity provider refused the Activity's exchange: {Error} ({Status}).",
                result.Error, result.AccountStatus ?? "no account status");
            return Refuse(result.AccountStatus switch
            {
                "pending" => "Your account is waiting for an administrator to approve it.",
                "disabled" => "Your account is switched off.",
                "unknown" => "This Discord account is not linked to an account that can sign in here.",
                _ => "The sign-in service refused this Discord sign-in.",
            }, result.AccountStatus, StatusCodes.Status403Forbidden);
        }).AllowAnonymous();
    }

    private static IResult Refuse(string error, string? accountStatus, int status) =>
        Results.Json(new ActivitySignInRefusal(error, accountStatus), ApiJsonContext.Default.ActivitySignInRefusal, statusCode: status);
}
