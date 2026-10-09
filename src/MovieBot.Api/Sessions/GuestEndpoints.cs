using TheKrystalShip.MovieBot.Api.Auth;
using TheKrystalShip.MovieBot.Api.Library;
using TheKrystalShip.MovieBot.Core;

namespace TheKrystalShip.MovieBot.Api.Sessions;

/// <summary>
/// A room's guest link: handed out and taken away by somebody in the room who may, and read by the page
/// it lands on.
/// </summary>
public static class GuestEndpoints
{
    public static void MapGuests(this WebApplication app)
    {
        // The room's guest link, the one it has while it is good or a new one. Everybody in the room who
        // asks is handed the same link, so two people inviting friends give out one.
        app.MapPost("/api/rooms/{room}/guests", async (string room, HttpContext context, RoomDoors doors,
            GuestCodes codes, CancellationToken ct) =>
        {
            var person = CinemaPerson.Of(context)!;
            if (await doors.IssueGuestsAsync(room, person.Actor, ct) is not { } pass) return Results.NotFound();
            return Results.Ok(new GuestLink { Path = codes.PathOf(pass.Id), ExpiresAt = pass.ExpiresAt });
        }).RequireAction(CinemaActions.Guests).RequireRoomEntry("room");

        // Takes the link away; every guest who came in on it is sent away.
        app.MapDelete("/api/rooms/{room}/guests", async (string room, HttpContext context, RoomDoors doors, CancellationToken ct) =>
        {
            await doors.RevokeGuestsAsync(room, CinemaPerson.Of(context)!.Actor, ct);
            return Results.NoContent();
        }).RequireAction(CinemaActions.Guests).RequireRoomEntry("room");

        // What a guest link opens, for the page it lands on. Open, since holding a good link is being
        // invited; a link that is no longer good is a 404 that says so.
        app.MapGet("/api/guest/{code}", (string code, GuestCodes codes, SessionStore sessions, TitleLibrary library) =>
        {
            if (codes.PassIdOf(code) is not { } passId || sessions.RoomOfGuests(passId) is not { } room
                || sessions.GuestsOf(room) is not { } pass)
                return Results.Json(new ErrorReply("This invite has ended."), ApiJsonContext.Default.ErrorReply,
                    statusCode: StatusCodes.Status404NotFound);

            var manifest = sessions.Get(room)?.TitleId is { } titleId ? library.Get(titleId) : null;
            return Results.Ok(new GuestInvitation
            {
                SessionId = room,
                Name = manifest is null ? null : manifest.Film?.Display ?? manifest.Title,
                ExpiresAt = pass.ExpiresAt,
            });
        }).AllowAnonymous();
    }
}
