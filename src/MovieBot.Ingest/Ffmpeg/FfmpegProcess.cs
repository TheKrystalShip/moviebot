using System.Diagnostics;
using System.Globalization;

namespace TheKrystalShip.MovieBot.Ingest.Ffmpeg;

/// <summary>How far ffmpeg has read into the source, reported through <c>-progress</c>.</summary>
public readonly record struct FfmpegProgress(TimeSpan OutTime, double Speed);

public sealed class FfmpegException(string message) : Exception(message);

/// <summary>
/// Runs one ffmpeg invocation, surfacing machine-readable progress and, on failure, the tail of
/// stderr. A transcode that dies silently leaves a player stalled with nothing to say.
/// </summary>
public static class FfmpegProcess
{
    /// <param name="onStarted">
    /// Called once with the running process. It exists so a caller can watch the process while it
    /// runs — reading its position, or holding it back — which nothing outside can do without a
    /// handle on it.
    /// </param>
    public static async Task RunAsync(
        IEnumerable<string> arguments,
        Action<FfmpegProgress>? onProgress = null,
        CancellationToken ct = default,
        Action<Process>? onStarted = null)
    {
        var info = new ProcessStartInfo("ffmpeg")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };

        // -nostdin: without it ffmpeg competes for the terminal and can suspend a background run.
        info.ArgumentList.Add("-hide_banner");
        info.ArgumentList.Add("-nostdin");
        info.ArgumentList.Add("-loglevel");
        info.ArgumentList.Add("error");

        if (onProgress is not null)
        {
            info.ArgumentList.Add("-progress");
            info.ArgumentList.Add("pipe:1");
            info.ArgumentList.Add("-nostats");
        }

        foreach (var argument in arguments)
            info.ArgumentList.Add(argument);

        using var process = Process.Start(info)
            ?? throw new FfmpegException("Could not start ffmpeg. Is it on PATH?");

        onStarted?.Invoke(process);

        var stderrTail = new Queue<string>();
        var stderrTask = Task.Run(async () =>
        {
            string? line;
            while ((line = await process.StandardError.ReadLineAsync(ct)) is not null)
            {
                stderrTail.Enqueue(line);
                if (stderrTail.Count > 12) stderrTail.Dequeue();
            }
        }, ct);

        var progressTask = Task.Run(async () =>
        {
            var outTime = TimeSpan.Zero;
            var speed = 0d;
            string? line;
            while ((line = await process.StandardOutput.ReadLineAsync(ct)) is not null)
            {
                var separator = line.IndexOf('=');
                if (separator <= 0) continue;

                var key = line[..separator];
                var value = line[(separator + 1)..].Trim();

                switch (key)
                {
                    case "out_time_us" when long.TryParse(value, out var microseconds) && microseconds >= 0:
                        outTime = TimeSpan.FromMicroseconds(microseconds);
                        break;
                    case "speed" when value.EndsWith('x')
                                      && double.TryParse(value[..^1], NumberStyles.Float,
                                          CultureInfo.InvariantCulture, out var parsed):
                        speed = parsed;
                        break;
                    case "progress":
                        onProgress?.Invoke(new FfmpegProgress(outTime, speed));
                        break;
                }
            }
        }, ct);

        await process.WaitForExitAsync(ct);
        await Task.WhenAll(stderrTask, progressTask);

        if (process.ExitCode != 0)
            throw new FfmpegException($"ffmpeg exited {process.ExitCode}: {string.Join(" | ", stderrTail)}");
    }
}
