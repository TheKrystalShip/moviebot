using System.Diagnostics;
using TheKrystalShip.MovieBot.Ingest.Ffmpeg;
using TheKrystalShip.MovieBot.Ingest.Pipeline;
using Xunit;

namespace TheKrystalShip.MovieBot.Tests;

/// <summary>
/// The guard is what stops a transcode reading past the part of a download that has arrived, and
/// its two mechanisms are both the kind that fail silently: a wrong signal number never stops
/// anything, and a read position it cannot find looks like a transcode that is not moving.
/// Neither throws, so both are exercised against a real process.
/// </summary>
public class ReadAheadGuardTests : IDisposable
{
    private readonly string _file = Path.Combine(
        Path.GetTempPath(), "moviebot-guard-" + Guid.NewGuid().ToString("N")[..8]);

    public ReadAheadGuardTests() => File.WriteAllBytes(_file, new byte[4 << 20]);

    public void Dispose()
    {
        try { File.Delete(_file); } catch (IOException) { }
        GC.SuppressFinalize(this);
    }

    /// <summary>Linux reports a stopped process as T in the third field of its stat line.</summary>
    private static char StateOf(int pid)
    {
        var stat = File.ReadAllText($"/proc/{pid}/stat");
        var afterName = stat.LastIndexOf(')');
        return stat[(afterName + 2)];
    }

    [Fact]
    public async Task Holds_a_reader_back_and_lets_it_go_again()
    {
        // A held reader is the whole mechanism. If the signal numbers were wrong this would run
        // through unpaused and the guard would be decoration.
        var availability = new StubAvailability { Readable = 0, Complete = false };

        // tail holds the file open itself. A shell redirect would not: the shell forks and the
        // child holds the descriptor, so the process being watched would have none.
        using var process = Process.Start(new ProcessStartInfo("tail", ["-f", _file])
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
        })!;

        var guard = new ReadAheadGuard(availability, _file, marginBytes: 1 << 20);
        using var cancellation = new CancellationTokenSource();
        var watching = guard.WatchAsync(process, cancellation.Token);

        await WaitUntil(() => StateOf(process.Id) == 'T', TimeSpan.FromSeconds(5));
        Assert.Equal('T', StateOf(process.Id));
        Assert.True(guard.Pauses > 0);

        // The download gets ahead, and the reader is expected to be let go.
        availability.Readable = long.MaxValue;

        await WaitUntil(() => StateOf(process.Id) != 'T', TimeSpan.FromSeconds(5));
        Assert.NotEqual('T', StateOf(process.Id));

        await cancellation.CancelAsync();
        await watching;
        process.Kill();
    }

    [Fact]
    public async Task Always_leaves_the_reader_running_when_it_stops_watching()
    {
        // A guard that returns while the process is still stopped hangs the transcode forever,
        // and it would look exactly like a slow film.
        var availability = new StubAvailability { Readable = 0, Complete = false };

        // tail holds the file open itself. A shell redirect would not: the shell forks and the
        // child holds the descriptor, so the process being watched would have none.
        using var process = Process.Start(new ProcessStartInfo("tail", ["-f", _file])
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
        })!;

        var guard = new ReadAheadGuard(availability, _file, marginBytes: 1 << 20);
        using var cancellation = new CancellationTokenSource();
        var watching = guard.WatchAsync(process, cancellation.Token);

        await WaitUntil(() => StateOf(process.Id) == 'T', TimeSpan.FromSeconds(5));

        await cancellation.CancelAsync();
        await watching;

        Assert.NotEqual('T', StateOf(process.Id));
        process.Kill();
    }

    [Fact]
    public async Task Stops_watching_once_the_source_is_whole()
    {
        var availability = new StubAvailability { Readable = 0, Complete = true };

        // tail holds the file open itself. A shell redirect would not: the shell forks and the
        // child holds the descriptor, so the process being watched would have none.
        using var process = Process.Start(new ProcessStartInfo("tail", ["-f", _file])
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
        })!;

        var guard = new ReadAheadGuard(availability, _file, marginBytes: 1 << 20);

        // Returns on its own rather than needing to be cancelled: there is nothing left to guard.
        await guard.WatchAsync(process, CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5));

        Assert.NotEqual('T', StateOf(process.Id));
        process.Kill();
    }

    private static async Task WaitUntil(Func<bool> condition, TimeSpan within)
    {
        var deadline = DateTimeOffset.UtcNow + within;
        while (DateTimeOffset.UtcNow < deadline)
        {
            if (condition()) return;
            await Task.Delay(100);
        }
    }

    private sealed class StubAvailability : ISourceAvailability
    {
        public long Readable { get; set; }
        public bool Complete { get; set; }

        public Task<long> ReadableBytesAsync(CancellationToken ct) => Task.FromResult(Readable);
        public Task<bool> IsCompleteAsync(CancellationToken ct) => Task.FromResult(Complete);
    }
}
