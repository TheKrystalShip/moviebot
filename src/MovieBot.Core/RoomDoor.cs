namespace TheKrystalShip.MovieBot.Core;

/// <summary>
/// Who may come into a room, as the people in it see it: whether it is open, and who is asking to be
/// let in.
/// </summary>
/// <remarks>
/// A room is open from the moment it is made, and lets in anybody who may watch. Closing it lets in
/// nobody new and turns away nobody who has already been in: whoever made it, whoever somebody in it
/// let in or invited, and everybody who came in while it was open. Opening it again lets in anybody
/// who may watch.
/// </remarks>
public sealed record RoomDoor
{
    /// <summary>Anybody who may watch can come in.</summary>
    public bool Open { get; init; }

    /// <summary>Who is asking to be let in, each person once, in the order they asked.</summary>
    public IReadOnlyList<Knock> Knocking { get; init; } = [];

    /// <summary>The room's guest link, while it has one. Never the link itself: that is asked for.</summary>
    public GuestAccess? Guests { get; init; }
}

/// <summary>A room's guest link as the people in it see it: that there is one, and until when.</summary>
public sealed record GuestAccess
{
    public required DateTimeOffset ExpiresAt { get; init; }
}

/// <summary>
/// A room's guest link, for somebody in the room who may hand it out. <see cref="Path"/> is the
/// site's own address for it, which the page puts after its origin.
/// </summary>
public sealed record GuestLink
{
    public required string Path { get; init; }
    public required DateTimeOffset ExpiresAt { get; init; }
}

/// <summary>
/// What a guest link opens, for the page it lands on: the room, and what it is watching. Answered to
/// anybody holding a good link, since holding one is being invited.
/// </summary>
public sealed record GuestInvitation
{
    public required string SessionId { get; init; }

    /// <summary>The film the room holds, as the catalogue names it; null while it holds nothing.</summary>
    public string? Name { get; init; }

    public required DateTimeOffset ExpiresAt { get; init; }
}

/// <summary>Why a guest was not let in, or was sent away.</summary>
public static class GuestRefusals
{
    /// <summary>Nobody with an account is in the room, and none has been for longer than the grace.</summary>
    public const string Unattended = "unattended";

    /// <summary>The room holds as many guests as it takes.</summary>
    public const string Full = "full";

    /// <summary>The link was taken away, or ran out.</summary>
    public const string Ended = "ended";
}

/// <summary>Somebody asking to be let into a room.</summary>
public sealed record Knock
{
    public required string UserId { get; init; }
    public required string DisplayName { get; init; }
}

/// <summary>
/// What a person entering a room is answered: the room and its door when they are let in, and nothing
/// else when it is private to them, so a room they may not enter tells them nothing about itself.
/// </summary>
public sealed record JoinReply
{
    public required bool Admitted { get; init; }

    /// <summary>Where the room is. Present when <see cref="Admitted"/>.</summary>
    public SessionStatePush? Push { get; init; }

    /// <summary>Who may come in. Present when a person with an account is <see cref="Admitted"/>.</summary>
    public RoomDoor? Door { get; init; }

    /// <summary>Why a guest was not let in (<see cref="GuestRefusals"/>). Null for anybody else.</summary>
    public string? Refused { get; init; }

    /// <summary>The guest as the room names them. Present when a guest is <see cref="Admitted"/>.</summary>
    public Participant? You { get; init; }
}
