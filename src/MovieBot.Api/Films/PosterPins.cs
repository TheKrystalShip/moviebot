using System.Collections.Concurrent;

namespace TheKrystalShip.MovieBot.Api.Films;

/// <summary>
/// The one poster each title is shown by: the film database's image path the API first showed it
/// with, whichever of a trending shelf, a search result or a description that was, kept one small
/// file per IMDb id.
///
/// The database's idea of a title's poster moves: a refresh can name another image, and its search,
/// trending and description answers can each name a different one on the same day. A person finds a
/// title by its poster, so every poster cache fetches the pinned path rather than the one it was
/// answered with, and a title keeps the face it was first seen with for as long as it is kept.
/// </summary>
public sealed class PosterPins(string root)
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

        var file = Path.Combine(root, imdbId);
        if (Read(file) is { } kept) return _pinned.GetOrAdd(imdbId, kept);

        if ((shown ?? offered) is not { Length: > 0 } first) return null;
        try
        {
            Directory.CreateDirectory(root);
            // CreateNew, so two caches pinning one title at once agree on whichever wrote first.
            using (var stream = new FileStream(file, FileMode.CreateNew, FileAccess.Write))
            using (var writer = new StreamWriter(stream))
                writer.Write(first);
            return _pinned.GetOrAdd(imdbId, first);
        }
        catch (IOException) when (Read(file) is { } won)
        {
            return _pinned.GetOrAdd(imdbId, won);
        }
    }

    /// <summary>The path a cache's kept poster was fetched from, by the marker written beside it; null when none is kept.</summary>
    public static string? Shown(string target)
    {
        var marker = target + ".src";
        try
        {
            return File.Exists(target) && File.Exists(marker) ? File.ReadAllText(marker) : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static string? Read(string file)
    {
        try
        {
            return File.Exists(file) && File.ReadAllText(file).Trim() is { Length: > 0 } path ? path : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
