using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using TheKrystalShip.MovieBot.Core;

namespace TheKrystalShip.MovieBot.Api.Subtitles;

/// <summary>
/// Which subtitle tracks somebody has watched a film with and confirmed.
///
/// Kept beside the fetched subtitles and outside the media root, for the same reason: a pin is a
/// person's judgement and nothing can regenerate it, where everything under the media root can be
/// rebuilt from the source file.
///
/// One file per pin rather than one list per film, so two people pinning at the same moment cannot
/// overwrite each other.
/// </summary>
public sealed class PinStore(string root, ILogger<PinStore> logger)
{
    private const string Directory = "pins";

    private readonly ConcurrentDictionary<string, CacheEntry> _cache = new();

    private sealed record CacheEntry(IReadOnlyDictionary<string, SubtitlePin> Pins, DateTime LastWriteUtc);

    /// <summary>
    /// Every pin on a title, by the track it belongs to.
    ///
    /// A pin naming a track the film no longer has is dropped rather than honoured. Ids come from
    /// stream indices, and a re-ingest can hand the same number to a different track — pointing an
    /// old judgement at whatever now holds that number would be worse than losing it.
    /// </summary>
    public IReadOnlyDictionary<string, SubtitlePin> For(string titleId)
    {
        var directory = DirectoryFor(titleId);
        if (directory is null || !System.IO.Directory.Exists(directory))
            return new Dictionary<string, SubtitlePin>();

        var lastWrite = System.IO.Directory.GetLastWriteTimeUtc(directory);
        if (_cache.TryGetValue(titleId, out var cached) && cached.LastWriteUtc == lastWrite)
            return cached.Pins;

        var pins = new Dictionary<string, SubtitlePin>(StringComparer.Ordinal);

        foreach (var file in System.IO.Directory.EnumerateFiles(directory, "*.json"))
        {
            try
            {
                var pin = JsonSerializer.Deserialize(
                    File.ReadAllText(file), ManifestJsonContext.Default.SubtitlePin);

                if (pin is not null) pins[Path.GetFileNameWithoutExtension(file)] = pin;
            }
            catch (Exception ex) when (ex is JsonException or IOException)
            {
                logger.LogWarning(ex, "Could not read the pin {Path}.", file);
            }
        }

        _cache[titleId] = new CacheEntry(pins, lastWrite);
        return pins;
    }

    public void Pin(string titleId, string trackId, SubtitlePin pin)
    {
        var path = PathFor(titleId, trackId)
            ?? throw new ArgumentException("Not a title and track this can hold.", nameof(trackId));

        System.IO.Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        var temporary = path + ".tmp";
        File.WriteAllText(
            temporary,
            JsonSerializer.Serialize(pin, ManifestJsonContext.Default.SubtitlePin),
            new UTF8Encoding(false));
        File.Move(temporary, path, overwrite: true);

        _cache.TryRemove(titleId, out _);
    }

    /// <summary>
    /// Removes a pin. Anyone may, deliberately: a wrong pin is fixed by one click from whoever
    /// notices, which is a better answer for a room of friends than deciding who is allowed.
    /// </summary>
    public void Unpin(string titleId, string trackId)
    {
        if (PathFor(titleId, trackId) is { } path && File.Exists(path)) File.Delete(path);

        _cache.TryRemove(titleId, out _);
    }

    private string? PathFor(string titleId, string trackId) =>
        DirectoryFor(titleId) is { } directory && IsSafeTrackId(trackId)
            ? Path.Combine(directory, trackId + ".json")
            : null;

    private string? DirectoryFor(string titleId) =>
        Library.TitleLibrary.IsSafeId(titleId)
            ? Path.Combine(Path.GetFullPath(root), titleId, Directory)
            : null;

    /// <summary>
    /// Track ids name a file, so anything that could climb out of the directory is refused rather
    /// than sanitised.
    /// </summary>
    public static bool IsSafeTrackId(string id) =>
        !string.IsNullOrWhiteSpace(id)
        && id.Length <= 100
        && id.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_');
}
