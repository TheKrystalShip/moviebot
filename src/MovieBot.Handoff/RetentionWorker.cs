using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TheKrystalShip.MovieBot.Acquire.Download;
using TheKrystalShip.MovieBot.Core;

namespace TheKrystalShip.MovieBot.Handoff;

/// <summary>
/// Lets go of films nobody has kept, once they have seeded for the window.
///
/// It lives here for the same reason the ingest does: this is the only process allowed to write
/// the media root, and a title's directory is the larger half of what a film costs. The torrent
/// and its files go through the client, so the client never finds itself seeding a file that is
/// no longer there.
///
/// Every decision is taken from what is in front of it on the pass: the torrent's own tags for
/// whether it is kept or still owes a transcode, the client's seeding clock for whether it is
/// due, the API for whether a room holds it, and the manifest on disk for whether the directory
/// under the film's id is this film's. Nothing is remembered between passes, so a stop at any
/// moment loses nothing and a pass that cannot answer one of those questions removes nothing.
/// </summary>
public sealed class RetentionWorker(
    AcquisitionService acquisition,
    Retention retention,
    OccupiedRooms rooms,
    IOptions<HandoffOptions> options,
    ILogger<RetentionWorker> logger) : BackgroundService
{
    /// <summary>
    /// How often the downloads are looked at. A film becomes due once a week and a room is
    /// forgotten half an hour after its last viewer, so nothing here is waiting on the minute.
    /// </summary>
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(5);

    private readonly HandoffOptions _options = options.Value;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation(
            "Pruning films after {Window} of seeding unless kept; never under {Floor}.",
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

    private async Task SweepAsync(CancellationToken ct)
    {
        var downloads = await acquisition.ListAsync(ct);

        // A film still owing its transcode is left alone however long it has seeded: the ingest
        // is what deletes and rewrites its directory, and two processes doing that to one
        // directory is how a film ends up half written. One owed for a week is a fault to read
        // about in the log, not a film to remove.
        var due = downloads
            .Where(d => !d.Tags.Contains(TorrentTags.Keep))
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

            await PruneAsync(download, id, ct);
        }
    }

    /// <summary>
    /// The title directory first, then the torrent. Removing the torrent first would leave a
    /// directory with nothing pointing at it if the second step failed, and nothing would ever
    /// come back for it; this way round a failure leaves the torrent to be found again next pass.
    /// </summary>
    private async Task PruneAsync(DownloadStatus download, string id, CancellationToken ct)
    {
        var directory = Path.Combine(_options.MediaRoot, id);
        var removedTitle = false;

        if (Directory.Exists(directory))
        {
            if (IsThisFilm(directory, download))
            {
                Directory.Delete(directory, recursive: true);
                removedTitle = true;
            }
            else
            {
                logger.LogWarning(
                    "{Id} does not hold the film {Name} arrived as; the directory stays and only "
                    + "the download goes.", id, download.Name);
            }
        }

        await acquisition.RemoveAsync(download.Hash, ct);

        logger.LogInformation(
            "Pruned {Name} after {Hours}h {Minutes}m of seeding{Title}.",
            download.Name, (int)download.Seeded.TotalHours, download.Seeded.Minutes,
            removedTitle ? $" and removed {id} from the library" : "");
    }

    /// <summary>
    /// Whether the directory under the id was made from this download's file.
    ///
    /// The id is a slug of the film's name, and a film fetched twice as two releases lands under
    /// the same id both times, the second ingest replacing the first's directory. The manifest
    /// names the source it was made from, so the older torrent's turn to go cannot take the
    /// newer release's transcode with it.
    /// </summary>
    private bool IsThisFilm(string directory, DownloadStatus download)
    {
        try
        {
            var path = Path.Combine(directory, "manifest.json");
            if (!File.Exists(path)) return false;

            var manifest = ManifestJson.Deserialize(File.ReadAllText(path));
            if (manifest?.Source?.Release is not { Length: > 0 } release) return false;

            var source = FilmFile.Locate(
                download.ContentPath, (long)_options.MinimumFilmSizeMiB * (1 << 20));
            if (source is null) return false;

            return string.Equals(
                release, Path.GetFileNameWithoutExtension(source), StringComparison.Ordinal);
        }
        catch (Exception ex) when (ex is IOException or System.Text.Json.JsonException)
        {
            logger.LogWarning(ex, "Could not read the manifest under {Directory}.", directory);
            return false;
        }
    }
}
