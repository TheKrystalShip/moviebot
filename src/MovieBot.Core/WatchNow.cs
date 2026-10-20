namespace TheKrystalShip.MovieBot.Core;

/// <summary>Where a film being fetched for a room has got to, as every viewer of the room is shown it.</summary>
public static class PreparingStages
{
    /// <summary>A release is being picked from what the tracker offers.</summary>
    public const string Finding = "finding";

    /// <summary>The film is arriving; the room loads it the moment it can be opened.</summary>
    public const string Downloading = "downloading";

    /// <summary>It will not arrive: nothing was offered, the download was refused, or it could not be made watchable.</summary>
    public const string Failed = "failed";
}

/// <summary>
/// What a room holds while it arrives: a film by <see cref="ImdbId"/>, or a show's episode by the
/// show's id with <see cref="Season"/> and <see cref="Episode"/>. A room holding it holds no title.
/// </summary>
public sealed record RoomPreparing
{
    public required string ImdbId { get; init; }

    /// <summary>The episode's season, for an episode; absent for a film.</summary>
    public int? Season { get; init; }

    /// <summary>The episode's number in its season, for an episode; absent for a film.</summary>
    public int? Episode { get; init; }

    /// <summary>What the film is called, with its year, or a show's episode as <c>Severance S02E04</c>.</summary>
    public required string Name { get; init; }

    /// <summary>One of <see cref="PreparingStages"/>.</summary>
    public required string Stage { get; init; }

    /// <summary>The release being fetched, as its resolution and source: <c>1080p WEB-DL</c>.</summary>
    public string? Release { get; init; }

    /// <summary>How much of it has arrived, from 0 to 1, while downloading.</summary>
    public double? Progress { get; init; }

    /// <summary>Why it will not arrive, as a word: <c>notOffered</c>, <c>refused</c>, <c>unavailable</c> or <c>notWatchable</c>.</summary>
    public string? Problem { get; init; }

    /// <summary>What the torrent client or the tracker said when it refused, where it said something.</summary>
    public string? Refusal { get; init; }

    /// <summary>Whether this names the same film or episode as <paramref name="other"/>, whatever either's progress.</summary>
    public bool SameItem(RoomPreparing? other) =>
        other is not null && other.ImdbId == ImdbId && other.Season == Season && other.Episode == Episode;
}

/// <summary>How many films are being watched while they arrive, and how many may be.</summary>
public sealed record StreamSlots(int Used, int Limit);

/// <summary>A film that is downloading, as a page about it shows it.</summary>
public sealed record FilmDownload
{
    public required string Hash { get; init; }

    /// <summary>From 0 to 1.</summary>
    public double Progress { get; init; }

    public bool IsWatchable { get; init; }

    public string? LibraryId { get; init; }
}

/// <summary>
/// Whether a film can be watched, and how: it is in the library (<see cref="LibraryId"/>), it is on its
/// way (<see cref="Download"/>), the tracker offers it (<see cref="Hd1080"/>, <see cref="Hd720"/>, the
/// best of each at a digital source or better), or it is not out yet (<see cref="NotOut"/>). The tracker
/// is asked only when the film is neither here nor arriving.
/// </summary>
public sealed record FilmAvailability
{
    public required string ImdbId { get; init; }

    public string? LibraryId { get; init; }

    public FilmDownload? Download { get; init; }

    public ReleaseOffer? Hd1080 { get; init; }

    public ReleaseOffer? Hd720 { get; init; }

    /// <summary>The tracker holds nothing from a digital source up: a film somebody can wait on.</summary>
    public bool NotOut { get; init; }

    /// <summary>
    /// The tracker holds it from a digital source up, but nothing this server takes: every release is
    /// in a category, at a size or with a swarm it turns down. Waiting would not change that.
    /// </summary>
    public bool NotOffered { get; init; }

    /// <summary>Why nothing is offered, when the tracker could say.</summary>
    public string? EmptyExplanation { get; init; }

    /// <summary>The tracker could not be asked; nothing is known about what it offers.</summary>
    public bool TrackerUnanswered { get; init; }

    /// <summary>Whether the person asking wants it.</summary>
    public bool Waiting { get; init; }

    /// <summary>How many people want it as the person asking may count them: every visible want, and their own.</summary>
    public int Waiters { get; init; }

    public required StreamSlots Streams { get; init; }
}

/// <summary>
/// Watch a film that is not here yet: fetch it and play it in a room as soon as it can be opened.
/// <see cref="TorrentId"/> names a release a page was offered; without one, the best release at
/// <see cref="Quality"/> is picked. <see cref="Episodes"/> fetches only those out of a show's release.
/// Without a <see cref="Room"/>, a new one is made.
/// </summary>
public sealed record WatchNowRequest
{
    public required string ImdbId { get; init; }

    public long? TorrentId { get; init; }

    /// <summary><c>720p</c> or <c>1080p</c>; a film's default is 1080p.</summary>
    public string? Quality { get; init; }

    public IReadOnlyList<EpisodePick>? Episodes { get; init; }

    public string? Room { get; init; }
}

/// <summary>
/// How a Watch now went: <c>preparing</c> (the room is waiting on it, in <see cref="Room"/>),
/// <c>inLibrary</c> (it is here already: <see cref="LibraryId"/>, loaded into <see cref="Room"/>),
/// <c>full</c> (as many films are being watched while they arrive as may be, in <see cref="Streams"/>),
/// or <c>badRoom</c> (the room id cannot be used).
/// </summary>
public sealed record WatchNowReply
{
    public required string Outcome { get; init; }

    public string? Room { get; init; }

    public string? LibraryId { get; init; }

    public StreamSlots? Streams { get; init; }
}
