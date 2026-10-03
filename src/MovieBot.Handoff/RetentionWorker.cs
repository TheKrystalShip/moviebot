using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TheKrystalShip.MovieBot.Acquire.Download;
using TheKrystalShip.MovieBot.Core;

namespace TheKrystalShip.MovieBot.Handoff;

/// <summary>
/// The tracker's clock: lets go of the source once it has seeded for the window.
///
/// What goes is the torrent and the files it downloaded, through the client, so the client never
/// finds itself seeding a file that is no longer there. The film's own directory, the transcode, is
/// not touched: how long a film stays in the library is the library's clock
/// (<see cref="LibraryWorker"/>), which runs on plays rather than on seeding.
///
/// Every decision is taken from what is in front of it on the pass: the torrent's own tags for
/// whether it still owes a transcode, the client's seeding clock for whether it is due, and the API
/// for whether a room holds it. Nothing is remembered between passes, so a stop at any moment loses
/// nothing and a pass that cannot answer one of those questions removes nothing.
/// </summary>
public sealed class RetentionWorker(
    ISeedingDownloads downloads,
    Retention retention,
    OccupiedRooms rooms,
    MediaRoots roots,
    ILogger<RetentionWorker> logger) : BackgroundService
{
    /// <summary>
    /// How often the downloads are looked at. A film becomes due once a week and a room is
    /// forgotten half an hour after its last viewer, so nothing here is waiting on the minute.
    /// </summary>
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(5);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation(
            "Removing sources after {Window} of seeding; never under {Floor}.",
            retention.Window, retention.Floor);

        using var timer = new PeriodicTimer(Interval);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (!await timer.WaitForNextTickAsync(stoppingToken)) return;
                await SweepAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "A retention sweep failed.");
            }
        }
    }

    public async Task SweepAsync(CancellationToken ct)
    {
        // A pass that cannot see the cold volume cannot see the library either, and it acts on
        // nothing until it can: the same rule that stops it removing anything it cannot ask about.
        if (roots.Cold is not null && !roots.ColdState.Ready)
        {
            logger.LogWarning(
                "Cold storage is unusable: {Problem} Nothing is removed until it is back.",
                roots.ColdState.Problem);

            return;
        }

        var all = await downloads.ListAsync(ct);

        // A film still owing its transcode is left alone however long it has seeded: the transcode
        // is reading the source, and one owed for a week is a fault to read about in the log, not
        // a source to remove.
        var due = all
            .Where(d => !d.Tags.Contains(TorrentTags.NeedsIngest))
            .Where(retention.IsDue)
            .ToList();

        if (due.Count == 0) return;

        var held = await rooms.HeldTitlesAsync(ct);
        if (held is null) return;

        foreach (var download in due)
        {
            if (ct.IsCancellationRequested) return;

            var id = download.Tags.Select(TorrentTags.ReadLibrary).FirstOrDefault(i => i is not null)
                     ?? LibraryId.For(download.Name);

            if (held.Contains(id))
            {
                logger.LogInformation(
                    "{Name} is due but a room holds {Id}; it stays until the room lets go.",
                    download.Name, id);
                continue;
            }

            await downloads.RemoveAsync(download.Hash, ct);

            logger.LogInformation(
                "Removed the source of {Name} after {Hours}h {Minutes}m of seeding.",
                download.Name, (int)download.Seeded.TotalHours, download.Seeded.Minutes);
        }
    }
}
