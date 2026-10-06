using Microsoft.Extensions.Logging;
using TheKrystalShip.MovieBot.Acquire.Download;
using TheKrystalShip.MovieBot.Core;
using TheKrystalShip.MovieBot.Ingest.Ffmpeg;
using TheKrystalShip.MovieBot.Ingest.Pipeline;

namespace TheKrystalShip.MovieBot.Handoff;

/// <summary>
/// Encodes the library's films again at the library's quality, and adds the rungs a film's ladder
/// has and the film lacks (<see cref="Reencoder"/>), one film at a time, while nobody is watching
/// anything.
///
/// It runs as its own process beside the hand-off service and shares no lock with it, so it takes
/// only films that service leaves alone: settled ones, under the cold root and not the media root.
/// It writes beside a title, in the cold root's dotted <c>.reencode</c> directory, and changes the
/// title only by renames on that one disk, so a stop at any point leaves a title playable.
/// </summary>
public sealed class LibraryReencode(
    MediaRoots roots, OccupiedRooms rooms, TranscodeLedger transcodes, ILogger<LibraryReencode> logger)
{
    public const string WorkFolder = ".reencode";

    private const string OldPrefix = "old-";
    private static readonly TimeSpan IdlePoll = TimeSpan.FromMinutes(1);

    /// <param name="only">The titles to go over; empty for every one owed a pass.</param>
    /// <param name="waitForIdle">
    /// Whether each title waits for every room to stop playing and every transcode to end. A title
    /// is never swapped while a room plays it either way.
    /// </param>
    public async Task<int> RunAsync(IReadOnlyCollection<string> only, bool waitForIdle, CancellationToken ct)
    {
        if (roots.Cold is not { } cold || !roots.ColdState.Ready)
        {
            logger.LogError("Cold storage is unusable: {Problem} Only settled films are encoded again.",
                roots.ColdState.Problem ?? "no cold root is configured.");
            return 1;
        }

        var work = Path.Combine(cold, WorkFolder);
        var owed = roots.Ids()
            .Where(id => only.Count == 0 || only.Contains(id))
            .Where(id => IsSettled(cold, id))
            .Where(id => ReadManifest(Path.Combine(cold, id)) is { } manifest && Reencoder.IsOwed(manifest))
            .Order(StringComparer.Ordinal)
            .ToList();

        logger.LogInformation("{Count} films owed a pass at quality {Cq}.", owed.Count, Reencoder.LibraryCq);

        var failed = 0;
        long before = 0, after = 0;

        foreach (var id in owed)
        {
            ct.ThrowIfCancellationRequested();
            if (waitForIdle) await WaitUntilIdleAsync(ct);

            var outcome = await ReencodeAsync(cold, work, id, ct);
            if (outcome is null) { failed++; continue; }

            before += outcome.Value.Before;
            after += outcome.Value.After;
        }

        logger.LogInformation(
            "Done. Video went from {Before:F1} GiB to {After:F1} GiB; {Failed} films failed.",
            before / Gib, after / Gib, failed);

        if (Directory.Exists(work) && !Directory.EnumerateFileSystemEntries(work).Any())
            Directory.Delete(work);

        return failed > 0 ? 1 : 0;
    }

    private const double Gib = 1024d * 1024 * 1024;

    /// <summary>One title; null when it failed, zero sizes when it was left as it is.</summary>
    private async Task<(long Before, long After)?> ReencodeAsync(
        string cold, string work, string id, CancellationToken ct)
    {
        var titleDirectory = Path.Combine(cold, id);
        var titleWork = Path.Combine(work, id);

        Recover(titleDirectory, titleWork);

        if (ReadManifest(titleDirectory) is not { } manifest || !Reencoder.IsOwed(manifest))
            return (0, 0);

        var before = RungBytes(titleDirectory, manifest);
        var replaces = Reencoder.Replaces(manifest);
        if (replaces)
            logger.LogInformation("Encoding {Id} again ({Size:F1} GiB of video).", id, before / Gib);
        else
            logger.LogInformation("Adding {Rungs} to {Id}.",
                string.Join(", ", Reencoder.Owed(manifest).Select(r => r.Directory)), id);

        IReadOnlyList<Rendition> renditions;
        try
        {
            renditions = await new Reencoder(line => logger.LogInformation("{Line}", line))
                .EncodeAsync(titleDirectory, manifest, titleWork, ct);
        }
        catch (Exception ex) when (ex is ReencodeException or FfmpegException or IOException
                                       or InvalidOperationException)
        {
            logger.LogError(ex, "Could not encode {Id} again. It stays as it is.", id);
            DeleteQuietly(titleWork);
            return null;
        }

        if (!Directory.Exists(titleDirectory))
        {
            logger.LogWarning("{Id} is gone; nothing is moved in.", id);
            DeleteQuietly(titleWork);
            return (0, 0);
        }

        // Read again: the manifest may have been rewritten while the rungs were made.
        if (ReadManifest(titleDirectory) is not { } current)
        {
            DeleteQuietly(titleWork);
            return null;
        }

        if (!replaces)
        {
            AddRungs(titleDirectory, titleWork, current, renditions);
            DeleteQuietly(titleWork);

            var added = RungBytes(titleDirectory, current) - before;
            logger.LogInformation("{Id}: added {Size:F2} GiB of video.", id, added / Gib);
            return (before, before + added);
        }

        // Swapped only when nobody is playing it: a player holding the old initialisation segment
        // cannot decode the new rungs. A pass that cannot ask swaps nothing.
        var playing = await rooms.PlayingTitlesAsync(ct);
        if (playing is null || playing.Contains(id))
        {
            logger.LogWarning("{Id} is being played; it stays as it is.", id);
            DeleteQuietly(titleWork);
            return (0, 0);
        }

        var directories = current.Video.Renditions.Select(DirectoryOf)
            .Concat(renditions.Select(DirectoryOf))
            .Distinct(StringComparer.Ordinal)
            .ToList();

        foreach (var directory in directories)
        {
            var live = Path.Combine(titleDirectory, directory);
            if (Directory.Exists(live)) Directory.Move(live, Path.Combine(titleWork, OldPrefix + directory));
        }

        foreach (var rendition in renditions)
        {
            var directory = DirectoryOf(rendition);
            Directory.Move(Path.Combine(titleWork, directory), Path.Combine(titleDirectory, directory));
        }

        // The master first, so the manifest never lists a rung the master cannot play.
        current.Video = current.Video with { Renditions = renditions };
        MasterPlaylist.Write(titleDirectory, current);
        ManifestJson.WriteAtomic(Path.Combine(titleDirectory, "manifest.json"), current);

        DeleteQuietly(titleWork);

        var after = RungBytes(titleDirectory, current);
        logger.LogInformation("{Id}: {Before:F1} GiB to {After:F1} GiB of video.", id, before / Gib, after / Gib);

        return (before, after);
    }

    /// <summary>
    /// Moves the rungs a title lacks in beside the ones it has, then lists them, the master first so
    /// the manifest never lists a rung the master cannot play. The rungs already there are not
    /// touched, so a player on them carries on, and one holding the master it loaded sees the new
    /// rung when it next loads the film. A directory of the same name that the manifest does not
    /// list is what a pass that stopped before listing it left behind, and is replaced.
    /// </summary>
    private static void AddRungs(
        string titleDirectory, string titleWork, Manifest current, IReadOnlyList<Rendition> renditions)
    {
        var listed = current.Video.Renditions.Select(DirectoryOf).ToHashSet(StringComparer.Ordinal);
        var adding = renditions.Where(r => !listed.Contains(DirectoryOf(r))).ToList();
        if (adding.Count == 0) return;

        foreach (var rendition in adding)
        {
            var live = Path.Combine(titleDirectory, DirectoryOf(rendition));
            if (Directory.Exists(live)) Directory.Delete(live, recursive: true);
            Directory.Move(Path.Combine(titleWork, DirectoryOf(rendition)), live);
        }

        var order = Ladder.For(current.Video.Width, current.Video.Height).Select(r => r.Directory).ToList();
        current.Video = current.Video with
        {
            Renditions = current.Video.Renditions.Concat(adding)
                .OrderBy(r => order.IndexOf(DirectoryOf(r)) is var i and >= 0 ? i : order.Count)
                .ToList()
        };
        MasterPlaylist.Write(titleDirectory, current);
        ManifestJson.WriteAtomic(Path.Combine(titleDirectory, "manifest.json"), current);
    }

    /// <summary>
    /// Puts a title back together after a pass that stopped between moving its rungs aside and
    /// moving the new ones in: a rung the title is missing is the old one, moved back. Then the
    /// work is cleared.
    /// </summary>
    private void Recover(string titleDirectory, string titleWork)
    {
        if (!Directory.Exists(titleWork)) return;

        foreach (var old in Directory.EnumerateDirectories(titleWork, OldPrefix + "*"))
        {
            var live = Path.Combine(titleDirectory, Path.GetFileName(old)[OldPrefix.Length..]);
            if (Directory.Exists(titleDirectory) && !Directory.Exists(live))
            {
                Directory.Move(old, live);
                logger.LogWarning("Put {Rung} back after a pass that stopped partway.", live);
            }
        }

        DeleteQuietly(titleWork);
    }

    /// <summary>
    /// Waits until no room is playing anything and no transcode is running. A viewer's playback
    /// reads the same disk and a requested film's transcode wants the card; neither waits on this.
    /// </summary>
    private async Task WaitUntilIdleAsync(CancellationToken ct)
    {
        var said = false;

        while (true)
        {
            var playing = await rooms.PlayingTitlesAsync(ct);
            var running = transcodes.List().Count;
            if (playing is { Count: 0 } && running == 0) return;

            if (!said)
            {
                logger.LogInformation(
                    "Waiting: {Playing} titles playing, {Running} transcodes running.",
                    playing?.Count.ToString() ?? "unknown", running);
                said = true;
            }

            await Task.Delay(IdlePoll, ct);
        }
    }

    private bool IsSettled(string cold, string id) =>
        Directory.Exists(Path.Combine(cold, id)) && !Directory.Exists(roots.WorkingDirectoryOf(id));

    private static string DirectoryOf(Rendition rendition) => Reencoder.DirectoryOf(rendition);

    private static long RungBytes(string titleDirectory, Manifest manifest) =>
        manifest.Video.Renditions
            .Select(r => Path.Combine(titleDirectory, DirectoryOf(r)))
            .Where(Directory.Exists)
            .SelectMany(d => Directory.EnumerateFiles(d))
            .Sum(f => new FileInfo(f).Length);

    private static Manifest? ReadManifest(string titleDirectory)
    {
        try
        {
            var path = Path.Combine(titleDirectory, "manifest.json");
            return File.Exists(path) ? ManifestJson.Deserialize(File.ReadAllText(path)) : null;
        }
        catch (Exception ex) when (ex is IOException or System.Text.Json.JsonException)
        {
            return null;
        }
    }

    private void DeleteQuietly(string directory)
    {
        try
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
        catch (IOException ex)
        {
            logger.LogWarning(ex, "Could not clear {Directory}.", directory);
        }
    }
}
