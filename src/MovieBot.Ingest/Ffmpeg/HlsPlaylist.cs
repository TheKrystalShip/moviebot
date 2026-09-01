using System.Globalization;

namespace TheKrystalShip.MovieBot.Ingest.Ffmpeg;

/// <summary>
/// Reads how much of a growing EVENT playlist is actually playable.
/// </summary>
public static class HlsPlaylist
{
    /// <summary>
    /// Sums the <c>#EXTINF</c> durations already written to a playlist. This, not ffmpeg's own
    /// progress, is the truthful head: a frame the encoder has read is not watchable until its
    /// segment is closed and listed.
    /// </summary>
    public static double PlayableSeconds(string playlistPath)
    {
        if (!File.Exists(playlistPath)) return 0;

        double total = 0;
        try
        {
            // The file is being appended to as it is read, so share write access.
            using var stream = new FileStream(playlistPath, FileMode.Open, FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(stream);

            while (reader.ReadLine() is { } line)
            {
                if (!line.StartsWith("#EXTINF:", StringComparison.Ordinal)) continue;

                var value = line["#EXTINF:".Length..].TrimEnd(',');
                var comma = value.IndexOf(',');
                if (comma >= 0) value = value[..comma];

                if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds))
                    total += seconds;
            }
        }
        catch (IOException)
        {
            // ffmpeg rewrites the playlist in place on every segment; a read that lands mid-write
            // is retried on the next poll rather than being treated as a failure.
            return total;
        }

        return total;
    }

    /// <summary>
    /// The head across a set of playlists, which is the shortest of them: video that exists
    /// without its audio is not playable, so the room can only go as far as the laggard.
    /// </summary>
    public static double PlayableSeconds(IEnumerable<string> playlistPaths)
    {
        var min = double.MaxValue;
        foreach (var path in playlistPaths)
            min = Math.Min(min, PlayableSeconds(path));
        return min is double.MaxValue ? 0 : min;
    }

    /// <summary>True once ffmpeg has closed the playlist, which is what turns EVENT into a complete VOD.</summary>
    public static bool IsComplete(string playlistPath) =>
        File.Exists(playlistPath)
        && File.ReadAllText(playlistPath).Contains("#EXT-X-ENDLIST", StringComparison.Ordinal);
}
