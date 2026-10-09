namespace TheKrystalShip.MovieBot.Core;

/// <summary>
/// Who may come into a room, as the people in it see it: whether it is open, and who is asking to be
/// let in.
/// </summary>
/// <remarks>
/// A room is private from the moment it is made. Only the people let in may enter it: whoever made it,
/// whoever somebody in it let in or invited, and everybody who came in while it was open. Opening it
/// lets in anybody who may watch; closing it again lets in nobody new, and turns away nobody who has
/// already been in.
/// </remarks>
public sealed record RoomDoor
{
    /// <summary>Anybody who may watch can come in.</summary>
    public bool Open { get; init; }

    /// <summary>Who is asking to be let in, each person once, in the order they asked.</summary>
    public IReadOnlyList<Knock> Knocking { get; init; } = [];
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

    /// <summary>Who may come in. Present when <see cref="Admitted"/>.</summary>
    public RoomDoor? Door { get; init; }
}
