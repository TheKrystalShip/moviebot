using System.Globalization;

namespace TheKrystalShip.MovieBot.Core;

/// <summary>
/// Measures what each track of a finished title weighs, from its media playlist and the sizes of the
/// segments it names, and records it on the manifest (<see cref="Rendition.Rate"/>,
/// <see cref="AudioTrack.Rate"/>).
/// </summary>
/// <remarks>
/// A rung is encoded at a constant quality with its bitrate as a ceiling, so what it really weighs is
/// known only once it is written. A player sizes its buffer by the peak the master states and picks a
/// rung by it, or by the average once it has a little buffered: a ceiling stated as the rate keeps it
/// on a lower rung than its connection carries and holds a fraction of what its buffer could.
/// </remarks>
public static class MeasuredRates
{
    /// <summary>
    /// Measures every track of <paramref name="manifest"/> under <paramref name="titleDirectory"/>
    /// whose playlist is complete, replacing what it held. True when every track has a rate after it.
    /// </summary>
    public static bool Measure(string titleDirectory, Manifest manifest)
    {
        var whole = true;
        foreach (var rendition in manifest.Video.Renditions)
        {
            rendition.Rate = Of(Path.Combine(titleDirectory, rendition.Uri)) ?? rendition.Rate;
            whole &= rendition.Rate is not null;
        }
        foreach (var track in manifest.Audio)
        {
            track.Rate = Of(Path.Combine(titleDirectory, track.Uri)) ?? track.Rate;
            whole &= track.Rate is not null;
        }
        return whole;
    }

    /// <summary>Whether a track of the manifest has no measured rate.</summary>
    public static bool Owed(Manifest manifest) =>
        manifest.Video.Renditions.Any(r => r.Rate is null) || manifest.Audio.Any(a => a.Rate is null);

    /// <summary>
    /// The rate of the media playlist at <paramref name="playlistPath"/>: its segments' bytes over their
    /// durations, in all and at the heaviest. Null for a playlist that is missing, unfinished, or names
    /// a segment that is not on disk.
    /// </summary>
    public static MeasuredRate? Of(string playlistPath)
    {
        if (!File.Exists(playlistPath)) return null;

        var directory = Path.GetDirectoryName(playlistPath)!;
        double totalBits = 0, totalSeconds = 0, peak = 0;
        double? duration = null;
        var ended = false;

        foreach (var raw in File.ReadLines(playlistPath))
        {
            var line = raw.Trim();
            if (line.StartsWith("#EXTINF:", StringComparison.Ordinal))
            {
                var value = line["#EXTINF:".Length..].Split(',')[0];
                duration = double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) && d > 0 ? d : null;
            }
            else if (line == "#EXT-X-ENDLIST")
            {
                ended = true;
            }
            else if (line.Length > 0 && !line.StartsWith('#') && duration is { } seconds)
            {
                var segment = new FileInfo(Path.Combine(directory, line));
                if (!segment.Exists) return null;
                var bits = segment.Length * 8.0;
                totalBits += bits;
                totalSeconds += seconds;
                peak = Math.Max(peak, bits / seconds);
                duration = null;
            }
        }

        if (!ended || totalSeconds <= 0) return null;
        return new MeasuredRate(
            (int)Math.Ceiling(totalBits / totalSeconds / 1000),
            (int)Math.Ceiling(peak / 1000));
    }
}
