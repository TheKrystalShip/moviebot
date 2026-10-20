using Microsoft.Data.Sqlite;
using TheKrystalShip.MovieBot.Api.Data;
using TheKrystalShip.MovieBot.Acquire.Imdb;
using TheKrystalShip.MovieBot.Acquire.Search;
using TheKrystalShip.MovieBot.Core;

namespace TheKrystalShip.MovieBot.Api.Acquisition;

/// <summary>How films people are waiting on are watched for.</summary>
public sealed class NotifyOptions
{
    public const string Section = "Notify";

    /// <summary>
    /// How often the tracker is asked about every film on the list, in minutes. A film that was
    /// not out this morning is not out this minute, and the tracker caps how often a member may
    /// call it; an hour answers within the day a film appears without spending the cap on it.
    /// </summary>
    public int SweepMinutes { get; set; } = 60;

    /// <summary>
    /// The least a release's source may be for the film to count as available.
    ///
    /// A film in cinemas is on the tracker within days as a camcorder recording, and the ranker
    /// only weighs those down rather than dropping them. What somebody waiting on a film means by
    /// available is its digital release, and a web encode is the first form that takes.
    /// </summary>
    public Source MinimumSource { get; set; } = Source.Web;
}

/// <summary>One person who wants a film and held <c>cinema:wish</c> when they did, by the account the identity provider names them by.</summary>
public sealed record WishSubscriber
{
    public required string UserId { get; init; }

    public required DateTimeOffset AskedAt { get; init; }
}

/// <summary>
/// A film somebody wants that cannot be downloaded yet, and everybody who can be told when it can.
///
/// Keyed by the film rather than by the person: two people wanting the same film share one wish and
/// one tracker call per sweep. What is kept about the film is what the catalogue said when it was
/// asked, so a notification can name it and a page show its poster without asking again. The people
/// are the wants on the film whose token held <c>cinema:wish</c>.
/// </summary>
public sealed record Wish
{
    /// <summary>The canonical <c>tt0458352</c> form. It is the key and what the tracker is asked.</summary>
    public required string ImdbId { get; init; }

    public required string Title { get; init; }

    public int? Year { get; init; }

    public string? Starring { get; init; }

    public string? PosterUrl { get; init; }

    public IReadOnlyList<WishSubscriber> Subscribers { get; init; } = [];

    /// <summary>The one spelling of the name and the year together.</summary>
    public string Display => Year is { } year ? $"{Title} ({year})" : Title;
}

/// <summary>
/// The films nothing has yet that people want.
///
/// A download is kept as tags on the torrent and a room as its row, but a film that is not on the
/// tracker yet has no torrent to tag and no manifest to write: the catalogue's description of it
/// exists nowhere unless it is kept here. A wish is listed in the order it was first made, and its
/// people in the order they wanted it. Who is on it is <see cref="Wants.WantStore"/>'s to say, and the
/// wish goes when the last of them does.
/// </summary>
public sealed class WishList(CinemaDatabase database)
{
    // The release each wish that has come out was found as. Held in memory only: the sweep finds it
    // again on its first pass after a restart.
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, Release> _out = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyList<Wish> All() => database.Read(c => Load(c, null));

    /// <summary>
    /// Every wish with how many people want the film as the person asking may count them (every
    /// visible want, and their own) and whether they are one, the most wanted first and, among equals,
    /// the one most recently wanted. Names nobody.
    /// </summary>
    public IReadOnlyList<AnticipatedView> Anticipated(string userId) => database.Read(c => c.Query(
        "SELECT w.imdb_id, w.title, w.year, w.poster_url, COUNT(*) AS wanters, MAX(t.user_id = $user) AS wanting "
        + "FROM wishes w JOIN title_wants t ON t.imdb_id = w.imdb_id AND (t.visible = 1 OR t.user_id = $user) "
        + "GROUP BY w.imdb_id ORDER BY wanters DESC, MAX(t.since) DESC, w.rowid",
        r =>
        {
            var title = r.GetString(1);
            var year = r.GetIntOrNull(2);
            return new AnticipatedView
            {
                ImdbId = r.GetString(0), Title = title, Year = year, PosterUrl = r.GetStringOrNull(3),
                Display = year is { } y ? $"{title} ({y})" : title,
                Waiters = r.GetInt32(4), Waiting = r.GetInt32(5) != 0,
            };
        },
        ("$user", userId)));

    public Wish? Find(string imdbId) => database.Read(c => Load(c, imdbId).FirstOrDefault());

    /// <summary>
    /// Keeps the catalogue's description of a film nothing has yet, if it is not kept already. What is
    /// kept is what the catalogue said now, which stands in for the film until it is out.
    /// </summary>
    public void Keep(ImdbTitle film, DateTimeOffset now) => database.Write(c => c.Execute(
        "INSERT OR IGNORE INTO wishes (imdb_id, title, year, starring, poster_url, made_at) VALUES ($imdb, $title, $year, $starring, $poster, $at)",
        ("$imdb", film.ImdbId), ("$title", film.Title), ("$year", film.Year), ("$starring", film.Starring),
        ("$poster", film.PosterUrl?.ToString()), ("$at", now)));

    /// <summary>Notes that a wished film is out, as this release.</summary>
    public void MarkOut(string imdbId, Release release) => _out[imdbId] = release;

    /// <summary>The release a wished film came out as, or null while it is not out or nobody wants it.</summary>
    public Release? OutAs(string imdbId)
    {
        if (!_out.TryGetValue(imdbId, out var release)) return null;
        if (Find(imdbId) is not null) return release;

        // A film nobody wants any longer is nobody's news.
        _out.TryRemove(imdbId, out _);
        return null;
    }

    /// <summary>
    /// The wishes, all of them or the one <paramref name="imdbId"/> names (whatever its case), each with
    /// the people whose wants can be told.
    /// </summary>
    private static List<Wish> Load(SqliteConnection c, string? imdbId)
    {
        var wishes = c.Query(
            "SELECT imdb_id, title, year, starring, poster_url FROM wishes "
            + "WHERE ($imdb IS NULL OR imdb_id = $imdb COLLATE NOCASE) ORDER BY rowid",
            r => new Wish
            {
                ImdbId = r.GetString(0), Title = r.GetString(1), Year = r.GetIntOrNull(2), Starring = r.GetStringOrNull(3),
                PosterUrl = r.GetStringOrNull(4),
            },
            ("$imdb", imdbId));

        return [.. wishes.Select(w => w with
        {
            Subscribers = c.Query("SELECT user_id, since FROM title_wants WHERE imdb_id = $imdb AND may_wish = 1 ORDER BY since, user_id",
                r => new WishSubscriber { UserId = r.GetString(0), AskedAt = r.GetInstant(1) }, ("$imdb", w.ImdbId)),
        })];
    }
}
