using Microsoft.Extensions.Options;
using TheKrystalShip.MovieBot.Core;

namespace TheKrystalShip.MovieBot.Api.Transcode;

/// <summary>
/// The executor's last report, kept in memory with the time it arrived. It describes a card that
/// moves every second, so it is a fact for <c>StatusStaleSeconds</c> and nothing after: a plan made
/// past that rests on manifests alone rather than on an executor that may be gone.
/// </summary>
public sealed class TranscodeStatusStore(IOptionsMonitor<TranscodeOptions> options, TimeProvider clock)
{
    private readonly Lock _gate = new();
    private TranscodeStatus? _status;
    private DateTimeOffset _receivedAt;

    public void Put(TranscodeStatus status)
    {
        lock (_gate)
        {
            _status = status;
            _receivedAt = clock.GetUtcNow();
        }
    }

    /// <summary>The report while it is fresh; null when none has come or the last one has gone stale.</summary>
    public TranscodeStatus? Current()
    {
        lock (_gate)
        {
            return _status is not null && clock.GetUtcNow() - _receivedAt <= options.CurrentValue.StatusStale
                ? _status
                : null;
        }
    }
}
