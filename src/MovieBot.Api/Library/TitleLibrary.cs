using System.Collections.Concurrent;
using TheKrystalShip.MovieBot.Core;

namespace TheKrystalShip.MovieBot.Api.Library;

/// <summary>What the library listing shows, without the full track graph behind it.</summary>
public sealed record TitleSummary
{
    public required string Id { get; init; }
    public required string Title { get; init; }
    public required double DurationSeconds { get; init; }
    public required TitleStatus Status { get; init; }
    public double? HeadSeconds { get; init; }
    public string? Poster { get; init; }
}

/// <summary>
/// Reads the manifests the ingest worker writes.
///
/// A manifest is rewritten every couple of seconds during a transcode, so this caches by
/// last-write time rather than holding a snapshot: the head a seek is judged against has to be
/// the head that is actually on disk, and re-reading a small JSON file when its timestamp moves
/// costs nothing next to being wrong.
/// </summary>
public sealed class TitleLibrary(string mediaRoot, ILogger<TitleLibrary> logger)
{
    private readonly ConcurrentDictionary<string, CacheEntry> _cache = new();

    public string MediaRoot { get; } = Path.GetFullPath(mediaRoot);

    private sealed record CacheEntry(Manifest Manifest, DateTime LastWriteUtc);

    public IReadOnlyList<TitleSummary> List()
    {
        if (!Directory.Exists(MediaRoot)) return [];

        var summaries = new List<TitleSummary>();
        foreach (var directory in Directory.EnumerateDirectories(MediaRoot))
        {
            var id = Path.GetFileName(directory);
            if (Get(id) is not { } manifest) continue;

            summaries.Add(new TitleSummary
            {
                Id = manifest.Id,
                Title = manifest.Title,
                DurationSeconds = manifest.DurationSeconds,
                Status = manifest.Status,
                HeadSeconds = manifest.HeadSeconds,
                Poster = manifest.Poster
            });
        }

        return summaries.OrderBy(s => s.Title, StringComparer.OrdinalIgnoreCase).ToList();
    }

    public Manifest? Get(string id)
    {
        if (!IsSafeId(id)) return null;

        var path = Path.Combine(MediaRoot, id, "manifest.json");
        if (!File.Exists(path)) return null;

        var lastWrite = File.GetLastWriteTimeUtc(path);
        if (_cache.TryGetValue(id, out var cached) && cached.LastWriteUtc == lastWrite)
            return cached.Manifest;

        try
        {
            var manifest = ManifestJson.Deserialize(File.ReadAllText(path));
            if (manifest is null) return null;

            _cache[id] = new CacheEntry(manifest, lastWrite);
            return manifest;
        }
        catch (Exception ex) when (ex is IOException or System.Text.Json.JsonException)
        {
            // The ingest worker writes through a temp file and an atomic move, so a torn read
            // should not happen; if it somehow does, the previous good copy is better than none.
            logger.LogWarning(ex, "Could not read manifest for {TitleId}", id);
            return _cache.TryGetValue(id, out var stale) ? stale.Manifest : null;
        }
    }

    /// <summary>
    /// The head a seek is judged against: how far the transcode has reached, or null when the
    /// title is complete and the whole film is seekable.
    /// </summary>
    public double? HeadOf(string? titleId)
    {
        if (titleId is null) return null;
        var manifest = Get(titleId);
        return manifest?.Status == TitleStatus.Transcoding ? manifest.HeadSeconds : null;
    }

    /// <summary>
    /// Ids name a directory under the media root, so anything that could climb out of it is
    /// refused rather than sanitised — a rewritten path that still resolves is worse than a 404.
    /// </summary>
    public static bool IsSafeId(string id) =>
        !string.IsNullOrWhiteSpace(id)
        && id.Length <= 200
        && id.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '.')
        && !id.Contains("..", StringComparison.Ordinal)
        && id[0] != '.';
}
