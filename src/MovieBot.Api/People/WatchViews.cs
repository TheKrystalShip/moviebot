namespace TheKrystalShip.MovieBot.Api.People;

/// <summary>A film somebody started and did not finish, and how much of it is left.</summary>
public sealed record ProgressEntry(
    string TitleId,
    string Name,
    double PositionSeconds,
    double DurationSeconds,
    double RemainingSeconds,
    DateTimeOffset LastWatchedAt);

/// <summary>A film somebody watched, and how far they got.</summary>
public sealed record HistoryEntry(
    string TitleId,
    string Name,
    double PositionSeconds,
    double DurationSeconds,
    bool Finished,
    DateTimeOffset LastWatchedAt);

/// <summary>Where the asking person is in a film, as the library listing carries it.</summary>
public sealed record TitleProgress(double PositionSeconds, bool Finished, DateTimeOffset LastWatchedAt);

/// <summary>
/// The last time anybody played a film and who it was: their Discord id, which a page compares with
/// the signed-in person's to say "you", and their name. <see cref="ByName"/> is absent when the
/// person's name was never heard, which is never a Discord id put in its place.
/// </summary>
public sealed record TitleLastPlayed(DateTimeOffset At, string ById, string? ByName);
