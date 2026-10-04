using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TheKrystalShip.MovieBot.Acquire.Download;
using TheKrystalShip.MovieBot.Core;

namespace TheKrystalShip.MovieBot.Handoff;

/// <summary>
/// Which finished films may be moved on this pass.
/// </summary>
public static class SettleGate
{
    /// <summary>
    /// The finished films no room is playing. When the rooms could not be read nothing is owed a
    /// move this pass: moving later is harmless, and a copy under a viewer is not.
    /// </summary>
    public static IEnumerable<string> Owed(IEnumerable<string> finished, IReadOnlySet<string>? playing) =>
        playing is null ? [] : finished.Where(id => !playing.Contains(id));
}

/// <summary>
/// Moves finished films off the disk they were made on.
///
/// A transcode writes a film in four-second pieces for as long as it runs, and the download it
/// reads from sits beside it seeding for a week; both want the fast disk, and between them they
/// are what fills it. A finished film wants nothing: one playhead reads it back at a little over
/// a megabyte a second. So the hot disk holds the work in progress and the cold disk holds the
/// library, and this is what carries a film from one to the other.
///
/// It holds nothing between passes. What is owed is read off the two roots every time, so
/// stopping this process at any moment loses nothing beyond the copy in flight, and that one is
/// simply done again.
/// </summary>
public sealed class SettleWorker(
    ColdStore cold,
    OccupiedRooms rooms,
    MediaRoots roots,
    TitleLocks locks,
    TranscodeLedger transcodes,
    ILogger<SettleWorker> logger) : BackgroundService
{
    /// <summary>
    /// How often finished films are looked for. A transcode runs for minutes and the copy after
    /// it takes another one or two, so there is nothing here that is waiting on the second.
    /// </summary>
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(1);

    /// <summary>
    /// Whether the last pass reported the volume as gone, so that a disk that stays missing is
    /// one line rather than one a minute, and a disk that comes back says so.
    /// </summary>
    private bool _reportedMissing;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (roots.Cold is null)
        {
            logger.LogInformation(
                "No cold storage configured; films stay on {MediaRoot}.", roots.Hot);
            return;
        }

        logger.LogInformation(
            "Finished films move from {MediaRoot} to {ColdRoot}.", roots.Hot, roots.Cold);

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
                // What is owed is described by the two roots rather than by this loop, so a
                // failed pass costs nothing but the wait until the next one.
                logger.LogWarning(ex, "A settle sweep failed.");
            }
        }
    }

    private async Task SweepAsync(CancellationToken ct)
    {
        var state = roots.ColdState;
        if (!state.Ready)
        {
            if (!_reportedMissing)
            {
                // Said once. Films go on being made and go on being watched from the disk they
                // are made on; what stops is the library growing past that disk.
                logger.LogError(
                    "Cold storage is unusable: {Problem} Films stay on {MediaRoot} until it is "
                    + "back, and nothing is removed from it in the meantime.",
                    state.Problem, roots.Hot);

                _reportedMissing = true;
            }

            return;
        }

        if (_reportedMissing)
        {
            logger.LogInformation("Cold storage is back at {ColdRoot}.", roots.Cold);
            _reportedMissing = false;
        }

        cold.SweepIncoming();

        // A film being made again has its settled copy cleared before the transcode starts, but
        // only if this volume was there at the time. One that was cleared while the volume was
        // gone would otherwise shadow the film being made for the whole of its transcode, and a
        // room that asked for the new release would watch the old one until it finished.
        foreach (var id in BeingMade()) cold.ClearSettled(id);

        // A title a transcode is making is not owed a move, wherever it is being written: settling
        // it would replace the directory the transcode is writing into.
        var finished = Finished().Where(id => !transcodes.IsMaking(id)).ToList();
        if (finished.Count == 0) return;

        // A film somebody is watching stays where it is. Both copies are whole for the moment they
        // overlap, so moving one out from under a room is safe, but the copy saturates the disk
        // the viewer would then be reading from, and waiting costs nothing: the film is finished
        // and does not grow. A pass that cannot reach the API cannot tell an empty room from an
        // unreachable one, and moves nothing.
        var playing = await rooms.PlayingTitlesAsync(ct);

        // One at a time. The copy saturates the disk it writes to, and a second one alongside it
        // would only divide the same throughput while the transcode this is making room for is
        // reading the disk being drained.
        foreach (var id in SettleGate.Owed(finished, playing))
        {
            if (ct.IsCancellationRequested) return;

            // Checked per film rather than per pass: a transcode can begin writing to the cold
            // disk between one copy and the next, and nothing is copied onto a disk while one
            // is being written to.
            if (transcodes.IsWritingUnder(roots.Cold!))
            {
                if (!_reportedWriting)
                {
                    logger.LogInformation(
                        "A transcode is writing to {ColdRoot}; finished films settle once it is done.",
                        roots.Cold);

                    _reportedWriting = true;
                }

                return;
            }

            _reportedWriting = false;

            // Held for the whole copy, so a manifest rewritten meanwhile is either in the copy or
            // written after it, to the root the film is on by then.
            using (await locks.AcquireAsync(id, ct))
                await cold.SettleAsync(id, ct);
        }
    }

    /// <summary>
    /// Whether the last pass held films back for a transcode writing to the cold disk, so that one
    /// that runs for an hour is one line rather than one a minute.
    /// </summary>
    private bool _reportedWriting;

    /// <summary>
    /// The films on the hot root that are done being made.
    ///
    /// Only the hot root is read: a title is in exactly one root, so one that is there at all is
    /// one that has not settled. A finished manifest is the mark, and it is the right one — the
    /// subtitles, the cover art and the preview sheet are all written before the status turns,
    /// so a film that reports itself ready is a directory nothing is going to add to.
    /// </summary>
    private IEnumerable<string> Finished()
    {
        if (!Directory.Exists(roots.Hot)) yield break;

        foreach (var directory in Directory.EnumerateDirectories(roots.Hot))
        {
            var id = Path.GetFileName(directory);
            if (!MediaRoots.IsTitleDirectory(id)) continue;

            Manifest? manifest = null;
            try
            {
                var path = Path.Combine(directory, "manifest.json");
                if (File.Exists(path)) manifest = ManifestJson.Deserialize(File.ReadAllText(path));
            }
            catch (Exception ex) when (ex is IOException or System.Text.Json.JsonException)
            {
                // A manifest is written through a temp file and moved, so a torn read is not
                // expected; one here only means waiting a minute.
                logger.LogDebug(ex, "Could not read the manifest under {Directory}.", directory);
            }

            if (manifest?.Status == TitleStatus.Ready) yield return id;
        }
    }

    /// <summary>
    /// The titles on the hot root that are not done being made, and that the cold root also holds.
    /// Two copies of one id with the hot one unfinished means the settled copy is a film the
    /// library has replaced.
    /// </summary>
    private IEnumerable<string> BeingMade()
    {
        if (!Directory.Exists(roots.Hot)) yield break;

        foreach (var directory in Directory.EnumerateDirectories(roots.Hot))
        {
            var id = Path.GetFileName(directory);
            if (!MediaRoots.IsTitleDirectory(id)) continue;
            if (roots.SettledDirectoryOf(id) is not { } settled) continue;
            if (!Directory.Exists(settled)) continue;

            Manifest? manifest = null;
            try
            {
                var path = Path.Combine(directory, "manifest.json");
                if (File.Exists(path)) manifest = ManifestJson.Deserialize(File.ReadAllText(path));
            }
            catch (Exception ex) when (ex is IOException or System.Text.Json.JsonException)
            {
                logger.LogDebug(ex, "Could not read the manifest under {Directory}.", directory);
            }

            // A manifest that says the film is being made is the evidence, and nothing else is:
            // an unreadable one, or a directory that carries none, is not a reason to delete the
            // only finished copy of a film. A film mid-settle is in both roots with the hot one
            // finished, which is the overlap this must not act on either.
            if (manifest is not null && manifest.Status != TitleStatus.Ready) yield return id;
        }
    }
}
