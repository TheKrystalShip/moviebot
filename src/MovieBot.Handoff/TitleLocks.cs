using System.Collections.Concurrent;

namespace TheKrystalShip.MovieBot.Handoff;

/// <summary>
/// One lock per title, held by whoever is about to change what is under its directory.
///
/// Two of this process's workers write a finished title: settling moves its directory to the cold
/// root, and the marker pass rewrites its manifest. A manifest rewritten in the middle of a copy is
/// missing from the copy and then deleted from the disk it was written to, so both take the title's
/// lock for the whole of what they do and look the directory up again once they hold it.
/// </summary>
public sealed class TitleLocks
{
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _locks = new(StringComparer.Ordinal);

    /// <summary>Waits for the title, and returns what releases it.</summary>
    public async Task<IDisposable> AcquireAsync(string id, CancellationToken ct)
    {
        var gate = _locks.GetOrAdd(id, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(ct);
        return new Release(gate);
    }

    private sealed class Release(SemaphoreSlim gate) : IDisposable
    {
        private int _released;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _released, 1) == 0) gate.Release();
        }
    }
}
