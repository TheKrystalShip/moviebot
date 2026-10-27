using System.Collections.Concurrent;

namespace TheKrystalShip.MovieBot.Api.Acquisition;

/// <summary>
/// Whether what a room waits on has stopped arriving: none of it has come for <see cref="Patience"/>.
///
/// Measured from what arrives, not from the torrent client's own word for its state, which flips to
/// stalled and back within seconds on a download that is fine. Held in memory, by room: a restart
/// starts every count again, which only delays saying so.
/// </summary>
public sealed class ArrivalWatch(TimeProvider clock)
{
    /// <summary>How long nothing may arrive before the room is told it is late.</summary>
    public static readonly TimeSpan Patience = TimeSpan.FromSeconds(60);

    private readonly ConcurrentDictionary<string, (string Item, double Progress, DateTimeOffset Since)> _seen =
        new(StringComparer.Ordinal);

    /// <summary>
    /// Notes how far <paramref name="item"/> has got for <paramref name="room"/>; true when it has not
    /// moved for <see cref="Patience"/>. Another item, or any of this one arriving, starts the count again.
    /// </summary>
    public bool Stalled(string room, string item, double progress)
    {
        var now = clock.GetUtcNow();
        if (!_seen.TryGetValue(room, out var seen) || seen.Item != item || progress > seen.Progress)
        {
            _seen[room] = (item, progress, now);
            return false;
        }
        return now - seen.Since >= Patience;
    }

    /// <summary>The room waits on nothing any more.</summary>
    public void Forget(string room) => _seen.TryRemove(room, out _);
}
