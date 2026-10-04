using TheKrystalShip.MovieBot.Api.Library;
using TheKrystalShip.MovieBot.Api.People;
using TheKrystalShip.MovieBot.Api.Sessions;
using TheKrystalShip.MovieBot.Core;

namespace TheKrystalShip.MovieBot.Api.Notifications;

/// <summary>
/// A person's notifications, their pushes and their choices, the queue of Discord messages the bot
/// sends for them, and inviting somebody into a room. Everything under <c>/api/me</c> answers for
/// whoever the token names; the queue answers the service key alone.
/// </summary>
public static class NotificationEndpoints
{
    public static void MapNotifications(this WebApplication app)
    {
        app.MapGet("/api/push/key", (PushDevices devices) => Results.Ok(new PushKeyReply(devices.Keys().PublicKey)));

        // Whether the browser asking is registered to this person. It is the browser's own endpoint
        // that is asked about, so a device that belongs to somebody else answers no.
        app.MapGet("/api/me/push", (string? endpoint, HttpContext context, PushDevices devices) =>
            PeopleEndpoints.Person(context) is { } person
                ? Results.Ok(new PushStatusReply(
                    endpoint is { Length: > 0 } && devices.Holds(person.UserId, endpoint),
                    devices.For(person.UserId).Count))
                : NoPerson());

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
        });

        app.MapDelete("/api/me/push", (string? endpoint, HttpContext context, PushDevices devices) =>
            PeopleEndpoints.Person(context) is not { } person ? NoPerson()
            : endpoint is { Length: > 0 } && devices.Remove(person.UserId, endpoint) ? Results.NoContent()
            : Results.NotFound());

        app.MapGet("/api/me/notifications", (HttpContext context, NotificationStore inbox) =>
        {
            if (PeopleEndpoints.Person(context) is not { } person) return NoPerson();
            var mine = inbox.For(person.UserId);
            return Results.Ok(new NotificationsReply(mine.Count(n => n.ReadAt is null), mine.Select(n => n.View()).ToList()));
        });

        app.MapPost("/api/me/notifications/read", (MarkReadRequest body, HttpContext context, NotificationStore inbox, TimeProvider clock) =>
        {
            if (PeopleEndpoints.Person(context) is not { } person) return NoPerson();
            inbox.MarkRead(person.UserId, body.Ids, clock.GetUtcNow());
            return Results.NoContent();
        });

        app.MapGet("/api/me/notifications/preferences", (HttpContext context, NotificationPreferenceStore store) =>
            PeopleEndpoints.Person(context) is { } person ? Results.Ok(store.For(person.UserId)) : NoPerson());

        app.MapPut("/api/me/notifications/preferences", (NotificationPreferences body, HttpContext context, NotificationPreferenceStore store) =>
            PeopleEndpoints.Person(context) is { } person ? Results.Ok(store.Set(person.UserId, body)) : NoPerson());

        // The Discord messages owed, for the bot to send. The link is made absolute here, where the
        // public address is known.
        app.MapGet("/api/notifications/direct", (NotificationStore inbox, IConfiguration configuration) =>
        {
            var origin = configuration["Api:PublicBaseUrl"] is { Length: > 0 } url ? url.TrimEnd('/') : null;
            return Results.Ok(inbox.Waiting().Select(w => new DirectMessage
            {
                Id = w.Notification.Id,
                UserId = w.UserId,
                Text = w.Notification.Text,
                Url = w.Notification.Url is { } path && origin is not null ? origin + path : null,
            }).ToList());
        }).AddEndpointFilter(Acquisition.AcquisitionEndpoints.ServiceOnly);

        app.MapPost("/api/notifications/direct/{id}", (string id, DirectMessageReport report, NotificationStore inbox) =>
        {
            if (inbox.OwnerOf(id) is not { } owner) return Results.NotFound();
            inbox.Update(owner, id, n => n with { Direct = report.Sent ? DirectState.Sent : DirectState.Refused });
            return Results.NoContent();
        }).AddEndpointFilter(Acquisition.AcquisitionEndpoints.ServiceOnly);

        // Who can be invited: everybody the service has a name for, but the person asking, most
        // recently seen first.
        app.MapGet("/api/people", (HttpContext context, PersonDirectory people, DeviceSessions devices) =>
        {
            if (PeopleEndpoints.Person(context) is not { } person) return NoPerson();
            var seen = devices.LastSeen();
            return Results.Ok(people.All()
                .Where(p => p.Key != person.UserId)
                .Select(p => new PersonView { UserId = p.Key, Name = p.Value, LastSeenAt = seen.TryGetValue(p.Key, out var at) ? at : null })
                .OrderByDescending(p => p.LastSeenAt ?? DateTimeOffset.MinValue)
                .ThenBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
                .ToList());
        });

        // Asks one person into a room. An invite is addressed on purpose, so it is pushed whatever
        // they chose for everything else, and sent on Discord when no push reaches them.
        app.MapPost("/api/rooms/{room}/invite", async (string room, InviteRequest body, HttpContext context,
            PersonDirectory people, SessionStore sessions, TitleLibrary library, Notifier notifier, CancellationToken ct) =>
        {
            if (PeopleEndpoints.Person(context) is not { } person) return NoPerson();
            if (people.NameOf(body.UserId) is null || body.UserId == person.UserId)
                return Results.Json(new ErrorReply("Nobody by that id can be invited."),
                    ApiJsonContext.Default.ErrorReply, statusCode: StatusCodes.Status404NotFound);

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

            return Results.Ok(new InviteReply(delivery is { Pushed: > 0 } ? "push" : "discord"));
        });
    }

    private static IResult NoPerson() =>
        Results.Json(new ErrorReply("This belongs to a person; sign in as one."),
            ApiJsonContext.Default.ErrorReply, statusCode: StatusCodes.Status403Forbidden);
}
