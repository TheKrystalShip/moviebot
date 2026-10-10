using System.Collections.Concurrent;
using TheKrystalShip.MovieBot.Api.Data;

namespace TheKrystalShip.MovieBot.Api.Films;

/// <summary>
/// The one poster each title is shown by: the film database's image path the API first showed it
/// with, whichever of a trending shelf, a search result or a description that was, kept by IMDb id.
///
/// The database's idea of a title's poster moves: a refresh can name another image, and its search,
/// trending and description answers can each name a different one on the same day. A person finds a
/// title by its poster, so every poster cache fetches the pinned path rather than the one it was
/// answered with, and a title keeps the face it was first seen with for as long as it is kept.
/// </summary>
public sealed class PosterPins(CinemaDatabase database)
{
    private readonly ConcurrentDictionary<string, string> _pinned = new(StringComparer.Ordinal);

    /// <summary>
    /// The path a title's poster is fetched from. <paramref name="shown"/> is the path a cache already
    /// shows the title with, and <paramref name="offered"/> the one the database answered with now; the
    /// first a title is asked about with is pinned. Null when nothing is pinned and nothing is offered.
    /// </summary>
    public string? Pin(string imdbId, string? shown, string? offered)
    {
        if (!FilmStore.IsImdbId(imdbId)) return shown ?? offered;
        if (_pinned.TryGetValue(imdbId, out var pinned)) return pinned;

        const string Pinned = "SELECT path FROM poster_pins WHERE imdb_id = $imdb";
        var kept = (shown ?? offered) is { Length: > 0 } first
            ? database.Write(c =>
            {
                // Two caches pinning one title at once agree on whichever was first.
                c.Execute("INSERT OR IGNORE INTO poster_pins (imdb_id, path) VALUES ($imdb, $path)", ("$imdb", imdbId), ("$path", first));
                return c.First(Pinned, r => r.GetString(0), ("$imdb", imdbId));
            })
            : database.Read(c => c.First(Pinned, r => r.GetString(0), ("$imdb", imdbId)));
        return kept is null ? null : _pinned.GetOrAdd(imdbId, kept);
    }
}
