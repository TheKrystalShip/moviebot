namespace TheKrystalShip.MovieBot.Core;

/// <summary>
/// What a notification is about. Each is one of the switches a person turns pushes on and off by,
/// except an invite, which is addressed to one person on purpose and always pushed.
/// </summary>
public static class NotificationKinds
{
    /// <summary>A film somebody is waiting on can be downloaded.</summary>
    public const string Available = "available";

    /// <summary>A download somebody started can be watched, or could not be made watchable.</summary>
    public const string Ready = "ready";

    /// <summary>An episode of a show this person follows is out and the tracker offers it.</summary>
    public const string Episode = "episode";

    /// <summary>Somebody asked this person into their room.</summary>
    public const string Invite = "invite";
}

/// <summary>One line in a person's notifications, as the bell lists it.</summary>
public sealed record NotificationView
{
    public required string Id { get; init; }

    /// <summary>One of <see cref="NotificationKinds"/>.</summary>
    public required string Kind { get; init; }

    /// <summary>
    /// The sentence, written once for every surface that shows it: the bell and the push say the same
    /// thing, and neither mentions anybody or names a command.
    /// </summary>
    public required string Text { get; init; }

    /// <summary>The site's own path the notification opens.</summary>
    public string? Url { get; init; }

    /// <summary>What the link is called where it is offered as an action, such as <c>Download</c>.</summary>
    public string? Action { get; init; }

    public required DateTimeOffset CreatedAt { get; init; }

    public bool Read { get; init; }
}

/// <summary>A person's notifications, newest first, and how many they have not seen.</summary>
public sealed record NotificationsReply(int Unread, List<NotificationView> Items);

/// <summary>Marks some notifications read, or every one when none are named.</summary>
public sealed record MarkReadRequest
{
    public List<string>? Ids { get; init; }
}

/// <summary>
/// Which notifications a person is pushed, one switch per kind. Whatever is switched off still
/// reaches the bell; it is the push that the switch holds back.
/// </summary>
public sealed record NotificationPreferences
{
    public bool Available { get; init; } = true;

    public bool Ready { get; init; } = true;

    public bool Episode { get; init; } = true;

    /// <summary>Whether a notification of this kind is pushed. An invite always is.</summary>
    public bool Allows(string kind) => kind switch
    {
        NotificationKinds.Available => Available,
        NotificationKinds.Ready => Ready,
        NotificationKinds.Episode => Episode,
        _ => true,
    };
}

/// <summary>The application server key a browser subscribes with, base64url.</summary>
public sealed record PushKeyReply(string PublicKey);

/// <summary>A browser's push subscription, exactly as <c>PushSubscription.toJSON()</c> writes it.</summary>
public sealed record PushSubscribeRequest
{
    public required string Endpoint { get; init; }

    public required PushSubscriptionKeys Keys { get; init; }
}

public sealed record PushSubscriptionKeys
{
    public required string P256dh { get; init; }

    public required string Auth { get; init; }
}

/// <summary>Whether this browser is registered to the person asking, and how many devices they have.</summary>
public sealed record PushStatusReply(bool Subscribed, int Devices);

/// <summary>Somebody the site knows, as an invite list shows them.</summary>
public sealed record PersonView
{
    public required string UserId { get; init; }

    public required string Name { get; init; }

    /// <summary>
    /// When they last called the API themselves, as this run of it saw; absent when it has not seen
    /// them since it started.
    /// </summary>
    public DateTimeOffset? LastSeenAt { get; init; }
}

/// <summary>Ask one person into a room.</summary>
public sealed record InviteRequest
{
    public required string UserId { get; init; }
}

/// <summary>
/// How an invite went out: <c>push</c> (a push service took it for at least one of their devices)
/// or <c>bell</c> (no push reached them, and it waits in their notifications on the site). Taken by a
/// push service is not shown on a phone, and nothing here says it was.
/// </summary>
public sealed record InviteReply(string Delivery);
