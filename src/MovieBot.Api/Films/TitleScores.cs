using System.Collections.Concurrent;
using TheKrystalShip.MovieBot.Acquire.Tmdb;
using TheKrystalShip.MovieBot.Api.Data;

namespace TheKrystalShip.MovieBot.Api.Films;

/// <summary>
/// The one rating each title is shown with, wherever it is shown (<see cref="Serve"/>): IMDb's,
/// where the IMDb import holds the title, else the newest the film database has given.
///
/// IMDb's votes are deep enough for a rating to mean the same thing across titles, so IMDb's is the
/// rating; a title the import does not hold (one with fewer than its vote floor, or out too recently
/// for the day's files) is rated by the film database instead, and <c>ratedBy</c> on a title's own
/// page says which.
///
/// Every answer the API keeps carries the film database's rating from when it was asked, and a new
/// release's moves daily, so a film page, a search result and a person's credits would each show a
/// different number for one title. Each place a rating arrives from the database records it here as
/// an observation stamped with when the answer was asked, and an observation older than what is held
/// is dropped. <see cref="Newest"/> is that rating, falling back to the answer's own for a title
/// nothing is held for; a rating with fewer than <see cref="CatalogueSearch.RatedVotes"/> votes behind
/// it is no rating. Only the film database's ratings are recorded: what <see cref="Newest"/> answers is
/// what a glance carries, and IMDb's is put on a title only as a view is made of it.
///
/// The key is the database's id and kind because a chart's answer names no IMDb id. A listing reads
/// a score on every row, so all of them are held in memory, written through.
/// </summary>
public sealed class TitleScores(CinemaDatabase database, ImdbRatings imdb)
{
    /// <summary>One rating as an answer carried it.</summary>
    internal readonly record struct Reading(int TmdbId, string Kind, double? Rating, int Votes);

    private readonly record struct Score(double Rating, int Votes, DateTimeOffset SeenAt);

    private readonly ConcurrentDictionary<(int TmdbId, string Kind), Score> _held = new();
    private readonly Lock _gate = new();
    private volatile bool _loaded;

    /// <summary>Records what the database said of one title at <paramref name="seenAt"/>.</summary>
    public void Observe(int tmdbId, string kind, double? rating, int votes, DateTimeOffset seenAt) =>
        Observe([new Reading(tmdbId, kind, rating, votes)], seenAt);

    /// <summary>Records every rating in an answer asked at <paramref name="seenAt"/>, in one transaction.</summary>
    internal void Observe(IEnumerable<Reading> readings, DateTimeOffset seenAt)
    {
        Load();
        var newer = readings
            .Where(r => r.Rating is not null && (r.Kind is "film" or "show")
                        && !(_held.TryGetValue((r.TmdbId, r.Kind), out var held) && held.SeenAt >= seenAt))
            .ToList();
        if (newer.Count == 0) return;

        lock (_gate)
        {
            var kept = newer
                .Where(r => !(_held.TryGetValue((r.TmdbId, r.Kind), out var held) && held.SeenAt >= seenAt))
                .DistinctBy(r => (r.TmdbId, r.Kind))
                .ToList();
            if (kept.Count == 0) return;

            database.Write(c =>
            {
                foreach (var r in kept)
                    c.Execute("""
                        INSERT INTO title_scores (tmdb_id, kind, rating, votes, seen_at) VALUES ($id, $kind, $rating, $votes, $at)
                        ON CONFLICT (tmdb_id, kind) DO UPDATE SET rating = excluded.rating, votes = excluded.votes, seen_at = excluded.seen_at
                        WHERE excluded.seen_at > title_scores.seen_at
                        """,
                        ("$id", r.TmdbId), ("$kind", r.Kind), ("$rating", r.Rating), ("$votes", r.Votes), ("$at", seenAt));
            });
            foreach (var r in kept) _held[(r.TmdbId, r.Kind)] = new Score(r.Rating!.Value, r.Votes, seenAt);
        }
    }

    /// <summary>What <c>ratedBy</c> names: whose rating a title is shown with.</summary>
    public const string ByImdb = "imdb";
    public const string ByDatabase = "tmdb";

    /// <summary>
    /// The rating and votes a view shows for a title, and whose they are: IMDb's for
    /// <paramref name="imdbId"/> where the import holds it, else the film database's newest for
    /// <paramref name="tmdbId"/> (<see cref="Newest"/>), else the answer's own
    /// (<paramref name="rating"/>, <paramref name="votes"/>) held to the same floor. No rating, and
    /// whose absent, when there is none.
    /// </summary>
    public (double? Rating, int Votes, string? RatedBy) Serve(string? imdbId, int? tmdbId, string kind, double? rating, int votes)
    {
        if (imdb.Of(imdbId) is { } fromImdb) return (fromImdb.Value, fromImdb.Votes, ByImdb);
        var (shown, count) = tmdbId is { } id ? Newest(id, kind, rating, votes) : Floored(rating, votes);
        return (shown, count, shown is null ? null : ByDatabase);
    }

    /// <summary>
    /// The film database's newest rating and votes for a title: the held ones, else the answer's own
    /// (<paramref name="rating"/>, <paramref name="votes"/>). No rating when fewer than
    /// <see cref="CatalogueSearch.RatedVotes"/> people voted; the votes are served either way.
    /// </summary>
    public (double? Rating, int Votes) Newest(int tmdbId, string kind, double? rating, int votes)
    {
        Load();
        if (_held.TryGetValue((tmdbId, kind), out var held)) (rating, votes) = (held.Rating, held.Votes);
        return Floored(rating, votes);
    }

    private static (double? Rating, int Votes) Floored(double? rating, int votes) =>
        (rating is not null && votes >= CatalogueSearch.RatedVotes ? rating : null, votes);

    /// <summary>Every rating in an answer the film database gave, for the answers that carry any.</summary>
    internal static IEnumerable<Reading> ReadingsOf(object answer) => answer switch
    {
        PersonCredits person => person.Titles.Select(Of),
        IEnumerable<CatalogueTitle> titles => titles.Select(Of),
        IEnumerable<ListedTitle> titles => titles.Select(Of),
        ListedPage page => page.Titles.Select(Of),
        Filmography filmography => filmography.Credits.Select(c => new Reading(c.TmdbId, c.Kind, c.Rating, c.Votes)),
        TitleCollection collection => collection.Films.Select(Of),
        _ => [],
    };

    private static Reading Of(CatalogueTitle t) => new(t.TmdbId, t.Kind, t.Rating, t.Votes);

    private static Reading Of(ListedTitle t) => new(t.TmdbId, t.Kind, t.Rating, t.Votes);

    private void Load()
    {
        if (_loaded) return;
        lock (_gate)
        {
            if (_loaded) return;
            foreach (var (id, kind, score) in database.Read(c => c.Query(
                         "SELECT tmdb_id, kind, rating, votes, seen_at FROM title_scores",
                         r => (r.GetInt32(0), r.GetString(1), new Score(r.GetDouble(2), r.GetInt32(3), r.GetInstant(4))))))
                _held[(id, kind)] = score;
            _loaded = true;
        }
    }
}
