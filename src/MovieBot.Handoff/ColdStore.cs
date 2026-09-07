using System.Diagnostics;
using Microsoft.Extensions.Logging;
using TheKrystalShip.MovieBot.Core;

namespace TheKrystalShip.MovieBot.Handoff;

/// <summary>
/// Moves a finished film from the disk it was made on to the disk it is kept on.
///
/// The two roots are different filesystems, so this is a copy and a delete rather than a rename,
/// and nothing about it is atomic on its own. The order is what makes it safe: the film is
/// assembled under a dotted directory on the cold root, which is skipped by everything that
/// lists titles; it is checked against what was read; the filesystem is made to commit it; and
/// only then is it renamed into place — a rename within one filesystem, which either happened or
/// did not. The copy on the hot root goes last, so every failure before that point leaves the
/// film exactly where it was.
///
/// The cold root's own mount point is deliberately not writable when the volume is absent, so a
/// settle attempted against a missing disk fails rather than filling the disk it was draining.
/// The marker file is checked first anyway: a two-minute copy that was never going to land is
/// worth refusing before it starts rather than in the middle.
/// </summary>
public sealed class ColdStore(MediaRoots roots, ILogger<ColdStore> logger)
{
    /// <summary>
    /// Takes a title off the cold root, so that the hot root holds the only copy while the film
    /// is being made.
    ///
    /// A film re-ingested under an id the library already holds replaces what was there, and it
    /// has to be replaced wherever it is: the ingest writes to the hot root and clears only what
    /// it finds there, so a settled copy would outlive it and, being resolved first, be the one
    /// every room opened.
    /// </summary>
    public void ClearSettled(string id)
    {
        if (!roots.ColdReady || roots.SettledDirectoryOf(id) is not { } settled) return;
        if (!Directory.Exists(settled)) return;

        Directory.Delete(settled, recursive: true);
        logger.LogInformation("{Id} is being made again; the settled copy is gone.", id);
    }

    /// <summary>
    /// Clears what a settle interrupted partway through. A film half copied is worth nothing and
    /// the film it was copied from is still where it was, so there is nothing to recover.
    /// </summary>
    public void SweepIncoming()
    {
        if (!roots.ColdReady || roots.Incoming is not { } incoming) return;
        if (!Directory.Exists(incoming)) return;

        foreach (var abandoned in Directory.EnumerateDirectories(incoming))
        {
            Directory.Delete(abandoned, recursive: true);
            logger.LogInformation(
                "Cleared {Path}, left by a settle that did not finish.", abandoned);
        }
    }

    /// <summary>
    /// Moves one title. Returns whether it landed.
    /// </summary>
    public async Task<bool> SettleAsync(string id, CancellationToken ct)
    {
        if (!roots.ColdReady) return false;
        if (roots.Incoming is not { } incomingRoot) return false;
        if (roots.SettledDirectoryOf(id) is not { } settled) return false;

        var source = roots.WorkingDirectoryOf(id);
        var staging = Path.Combine(incomingRoot, id);

        if (!Directory.Exists(source)) return false;

        var size = Measure(source);
        var free = new DriveInfo(roots.Cold!).AvailableFreeSpace;

        // A margin over the film itself, because the volume filling exactly to its last block is
        // how a filesystem stops being able to record that it is full.
        if (free < size.Bytes + (1L << 30))
        {
            logger.LogError(
                "{Id} needs {Needed:0.#} GiB and cold storage has {Free:0.#} GiB. It stays where "
                + "it is; let some films go to make room.",
                id, size.Bytes / (double)(1L << 30), free / (double)(1L << 30));
            return false;
        }

        if (Directory.Exists(staging)) Directory.Delete(staging, recursive: true);

        var started = Stopwatch.StartNew();

        try
        {
            var written = await CopyAsync(source, staging, ct);

            if (written != size)
            {
                logger.LogError(
                    "{Id} did not copy whole: read {ReadFiles} files of {ReadBytes} bytes and "
                    + "wrote {WrittenFiles} of {WrittenBytes}. It stays where it is.",
                    id, size.Files, size.Bytes, written.Files, written.Bytes);

                Directory.Delete(staging, recursive: true);
                return false;
            }

            // One barrier for the whole film rather than one per file. A title is thousands of
            // segments and this disk pays a seek for every commit, so flushing each of them
            // would cost more than the copy; what has to be true before the film on the hot root
            // is deleted is that this one is on the platter, and that is one question.
            if (!await CommitAsync(staging, ct))
            {
                logger.LogError(
                    "{Id} could not be committed to disk. It stays where it is.", id);
                return false;
            }

            // Both sides of this are on the cold volume, so it is a rename: after it the film is
            // in the library from the cold root, and every reader resolves there first.
            if (Directory.Exists(settled)) Directory.Delete(settled, recursive: true);
            Directory.Move(staging, settled);

            // The rename is committed too, before the only copy left is the one it named. The
            // window between a rename that is not on the platter and a delete that is, is the one
            // place in this where losing power loses the film. It is metadata alone, so it costs
            // a fraction of what committing the film did.
            if (!await CommitAsync(settled, ct))
            {
                logger.LogError(
                    "{Id} landed on cold storage but the move could not be committed. Both copies "
                    + "stay until a later pass.", id);

                return false;
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Left for the next start to clear. The film is still on the hot root and the library
            // still holds it, so a stop in the middle of this costs nothing but the copy.
            logger.LogInformation("Stopped partway through settling {Id}; it stays where it is.", id);
            throw;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogError(ex, "Could not settle {Id}. It stays where it is.", id);
            TryClear(staging);
            return false;
        }

        // Last, and only once the film is readable from where it is kept.
        try
        {
            Directory.Delete(source, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // The film is settled and playing from cold storage; what is left is the copy this
            // was freeing space by removing. Reported rather than thrown, because a throw here
            // would have the next pass copy the whole film again to reach the same line.
            logger.LogError(ex,
                "{Id} is settled but its copy under {MediaRoot} could not be removed.",
                id, roots.Hot);

            return false;
        }

        logger.LogInformation(
            "Settled {Id} onto cold storage: {Size:0.#} GiB in {Seconds:0} s ({Rate:0} MB/s).",
            id, size.Bytes / (double)(1L << 30), started.Elapsed.TotalSeconds,
            size.Bytes / (1024.0 * 1024) / Math.Max(0.001, started.Elapsed.TotalSeconds));

        return true;
    }

    /// <summary>What a directory holds, as the pair that says a copy of it is whole.</summary>
    private readonly record struct Extent(int Files, long Bytes);

    private static Extent Measure(string directory)
    {
        var files = 0;
        var bytes = 0L;

        foreach (var path in Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories))
        {
            files++;
            bytes += new FileInfo(path).Length;
        }

        return new Extent(files, bytes);
    }

    /// <summary>
    /// Copies a title, and reports what actually landed rather than what was asked for.
    ///
    /// Timestamps are carried across. A poster and a sheet of scrub previews keep their names and
    /// are served asking to be revalidated, so a film whose artwork was stamped with the moment
    /// it moved would have every viewer fetch all of it again for nothing.
    /// </summary>
    private static async Task<Extent> CopyAsync(string source, string destination, CancellationToken ct)
    {
        Directory.CreateDirectory(destination);

        var files = 0;
        var bytes = 0L;

        foreach (var directory in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories))
            Directory.CreateDirectory(Path.Combine(destination, Path.GetRelativePath(source, directory)));

        foreach (var path in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            ct.ThrowIfCancellationRequested();

            var landed = Path.Combine(destination, Path.GetRelativePath(source, path));

            await using (var reading = new FileStream(
                path, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 20, useAsync: true))
            await using (var writing = new FileStream(
                landed, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 20, useAsync: true))
            {
                await reading.CopyToAsync(writing, 1 << 20, ct);
            }

            File.SetLastWriteTimeUtc(landed, File.GetLastWriteTimeUtc(path));

            files++;
            bytes += new FileInfo(landed).Length;
        }

        return new Extent(files, bytes);
    }

    /// <summary>
    /// Makes the filesystem commit everything written under a path, so that what is renamed into
    /// place survives the machine losing power between here and the hot copy being deleted.
    /// </summary>
    private async Task<bool> CommitAsync(string path, CancellationToken ct)
    {
        try
        {
            using var sync = Process.Start(new ProcessStartInfo("sync", ["-f", path])
            {
                RedirectStandardError = true,
                RedirectStandardOutput = true
            });

            if (sync is null) return false;

            await sync.WaitForExitAsync(ct);
            return sync.ExitCode == 0;
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            logger.LogError(ex, "Could not run sync to commit {Path}.", path);
            return false;
        }
    }

    private void TryClear(string path)
    {
        try
        {
            if (Directory.Exists(path)) Directory.Delete(path, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(ex, "Could not clear {Path}; the next pass will.", path);
        }
    }
}
