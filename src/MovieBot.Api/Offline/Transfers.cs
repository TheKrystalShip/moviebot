using Microsoft.Extensions.Options;
using TheKrystalShip.MovieBot.Api.Sessions;

namespace TheKrystalShip.MovieBot.Api.Offline;

/// <summary>
/// The tracks being sent to devices right now, and what each may take.
///
/// A save yields to the rooms. The server sends at most <see cref="OfflineOptions.MaxTransfers"/>
/// tracks at once and a person fetches at most <see cref="OfflineOptions.MaxTransfersPerPerson"/>, so a
/// phone filling up never takes the disk or the uplink a room is reading from; and while a room has
/// somebody in it each transfer is held to <see cref="OfflineOptions.MegabitsPerTransferWhileWatched"/>.
/// The count is in memory alone: a transfer is a request, and a restart ends every one of them.
/// </summary>
public sealed class Transfers(SessionStore sessions, IOptionsMonitor<OfflineOptions> options, TimeProvider clock)
{
    private readonly Lock _gate = new();
    private readonly Dictionary<string, int> _byPerson = new(StringComparer.Ordinal);
    private int _running;

    /// <summary>
    /// Whether anybody is in a room, read at most once a second: a transfer asks between every
    /// chunk it writes, and a room starting mid-transfer slows it within that second.
    /// </summary>
    private (DateTimeOffset At, bool Watched) _watched;

    /// <summary>How many tracks are being sent right now.</summary>
    public int Running
    {
        get { lock (_gate) return _running; }
    }

    /// <summary>A transfer's place, or null when the person or the server is already at its limit.</summary>
    public Lease? TryBegin(string userId)
    {
        var limits = options.CurrentValue;
        lock (_gate)
        {
            var mine = _byPerson.GetValueOrDefault(userId);
            if (_running >= limits.MaxTransfers || mine >= limits.MaxTransfersPerPerson) return null;

            _byPerson[userId] = mine + 1;
            _running++;
            return new Lease(this, userId);
        }
    }

    /// <summary>
    /// What a transfer may send per second now, or zero for as fast as it goes: the limit while
    /// somebody is in a room, and none otherwise.
    /// </summary>
    public long BytesPerSecond()
    {
        var megabits = options.CurrentValue.MegabitsPerTransferWhileWatched;
        if (megabits <= 0 || !Watched()) return 0;
        return (long)(megabits * 1_000_000 / 8);
    }

    private bool Watched()
    {
        var now = clock.GetUtcNow();
        var known = _watched;
        if (now - known.At < TimeSpan.FromSeconds(1)) return known.Watched;

        var watched = sessions.Viewing().Count > 0;
        _watched = (now, watched);
        return watched;
    }

    private void End(string userId)
    {
        lock (_gate)
        {
            _running--;
            var mine = _byPerson.GetValueOrDefault(userId) - 1;
            if (mine <= 0) _byPerson.Remove(userId); else _byPerson[userId] = mine;
        }
    }

    /// <summary>A transfer under way; disposing it frees its place.</summary>
    public sealed class Lease(Transfers owner, string userId) : IDisposable
    {
        private int _ended;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _ended, 1) == 0) owner.End(userId);
        }
    }

    /// <summary>
    /// How long to wait before sending more, having sent <paramref name="sent"/> bytes in
    /// <paramref name="elapsed"/> at no more than <paramref name="bytesPerSecond"/>: the time the bytes
    /// were owed less the time they took. Zero when the transfer is behind its rate or has none.
    /// </summary>
    public static TimeSpan Wait(long sent, TimeSpan elapsed, long bytesPerSecond)
    {
        if (bytesPerSecond <= 0) return TimeSpan.Zero;
        var owed = TimeSpan.FromSeconds((double)sent / bytesPerSecond);
        return owed > elapsed ? owed - elapsed : TimeSpan.Zero;
    }
}
