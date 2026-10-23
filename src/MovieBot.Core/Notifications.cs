namespace TheKrystalShip.MovieBot.Core;

/// <summary>
/// What a notification is about. Each is one of the switches a person turns it on and off by,
/// except an invite and a plan, which are addressed to people on purpose and always kept and pushed.
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

    /// <summary>A title this person wants is ready and somebody else visibly wants it too.</summary>
    public const string Together = "together";

    /// <summary>A plan to watch something together was proposed, fixed, is about to start or was cancelled.</summary>
    public const string Plan = "plan";
}

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

/// <summary>Ask one person into a room or a plan.</summary>
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
