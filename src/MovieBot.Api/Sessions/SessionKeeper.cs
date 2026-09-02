using Microsoft.Extensions.Options;

namespace TheKrystalShip.MovieBot.Api.Sessions;

/// <summary>
/// Writes the rooms down, and puts them back.
///
/// Restoring is called for explicitly before the server begins listening rather than left to the
/// order hosted services happen to start in, because a room restored after the first client has
/// already joined is a room that client was told did not exist.
///
/// Writing is on a short delay rather than per change. A drag along the scrub bar is a burst of
/// changes and each one would be a whole file; a delay collapses the burst into one write and
/// costs, at worst, the last couple of seconds of a room whose server stopped without warning. A
/// room whose server stopped with warning loses nothing: the last write is on the way out.
/// </summary>
public sealed class SessionKeeper(
    SessionStore sessions,
    SessionJournal journal,
    IOptions<RoomOptions> rooms,
    TimeProvider clock,
    ILogger<SessionKeeper> logger) : IHostedService, IDisposable
{
    /// <summary>Long enough to collapse a scrub into one write, short enough to lose nothing worth having.</summary>
    private static readonly TimeSpan SettleFor = TimeSpan.FromSeconds(2);

    private readonly CancellationTokenSource _stopping = new();
    private Timer? _timer;

    /// <summary>Puts back what the last run was holding. Called before anything can serve a request.</summary>
    public void Restore()
    {
        var restored = journal.Read(clock.GetUtcNow(), rooms.Value.IdleTimeout);
        sessions.Restore(restored);

        if (restored.Count == 0) return;

        logger.LogInformation(
            "Restored {Count} room(s) from {Path}: {Rooms}",
            restored.Count, journal.Path,
            string.Join(", ", restored.Select(r =>
                $"{r.State.SessionId} ({r.State.TitleId ?? "nothing playing"})")));
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _timer = new Timer(_ => Flush(), null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        sessions.Changed += OnChanged;
        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        sessions.Changed -= OnChanged;
        await _stopping.CancelAsync();

        // Whatever the delay was still holding. A planned stop is the one case where nothing has
        // to be lost at all.
        await journal.WriteAsync(sessions.Snapshot(), CancellationToken.None);
    }

    /// <summary>One timer, restarted on every change: a burst becomes one write at the end of it.</summary>
    private void OnChanged() => _timer?.Change(SettleFor, Timeout.InfiniteTimeSpan);

    private void Flush()
    {
        if (_stopping.IsCancellationRequested) return;
        _ = journal.WriteAsync(sessions.Snapshot(), _stopping.Token);
    }

    public void Dispose()
    {
        _timer?.Dispose();
        _stopping.Dispose();
    }
}
