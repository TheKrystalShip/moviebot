using System.Diagnostics;
using System.Globalization;
using TheKrystalShip.MovieBot.Ingest.Ffmpeg;

namespace TheKrystalShip.MovieBot.Ingest.Markers;

/// <summary>
/// Runs chromaprint's <c>fpcalc</c> over an audio file or playlist and returns the raw fingerprint:
/// one 32-bit value per slice of audio, each a coarse description of the pitch content of about two
/// seconds that begins <see cref="ItemSeconds"/> after the previous one. Two recordings of the same
/// sound produce values that differ in a few bits, which is what <see cref="RunFinder"/> compares.
/// </summary>
public static class Fpcalc
{
    /// <summary>
    /// How far one fingerprint value starts after the one before: chromaprint's frame hop, a third
    /// of a 4096-sample frame at its 11025 Hz analysis rate.
    /// </summary>
    public const double ItemSeconds = 4096.0 / 3 / 11025;

    /// <summary>
    /// How far a shared stretch's edge is from the value that first or last matches it. A value
    /// describes about 1.9 s of sound and still matches while about half of that is shared, so a run
    /// of values begins this long before the shared sound does and its last value starts this long
    /// before the shared sound ends. Measured against the frames of two episodes, whose titles begin
    /// and end on the same frame as the sound.
    /// </summary>
    public const double EdgeSeconds = 0.9;

    /// <summary>
    /// How far past its last matching value a stretch is taken to end. Less than
    /// <see cref="EdgeSeconds"/>: a skip that ends early leaves a moment of the titles, and one that
    /// ends late takes a moment of the scene after them.
    /// </summary>
    public const double EndAllowanceSeconds = 0.5;

    /// <summary>
    /// Fingerprints the first <paramref name="lengthSeconds"/> of the audio, or all of it when zero.
    /// It reads the transcoded playlist directly, so nothing is decoded twice and the source file
    /// is not needed.
    /// </summary>
    public static async Task<uint[]> RunAsync(string audioPath, int lengthSeconds, CancellationToken ct)
    {
        var info = new ProcessStartInfo("fpcalc")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };

        info.ArgumentList.Add("-raw");
        info.ArgumentList.Add("-length");
        info.ArgumentList.Add(lengthSeconds.ToString(CultureInfo.InvariantCulture));
        info.ArgumentList.Add(audioPath);

        using var process = Process.Start(info)
            ?? throw new FfmpegException("Could not start fpcalc. Is chromaprint installed and on PATH?");

        // The pass is a rider on the host, never what a transcode or a viewer waits on.
        try { process.PriorityClass = ProcessPriorityClass.BelowNormal; }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception) { }

        var stdout = process.StandardOutput.ReadToEndAsync(ct);
        var stderr = process.StandardError.ReadToEndAsync(ct);

        try
        {
            await process.WaitForExitAsync(ct);
        }
        catch (OperationCanceledException)
        {
            try { process.Kill(entireProcessTree: true); }
            catch (InvalidOperationException) { }
            throw;
        }

        var output = await stdout;
        if (process.ExitCode != 0)
            throw new FfmpegException($"fpcalc exited {process.ExitCode}: {(await stderr).Trim()}");

        return Parse(output);
    }

    /// <summary>Reads the <c>FINGERPRINT=</c> line of <c>fpcalc -raw</c>, which is comma separated.</summary>
    public static uint[] Parse(string output)
    {
        foreach (var line in output.Split('\n'))
        {
            if (!line.StartsWith("FINGERPRINT=", StringComparison.Ordinal)) continue;

            var text = line["FINGERPRINT=".Length..].Trim();
            if (text.Length == 0) return [];

            var parts = text.Split(',');
            var items = new uint[parts.Length];
            for (var i = 0; i < parts.Length; i++)
                items[i] = uint.Parse(parts[i], NumberStyles.None, CultureInfo.InvariantCulture);

            return items;
        }

        throw new FormatException("fpcalc printed no FINGERPRINT line.");
    }

    /// <summary>The time at which the value at an index begins.</summary>
    public static double TimeOf(int index) => index * ItemSeconds;

    /// <summary>The index of the value that begins at or after a time.</summary>
    public static int IndexAt(double seconds) => (int)Math.Ceiling(seconds / ItemSeconds);
}
