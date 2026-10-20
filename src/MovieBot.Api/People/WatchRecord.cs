using Microsoft.Data.Sqlite;
using TheKrystalShip.MovieBot.Api.Data;

namespace TheKrystalShip.MovieBot.Api.People;

/// <summary>How far one person has got in one film, as of the last time a room playing it was sampled.</summary>
public sealed record WatchEntry
{
    public double PositionSeconds { get; init; }
    public double DurationSeconds { get; init; }
    public DateTimeOffset LastWatchedAt { get; init; }
    public bool Finished { get; init; }
}

/// <summary>The last time a film was played by anybody, and by whom.</summary>
public sealed record TitlePlay
{
    public DateTimeOffset LastPlayedAt { get; init; }
    public string LastPlayedBy { get; init; } = "";
}

/// <summary>
/// What each person has watched, and when each film was last played.
///
/// Two things are kept and they are forgotten differently. A person's entry for a film is theirs: it
/// is what Continue watching and History read, and they can have it removed. The film's own last
/// play is the library's, read by the clock that decides when an unwatched film leaves, so a person
/// forgetting a film does not make it look unplayed.
/// </summary>
public sealed class WatchRecord : IPlayHistory
{
    /// <summary>A film is finished once the position passes this share of its length.</summary>
    public const double FinishedFraction = 0.9;

    private const string SinceKey = "watch_since";

    private readonly CinemaDatabase _database;

    /// <summary>Raised, with the person and the title, when a sample takes a title past the point it counts as finished.</summary>
    public event Action<string, string>? Finished;

    public WatchRecord(CinemaDatabase database, TimeProvider clock)
    {
        _database = database;
        RecordingSince = database.Write(c =>
        {
            if (c.First("SELECT value FROM meta WHERE key = $key", r => r.GetString(0), ("$key", SinceKey)) is { } kept)
                return Sql.ReadInstant(kept);
            var now = clock.GetUtcNow();
            c.Execute("INSERT INTO meta (key, value) VALUES ($key, $value)", ("$key", SinceKey), ("$value", Sql.Instant(now)));
            return now;
        });
    }

    /// <summary>
    /// When the record began, which is as far back as "not played" can be known. A film with no play
    /// recorded is one nobody played since then, not one nobody ever played, so the library's clock
    /// counts from no earlier than this.
    /// </summary>
    public DateTimeOffset RecordingSince { get; }

    /// <summary>
    /// Notes that somebody watched a film up to a position at an instant. The latest sample is the
    /// entry: somebody who starts a film again after finishing it is partway through it again.
    /// </summary>
    public void Record(string userId, string titleId, double positionSeconds, double durationSeconds, DateTimeOffset now)
    {
        if (durationSeconds <= 0) return;
        var position = Math.Clamp(positionSeconds, 0, durationSeconds);

        _database.Write(c =>
        {
            Put(c, userId, titleId, position, durationSeconds, now);
            c.Execute(
                "INSERT OR REPLACE INTO title_plays (title_id, last_played_at, last_played_by) VALUES ($title, $at, $user)",
                ("$title", titleId), ("$at", now), ("$user", userId));
        });
        if (position >= durationSeconds * FinishedFraction) Finished?.Invoke(userId, titleId);
    }

    /// <summary>
    /// Notes where somebody got to while watching away from any room, when it is newer than what is
    /// held: a device reports what it watched once it is back online, which may be after the person
    /// has watched further somewhere else. The film's last play moves only forward too. False when
    /// what is held is as new or newer.
    /// </summary>
    public bool RecordIfNewer(string userId, string titleId, double positionSeconds, double durationSeconds, DateTimeOffset at)
    {
        if (durationSeconds <= 0) return false;
        var position = Math.Clamp(positionSeconds, 0, durationSeconds);

        var recorded = _database.Write(c =>
        {
            if (c.Exists("SELECT 1 FROM watch_progress WHERE user_id = $user AND title_id = $title AND last_watched_at >= $at",
                    ("$user", userId), ("$title", titleId), ("$at", at)))
                return false;

            Put(c, userId, titleId, position, durationSeconds, at);
            c.Execute(
                "INSERT INTO title_plays (title_id, last_played_at, last_played_by) VALUES ($title, $at, $user) "
                + "ON CONFLICT (title_id) DO UPDATE SET last_played_at = excluded.last_played_at, last_played_by = excluded.last_played_by "
                + "WHERE excluded.last_played_at > title_plays.last_played_at",
                ("$title", titleId), ("$at", at), ("$user", userId));
            return true;
        });
        if (recorded && position >= durationSeconds * FinishedFraction) Finished?.Invoke(userId, titleId);
        return recorded;
    }

    /// <summary>One person's films, newest first.</summary>
    public IReadOnlyList<(string TitleId, WatchEntry Entry)> Entries(string userId) =>
        _database.Read(c => c.Query(
            "SELECT title_id, position_seconds, duration_seconds, last_watched_at, finished FROM watch_progress "
            + "WHERE user_id = $user ORDER BY last_watched_at DESC",
            r => (r.GetString(0), Entry(r, 1)),
            ("$user", userId)));

    /// <summary>Every entry somebody touched since <paramref name="since"/>: who, which title, and when.</summary>
    public IReadOnlyList<(string UserId, string TitleId, DateTimeOffset At)> WatchedSince(DateTimeOffset since) =>
        _database.Read(c => c.Query(
            "SELECT user_id, title_id, last_watched_at FROM watch_progress WHERE last_watched_at >= $since",
            r => (r.GetString(0), r.GetString(1), r.GetInstant(2)),
            ("$since", since)));

    /// <summary>Everybody with an entry for any of the titles, which is who watched them.</summary>
    public IReadOnlyList<string> Watchers(IReadOnlyCollection<string> titleIds) =>
        _database.Read(c =>
        {
            var watchers = new HashSet<string>(StringComparer.Ordinal);
            foreach (var titleId in titleIds)
                watchers.UnionWith(c.Query("SELECT user_id FROM watch_progress WHERE title_id = $title",
                    r => r.GetString(0), ("$title", titleId)));
            return (IReadOnlyList<string>)[.. watchers];
        });

    public WatchEntry? EntryOf(string userId, string titleId) =>
        _database.Read(c => c.First(
            "SELECT position_seconds, duration_seconds, last_watched_at, finished FROM watch_progress "
            + "WHERE user_id = $user AND title_id = $title",
            r => Entry(r, 0),
            ("$user", userId), ("$title", titleId)));

    /// <summary>
    /// Removes the person's entry for a film and, with it, their progress. The film's last play stays:
    /// it says when the film was played, which is not something one person can take back.
    /// </summary>
    public void Forget(string userId, string titleId) =>
        _database.Write(c => c.Execute("DELETE FROM watch_progress WHERE user_id = $user AND title_id = $title",
            ("$user", userId), ("$title", titleId)));

    public TitlePlay? LastPlayed(string titleId) =>
        _database.Read(c => c.First(
            "SELECT last_played_at, last_played_by FROM title_plays WHERE title_id = $title",
            r => new TitlePlay { LastPlayedAt = r.GetInstant(0), LastPlayedBy = r.GetString(1) },
            ("$title", titleId)));

    public DateTimeOffset? LastPlayedAt(string titleId) => LastPlayed(titleId)?.LastPlayedAt;

    private static void Put(SqliteConnection c, string userId, string titleId, double position, double duration, DateTimeOffset at) =>
        c.Execute(
            "INSERT OR REPLACE INTO watch_progress (user_id, title_id, position_seconds, duration_seconds, last_watched_at, finished) "
            + "VALUES ($user, $title, $position, $duration, $at, $finished)",
            ("$user", userId), ("$title", titleId), ("$position", position), ("$duration", duration), ("$at", at),
            ("$finished", position >= duration * FinishedFraction));

    private static WatchEntry Entry(SqliteDataReader r, int first) => new()
    {
        PositionSeconds = r.GetDouble(first),
        DurationSeconds = r.GetDouble(first + 1),
        LastWatchedAt = r.GetInstant(first + 2),
        Finished = r.GetBool(first + 3),
    };
}
