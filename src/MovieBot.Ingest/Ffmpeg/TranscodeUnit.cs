using System.Diagnostics;
using System.Runtime.ExceptionServices;

namespace TheKrystalShip.MovieBot.Ingest.Ffmpeg;

/// <summary>
/// One ffmpeg process of the main pass: a video rung, or every audio rendition. It owns its
/// process, its progress and its read-ahead guard, so a unit can be started, watched and ended on
/// its own, whatever else is running beside it.
/// </summary>
public sealed class TranscodeUnit
{
    private readonly IReadOnlyList<IReadOnlyList<string>> _attempts;
    private readonly Func<bool> _hasOutput;
    private readonly Func<ReadAheadGuard>? _guard;
    private readonly Action<string> _log;

    private volatile Process? _process;
    private bool _guardFailed;
    private double _speed;

    /// <param name="name">What the unit is called in the log: <c>v0</c>, <c>audio.1</c>.</param>
    /// <param name="group">
    /// The part of the title the unit makes: a video rung's directory, or <c>audio</c>, which
    /// several units share. Whatever holds or resumes work addresses a group's units together.
    /// </param>
    /// <param name="attempts">
    /// Command lines in the order they are tried. A later one runs only when the one before it
    /// failed having written nothing, which is how a decoder that cannot open a source gives way
    /// to one that can; a failure after output exists is the unit's failure.
    /// </param>
    /// <param name="hasOutput">Whether the unit has written anything yet.</param>
    /// <param name="guard">
    /// Makes the guard for one attempt, when the source is still arriving. Each process reads the
    /// source through descriptors of its own, so each is guarded on its own.
    /// </param>
    public TranscodeUnit(
        string name,
        string group,
        IReadOnlyList<IReadOnlyList<string>> attempts,
        Func<bool> hasOutput,
        Func<ReadAheadGuard>? guard,
        Action<string> log)
    {
        Name = name;
        Group = group;
        _attempts = attempts;
        _hasOutput = hasOutput;
        _guard = guard;
        _log = log;
    }

    public string Name { get; }

    public string Group { get; }

    /// <summary>ffmpeg's own report of throughput against the film's clock, 0 until it has one.</summary>
    public double Speed => Volatile.Read(ref _speed);

    /// <summary>
    /// Runs the unit to the end of its source. Cancelling the token ends the process.
    /// </summary>
    /// <exception cref="FfmpegException">
    /// The process failed, or its guard could not do its job. The guard's message replaces
    /// ffmpeg's, which would only say that the process was killed.
    /// </exception>
    public async Task RunAsync(CancellationToken ct)
    {
        var started = Stopwatch.StartNew();

        for (var i = 0; i < _attempts.Count; i++)
        {
            try
            {
                await RunAttemptAsync(_attempts[i], ct);
                _log($"  {Name} finished in {started.Elapsed:m\\:ss}");
                return;
            }
            catch (FfmpegException ex) when (
                i + 1 < _attempts.Count && !_guardFailed && !ct.IsCancellationRequested && !_hasOutput())
            {
                _log($"  {Name}: {ex.Message} — trying again another way");
            }
        }
    }

    /// <summary>
    /// Runs every unit to its end. The first to fail ends the rest: a film missing any of its
    /// renditions is not one a player can be handed, so there is nothing for the others to finish,
    /// and the error that is reported is the one that started it.
    /// </summary>
    public static async Task RunAllAsync(IReadOnlyList<TranscodeUnit> units, CancellationToken ct)
    {
        using var together = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var pending = units.Select(unit => unit.RunAsync(together.Token)).ToList();
        Exception? failure = null;

        while (pending.Count > 0)
        {
            var finished = await Task.WhenAny(pending);
            pending.Remove(finished);

            if (finished.Exception is { } aggregate && failure is null)
            {
                failure = aggregate.InnerException ?? aggregate;
                await together.CancelAsync();
            }
        }

        ct.ThrowIfCancellationRequested();
        if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
    }

    /// <summary>Ends the process now. A unit already finished or never started is left alone.</summary>
    public void Kill()
    {
        try
        {
            if (_process is { HasExited: false } process) process.Kill(entireProcessTree: true);
        }
        catch (Exception ex) when (ex is InvalidOperationException or NotSupportedException)
        {
            // Already gone, which is the outcome that was wanted.
        }
    }

    private async Task RunAttemptAsync(IReadOnlyList<string> arguments, CancellationToken ct)
    {
        Task? guarding = null;

        // The guard is attached the moment the process exists, because what it protects against
        // begins with the first read. A guard that cannot do its job kills the process rather
        // than letting it run on unwatched: the output of an unwatched run over a part-downloaded
        // file is a film with silence and stillness in it and nothing to say so.
        try
        {
            await FfmpegProcess.RunAsync(arguments, p => Volatile.Write(ref _speed, p.Speed), ct,
                onStarted: process =>
                {
                    _process = process;
                    if (_guard is not null) guarding = GuardAsync(_guard(), process, ct);
                });
        }
        finally
        {
            // Awaited last and deliberately allowed to replace whatever the process reported:
            // when the guard is the reason it died, the guard's message says what happened.
            if (guarding is not null) await guarding;
        }
    }

    private async Task GuardAsync(ReadAheadGuard watching, Process process, CancellationToken ct)
    {
        try
        {
            await watching.WatchAsync(process, ct);
        }
        catch
        {
            _guardFailed = true;
            Kill();
            throw;
        }
    }
}
