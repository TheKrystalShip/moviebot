namespace TheKrystalShip.MovieBot.Core;

// The shapes of fetching films, as the API serves them to the site, the bot and the assistant. No
// request names who asked: the API takes the person from the token the request carries, so a caller
// can neither forget to say who it is nor say it is somebody else.
//
// Every outcome is a word and the facts behind it, never a sentence. What to tell a person depends on
// where they asked — a Discord channel mentions people and names slash commands, a web page does
// neither — so each client words the outcome its own way from the same facts.

/// <summary>A release the tracker offers, as a client shows it.</summary>
public sealed record ReleaseOffer
{
    /// <summary>The tracker's id for the release, which is what picking it hands back.</summary>
    public required long TorrentId { get; init; }

    public required string ReleaseName { get; init; }

    /// <summary>The film's name, without the release's.</summary>
    public required string Title { get; init; }

    public int? Year { get; init; }

    /// <summary>The one spelling of the name and the year together.</summary>
    public required string Display { get; init; }

    /// <summary>Resolution, source, size and the rest, in one line.</summary>
    public required string Summary { get; init; }

    public string? ImdbId { get; init; }

    public long SizeBytes { get; init; }

    public int Seeders { get; init; }

    /// <summary><c>720p</c>, <c>1080p</c>, <c>2160p</c> or <c>SD</c>; absent when the name does not say.</summary>
    public string? Resolution { get; init; }

    /// <summary>For a show's release, where it sits in the show: <c>S02</c>, <c>S06E02</c>, <c>S01–S03</c>.</summary>
    public string? Episodic { get; init; }

    /// <summary>For a show's release, how many episodes it holds.</summary>
    public int? EpisodeCount { get; init; }

    /// <summary>For a show's release, the size of one episode on average, which is what picking a few costs.</summary>
    public long? SizePerEpisode { get; init; }
}

/// <summary>One row of a picker: what to show, and the release it stands for.</summary>
public sealed record ReleaseChoiceView(string Label, long TorrentId);

/// <summary>
/// What to offer for something typed into a picker. <see cref="Explanation"/> says why nothing was
/// offered, when nothing was.
/// </summary>
public sealed record ReleaseSuggestions(IReadOnlyList<ReleaseChoiceView> Choices, string? Explanation);

/// <summary>A film as the title index names it.</summary>
public sealed record FilmView
{
    public required string ImdbId { get; init; }

    public required string Title { get; init; }

    public int? Year { get; init; }

    public required string Display { get; init; }
}

/// <summary>
/// The releases the tracker holds for a film, best first. <see cref="Film"/> is the film the search
/// settled on when it was found by name in the title index; <see cref="EmptyExplanation"/> says why
/// nothing is on offer, when nothing is — a dozen releases each over the size ceiling is a different
/// answer from a tracker that has nothing.
/// </summary>
public sealed record RankedOffers(
    IReadOnlyList<ReleaseOffer> Candidates,
    FilmView? Film = null,
    string? EmptyExplanation = null);

/// <summary>Start fetching a release a picker or a search offered.</summary>
public sealed record StartDownloadRequest
{
    public required long TorrentId { get; init; }

    /// <summary>The Discord channel to announce the film in once it can be watched, if any.</summary>
    public string? NotifyChannelId { get; init; }

    /// <summary>The room to put the film on in as soon as it can be watched, if any.</summary>
    public string? RoomId { get; init; }

    /// <summary>
    /// For a show's release, the episodes wanted out of it; only their files are fetched. Absent or
    /// empty fetches the whole release.
    /// </summary>
    public IReadOnlyList<EpisodePick>? Episodes { get; init; }
}

/// <summary>One episode of a show, by season and number.</summary>
public sealed record EpisodePick(int Season, int Episode);

/// <summary>
/// What the tracker offers for a show, season by season. <see cref="EmptyExplanation"/> says why
/// nothing is on offer, when nothing is.
/// </summary>
public sealed record ShowOffers(IReadOnlyList<SeasonOffers> Seasons, string? EmptyExplanation = null);

/// <summary>
/// One season's releases: packs holding all of it and releases holding single episodes, each best
/// first. Downloading a pack with <see cref="StartDownloadRequest.Episodes"/> fetches only those.
/// </summary>
public sealed record SeasonOffers(int Season, IReadOnlyList<ReleaseOffer> Packs, IReadOnlyList<EpisodeOffers> Episodes);

/// <summary>The releases holding one episode on its own, best first.</summary>
public sealed record EpisodeOffers(int Episode, IReadOnlyList<ReleaseOffer> Releases);

/// <summary>
/// How a download request went: <c>started</c>, <c>noLongerOffered</c> (the pick is no longer one
/// this API offered), <c>refused</c> (the disk budget or the client said no, in
/// <see cref="Refusal"/>) or <c>unavailable</c> (the tracker or the torrent client did not answer).
/// </summary>
public sealed record StartDownloadReply
{
    public required string Outcome { get; init; }

    public string? Refusal { get; init; }

    public ReleaseOffer? Release { get; init; }

    public string? Hash { get; init; }
}

/// <summary>One download, running or seeding, with what its notes say about it.</summary>
public sealed record DownloadView
{
    public required string Hash { get; init; }

    /// <summary>The library's name for the film where it has one, the release's otherwise.</summary>
    public required string Name { get; init; }

    public required string Release { get; init; }

    /// <summary>How far it has got, in one line.</summary>
    public required string Summary { get; init; }

    /// <summary>How much of it has arrived, from 0 to 1.</summary>
    public double Progress { get; init; }

    public required bool IsFinished { get; init; }

    /// <summary>Enough has been transcoded to start watching while the rest arrives.</summary>
    public bool IsWatchable { get; init; }

    /// <summary>The library title it became, once it has become one.</summary>
    public string? LibraryId { get; init; }

    public string? ImdbId { get; init; }

    /// <summary>The account of whoever asked for it, so a person can be shown their own requests.</summary>
    public string? RequestedBy { get; init; }
}

/// <summary>
/// A film people want that nothing has yet, as anybody browsing sees it: how many want it and whether
/// the asker is one of them, and nobody by name.
/// </summary>
public sealed record AnticipatedView
{
    public required string ImdbId { get; init; }

    /// <summary>What the title is. A wish is kept for a film, so this is <c>film</c>.</summary>
    public string Kind { get; init; } = "film";

    public required string Title { get; init; }

    public int? Year { get; init; }

    public required string Display { get; init; }

    /// <summary>The poster the film's page shows, served from the API at an address relative to it; the title index's own, absolute, for a film the film database has no poster for.</summary>
    public string? PosterUrl { get; init; }

    public required int Waiters { get; init; }

    /// <summary>Whether the person asking is one of the <see cref="Waiters"/>.</summary>
    public required bool Waiting { get; init; }
}
