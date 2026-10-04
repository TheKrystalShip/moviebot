using System.Diagnostics;
using System.Runtime.Versioning;
using TheKrystalShip.MovieBot.Ingest.Ffmpeg;
using TheKrystalShip.MovieBot.Ingest.Pipeline;
using Xunit;

namespace TheKrystalShip.MovieBot.Tests;

[CollectionDefinition("fake ffmpeg", DisableParallelization = true)]
public sealed class FakeFfmpegCollection;

/// <summary>
/// The main pass is several ffmpeg processes that stand or fall together. These run units
/// against a stand-in <c>ffmpeg</c> that does what its first argument says, because what is under
/// test is what the units do about each outcome, not what ffmpeg encodes.
/// </summary>
[Collection("fake ffmpeg")]
[SupportedOSPlatform("linux")]
public sealed class TranscodeUnitTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "moviebot-fake-ffmpeg-" + Guid.NewGuid().ToString("N"));
    private readonly string _path = Environment.GetEnvironmentVariable("PATH") ?? "";
    private readonly string? _pids = Environment.GetEnvironmentVariable("FAKE_FFMPEG_PIDS");

    public TranscodeUnitTests()
    {
        Directory.CreateDirectory(_directory);
        var script = Path.Combine(_directory, "ffmpeg");

        // The unit appends its own options ahead of the arguments it was given, so the behaviour
        // is whichever word in the command line names one.
        File.WriteAllText(script, """
            #!/bin/sh
            echo $$ >> "$FAKE_FFMPEG_PIDS"
            for a in "$@"; do
              case "$a" in
                ok) exit 0 ;;
                fail) echo "decoder cannot open the source" >&2; exit 3 ;;
                write-then-fail) touch "$FAKE_FFMPEG_PIDS.output"; echo "died midway" >&2; exit 4 ;;
                hang) exec sleep 60 ;;
                read) exec 3< "$FAKE_FFMPEG_SOURCE"; exec sleep 60 ;;
              esac
            done
            exit 0
            """);
        File.SetUnixFileMode(script, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);

        File.WriteAllBytes(Source, new byte[4 << 20]);

        Environment.SetEnvironmentVariable("PATH", _directory + ":" + _path);
        Environment.SetEnvironmentVariable("FAKE_FFMPEG_PIDS", Path.Combine(_directory, "pids"));
        Environment.SetEnvironmentVariable("FAKE_FFMPEG_SOURCE", Source);
    }

    private string Source => Path.Combine(_directory, "source.mkv");

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("PATH", _path);
        Environment.SetEnvironmentVariable("FAKE_FFMPEG_PIDS", _pids);
        Environment.SetEnvironmentVariable("FAKE_FFMPEG_SOURCE", null);
        Directory.Delete(_directory, recursive: true);
    }

    private TranscodeUnit Unit(string name, string[][] attempts, List<string> log, Func<bool>? hasOutput = null) =>
        new(name, name, attempts, hasOutput ?? (() => false), guard: null, log.Add);

    private IEnumerable<int> Started() =>
        File.Exists(Path.Combine(_directory, "pids"))
            ? File.ReadAllLines(Path.Combine(_directory, "pids")).Select(int.Parse)
            : [];

    private static bool Alive(int pid)
    {
        try
        {
            using var process = Process.GetProcessById(pid);
            return !process.HasExited;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    [Fact]
    public async Task A_unit_that_failed_before_writing_anything_tries_its_next_command()
    {
        var log = new List<string>();
        var unit = Unit("v1", [["fail"], ["ok"]], log);

        await unit.RunAsync(CancellationToken.None);

        Assert.Equal(2, Started().Count());
        Assert.Contains(log, l => l.Contains("decoder cannot open the source") && l.Contains("another way"));
    }

    [Fact]
    public async Task A_unit_that_failed_after_writing_reports_the_failure()
    {
        var output = Path.Combine(_directory, "pids.output");
        var unit = Unit("v0", [["write-then-fail"], ["ok"]], [], () => File.Exists(output));

        var failure = await Assert.ThrowsAsync<FfmpegException>(() => unit.RunAsync(CancellationToken.None));

        Assert.Contains("exited 4", failure.Message);
        Assert.Single(Started());
    }

    [Fact]
    public async Task The_last_command_that_fails_is_the_units_failure()
    {
        var unit = Unit("v0", [["fail"], ["fail"]], []);

        var failure = await Assert.ThrowsAsync<FfmpegException>(() => unit.RunAsync(CancellationToken.None));

        Assert.Contains("exited 3", failure.Message);
        Assert.Equal(2, Started().Count());
    }

    [Fact]
    public async Task One_unit_failing_ends_the_others_and_its_error_is_the_one_reported()
    {
        var units = new List<TranscodeUnit>
        {
            Unit("v0", [["hang"]], []),
            Unit("v1", [["hang"]], []),
            Unit("audio", [["fail"]], [])
        };

        var failure = await Assert.ThrowsAsync<FfmpegException>(
            () => TranscodeUnit.RunAllAsync(units, CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(20)));

        Assert.Contains("exited 3", failure.Message);
        Assert.Contains("decoder cannot open the source", failure.Message);
        await AssertAllEndedAsync();
    }

    [Fact]
    public async Task Cancelling_ends_every_process()
    {
        var units = new List<TranscodeUnit>
        {
            Unit("v0", [["hang"]], []),
            Unit("audio", [["hang"]], [])
        };
        using var cancel = new CancellationTokenSource();

        var running = TranscodeUnit.RunAllAsync(units, cancel.Token);
        while (Started().Count() < 2) await Task.Delay(20);
        await cancel.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => running.WaitAsync(TimeSpan.FromSeconds(20)));
        await AssertAllEndedAsync();
    }

    private sealed class Arriving(long readable) : ISourceAvailability
    {
        public Task<long> ReadableBytesAsync(CancellationToken ct) => Task.FromResult(readable);
        public Task<bool> IsCompleteAsync(CancellationToken ct) => Task.FromResult(false);
    }

    private TranscodeUnit Guarded(string[] arguments, long readable) =>
        new("guarded", "guarded", [arguments], () => false,
            () => new ReadAheadGuard(new Arriving(readable), Source, marginBytes: 1 << 20,
                openTimeout: TimeSpan.FromSeconds(1)),
            _ => { });

    private static char StateOf(int pid) =>
        File.ReadAllText($"/proc/{pid}/stat") is var stat ? stat[(stat.LastIndexOf(')') + 2)] : '?';

    [Fact]
    public async Task Every_process_of_a_pass_reading_an_arriving_source_is_held_on_its_own()
    {
        // Nothing has arrived, so each reader is at the edge of what is readable.
        var units = new List<TranscodeUnit> { Guarded(["read"], 0), Guarded(["read"], 0) };
        using var cancel = new CancellationTokenSource();

        var running = TranscodeUnit.RunAllAsync(units, cancel.Token);
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (DateTime.UtcNow < deadline
               && !(Started().Count() == 2 && Started().All(p => StateOf(p) == 'T')))
            await Task.Delay(50);

        Assert.All(Started(), pid => Assert.Equal('T', StateOf(pid)));

        await cancel.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => running.WaitAsync(TimeSpan.FromSeconds(20)));
        await AssertAllEndedAsync();
    }

    [Fact]
    public async Task A_process_that_cannot_be_guarded_fails_the_pass_and_ends_the_others()
    {
        // One process opens the source and the other never does, so only one can be guarded.
        var units = new List<TranscodeUnit>
        {
            Guarded(["read"], long.MaxValue / 2),
            Guarded(["hang"], long.MaxValue / 2)
        };

        var failure = await Assert.ThrowsAsync<FfmpegException>(
            () => TranscodeUnit.RunAllAsync(units, CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(20)));

        Assert.Contains("Could not find an open descriptor", failure.Message);
        await AssertAllEndedAsync();
    }

    private async Task AssertAllEndedAsync()
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (Started().Any(Alive) && DateTime.UtcNow < deadline) await Task.Delay(50);

        Assert.DoesNotContain(Started(), Alive);
    }
}
