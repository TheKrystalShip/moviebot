namespace TheKrystalShip.MovieBot.Handoff;

/// <summary>
/// Finds the film inside what a torrent left on disk.
///
/// A torrent is rarely one file. Alongside the feature there are samples, trailers, featurettes,
/// subtitle sidecars and an nfo, and none of them are what somebody asked to watch.
/// </summary>
public static class FilmFile
{
    private static readonly string[] VideoExtensions =
        [".mkv", ".mp4", ".avi", ".m4v", ".mov", ".ts", ".m2ts", ".wmv"];

    /// <summary>
    /// Words that name a file as something other than the feature. Matched against the file name
    /// only, because a directory named for the release can contain any of them.
    /// </summary>
    private static readonly string[] NotTheFeature =
        ["sample", "trailer", "featurette", "extra", "behind.the.scenes", "deleted"];

    /// <summary>
    /// The film, or null when there is nothing here that looks like one.
    /// </summary>
    /// <param name="contentPath">What the torrent client reports as the torrent's contents.</param>
    /// <param name="minimumBytes">
    /// The floor under what counts as a feature. Without one, a torrent holding only a sample
    /// yields the sample and the room watches ninety seconds of a film.
    /// </param>
    public static string? Locate(string contentPath, long minimumBytes)
    {
        if (File.Exists(contentPath))
            return IsFilm(contentPath, minimumBytes) ? contentPath : null;

        if (!Directory.Exists(contentPath)) return null;

        return Directory
            .EnumerateFiles(contentPath, "*", SearchOption.AllDirectories)
            .Where(f => IsFilm(f, minimumBytes))
            // The feature is the largest thing in the torrent once the obvious extras are gone.
            .OrderByDescending(f => new FileInfo(f).Length)
            .FirstOrDefault();
    }

    private static bool IsFilm(string path, long minimumBytes)
    {
        var extension = Path.GetExtension(path);
        if (!VideoExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase)) return false;

        var name = Path.GetFileNameWithoutExtension(path);
        if (NotTheFeature.Any(w => name.Contains(w, StringComparison.OrdinalIgnoreCase)))
            return false;

        try
        {
            return new FileInfo(path).Length >= minimumBytes;
        }
        catch (FileNotFoundException)
        {
            return false;
        }
    }
}
