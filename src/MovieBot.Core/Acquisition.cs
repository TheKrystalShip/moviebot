namespace TheKrystalShip.MovieBot.Core;

// The shapes of fetching films, as the API serves them to the services that act on somebody's
// behalf: the bot and the assistant. Each request names who asked as an Actor, because the API does
// not know them — it trusts the service key and records the person the service vouches for.
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

    public required Actor RequestedBy { get; init; }

    /// <summary>The Discord channel to announce the film in once it can be watched, if any.</summary>
    public string? NotifyChannelId { get; init; }

    /// <summary>The room to put the film on in as soon as it can be watched, if any.</summary>
    public string? RoomId { get; init; }
}

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

    public required bool IsFinished { get; init; }

    /// <summary>Enough has been transcoded to start watching while the rest arrives.</summary>
    public bool IsWatchable { get; init; }

    /// <summary>The library title it became, once it has become one.</summary>
    public string? LibraryId { get; init; }

    public string? ImdbId { get; init; }
}

/// <summary>One person waiting on a film, and where they asked to be told.</summary>
public sealed record WishSubscriberView
{
    public required string UserId { get; init; }

    /// <summary>The Discord channel the announcement goes to; absent for somebody who asked elsewhere.</summary>
    public string? ChannelId { get; init; }

    public required DateTimeOffset AskedAt { get; init; }
}

/// <summary>A film somebody wants that cannot be downloaded yet, and everybody waiting on it.</summary>
public sealed record WishView
{
    public required string ImdbId { get; init; }

    public required string Title { get; init; }

    public int? Year { get; init; }

    public required string Display { get; init; }

    public string? Starring { get; init; }

    public string? PosterUrl { get; init; }

    public IReadOnlyList<WishSubscriberView> Subscribers { get; init; } = [];
}

/// <summary>Wait on a film: an IMDb id, or what was typed over the suggestions, often a pasted link.</summary>
public sealed record AddWishRequest
{
    public required string Chosen { get; init; }

    public required Actor RequestedBy { get; init; }

    public string? ChannelId { get; init; }
}

/// <summary>
/// How a wish went: <c>subscribed</c>, <c>alreadySubscribed</c>, <c>alreadyAvailable</c> (the
/// tracker has it, in <see cref="Release"/>), <c>inLibrary</c>, <c>downloading</c> (as
/// <see cref="DownloadName"/>), <c>notAFilm</c>, <c>notFound</c> or <c>nothingPicked</c>.
/// <see cref="Wish"/> carries the film whenever the catalogue named it.
/// </summary>
public sealed record AddWishReply
{
    public required string Outcome { get; init; }

    public WishView? Wish { get; init; }

    public ReleaseOffer? Release { get; init; }

    public string? LibraryId { get; init; }

    public string? LibraryName { get; init; }

    public string? DownloadName { get; init; }

    /// <summary>What the catalogue calls the film, for the outcomes that carry no wish.</summary>
    public string? FilmTitle { get; init; }

    public string? ImdbId { get; init; }
}

/// <summary>How taking somebody off a wish went: <c>cancelled</c>, <c>notWaiting</c> or <c>nothingPicked</c>.</summary>
public sealed record CancelWishReply
{
    public required string Outcome { get; init; }

    public WishView? Wish { get; init; }
}

/// <summary>A wish the tracker can now answer, with the release that would answer it.</summary>
public sealed record AvailableWish(WishView Wish, ReleaseOffer Release);

/// <summary>The channels a wish has been announced in, whose people are told and can be forgotten.</summary>
public sealed record WishesTold(IReadOnlyList<string> ChannelIds);

/// <summary>
/// Where a film in the library stands on the library's clock: how long it stays once it has
/// settled on cold storage, and who keeps it past that.
/// </summary>
public sealed record TitleStay
{
    public required string TitleId { get; init; }

    /// <summary>
    /// Whether the film has settled on the cold disk. Only a settled film is on the clock, so
    /// <see cref="LeavesAt"/> and <see cref="DaysLeft"/> are absent until it has.
    /// </summary>
    public required bool Settled { get; init; }

    /// <summary>The Discord id of whoever keeps it, or absent when nobody does.</summary>
    public string? KeeperId { get; init; }

    /// <summary>What the keeper is called, the id until a name is known.</summary>
    public string? KeeperName { get; init; }

    public DateTimeOffset? KeptAt { get; init; }

    /// <summary>The last moment a room was playing the film, or absent when none is recorded.</summary>
    public DateTimeOffset? LastPlayedAt { get; init; }

    /// <summary>When the film leaves unless somebody plays or keeps it first; absent while kept or unsettled.</summary>
    public DateTimeOffset? LeavesAt { get; init; }

    /// <summary>Whole days until <see cref="LeavesAt"/>, rounded up and never below zero.</summary>
    public int? DaysLeft { get; init; }
}

/// <summary>Keep a film, or let one go, as somebody. A member's request names nobody: the token does.</summary>
public sealed record KeepRequest
{
    public required Actor RequestedBy { get; init; }
}
