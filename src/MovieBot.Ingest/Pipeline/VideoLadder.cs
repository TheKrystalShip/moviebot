using System.Globalization;
using TheKrystalShip.MovieBot.Core;

namespace TheKrystalShip.MovieBot.Ingest.Pipeline;

/// <summary>One encoded copy of the film, and where it is written.</summary>
public sealed record VideoRung
{
    /// <summary>The directory under the title, which is also the first part of the playlist URI.</summary>
    public required string Directory { get; init; }

    public required int Width { get; init; }
    public required int Height { get; init; }
    public required int BitrateKbps { get; init; }

    /// <summary>Null on the rung encoded at the source's own size, which is not scaled at all.</summary>
    public required string? ScaleFilter { get; init; }

    public string Name => $"{Height}p";
    public string Uri => $"{Directory}/index.m3u8";

    /// <summary>
    /// The ceiling the encoder may spend on a demanding passage, and the window it is averaged
    /// over. Both are derived from this rung's own target, so each rung is held to the bitrate it
    /// is sized for.
    /// </summary>
    public string Bitrate => Rate(BitrateKbps);
    public string Maxrate => Rate(BitrateKbps * 4 / 3);
    public string Bufsize => Rate(BitrateKbps * 8 / 3);

    private static string Rate(int kbps) =>
        kbps.ToString(CultureInfo.InvariantCulture) + "k";
}

/// <summary>
/// The set of copies a source is encoded into.
///
/// A player can only drop to a rung that exists, so the ladder is what carries a viewer whose
/// path slows. It is deliberately short: every rung is a full copy of the film on a disk that
/// holds the whole library.
/// </summary>
public static class VideoLadder
{
    /// <summary>
    /// Builds the ladder for a source of the given size.
    ///
    /// The top rung is the source's own dimensions. The step-down joins it only where the source
    /// is wider: scaling up spends a second copy of the film on no more picture than the first
    /// one carries.
    /// </summary>
    public static List<VideoRung> For(int sourceWidth, int sourceHeight, IngestOptions options)
    {
        var shape = Ladder.For(
            sourceWidth, sourceHeight, ParseBitrate(options.VideoBitrate),
            options.StepDownWidth, ParseBitrate(options.StepDownBitrate));

        return shape.Select(r => new VideoRung
        {
            Directory = r.Directory,
            Width = r.Width,
            Height = r.Height,
            BitrateKbps = r.BitrateKbps,
            // -2 asks ffmpeg for the height that keeps the source's aspect ratio, rounded to the
            // nearest even number. H.264 requires even dimensions.
            ScaleFilter = r.Directory == Ladder.TopDirectory
                ? null
                : $"scale={r.Width.ToString(CultureInfo.InvariantCulture)}:-2"
        }).ToList();
    }

    /// <summary>Reads <c>9M</c>, <c>4500k</c> or a bare number of kbps.</summary>
    public static int ParseBitrate(string value)
    {
        var trimmed = value.Trim();
        var multiplier = 1;

        if (trimmed.EndsWith('M') || trimmed.EndsWith('m')) { multiplier = 1000; trimmed = trimmed[..^1]; }
        else if (trimmed.EndsWith('K') || trimmed.EndsWith('k')) { trimmed = trimmed[..^1]; }

        return double.TryParse(trimmed, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)
            ? (int)Math.Round(parsed * multiplier)
            : 0;
    }
}
