using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using TheKrystalShip.MovieBot.Core;

namespace TheKrystalShip.MovieBot.Api.Subtitles;

/// <summary>
/// Subtitles fetched from outside, kept beside the library rather than inside it.
///
/// They live under their own root, away from the media directory, for three reasons that all
/// point the same way. Everything under the media root is regenerable from the source file and
/// these are not — each one cost a download from a limited daily allowance — so a re-ingest that
/// replaces a title's directory must not be able to take them with it. The transcode owns that
/// directory and rewrites the manifest inside it every few seconds, so anything written there by
/// another process is racing. And the service that serves the library holds the media root
/// read-only, which is a protection worth keeping rather than widening.
/// </summary>
public sealed class SubtitleStore(string root, ILogger<SubtitleStore> logger)
{
    public string Root { get; } = Path.GetFullPath(root);

    /// <summary>The path segment a subtitle is served under, within a title.</summary>
    public const string UriPrefix = "subs/";

    private readonly ConcurrentDictionary<string, CacheEntry> _cache = new();

    private sealed record CacheEntry(IReadOnlyList<SidecarSubtitle> Subtitles, DateTime LastWriteUtc);

    /// <summary>
    /// Everything fetched for one title. Cached against the directory's own timestamp rather than
    /// held, because another request adding a subtitle has to become visible without a restart.
    /// </summary>
    public IReadOnlyList<SidecarSubtitle> For(string titleId)
    {
        var directory = DirectoryFor(titleId);
        if (directory is null || !Directory.Exists(directory)) return [];

        var lastWrite = Directory.GetLastWriteTimeUtc(directory);
        if (_cache.TryGetValue(titleId, out var cached) && cached.LastWriteUtc == lastWrite)
            return cached.Subtitles;

        var found = new List<SidecarSubtitle>();

        foreach (var descriptor in Directory.EnumerateFiles(directory, "*.json"))
        {
            try
            {
                var subtitle = JsonSerializer.Deserialize(
                    File.ReadAllText(descriptor), ManifestJsonContext.Default.SidecarSubtitle);

                // A descriptor is only written once its subtitle is on disk, so one pointing at a
                // missing file means something removed the file by hand.
                if (subtitle is not null && File.Exists(Path.ChangeExtension(descriptor, ".vtt")))
                    found.Add(subtitle);
            }
            catch (Exception ex) when (ex is JsonException or IOException)
            {
                logger.LogWarning(ex, "Could not read the subtitle descriptor {Path}.", descriptor);
            }
        }

        var ordered = found.OrderBy(s => s.AddedAt).ToList();
        _cache[titleId] = new CacheEntry(ordered, lastWrite);
        return ordered;
    }

    /// <summary>
    /// The file behind a <c>subs/</c> URI, or null when it is not one this holds. Resolved first
    /// and checked for containment afterwards, because a path that merely looks safe can still
    /// land outside the root through an encoded traversal or a symlink.
    /// </summary>
    public string? Resolve(string titleId, string fileName)
    {
        var directory = DirectoryFor(titleId);
        if (directory is null) return null;

        var requested = Path.GetFullPath(Path.Combine(directory, fileName));

        return requested.StartsWith(directory + Path.DirectorySeparatorChar, StringComparison.Ordinal)
               && File.Exists(requested)
            ? requested
            : null;
    }

    /// <summary>
    /// Writes a subtitle and the description of it.
    ///
    /// The subtitle lands first and the descriptor second, both through a temporary file and an
    /// atomic move. That order is the whole guarantee: a reader that sees a descriptor can always
    /// open the file it names, and a write interrupted halfway leaves a file nothing points at
    /// rather than a menu row that fails to load.
    /// </summary>
    public void Save(string titleId, SidecarSubtitle subtitle, string webVtt)
    {
        var directory = DirectoryFor(titleId)
            ?? throw new ArgumentException($"'{titleId}' is not a title id.", nameof(titleId));

        Directory.CreateDirectory(directory);

        WriteAtomic(Path.Combine(directory, subtitle.Id + ".vtt"), webVtt);
        WriteAtomic(
            Path.Combine(directory, subtitle.Id + ".json"),
            JsonSerializer.Serialize(subtitle, ManifestJsonContext.Default.SidecarSubtitle));

        _cache.TryRemove(titleId, out _);
    }

    private static void WriteAtomic(string path, string content)
    {
        var temporary = path + ".tmp";
        File.WriteAllText(temporary, content, new UTF8Encoding(false));
        File.Move(temporary, path, overwrite: true);
    }

    private string? DirectoryFor(string titleId) =>
        Library.TitleLibrary.IsSafeId(titleId) ? Path.Combine(Root, titleId) : null;

    /// <summary>
    /// What the player sees: one more entry in the title's subtitle list, marked as having come
    /// from outside the file rather than out of it.
    /// </summary>
    public static SubtitleTrack AsTrack(SidecarSubtitle subtitle) => new()
    {
        Id = subtitle.Id,
        Kind = TrackKind.Feature,
        Language = subtitle.Language,
        Label = subtitle.Label,
        HearingImpaired = subtitle.HearingImpaired,
        Source = SubtitleSource.Sidecar,
        Available = true,
        Uri = UriPrefix + subtitle.Id + ".vtt"
    };
}
