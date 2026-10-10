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

/// <summary>One person waiting on a film, by the account the identity provider names them by.</summary>
public sealed record WishSubscriber
{
    public required string UserId { get; init; }

    public required DateTimeOffset AskedAt { get; init; }
}

/// <summary>
/// A film somebody wants that cannot be downloaded yet, and everybody waiting on it.
///
/// Keyed by the film rather than by the person: two people asking for the same film share one
/// wish and one tracker call per sweep. What is kept about the film is what the catalogue said when
/// it was asked, so a notification can name it and a page show its poster without asking again.
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

    /// <summary>The wish as it crosses the wire, with the release that answers it once there is one.</summary>
    public WishView View(ReleaseOffer? release = null) => new()
    {
        ImdbId = ImdbId,
        Title = Title,
        Year = Year,
        Display = Display,
        Starring = Starring,
        PosterUrl = PosterUrl,
        Subscribers = Subscribers.Select(s => new WishSubscriberView
        {
            UserId = s.UserId,
            AskedAt = s.AskedAt,
        }).ToList(),
        Release = release,
    };
}

public enum WishAdded
{
    Subscribed,
    AlreadySubscribed,
}

/// <summary>
/// The films people are waiting on.
///
/// A download is kept as tags on the torrent and a room as its row, but a film that is not on the
/// tracker yet has no torrent to tag and no manifest to write: a wish for it exists nowhere unless it
/// is kept here. A wish is listed in the order it was first made, and its subscribers in the order
/// they asked.
/// </summary>
public sealed class WishList(CinemaDatabase database)
{
    // The release each wish that has come out was found as. Held in memory only: the sweep finds it
    // again on its first pass after a restart.
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, Release> _out = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyList<Wish> All() => database.Read(c => Load(c, null, null));

    /// <summary>The films one person is waiting on.</summary>
    public IReadOnlyList<Wish> For(string userId) => database.Read(c => Load(c, null, userId));

    /// <summary>
    /// Every wish with how many people wait on it and whether <paramref name="userId"/> is one, the
    /// most waited on first and, among equals, the one most recently asked for. Names nobody.
    /// </summary>
    public IReadOnlyList<AnticipatedView> Anticipated(string userId) => database.Read(c => c.Query(
        "SELECT w.imdb_id, w.title, w.year, w.poster_url, COUNT(*) AS waiters, MAX(s.user_id = $user) AS waiting "
        + "FROM wishes w JOIN wish_subscribers s ON s.imdb_id = w.imdb_id "
        + "GROUP BY w.imdb_id ORDER BY waiters DESC, MAX(s.asked_at) DESC, w.rowid",
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

    public Wish? Find(string imdbId) => database.Read(c => Load(c, imdbId, null).FirstOrDefault());

    /// <summary>
    /// Adds a person to a film's wish, making the wish if it is the first. What is kept about the
    /// film is what the catalogue said now, which stands in for it until the film is out.
    /// </summary>
    public (WishAdded Outcome, Wish Wish) Add(ImdbTitle film, WishSubscriber subscriber) => database.Write(c =>
    {
        if (Load(c, film.ImdbId, null).FirstOrDefault() is { } existing)
        {
            if (existing.Subscribers.Any(s => s.UserId == subscriber.UserId)) return (WishAdded.AlreadySubscribed, existing);

            Subscribe(c, existing.ImdbId, subscriber);
            return (WishAdded.Subscribed, existing with { Subscribers = [.. existing.Subscribers, subscriber] });
        }

        var wish = new Wish
        {
            ImdbId = film.ImdbId,
            Title = film.Title,
            Year = film.Year,
            Starring = film.Starring,
            PosterUrl = film.PosterUrl?.ToString(),
            Subscribers = [subscriber],
        };
        c.Execute(
            "INSERT INTO wishes (imdb_id, title, year, starring, poster_url, made_at) VALUES ($imdb, $title, $year, $starring, $poster, $at)",
            ("$imdb", wish.ImdbId), ("$title", wish.Title), ("$year", wish.Year), ("$starring", wish.Starring),
            ("$poster", wish.PosterUrl), ("$at", subscriber.AskedAt));
        Subscribe(c, wish.ImdbId, subscriber);
        return (WishAdded.Subscribed, wish);
    });

    /// <summary>Takes one person off a film's wish. The wish goes with its last subscriber.</summary>
    public Wish? Remove(string userId, string imdbId) => database.Write(c =>
    {
        if (Load(c, imdbId, null).FirstOrDefault() is not { } wish || wish.Subscribers.All(s => s.UserId != userId)) return null;

        c.Execute("DELETE FROM wish_subscribers WHERE imdb_id = $imdb AND user_id = $user", ("$imdb", wish.ImdbId), ("$user", userId));
        c.Execute("DELETE FROM wishes WHERE imdb_id = $imdb AND NOT EXISTS (SELECT 1 FROM wish_subscribers WHERE imdb_id = $imdb)",
            ("$imdb", wish.ImdbId));
        return wish;
    });

    /// <summary>Notes that a wished film is out, as this release.</summary>
    public void MarkOut(string imdbId, Release release) => _out[imdbId] = release;

    /// <summary>The release a wished film came out as, or null while it is not out or nobody waits on it.</summary>
    public Release? OutAs(string imdbId)
    {
        if (!_out.TryGetValue(imdbId, out var release)) return null;
        if (Find(imdbId) is not null) return release;

        // A film nobody waits on any longer is nobody's news.
        _out.TryRemove(imdbId, out _);
        return null;
    }

    private static void Subscribe(SqliteConnection c, string imdbId, WishSubscriber subscriber) =>
        c.Execute("INSERT INTO wish_subscribers (imdb_id, user_id, asked_at) VALUES ($imdb, $user, $at)",
            ("$imdb", imdbId), ("$user", subscriber.UserId), ("$at", subscriber.AskedAt));

    /// <summary>
    /// The wishes, all of them or the one <paramref name="imdbId"/> names (whatever its case), or the ones
    /// <paramref name="subscriber"/> waits on, each with everybody waiting on it.
    /// </summary>
    private static List<Wish> Load(SqliteConnection c, string? imdbId, string? subscriber)
    {
        var wishes = c.Query(
            "SELECT imdb_id, title, year, starring, poster_url FROM wishes "
            + "WHERE ($imdb IS NULL OR imdb_id = $imdb COLLATE NOCASE) "
            + "AND ($user IS NULL OR imdb_id IN (SELECT imdb_id FROM wish_subscribers WHERE user_id = $user)) ORDER BY rowid",
            r => new Wish
            {
                ImdbId = r.GetString(0), Title = r.GetString(1), Year = r.GetIntOrNull(2), Starring = r.GetStringOrNull(3),
                PosterUrl = r.GetStringOrNull(4),
            },
            ("$imdb", imdbId), ("$user", subscriber));

        return [.. wishes.Select(w => w with
        {
            Subscribers = c.Query("SELECT user_id, asked_at FROM wish_subscribers WHERE imdb_id = $imdb ORDER BY rowid",
                r => new WishSubscriber { UserId = r.GetString(0), AskedAt = r.GetInstant(1) }, ("$imdb", w.ImdbId)),
        })];
    }
}
