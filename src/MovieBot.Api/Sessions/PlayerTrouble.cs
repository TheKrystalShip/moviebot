namespace TheKrystalShip.MovieBot.Api.Sessions;

/// <summary>
/// What a viewer's player says went wrong with its film, written to the log, and to nothing else.
///
/// Everything that keeps a film from playing happens in a browser: a playlist that failed, a store on
/// the device that would not answer, a playhead left on nothing it can play. None of it reaches the
/// server as anything but a pattern of requests, so the player says it outright, naming its playback,
/// the id its requests carry as their CMCD session in the ingress's delivery log, which joins a report
/// to every request that playback made.
///
/// A report is a kind from a fixed list, a playhead and a short detail, never anything the page made
/// up. Each connection is held to <see cref="PerMinute"/> lines a minute; what it says past that is
/// counted, and the count is written once the minute is over, so a player stuck in a loop costs the
/// log a line a minute.
/// </summary>
public sealed class PlayerTrouble(TimeProvider clock, ILogger<PlayerTrouble> logger)
{
    /// <summary>What a player may report, as the player names it.</summary>
    public static readonly IReadOnlySet<string> Kinds = new HashSet<string>(StringComparer.Ordinal)
    {
        "network",     // a fatal network error, waited out and loaded again
        "media",       // a fatal media error, recovered from
        "stuck",       // the playhead sat on nothing it could play and the element was attached afresh
        "device-slow", // the device's storage did not answer in time and the film streamed without it
        "no-start",    // the film did not reach the element within the load's wait
        "gave-up",     // the player stopped trying, and says why to the viewer
    };

    public const int PerMinute = 6;
    public const int DetailLength = 160;

    private readonly Lock _gate = new();
    private readonly Dictionary<string, Window> _windows = new();

    private sealed class Window
    {
        public DateTimeOffset Start;
        public int Written;
        public int Dropped;
    }

    /// <summary>Writes a report, unless its connection already wrote its share this minute.</summary>
    public void Report(string connectionId, string who, string sessionId, string? titleId,
        string kind, double atSeconds, string? detail, string? playback)
    {
        if (!Kinds.Contains(kind)) return;
        var now = clock.GetUtcNow();
        int dropped;
        lock (_gate)
        {
            if (!_windows.TryGetValue(connectionId, out var window) || now - window.Start >= TimeSpan.FromMinutes(1))
            {
                dropped = window?.Dropped ?? 0;
                _windows[connectionId] = window = new Window { Start = now };
            }
            else dropped = 0;

            if (window.Written >= PerMinute)
            {
                window.Dropped++;
                return;
            }
            window.Written++;
        }

        if (dropped > 0)
            logger.LogWarning("{Who}'s player in {SessionId} reported {Dropped} more problems last minute, not written",
                who, sessionId, dropped);
        logger.LogWarning("{Who}'s player in {SessionId}: {Kind} at {At:F1}s of {TitleId}{Detail} (playback {Playback})",
            who, sessionId, kind, double.IsFinite(atSeconds) ? atSeconds : 0, titleId ?? "nothing",
            Clean(detail) is { Length: > 0 } said ? $": {said}" : "", Clean(playback) ?? "unnamed");
    }

    /// <summary>Forgets a connection that left, and writes what it was still holding back.</summary>
    public void Leave(string connectionId, string who, string sessionId)
    {
        Window? window;
        lock (_gate)
        {
            if (!_windows.Remove(connectionId, out window)) return;
        }
        if (window.Dropped > 0)
            logger.LogWarning("{Who}'s player in {SessionId} reported {Dropped} more problems before leaving, not written",
                who, sessionId, window.Dropped);
    }

    /// <summary>Printable characters only, cut to <see cref="DetailLength"/>; null for nothing.</summary>
    private static string? Clean(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var kept = new string(text.Where(c => !char.IsControl(c)).Take(DetailLength).ToArray()).Trim();
        return kept.Length == 0 ? null : kept;
    }
}
