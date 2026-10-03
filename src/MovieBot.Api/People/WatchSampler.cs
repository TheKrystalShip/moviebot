using TheKrystalShip.MovieBot.Api.Library;
using TheKrystalShip.MovieBot.Api.Sessions;

namespace TheKrystalShip.MovieBot.Api.People;

/// <summary>
/// Looks at the rooms every half minute and writes down what the people in them are watching.
///
/// Only a room that is playing counts. A paused room left open overnight is nobody watching, and
/// counting it would make a film look played this morning, which is what the library's clock reads
/// to decide whether to keep it. Sampling rather than listening for plays keeps it independent of
/// which door a change came through, and a room's position is derived from its anchor at the
/// instant asked, so a sample is exact however long ago the last control was touched.
///
/// Every person in a playing room is credited with where it is, including somebody who joined
/// halfway: they are watching it from there.
/// </summary>
public sealed class WatchSampler(
    SessionStore sessions,
    TitleLibrary library,
    WatchRecord record,
    PersonDirectory people,
    TimeProvider clock,
    ILogger<WatchSampler> logger) : BackgroundService
{
    public static readonly TimeSpan Interval = TimeSpan.FromSeconds(30);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval, clock);

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                Sample();
            }
            catch (Exception ex)
            {
                // A pass that throws must not take the API down with it; the next one tries again.
                logger.LogError(ex, "Sampling the rooms failed");
            }
        }
    }

    /// <summary>One pass over the rooms that are playing a film.</summary>
    public void Sample()
    {
        var now = clock.GetUtcNow();

        foreach (var room in sessions.Playing())
        {
            if (library.Get(room.TitleId) is not { } manifest) continue;

            foreach (var viewer in room.Viewers)
            {
                people.Remember(viewer.UserId, viewer.DisplayName);
                record.Record(viewer.UserId, room.TitleId, room.PositionSeconds, manifest.DurationSeconds, now);
            }
        }
    }
}
