using Microsoft.Extensions.Options;
using TheKrystalShip.MovieBot.Api.Auth;

namespace TheKrystalShip.MovieBot.Api.Sessions;

/// <summary>
/// A phone's pairings with its person's screens. The phone is the anchor session the token belongs to
/// and the person is the token's, so there is no route by which anybody pairs, lists or unpairs anybody
/// else's; a screen's own side is the hub (<see cref="SessionHub.PairingCode"/>, <see cref="SessionHub.Phones"/>,
/// <see cref="SessionHub.Unpair"/>).
/// </summary>
public static class PairingEndpoints
{
    public static void MapPairings(this WebApplication app)
    {
        // The screens this session is paired with, connected or not.
        app.MapGet("/api/me/screens", async (HttpContext context, ScreenPresence screens, IOptions<OidcOptions> oidc) =>
            Phone(context, oidc.Value) is not { } phone
                ? NoSession()
                : Results.Json(await screens.PairedWithAsync(phone.Person.UserId, phone.Sid), ApiJsonContext.Default.ListScreenView))
            .RequireAction(CinemaActions.ProfileRead);

        // Pairs this session with the screen whose code it scanned. Pairing again is no change.
        app.MapPost("/api/me/screens", async (PairRequest body, HttpContext context, ScreenPresence screens, IOptions<OidcOptions> oidc) =>
        {
            if (Phone(context, oidc.Value) is not { } phone) return NoSession();
            var (screen, refused) = await screens.PairAsync(phone.Person.UserId, phone.Sid,
                PhoneNames.Of(context.Request.Headers.UserAgent.ToString()), body.Code ?? "");
            return refused switch
            {
                null => Results.Json(screen!, ApiJsonContext.Default.ScreenView),
                PairingRefusal.Expired => Refuse(StatusCodes.Status410Gone, "That code has run out. Scan the TV again."),
                PairingRefusal.AnotherPerson => Refuse(StatusCodes.Status403Forbidden, "That TV is signed in to another account."),
                _ => Refuse(StatusCodes.Status404NotFound, "That code is not one a TV is showing."),
            };
        }).RequireAction(CinemaActions.ProfileWrite);

        // Ends this session's pairing with a screen. Ending one there is none of is no change.
        app.MapDelete("/api/me/screens/{id}", async (string id, HttpContext context, ScreenPresence screens, IOptions<OidcOptions> oidc) =>
        {
            if (Phone(context, oidc.Value) is not { } phone) return NoSession();
            await screens.UnpairAsync(phone.Person.UserId, phone.Sid, id);
            return Results.NoContent();
        }).RequireAction(CinemaActions.ProfileWrite);
    }

    /// <summary>
    /// The person and the anchor session of a request that may pair: signed in on a session of its own,
    /// and not as a television, which is paired with rather than pairing.
    /// </summary>
    private static (CinemaPerson Person, string Sid)? Phone(HttpContext context, OidcOptions oidc) =>
        CinemaPerson.Of(context) is { } person
        && context.User.Identities.FirstOrDefault(i => i.AuthenticationType == CinemaSchemes.Bearer) is { } identity
        && ScreenIdentity.Of(identity, oidc.TvClientId) is null
        && SessionClaims.SidOf(identity) is { } sid
            ? (person, sid)
            : null;

    private static IResult NoSession() =>
        Refuse(StatusCodes.Status400BadRequest, "Only a phone or a browser signed in on its own session pairs with a TV.");

    private static IResult Refuse(int status, string error) =>
        Results.Json(new ErrorReply(error), ApiJsonContext.Default.ErrorReply, statusCode: status);
}
