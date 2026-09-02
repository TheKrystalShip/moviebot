namespace TheKrystalShip.MovieBot.Core;

/// <summary>Who performed an action, so the room can see who paused it.</summary>
public sealed record Actor(string UserId, string DisplayName);

/// <summary>
/// Everything the room shares. Deliberately small.
///
/// Volume, subtitle selection, audio track and quality are not here and must never be added:
/// they are per-viewer preferences. Shared volume is a way for one person to deafen everyone
/// else, and one person needing subtitles should not put them on five other screens.
/// </summary>
public sealed record SessionState
{
    public required string SessionId { get; init; }

    public string? TitleId { get; init; }

    public bool Paused { get; init; } = true;

    /// <summary>The position, true as of <see cref="AnchorUtc"/> — not a ticking number.</summary>
    public double PositionSeconds { get; init; }

    public DateTimeOffset AnchorUtc { get; init; }

    public double Rate { get; init; } = 1.0;

    public Actor? UpdatedBy { get; init; }

    /// <summary>
    /// Server-assigned and monotonic within an <see cref="Epoch"/>. A client discards any state
    /// whose revision is not greater than the last it applied, which is what makes reordered or
    /// duplicated pushes harmless.
    /// </summary>
    public long Revision { get; init; }

    /// <summary>
    /// Which run of this room the revision belongs to.
    ///
    /// Rooms live in memory. One the server has forgotten — restarted, or swept for being empty —
    /// is built again from nothing and counts revisions from zero again, so the numbers either
    /// side of that are not comparable. Without this a client goes on measuring the new run
    /// against the old one's revisions, discards every push the server sends it for as long as
    /// the page stays open, and drives the film off a state nothing can correct.
    /// </summary>
    public required string Epoch { get; init; }

    /// <summary>
    /// How far the transcode has reached, mirrored from the title's manifest. Null once the film
    /// is ready. While it is set, the server refuses seeks beyond it.
    /// </summary>
    public double? TranscodeHead { get; init; }

    /// <summary>
    /// Where the film is now. Derived from the anchor rather than transmitted, so a client that
    /// missed a push for ten seconds still computes the right answer.
    /// </summary>
    public double PositionAt(DateTimeOffset now)
    {
        if (Paused) return PositionSeconds;
        var elapsed = (now - AnchorUtc).TotalSeconds * Rate;
        return Math.Max(0, PositionSeconds + elapsed);
    }
}

/// <summary>
/// A state push. <see cref="ServerTime"/> rides along on every one so clients can maintain an
/// offset estimate: a viewer with a skewed system clock must not be able to drag the room.
/// </summary>
public sealed record SessionStatePush
{
    public required SessionState State { get; init; }
    public required DateTimeOffset ServerTime { get; init; }
}

/// <summary>
/// Sent only to the client whose seek was refused, so its UI can explain itself rather than
/// silently snapping somewhere else.
/// </summary>
public sealed record SeekClamped
{
    public required double RequestedSeconds { get; init; }
    public required double GrantedSeconds { get; init; }
    public required double HeadSeconds { get; init; }
}

/// <summary>
/// Puts a film into a room from outside it. The bot uses this because an Activity is launched
/// from a URL it never writes, so a query string cannot carry the choice.
/// </summary>
public sealed record SetTitleRequest(string TitleId, string? UserId, string? DisplayName);

public sealed record Participant
{
    public required string UserId { get; init; }
    public required string DisplayName { get; init; }
}
