using System.Collections.Concurrent;

namespace TheKrystalShip.MovieBot.Handoff;

/// <summary>
/// The episodes this process could not make, by download and file, so a pass does not start them
/// again every half minute.
///
/// A source still arriving is not yet the file it will be, so an episode that failed on one is
/// tried once more when its file is whole; one that failed on a whole file is not tried again. A
/// torrent this process gave up on that is owed a transcode again is one somebody asked for again,
/// and every failure of it is forgotten. All of it is held only for this process's life: a restart
/// tries each episode once more.
/// </summary>
public sealed class EpisodeFailures
{
    /// <summary>True where the source was whole when the episode failed.</summary>
    private readonly ConcurrentDictionary<string, bool> _failed = new(StringComparer.Ordinal);

    private readonly ConcurrentDictionary<string, byte> _gaveUp = new(StringComparer.Ordinal);

    /// <summary>The key an episode is held by here and in the in-flight set: its download and its file.</summary>
    public static string Key(string hash, string path) => $"{hash}/{path}";

    public void Failed(string hash, string path, bool whole) => _failed[Key(hash, path)] = whole;

    /// <summary>
    /// Whether an episode may be started, its file having arrived to <paramref name="progress"/>:
    /// one that never failed, or one that failed while its file arrived and whose file is whole now.
    /// </summary>
    public bool MayStart(string hash, string path, double progress) =>
        !_failed.TryGetValue(Key(hash, path), out var whole) || (!whole && progress >= 1);

    /// <summary>Whether an episode failed while its file arrived and waits for the file to be whole.</summary>
    public bool Waiting(string hash, string path, double progress) =>
        _failed.TryGetValue(Key(hash, path), out var whole) && !whole && progress < 1;

    /// <summary>The torrent was marked failed: none of its episodes could be made.</summary>
    public void GaveUp(string hash) => _gaveUp.TryAdd(hash, 0);

    /// <summary>
    /// The torrent is owed a transcode. One given up on and owed again was asked for again, and its
    /// episodes are forgotten here so that each is tried.
    /// </summary>
    public void Owed(string hash)
    {
        if (!_gaveUp.TryRemove(hash, out _)) return;
        foreach (var key in _failed.Keys.Where(k => k.StartsWith($"{hash}/", StringComparison.Ordinal)))
            _failed.TryRemove(key, out _);
    }
}
