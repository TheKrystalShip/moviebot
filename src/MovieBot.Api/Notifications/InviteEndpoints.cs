using TheKrystalShip.MovieBot.Api.Auth;
using TheKrystalShip.MovieBot.Api.Library;
using TheKrystalShip.MovieBot.Api.People;
using TheKrystalShip.MovieBot.Api.Sessions;
using TheKrystalShip.MovieBot.Core;
using TheKrystalShip.Notifications;

namespace TheKrystalShip.MovieBot.Api.Notifications;

/// <summary>
/// Who can be invited, and asking somebody into a room. No route here takes the service key.
/// </summary>
public static class InviteEndpoints
{
    public static void MapInvites(this WebApplication app)
    {
        // Who can be invited: everybody the service has a name for, but the person asking, most
        // recently seen first. Asking somebody into a room is a room's act, so it takes watching.
        app.MapGet("/api/people", (HttpContext context, PersonDirectory people) =>
        {
            if (PeopleEndpoints.Person(context) is not { } person) return NoPerson();
            var seen = people.LastSeen();
            return Results.Ok(people.All()
                .Where(p => p.Key != person.UserId)
                .Select(p => new PersonView { UserId = p.Key, Name = p.Value, LastSeenAt = seen.TryGetValue(p.Key, out var at) ? at : null })
                .OrderByDescending(p => p.LastSeenAt ?? DateTimeOffset.MinValue)
                .ThenBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
                .ToList());
        }).RequireAction(CinemaActions.Watch);

        // Asks one person into a room, and lets them in: an invite is the room's own answer before they
        // ask. Only somebody the room admits invites anybody into it. An invite is addressed on
        // purpose, so it is pushed whatever they chose for everything else, and waits in their bell
        // when no push reaches them.
        app.MapPost("/api/rooms/{room}/invite", async (string room, InviteRequest body, HttpContext context,
            PersonDirectory people, SessionStore sessions, RoomDoors doors, TitleLibrary library, INotifier notifier,
            CancellationToken ct) =>
        {
            if (PeopleEndpoints.Person(context) is not { } person) return NoPerson();
            if (people.NameOf(body.UserId) is null || body.UserId == person.UserId)
                return Results.Json(new ErrorReply("Nobody by that id can be invited."),
                    ApiJsonContext.Default.ErrorReply, statusCode: StatusCodes.Status404NotFound);

            await doors.AdmitAsync(room, body.UserId, person.Actor, ct);

            var titleId = sessions.Get(room)?.TitleId;
            var manifest = titleId is null ? null : library.Get(titleId);
            var delivery = await notifier.TellAsync(body.UserId, new NotificationDraft
            {
                Kind = NotificationKinds.Invite,
                Key = $"invite:{room}:{Guid.NewGuid():N}",
                Text = manifest is null
                    ? $"{person.DisplayName} invites you to their room."
                    : $"{person.DisplayName} invites you to watch {manifest.Film?.Display ?? manifest.Title} with them.",
                Url = SitePaths.Room(room, titleId),
                Action = "Join",
            }, ct);

            return Results.Ok(new InviteReply(delivery is { Pushed: > 0 } ? "push" : "bell"));
        }).RequireAction(CinemaActions.Watch).RequireRoomEntry("room");
    }

    private static IResult NoPerson() =>
        Results.Json(new ErrorReply("This belongs to a person; sign in as one."),
            ApiJsonContext.Default.ErrorReply, statusCode: StatusCodes.Status403Forbidden);
}
