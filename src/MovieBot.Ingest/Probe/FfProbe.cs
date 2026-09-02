using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace TheKrystalShip.MovieBot.Ingest.Probe;

public static class FfProbe
{
    /// <summary>
    /// A Blu-ray rip carries a dozen PGS subtitle streams, and with the default probe window
    /// ffprobe cannot size them: it prints "Could not find codec parameters" once per stream and
    /// may return incomplete data. Reading well past the default costs a couple of seconds and
    /// removes the whole class of problem.
    /// </summary>
    private const string ProbeWindow = "200M";

    public static async Task<ProbeResult> RunAsync(string sourcePath, CancellationToken ct = default)
    {
        if (!File.Exists(sourcePath))
            throw new FileNotFoundException($"Source file not found: {sourcePath}", sourcePath);

        var info = new ProcessStartInfo("ffprobe")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };

        foreach (var arg in new[]
                 {
                     "-v", "error",
                     "-probesize", ProbeWindow,
                     "-analyzeduration", ProbeWindow,
                     "-show_format", "-show_streams", "-show_chapters",
                     "-print_format", "json",
                     sourcePath
                 })
        {
            info.ArgumentList.Add(arg);
        }

        using var process = Process.Start(info)
            ?? throw new InvalidOperationException("Could not start ffprobe. Is it on PATH?");

        var stdoutTask = process.StandardOutput.ReadToEndAsync(ct);
        var stderrTask = process.StandardError.ReadToEndAsync(ct);
        await process.WaitForExitAsync(ct);

        var stdout = await stdoutTask;
        var stderr = await stderrTask;

        if (process.ExitCode != 0)
            throw new InvalidOperationException($"ffprobe exited {process.ExitCode}: {Tail(stderr)}");

        var result = JsonSerializer.Deserialize(stdout, ProbeJsonContext.Default.ProbeResult)
            ?? throw new InvalidOperationException("ffprobe returned no parseable output.");

        if (result.Streams.Count == 0)
            throw new InvalidOperationException($"ffprobe found no streams in {sourcePath}.");

        return result;
    }

    private static string Tail(string text, int lines = 4)
    {
        var all = text.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        var builder = new StringBuilder();
        foreach (var line in all[Math.Max(0, all.Length - lines)..])
            builder.Append(line.Trim()).Append("; ");
        return builder.ToString().TrimEnd(' ', ';');
    }
}
