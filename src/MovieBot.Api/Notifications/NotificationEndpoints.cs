using TheKrystalShip.MovieBot.Api.Auth;
using TheKrystalShip.MovieBot.Api.Library;
using TheKrystalShip.MovieBot.Api.People;
using TheKrystalShip.MovieBot.Api.Sessions;
using TheKrystalShip.MovieBot.Core;

namespace TheKrystalShip.MovieBot.Api.Notifications;

/// <summary>
/// A person's notifications, their pushes and their choices, and inviting somebody into a room.
/// Everything under <c>/api/me</c> answers for whoever the token names, and no route here takes the
/// service key.
/// </summary>
public static class NotificationEndpoints
{
    public static void MapNotifications(this WebApplication app)
    {
        app.MapGet("/api/push/key", (PushDevices devices) => Results.Ok(new PushKeyReply(devices.Keys().PublicKey)))
            .RequireAction(CinemaActions.ProfileRead);

        // Whether the browser asking is registered to this person. It is the browser's own endpoint
        // that is asked about, so a device that belongs to somebody else answers no.
        app.MapGet("/api/me/push", (string? endpoint, HttpContext context, PushDevices devices) =>
            PeopleEndpoints.Person(context) is { } person
                ? Results.Ok(new PushStatusReply(
                    endpoint is { Length: > 0 } && devices.Holds(person.UserId, endpoint),
                    devices.For(person.UserId).Count))
                : NoPerson())
            .RequireAction(CinemaActions.ProfileRead);

        app.MapPost("/api/me/push", (PushSubscribeRequest body, HttpContext context, PushDevices devices, TimeProvider clock) =>
        {
            if (PeopleEndpoints.Person(context) is not { } person) return NoPerson();

            // A push service is always reached over TLS; anything else is not a browser's subscription.
            if (!Uri.TryCreate(body.Endpoint, UriKind.Absolute, out var endpoint) || endpoint.Scheme != Uri.UriSchemeHttps
                || string.IsNullOrWhiteSpace(body.Keys.P256dh) || string.IsNullOrWhiteSpace(body.Keys.Auth))
                return Results.Json(new ErrorReply("That is not a push subscription."),
                    ApiJsonContext.Default.ErrorReply, statusCode: StatusCodes.Status400BadRequest);

            devices.Add(new StoredPushSubscription
            {
                Endpoint = body.Endpoint, P256dh = body.Keys.P256dh, Auth = body.Keys.Auth,
                UserId = person.UserId, SubscribedAt = clock.GetUtcNow(),
            });
            return Results.NoContent();
        }).RequireAction(CinemaActions.ProfileWrite);

        app.MapDelete("/api/me/push", (string? endpoint, HttpContext context, PushDevices devices) =>
            PeopleEndpoints.Person(context) is not { } person ? NoPerson()
            : endpoint is { Length: > 0 } && devices.Remove(person.UserId, endpoint) ? Results.NoContent()
            : Results.NotFound())
            .RequireAction(CinemaActions.ProfileWrite);

        app.MapGet("/api/me/notifications", (HttpContext context, NotificationStore inbox) =>
        {
            if (PeopleEndpoints.Person(context) is not { } person) return NoPerson();
            var mine = inbox.For(person.UserId);
            return Results.Ok(new NotificationsReply(mine.Count(n => n.ReadAt is null), mine.Select(n => n.View()).ToList()));
        }).RequireAction(CinemaActions.ProfileRead);

        app.MapPost("/api/me/notifications/read", (MarkReadRequest body, HttpContext context, NotificationStore inbox, TimeProvider clock) =>
        {
            if (PeopleEndpoints.Person(context) is not { } person) return NoPerson();
            inbox.MarkRead(person.UserId, body.Ids, clock.GetUtcNow());
            return Results.NoContent();
        }).RequireAction(CinemaActions.ProfileWrite);

        app.MapGet("/api/me/notifications/preferences", (HttpContext context, NotificationPreferenceStore store) =>
            PeopleEndpoints.Person(context) is { } person ? Results.Ok(store.For(person.UserId)) : NoPerson())
            .RequireAction(CinemaActions.ProfileRead);

        app.MapPut("/api/me/notifications/preferences", (NotificationPreferences body, HttpContext context, NotificationPreferenceStore store) =>
            PeopleEndpoints.Person(context) is { } person ? Results.Ok(store.Set(person.UserId, body)) : NoPerson())
            .RequireAction(CinemaActions.ProfileWrite);

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
            PersonDirectory people, SessionStore sessions, RoomDoors doors, TitleLibrary library, Notifier notifier,
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
