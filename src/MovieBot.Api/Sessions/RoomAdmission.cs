using TheKrystalShip.MovieBot.Core;

namespace TheKrystalShip.MovieBot.Api.Sessions;

/// <summary>
/// What changed at a room's door: the door as it now stands, and the connections whose asking it
/// answered.
/// </summary>
public sealed record DoorChange(RoomDoor Door, IReadOnlyList<string> Answered);

/// <summary>
/// Who may come into one room, and who is asking to.
/// </summary>
/// <remarks>
/// <para>
/// <b>Entering is what lets somebody in for good.</b> A person who comes in while the room is open is
/// let in from then on, so closing the room keeps everybody who has been in it and lets in nobody new.
/// A page reloaded is a person leaving and coming back, and closing a room must not shut them out of
/// it.
/// </para>
/// <para>
/// <b>Asking is a page, not a person.</b> A knock belongs to the connection it came from, so a page
/// that closes stops asking, and the room sees each person asking once however many pages they ask
/// from.
/// </para>
/// <para>
/// <b>A guest comes in on the room's link, never through its door.</b> The link is one pass per room,
/// made by somebody in it, and holding it lets anybody watch; it admits no account and lets nobody
/// in for good, so it is not the door's to open or close.
/// </para>
/// <para>
/// Not safe on its own: the room's lock guards it, as it guards the rest of the room.
/// </para>
/// </remarks>
public sealed class RoomAdmission(bool open, IEnumerable<string> admitted, GuestPass? guests = null)
{
    private readonly HashSet<string> _admitted = new(admitted, StringComparer.Ordinal);
    private readonly List<(string Connection, Knock Knock)> _knocks = [];

    public bool Open { get; private set; } = open;

    /// <summary>Everybody let in, by account.</summary>
    public IReadOnlyCollection<string> Admitted => _admitted;

    /// <summary>The room's guest link, while it has one.</summary>
    public GuestPass? Guests { get; private set; } = guests;

    public RoomDoor Door => new()
    {
        Open = Open,
        Knocking = [.. _knocks.Select(k => k.Knock).DistinctBy(k => k.UserId)],
        Guests = Guests is { } pass ? new GuestAccess { ExpiresAt = pass.ExpiresAt } : null,
    };

    /// <summary>Gives the room a guest link, or keeps the one it has while it is still good.</summary>
    public (GuestPass Pass, DoorChange? Change) IssueGuests(Func<GuestPass> mint, DateTimeOffset now)
    {
        if (Guests is { } held && held.LiveAt(now)) return (held, null);
        Guests = mint();
        return (Guests, new DoorChange(Door, []));
    }

    /// <summary>Takes the guest link away. Null when there was none.</summary>
    public (GuestPass Pass, DoorChange Change)? RevokeGuests()
    {
        if (Guests is not { } held) return null;
        Guests = null;
        return (held, new DoorChange(Door, []));
    }

    public bool Admits(string userId) => Open || _admitted.Contains(userId);

    /// <summary>Lets the person in when the room admits them, and keeps them let in. False when it does not.</summary>
    public bool Enter(string userId)
    {
        if (!Admits(userId)) return false;
        _admitted.Add(userId);
        return true;
    }

    /// <summary>
    /// Opens or closes the room. Opening it answers everybody asking, and lets each of them in, so a
    /// room closed again before they arrive does not turn them away. Null when it already stood so.
    /// </summary>
    public DoorChange? SetOpen(bool open)
    {
        if (Open == open) return null;
        Open = open;
        if (!open) return new DoorChange(Door, []);

        foreach (var (_, knock) in _knocks) _admitted.Add(knock.UserId);
        var answered = _knocks.Select(k => k.Connection).ToList();
        _knocks.Clear();
        return new DoorChange(Door, answered);
    }

    /// <summary>Lets one person in, answering every page they were asking from.</summary>
    public DoorChange Admit(string userId)
    {
        _admitted.Add(userId);
        return Answer(userId);
    }

    /// <summary>Tells one person they are not let in, answering every page they were asking from.</summary>
    public DoorChange TurnAway(string userId) => Answer(userId);

    /// <summary>Records a page asking to be let in.</summary>
    public RoomDoor Knock(string connectionId, Knock knock)
    {
        _knocks.RemoveAll(k => k.Connection == connectionId);
        _knocks.Add((connectionId, knock));
        return Door;
    }

    /// <summary>Stops a page asking. Null when it was not asking.</summary>
    public RoomDoor? Withdraw(string connectionId) =>
        _knocks.RemoveAll(k => k.Connection == connectionId) > 0 ? Door : null;

    /// <summary>Takes away every page one person was asking from, and the door as it stands after.</summary>
    private DoorChange Answer(string userId)
    {
        var answered = _knocks.Where(k => k.Knock.UserId == userId).Select(k => k.Connection).ToList();
        _knocks.RemoveAll(k => k.Knock.UserId == userId);
        return new DoorChange(Door, answered);
    }
}
