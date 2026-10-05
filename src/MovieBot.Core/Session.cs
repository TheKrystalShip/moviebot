namespace TheKrystalShip.MovieBot.Core;

/// <summary>Who performed an action, so the room can see who paused it.</summary>
/// <param name="UserId">The account, as the identity provider names it.</param>
/// <param name="Via">
/// The client that acted for the person, when one did: the bot carrying out a slash command or a spoken
/// one. Absent when the person acted themselves.
/// </param>
public sealed record Actor(string UserId, string DisplayName, string? Via = null);

/// <summary>
/// Everything the room shares. Deliberately small.
///
/// Volume, subtitle selection, audio track and quality are not here and must never be added:
/// they are per-viewer preferences, and what a viewer plays or would choose is never shared with
/// the room. Shared volume is a way for one person to deafen everyone
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
    /// How far every rung of the transcode has reached, the shortest playlist, mirrored from the
    /// title's manifest. Null once the film is ready. It is what a reader with no use for rungs
    /// can say about the film: playable up to here on any quality.
    /// </summary>
    public double? TranscodeHead { get; init; }

    /// <summary>
    /// How far each rung has reached, for a title whose manifest tracks them separately: a player
    /// switching to a rung behind its playhead stalls until that rung catches up, so it chooses
    /// among the rungs that cover where it is. Null once the film is ready, and null for a manifest
    /// that records a single head, which says every rung is at <see cref="TranscodeHead"/>.
    /// The server refuses seeks beyond the best of these, held back to <see cref="AudioHead"/>.
    /// </summary>
    public IReadOnlyList<RungHead>? RungHeads { get; init; }

    /// <summary>How far the audio has reached, where the manifest tracks it separately; otherwise null.</summary>
    public double? AudioHead { get; init; }

    /// <summary>
    /// The film the room is waiting on, while it is fetched for somebody who pressed Watch now.
    /// Absent once the film is in the room, and in every room nobody is fetching anything for.
    /// </summary>
    public RoomPreparing? Preparing { get; init; }

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
/// <param name="StartSeconds">Where in the film the room starts; omitted is the beginning.</param>
public sealed record SetTitleRequest(string TitleId, double? StartSeconds = null);

/// <summary>
/// What a room control answers: the state the room was sent, and the clamp when the position asked
/// for was not the position granted.
/// </summary>
/// <remarks>
/// The clamp rides here rather than being broadcast because the caller that asked is the one thing a
/// broadcast cannot reach. Carried in the shared contract so the bot reads the same type the API
/// writes.
/// </remarks>
public sealed record RoomChanged(SessionStatePush Push, SeekClamped? Clamped);

/// <summary>
/// Starts or stops a room.
/// </summary>
/// <param name="AtSeconds">
/// Where the film is, according to whoever is asking. <b>Omitted means "wherever the room is"</b>,
/// which is the only honest answer from a caller that is not watching: a spoken "pause" knows the
/// room should stop and has no idea where the playhead sits, and sending a position it guessed
/// would move the film as well as stopping it.
/// </param>
public sealed record RoomPlaybackRequest(double? AtSeconds = null);

/// <summary>Moves a room to a position in the film.</summary>
public sealed record RoomSeekRequest(double ToSeconds);

/// <summary>
/// Moves a room by an amount from where it is — negative to go back.
/// </summary>
/// <remarks>
/// Separate from an absolute seek because the film is moving. A caller that reads the position and
/// then seeks to it minus fifteen has spent a round trip in between, and in a playing room that
/// round trip is part of the fifteen.
/// </remarks>
public sealed record RoomNudgeRequest(double DeltaSeconds);

public sealed record Participant
{
    public required string UserId { get; init; }
    public required string DisplayName { get; init; }

    /// <summary>
    /// This viewer's player has been waiting for the film for longer than a stutter. It is what the
    /// player reports of itself, so the room can see who the rest are waiting on.
    /// </summary>
    public bool Buffering { get; init; }
}

/// <summary>
/// One room as it stands, for whatever tells people about rooms from outside them: the bot's
/// status line, a voice channel's. It says what the room is watching and how many are in it, and
/// never who, because the people in a room are the room's business.
/// </summary>
public sealed record RoomSummary
{
    public required string SessionId { get; init; }

    public string? TitleId { get; init; }

    /// <summary>What the film is called: the catalogue's name where there is one.</summary>
    public string? Name { get; init; }

    public double? DurationSeconds { get; init; }

    public bool Paused { get; init; }

    /// <summary>Where the room is, as of the moment the summary was taken.</summary>
    public double PositionSeconds { get; init; }

    public int Participants { get; init; }
}
