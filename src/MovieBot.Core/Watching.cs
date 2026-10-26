namespace TheKrystalShip.MovieBot.Core;

/// <summary>
/// A film or an episode somebody can carry on with, and how much of it is left: one they started and
/// did not finish, or the episode after one they finished. For an episode, <see cref="Name"/> is the
/// show's, <see cref="Episode"/> says which, and <see cref="EpisodeName"/> and <see cref="StillUrl"/>
/// are what the show's description calls it and its still. <see cref="TitleId"/> is null for an
/// episode the library does not hold, which is watched by fetching it; its duration is the
/// description's runtime, zero when it names none.
/// </summary>
public sealed record ProgressEntry(
    string? TitleId,
    string Name,
    double PositionSeconds,
    double DurationSeconds,
    double RemainingSeconds,
    DateTimeOffset LastWatchedAt,
    EpisodeIdentity? Episode = null,
    string? ShowImdbId = null,
    string? EpisodeName = null,
    string? StillUrl = null);

/// <summary>A film or an episode somebody watched, and how far they got, named as <see cref="ProgressEntry"/>.</summary>
public sealed record HistoryEntry(
    string TitleId,
    string Name,
    double PositionSeconds,
    double DurationSeconds,
    bool Finished,
    DateTimeOffset LastWatchedAt,
    EpisodeIdentity? Episode = null,
    string? ShowImdbId = null);

/// <summary>Where the asking person is in a film, as the library listing carries it.</summary>
public sealed record TitleProgress(double PositionSeconds, bool Finished, DateTimeOffset LastWatchedAt);

/// <summary>
/// The last time anybody played a film and who it was: their account, which a page compares with
/// the signed-in person's to say "you", and their name. <see cref="ByName"/> is absent when the
/// person's name was never heard, which is never an account id put in its place.
/// </summary>
public sealed record TitleLastPlayed(DateTimeOffset At, string ById, string? ByName);

/// <summary>
/// A person saying they watched something, or taking it back: a film, a show's episode, a season of it,
/// or all of it, by IMDb id. A season or a show marked watched is every episode of it that has aired.
/// </summary>
public sealed record WatchedChange
{
    public string? ImdbId { get; init; }

    /// <summary><c>film</c> or <c>show</c>.</summary>
    public string? Kind { get; init; }

    /// <summary>For a show, the season; absent for all of it.</summary>
    public int? Season { get; init; }

    /// <summary>For a show's season, the episode; absent for the whole season.</summary>
    public int? Episode { get; init; }

    public bool Watched { get; init; }
}
