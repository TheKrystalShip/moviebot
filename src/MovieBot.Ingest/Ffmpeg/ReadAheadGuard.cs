using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using TheKrystalShip.MovieBot.Ingest.Pipeline;

namespace TheKrystalShip.MovieBot.Ingest.Ffmpeg;

/// <summary>
/// Keeps ffmpeg's read position behind the part of the source that has actually arrived.
///
/// Transcoding a file that is still downloading is only safe while the reader stays behind the
/// writer, and nothing enforces that on its own. The file is its full length from the moment it
/// is created, so reading past what has arrived returns zeros — not an error and not the end of
/// the file. ffmpeg would encode the zeros.
///
/// So the reader is watched rather than trusted: its position comes from the kernel, the arrived
/// length from whoever is fetching the file, and when the gap between them closes the process is
/// stopped until it opens again. Stopping is the safe failure — a paused transcode falls behind,
/// where an unpaused one silently produces a film made partly of nothing.
/// </summary>
public sealed class ReadAheadGuard(
    ISourceAvailability availability,
    string sourcePath,
    long marginBytes,
    Action<string>? log = null)
{
    private const int SIGSTOP = 19;
    private const int SIGCONT = 18;

    // Two integers and no marshalling, so the older attribute is enough and the project stays
    // free of unsafe code for the sake of one signal.
    [DllImport("libc", EntryPoint = "kill", SetLastError = true)]
    private static extern int kill(int pid, int sig);

    /// <summary>How often the reader and the writer are compared.</summary>
    private static readonly TimeSpan Interval = TimeSpan.FromMilliseconds(500);

    private bool _paused;
    private int _pauses;

    /// <summary>How many times the transcode had to wait for the download to get ahead.</summary>
    public int Pauses => _pauses;

    /// <summary>
    /// How long the descriptor is given to appear before guarding is declared impossible. ffmpeg
    /// opens its input almost immediately; this is generous rather than tight.
    /// </summary>
    private static readonly TimeSpan OpenTimeout = TimeSpan.FromSeconds(15);

    /// <summary>
    /// Watches until the source is complete or the token is cancelled. Always resumes the process
    /// on the way out: leaving a stopped process behind would hang the transcode forever.
    /// </summary>
    /// <exception cref="FfmpegException">
    /// The reader never opened the source, so its position cannot be known and it cannot be held
    /// back. Refusing is the only safe answer: running on would transcode whatever the unwritten
    /// part of the file happens to contain, and produce a film with no sign anything was wrong.
    /// </exception>
    public async Task WatchAsync(Process process, CancellationToken ct)
    {
        try
        {
            if (await availability.IsCompleteAsync(ct)) return;

            if (!await WaitForOpenAsync(process, ct))
            {
                if (process.HasExited || ct.IsCancellationRequested) return;

                throw new FfmpegException(
                    $"Could not find an open descriptor for {sourcePath} in the transcoder, so a "
                    + "source that is still downloading cannot be read safely.");
            }

            while (!ct.IsCancellationRequested && !process.HasExited)
            {
                if (await availability.IsCompleteAsync(ct))
                {
                    Resume(process);
                    return;
                }

                var readable = await availability.ReadableBytesAsync(ct);
                var position = ReadPosition(process.Id);

                // A position that cannot be read pauses rather than continues. It was readable
                // once, so this is a transient — and the safe answer to not knowing where the
                // reader is, is to stop it.
                if (position < 0 || position + marginBytes >= readable) Pause(process);
                else Resume(process);

                await Task.Delay(Interval, ct);
            }
        }
        catch (OperationCanceledException)
        {
            // Falls through to the resume below.
        }
        finally
        {
            Resume(process);
        }
    }

    /// <summary>
    /// Waits for the reader to open the source. Reports whether it did rather than throwing, so
    /// a process that simply finished first is not treated as a failure.
    /// </summary>
    private async Task<bool> WaitForOpenAsync(Process process, CancellationToken ct)
    {
        var deadline = DateTimeOffset.UtcNow + OpenTimeout;

        while (DateTimeOffset.UtcNow < deadline)
        {
            if (ct.IsCancellationRequested || process.HasExited) return false;
            if (ReadPosition(process.Id) >= 0) return true;

            await Task.Delay(TimeSpan.FromMilliseconds(100), ct);
        }

        return false;
    }

    private void Pause(Process process)
    {
        if (_paused || process.HasExited) return;

        if (kill(process.Id, SIGSTOP) == 0)
        {
            _paused = true;
            _pauses++;
            log?.Invoke("  waiting for the download to get ahead");
        }
    }

    private void Resume(Process process)
    {
        if (!_paused) return;

        try
        {
            if (!process.HasExited) kill(process.Id, SIGCONT);
        }
        catch (InvalidOperationException)
        {
            // The process is gone, which is the same as resumed for every purpose here.
        }

        _paused = false;
    }

    /// <summary>
    /// How far into the source the process has read, from the kernel's own record of the open
    /// descriptor. Negative when it cannot be determined.
    ///
    /// The descriptor is found by resolving each of the process's open files rather than assuming
    /// an index: ffmpeg opens the source somewhere among its own pipes and outputs, and which
    /// number it lands on is not ours to predict. The transcode opens the source once per
    /// rendition, so every descriptor on it is read and the furthest one is the position: that is
    /// the reader closest to running past what has arrived.
    /// </summary>
    private long ReadPosition(int pid)
    {
        var furthest = -1L;

        try
        {
            var full = Path.GetFullPath(sourcePath);

            foreach (var descriptor in Directory.EnumerateFiles($"/proc/{pid}/fd"))
            {
                string target;
                try
                {
                    target = File.ResolveLinkTarget(descriptor, returnFinalTarget: true)?.FullName ?? "";
                }
                catch (IOException) { continue; }

                if (!string.Equals(target, full, StringComparison.Ordinal)) continue;

                var info = $"/proc/{pid}/fdinfo/{Path.GetFileName(descriptor)}";
                foreach (var line in File.ReadLines(info))
                {
                    if (!line.StartsWith("pos:", StringComparison.Ordinal)) continue;

                    if (long.TryParse(line[4..].Trim(), NumberStyles.Integer,
                            CultureInfo.InvariantCulture, out var position))
                        furthest = Math.Max(furthest, position);
                    break;
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
                                   or DirectoryNotFoundException)
        {
            // The process ended between listing and reading, which the caller handles.
            return -1;
        }

        return furthest;
    }
}
