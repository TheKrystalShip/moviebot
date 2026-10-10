using TheKrystalShip.MovieBot.Acquire.Tmdb;
using TheKrystalShip.MovieBot.Api.Data;

namespace TheKrystalShip.MovieBot.Api.Films;

/// <summary>
/// The artwork every film database cache keeps, and which image in the database's store each kept file
/// was fetched from.
///
/// An image a path names never changes; a new image comes under a new path. So a file is fetched only
/// when nothing is kept under its name or what is kept came from another path, and a refresh that
/// names the same path costs nothing. The bytes are files, served as they are; what each came from is
/// a row, by the cache that keeps it and the file's name under that cache's directory.
/// </summary>
public sealed class KeptImages(CinemaDatabase database)
{
    /// <summary>The path a kept file was fetched from; null when no file is kept under the name.</summary>
    public string? Source(string cache, string root, string file)
    {
        if (!File.Exists(Path.Combine(root, file))) return null;
        return database.Read(c => c.First("SELECT source FROM images WHERE cache = $cache AND file = $file",
            r => r.GetString(0), ("$cache", cache), ("$file", file)));
    }

    /// <summary>
    /// Keeps the image <paramref name="source"/> names as <paramref name="file"/>, fetching it at
    /// <paramref name="size"/> when what is kept came from elsewhere, and removes the file when the
    /// database names no image. True when a file is kept under the name afterwards.
    /// </summary>
    public async Task<bool> KeepAsync(TmdbClient tmdb, string cache, string root, string file, string? source, string size, CancellationToken ct)
    {
        var target = Path.Combine(root, file);
        if (source is null)
        {
            Forget(cache, root, file);
            return false;
        }
        if (Source(cache, root, file) == source) return true;

        if (await tmdb.ImageAsync(source, size, ct) is not { } bytes) return File.Exists(target);
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        await File.WriteAllBytesAsync(target + ".tmp", bytes, ct);
        File.Move(target + ".tmp", target, overwrite: true);
        database.Write(c => c.Execute(
            "INSERT OR REPLACE INTO images (cache, file, source) VALUES ($cache, $file, $source)",
            ("$cache", cache), ("$file", file), ("$source", source)));
        return true;
    }

    /// <summary>Removes a kept file and what it was fetched from.</summary>
    public void Forget(string cache, string root, string file)
    {
        var path = Path.Combine(root, file);
        if (File.Exists(path)) File.Delete(path);
        database.Write(c => c.Execute("DELETE FROM images WHERE cache = $cache AND file = $file", ("$cache", cache), ("$file", file)));
    }
}
