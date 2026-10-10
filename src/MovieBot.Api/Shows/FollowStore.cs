using Microsoft.Data.Sqlite;
using TheKrystalShip.MovieBot.Api.Data;
using TheKrystalShip.MovieBot.Core;

namespace TheKrystalShip.MovieBot.Api.Shows;

/// <summary>One person's follow of one show, as it is kept.</summary>
public sealed record StoredFollow
{
    public required string ImdbId { get; init; }

    public required DateTimeOffset Since { get; init; }

    /// <summary>Whether a new episode is downloaded for this person as well as announced.</summary>
    public bool AutoDownload { get; init; }

    /// <summary>
    /// The latest episode that was already due when the follow began; only an episode after it is
    /// news to this person. Absent when the show had none, so every episode counts.
    /// </summary>
    public EpisodeIdentity? Baseline { get; init; }

    /// <summary>Whether an episode is news to this person: it comes after everything that was out when they followed.</summary>
    public bool Covers(int season, int number) =>
        Baseline is not { } known || season > known.Season || (season == known.Season && number > known.Number);
}

/// <summary>
/// Which shows each person follows, and which episodes of them have been announced.
///
/// What was announced is kept beside the follows so that a restart never tells anybody twice. It is
/// forgotten once nobody follows the show: a person who follows it later starts from the episodes
/// that are out by then.
/// </summary>
public sealed class FollowStore(CinemaDatabase database)
{
    private const string Columns = "imdb_id, since, auto_download, baseline_season, baseline_number";

    /// <summary>How an episode is named in what is said about it, <c>S02E04</c>.</summary>
    public static string Label(int season, int number) =>
        string.Create(System.Globalization.CultureInfo.InvariantCulture, $"S{season:00}E{number:00}");

    /// <summary>A person's follows, newest first.</summary>
    public List<StoredFollow> For(string userId) =>
        database.Read(c => c.Query($"SELECT {Columns} FROM follows WHERE user_id = $user ORDER BY since DESC, rowid DESC",
            r => Follow(r, 0), ("$user", userId)));

    public StoredFollow? Get(string userId, string imdbId) =>
        database.Read(c => Get(c, userId, imdbId));

    /// <summary>
    /// Follows a show, or changes whether it is downloaded for somebody who already does: a follow
    /// keeps the moment it began and the baseline it was given then.
    /// </summary>
    public StoredFollow Follow(string userId, string imdbId, bool autoDownload, EpisodeIdentity? baseline, DateTimeOffset now) =>
        database.Write(c =>
        {
            if (Get(c, userId, imdbId) is { } existing)
            {
                c.Execute("UPDATE follows SET auto_download = $auto WHERE user_id = $user AND imdb_id = $imdb",
                    ("$user", userId), ("$imdb", imdbId), ("$auto", autoDownload));
                return existing with { AutoDownload = autoDownload };
            }

            var follow = new StoredFollow { ImdbId = imdbId, Since = now, AutoDownload = autoDownload, Baseline = baseline };
            c.Execute(
                $"INSERT INTO follows (user_id, {Columns}) VALUES ($user, $imdb, $since, $auto, $season, $number)",
                ("$user", userId), ("$imdb", imdbId), ("$since", now), ("$auto", autoDownload),
                ("$season", baseline?.Season), ("$number", baseline?.Number));
            return follow;
        });

    /// <summary>Stops following. False when the person did not.</summary>
    public bool Unfollow(string userId, string imdbId) => database.Write(c =>
    {
        if (c.Execute("DELETE FROM follows WHERE user_id = $user AND imdb_id = $imdb", ("$user", userId), ("$imdb", imdbId)) == 0)
            return false;
        c.Execute("DELETE FROM announced_episodes WHERE imdb_id = $imdb AND NOT EXISTS (SELECT 1 FROM follows WHERE imdb_id = $imdb)",
            ("$imdb", imdbId));
        return true;
    });

    /// <summary>Every show anybody follows.</summary>
    public List<string> Shows() =>
        database.Read(c => c.Query("SELECT DISTINCT imdb_id FROM follows", r => r.GetString(0)))
            .Order(StringComparer.Ordinal).ToList();

    /// <summary>Everybody who follows a show, the earliest follow first.</summary>
    public List<(string UserId, StoredFollow Follow)> Followers(string imdbId) =>
        [.. database.Read(c => c.Query($"SELECT user_id, {Columns} FROM follows WHERE imdb_id = $imdb",
                r => (UserId: r.GetString(0), Follow: Follow(r, 1)), ("$imdb", imdbId)))
            .OrderBy(f => f.Follow.Since).ThenBy(f => f.UserId, StringComparer.Ordinal)];

    public bool IsAnnounced(string imdbId, int season, int number) =>
        database.Read(c => c.Exists("SELECT 1 FROM announced_episodes WHERE imdb_id = $imdb AND season = $season AND number = $number",
            ("$imdb", imdbId), ("$season", season), ("$number", number)));

    /// <summary>Notes that an episode of a followed show was announced; one nobody follows is nobody's news.</summary>
    public void Announce(string imdbId, int season, int number) =>
        database.Write(c => c.Execute(
            "INSERT OR IGNORE INTO announced_episodes (imdb_id, season, number) "
            + "SELECT $imdb, $season, $number WHERE EXISTS (SELECT 1 FROM follows WHERE imdb_id = $imdb)",
            ("$imdb", imdbId), ("$season", season), ("$number", number)));

    private static StoredFollow? Get(SqliteConnection c, string userId, string imdbId) =>
        c.First($"SELECT {Columns} FROM follows WHERE user_id = $user AND imdb_id = $imdb",
            r => Follow(r, 0), ("$user", userId), ("$imdb", imdbId));

    private static StoredFollow Follow(SqliteDataReader r, int first) => new()
    {
        ImdbId = r.GetString(first),
        Since = r.GetInstant(first + 1),
        AutoDownload = r.GetBool(first + 2),
        Baseline = r.IsDBNull(first + 3) ? null : new EpisodeIdentity { Season = r.GetInt32(first + 3), Number = r.GetInt32(first + 4) },
    };
}
