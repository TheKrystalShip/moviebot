using Microsoft.Data.Sqlite;
using TheKrystalShip.MovieBot.Api.Data;
using TheKrystalShip.MovieBot.Api.Library;
using TheKrystalShip.MovieBot.Core;

namespace TheKrystalShip.MovieBot.Api.People;

/// <summary>How far one person has got in one film, as of the last time a room playing it was sampled.</summary>
public sealed record WatchEntry
{
    public double PositionSeconds { get; init; }
    public double DurationSeconds { get; init; }
    public DateTimeOffset LastWatchedAt { get; init; }
    public bool Finished { get; init; }
}

/// <summary>
/// What can be watched, by what it is rather than by a library title: a film by its IMDb id, an
/// episode by its show's IMDb id, season and number as the releases number them.
/// </summary>
public readonly record struct Watchable(string ImdbId, int Season, int Number)
{
    public static Watchable Film(string imdbId) => new(imdbId, 0, 0);

    public bool IsFilm => Season == 0 && Number == 0;

    /// <summary>What a library title is, or null for one nothing identified.</summary>
    public static Watchable? Of(TitleSummary title) => title.Film?.ImdbId is not { } imdbId ? null
        : title.Kind == TitleKind.Show ? title.Episode is { } e ? new Watchable(imdbId, e.Season, e.Number) : null
        : Film(imdbId);
}

/// <summary>A person saying they watched something, here or elsewhere, and when they said it.</summary>
public sealed record WatchedMark(Watchable What, DateTimeOffset MarkedAt);

/// <summary>
/// What a person took off their Continue watching and when, films by library title and shows by IMDb
/// id. Each stays off while nothing about it is newer than the moment it was taken off.
/// </summary>
public sealed record HiddenFromContinue(IReadOnlyDictionary<string, DateTimeOffset> Films, IReadOnlyDictionary<string, DateTimeOffset> Shows)
{
    public static readonly HiddenFromContinue None = new(new Dictionary<string, DateTimeOffset>(), new Dictionary<string, DateTimeOffset>());

    /// <summary>Whether a film last watched at <paramref name="at"/> is off.</summary>
    public bool HidesFilm(string titleId, DateTimeOffset at) => Films.TryGetValue(titleId, out var hidden) && at <= hidden;

    /// <summary>Whether a show whose latest activity is at <paramref name="at"/> is off.</summary>
    public bool HidesShow(string imdbId, DateTimeOffset at) => Shows.TryGetValue(imdbId, out var hidden) && at <= hidden;
}

/// <summary>The titles the library holds, as it was last listed.</summary>
public interface IHeldTitles
{
    IReadOnlyList<TitleSummary> Titles();
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
///
/// Beside the entries are the person's marks: what they said they watched, here or elsewhere, kept by
/// what it is (<see cref="Watchable"/>) so it holds for a film the library has never had and outlives
/// one that leaves. A mark is no play, so it moves no film's last play. A title the library holds that
/// a mark names reads as finished at the time of the mark wherever its entry is read, unless the entry
/// is newer: somebody who starts a film again after marking it is partway through it again.
/// </summary>
public sealed class WatchRecord : IPlayHistory
{
    /// <summary>A film is finished once the position passes this share of its length.</summary>
    public const double FinishedFraction = 0.9;

    private const string SinceKey = "watch_since";
    private const string FilmKind = "film";
    private const string ShowKind = "show";

    private readonly CinemaDatabase _database;

    /// <summary>Raised, with the person and the title, when a sample takes a title past the point it counts as finished.</summary>
    public event Action<string, string>? Finished;

    private readonly IHeldTitles _held;

    public WatchRecord(CinemaDatabase database, TimeProvider clock, IHeldTitles held)
    {
        _database = database;
        _held = held;
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

    /// <summary>One person's films, newest first, each title the library holds that they marked read as finished when the mark is newer.</summary>
    public IReadOnlyList<(string TitleId, WatchEntry Entry)> Entries(string userId)
    {
        var (entries, marks) = _database.Read(c => (
            c.Query(
                "SELECT title_id, position_seconds, duration_seconds, last_watched_at, finished FROM watch_progress "
                + "WHERE user_id = $user",
                r => (Id: r.GetString(0), Entry: Entry(r, 1)),
                ("$user", userId)),
            MarksOf(c, userId)));

        var byTitle = entries.ToDictionary(e => e.Id, e => e.Entry, StringComparer.Ordinal);
        if (marks.Count > 0)
        {
            var marked = marks.ToDictionary(m => m.What, m => m.MarkedAt);
            foreach (var title in _held.Titles())
            {
                if (Watchable.Of(title) is not { } what || !marked.TryGetValue(what, out var at)) continue;
                if (byTitle.TryGetValue(title.Id, out var kept) && kept.LastWatchedAt >= at) continue;
                byTitle[title.Id] = MarkedEntry(title.DurationSeconds, at);
            }
        }

        return [.. byTitle.OrderByDescending(e => e.Value.LastWatchedAt).Select(e => (e.Key, e.Value))];
    }

    /// <summary>One person's marks, newest first.</summary>
    public IReadOnlyList<WatchedMark> Marks(string userId) => _database.Read(c => MarksOf(c, userId));

    /// <summary>
    /// Marks what the person says they watched, in the order given: the first is marked earliest and the
    /// last at <paramref name="at"/>, a millisecond apart, so the latest of a season marked at once is its
    /// last episode, as it is for a season watched through.
    /// </summary>
    public void Mark(string userId, IReadOnlyList<Watchable> what, DateTimeOffset at) =>
        _database.Write(c =>
        {
            for (var i = 0; i < what.Count; i++)
                c.Execute(
                    "INSERT OR REPLACE INTO watched_marks (user_id, imdb_id, season, number, marked_at) "
                    + "VALUES ($user, $imdb, $season, $number, $at)",
                    ("$user", userId), ("$imdb", what[i].ImdbId), ("$season", what[i].Season), ("$number", what[i].Number),
                    ("$at", at - TimeSpan.FromMilliseconds(what.Count - 1 - i)));
        });

    /// <summary>
    /// Takes back that the person watched these: their marks, and their entries for the titles the
    /// library holds of them, so nothing they did says they watched them. The films' last plays stay.
    /// </summary>
    public void Unmark(string userId, IReadOnlyList<Watchable> what)
    {
        var undone = what.ToHashSet();
        var titles = _held.Titles()
            .Where(t => Watchable.Of(t) is { } w && undone.Contains(w))
            .Select(t => t.Id)
            .ToList();

        _database.Write(c =>
        {
            foreach (var w in what)
                c.Execute("DELETE FROM watched_marks WHERE user_id = $user AND imdb_id = $imdb AND season = $season AND number = $number",
                    ("$user", userId), ("$imdb", w.ImdbId), ("$season", w.Season), ("$number", w.Number));
            foreach (var titleId in titles)
                c.Execute("DELETE FROM watch_progress WHERE user_id = $user AND title_id = $title",
                    ("$user", userId), ("$title", titleId));
        });
    }

    /// <summary>What the person took off their Continue watching.</summary>
    public HiddenFromContinue HiddenFromContinue(string userId)
    {
        var rows = _database.Read(c => c.Query(
            "SELECT kind, id, hidden_at FROM continue_hidden WHERE user_id = $user",
            r => (Kind: r.GetString(0), Id: r.GetString(1), At: r.GetInstant(2)),
            ("$user", userId)));
        if (rows.Count == 0) return People.HiddenFromContinue.None;
        return new(
            rows.Where(r => r.Kind == FilmKind).ToDictionary(r => r.Id, r => r.At, StringComparer.Ordinal),
            rows.Where(r => r.Kind == ShowKind).ToDictionary(r => r.Id, r => r.At, StringComparer.Ordinal));
    }

    /// <summary>
    /// Takes a film (by library title) or a show (by IMDb id) off the person's Continue watching as of
    /// <paramref name="at"/>, or with <paramref name="hidden"/> false puts it back. Their entries and marks
    /// stay as they are: History and every other reader see what they watched unchanged.
    /// </summary>
    public void HideFromContinue(string userId, bool show, string id, bool hidden, DateTimeOffset at) =>
        _database.Write(c =>
        {
            if (hidden)
                c.Execute("INSERT OR REPLACE INTO continue_hidden (user_id, kind, id, hidden_at) VALUES ($user, $kind, $id, $at)",
                    ("$user", userId), ("$kind", show ? ShowKind : FilmKind), ("$id", id), ("$at", at));
            else
                c.Execute("DELETE FROM continue_hidden WHERE user_id = $user AND kind = $kind AND id = $id",
                    ("$user", userId), ("$kind", show ? ShowKind : FilmKind), ("$id", id));
        });

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

    public WatchEntry? EntryOf(string userId, string titleId)
    {
        var kept = _database.Read(c => c.First(
            "SELECT position_seconds, duration_seconds, last_watched_at, finished FROM watch_progress "
            + "WHERE user_id = $user AND title_id = $title",
            r => Entry(r, 0),
            ("$user", userId), ("$title", titleId)));

        if (_held.Titles().FirstOrDefault(t => t.Id == titleId) is not { } title || Watchable.Of(title) is not { } what)
            return kept;
        var at = _database.Read(c => c.First(
            "SELECT marked_at FROM watched_marks WHERE user_id = $user AND imdb_id = $imdb AND season = $season AND number = $number",
            r => r.GetInstant(0),
            ("$user", userId), ("$imdb", what.ImdbId), ("$season", what.Season), ("$number", what.Number)));
        return at != default && (kept is null || kept.LastWatchedAt < at) ? MarkedEntry(title.DurationSeconds, at) : kept;
    }

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

    /// <summary>Every title's last play, by title id, in one read, for a listing that shows them all.</summary>
    public IReadOnlyDictionary<string, TitlePlay> LastPlays() =>
        _database.Read(c => c.Query(
            "SELECT title_id, last_played_at, last_played_by FROM title_plays",
            r => (Id: r.GetString(0), Play: new TitlePlay { LastPlayedAt = r.GetInstant(1), LastPlayedBy = r.GetString(2) })))
            .ToDictionary(p => p.Id, p => p.Play, StringComparer.Ordinal);

    private static void Put(SqliteConnection c, string userId, string titleId, double position, double duration, DateTimeOffset at) =>
        c.Execute(
            "INSERT OR REPLACE INTO watch_progress (user_id, title_id, position_seconds, duration_seconds, last_watched_at, finished) "
            + "VALUES ($user, $title, $position, $duration, $at, $finished)",
            ("$user", userId), ("$title", titleId), ("$position", position), ("$duration", duration), ("$at", at),
            ("$finished", position >= duration * FinishedFraction));

    private static List<WatchedMark> MarksOf(SqliteConnection c, string userId) =>
        c.Query(
            "SELECT imdb_id, season, number, marked_at FROM watched_marks WHERE user_id = $user ORDER BY marked_at DESC",
            r => new WatchedMark(new Watchable(r.GetString(0), r.GetInt32(1), r.GetInt32(2)), r.GetInstant(3)),
            ("$user", userId));

    private static WatchEntry MarkedEntry(double duration, DateTimeOffset at) => new()
    {
        PositionSeconds = duration,
        DurationSeconds = duration,
        LastWatchedAt = at,
        Finished = true,
    };

    private static WatchEntry Entry(SqliteDataReader r, int first) => new()
    {
        PositionSeconds = r.GetDouble(first),
        DurationSeconds = r.GetDouble(first + 1),
        LastWatchedAt = r.GetInstant(first + 2),
        Finished = r.GetBool(first + 3),
    };
}
