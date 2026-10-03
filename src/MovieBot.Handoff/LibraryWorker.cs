using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TheKrystalShip.MovieBot.Core;

namespace TheKrystalShip.MovieBot.Handoff;

/// <summary>
/// The library's clock: removes the settled films the API says have gone their time without a play
/// and nobody keeps.
///
/// It lives here because this is the only process allowed to write the media roots. What is due is
/// the API's to say (<see cref="DueTitles"/>), since the plays and the keeps are its records; this
/// decides nothing of its own beyond refusing anything that is not a settled directory, and acts only
/// on the cold root, where a film that has settled is. A pass that cannot ask the API, or cannot ask
/// which rooms hold a film, deletes nothing.
/// </summary>
public sealed class LibraryWorker(
    DueTitles due,
    OccupiedRooms rooms,
    MediaRoots roots,
    ILogger<LibraryWorker> logger) : BackgroundService
{
    /// <summary>How often the library's clock is read. A film comes due once in days, so nothing here waits on the minute.</summary>
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(5);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
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
                logger.LogWarning(ex, "A library sweep failed.");
            }
        }
    }

    public async Task SweepAsync(CancellationToken ct)
    {
        // Without a cold root nothing is settled, and with its volume gone a film there cannot be
        // told from one already deleted.
        if (roots.Cold is not { } cold) return;

        if (!roots.ColdState.Ready)
        {
            logger.LogWarning(
                "Cold storage is unusable: {Problem} Nothing is removed from the library until it is back.",
                roots.ColdState.Problem);

            return;
        }

        var ids = await due.AskAsync(ct);
        if (ids is null or { Count: 0 }) return;

        var held = await rooms.HeldTitlesAsync(ct);
        if (held is null) return;

        foreach (var id in ids)
        {
            if (ct.IsCancellationRequested) return;

            // The ids come over the wire. One that could reach outside the cold root is not a film
            // and is not followed.
            if (!MediaRoots.IsSafeId(id))
            {
                logger.LogWarning("The library's clock named {Id}, which is not a film id; skipped.", id);
                continue;
            }

            if (held.Contains(id))
            {
                logger.LogInformation("{Id} is due but a room holds it; it stays until the room lets go.", id);
                continue;
            }

            var directory = Path.Combine(cold, id);
            if (!Directory.Exists(directory)) continue;

            // A film also under the media root is being made again, or is mid-move. Either way it is
            // not one that has settled, and the cold copy is what its readers are on.
            if (Directory.Exists(roots.WorkingDirectoryOf(id)))
            {
                logger.LogInformation("{Id} is also on the media root, so it is not removed.", id);
                continue;
            }

            // A link is somebody pointing the library elsewhere, and what it points at is not this
            // process's to delete.
            if (new DirectoryInfo(directory).LinkTarget is not null)
            {
                logger.LogWarning("{Directory} is a link, so it is not removed.", directory);
                continue;
            }

            try
            {
                Directory.Delete(directory, recursive: true);
                logger.LogInformation("Removed {Id} from the library: its time without a play is up.", id);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                logger.LogWarning(ex, "Could not remove {Directory}; the next pass tries again.", directory);
            }
        }
    }
}
