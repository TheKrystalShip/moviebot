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

    /// <summary>
    /// How long to wait for the file's container header to appear before giving up.
    ///
    /// qBittorrent pre-allocates the full file as zeros, and BitTorrent downloads pieces in
    /// rarest-first order, so the first piece — containing the Matroska EBML header or MP4 ftyp
    /// box — is not guaranteed to arrive early. The probe cannot succeed until that header data
    /// has been written to disk.
    /// </summary>
    private static readonly TimeSpan HeaderTimeout = TimeSpan.FromSeconds(120);

    /// <summary>How often to re-check the header while waiting.</summary>
    private static readonly TimeSpan HeaderPollInterval = TimeSpan.FromSeconds(5);

    /// <summary>Magic bytes that open an MKV/Matroska container (EBML header).</summary>
    private static readonly byte[] MatroskaMagic = [0x1a, 0x45, 0xdf, 0xa3];

    public static async Task<ProbeResult> RunAsync(string sourcePath, CancellationToken ct = default)
    {
        await WaitForHeaderAsync(sourcePath, ct);

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

    /// <summary>
    /// Waits until the file exists and its first bytes show a recognised container header.
    ///
    /// qBittorrent pre-allocates the full file as zeros when a torrent is added. BitTorrent
    /// downloads pieces in rarest-first order, so the first piece (containing the MKV EBML
    /// header) is not guaranteed to arrive early. Probing a zero-filled file produces the
    /// EBML parse error; this gate prevents that by waiting for real header data.
    /// </summary>
    private static async Task WaitForHeaderAsync(string path, CancellationToken ct)
    {
        var deadline = DateTime.UtcNow + HeaderTimeout;

        while (true)
        {
            if (File.Exists(path) && await HasValidHeaderAsync(path))
                return;

            if (DateTime.UtcNow >= deadline)
                throw new InvalidOperationException(
                    $"Source header did not appear within {HeaderTimeout.TotalSeconds}s: {path}");

            await Task.Delay(HeaderPollInterval, ct);
        }
    }

    /// <summary>
    /// Reads the first four bytes of a file and checks whether they match a known container
    /// header. Returns false if the file is too short, unreadable, or starts with zeros — all
    /// of which mean the header piece has not landed yet.
    /// </summary>
    private static async Task<bool> HasValidHeaderAsync(string path)
    {
        try
        {
            using var fs = new FileStream(
                path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            var buf = new byte[4];
            var read = await fs.ReadAsync(buf);
            return read == 4 && buf.SequenceEqual(MatroskaMagic);
        }
        catch (IOException)
        {
            // File locked or not yet fully allocated — header is not ready.
            return false;
        }
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
