using TheKrystalShip.MovieBot.Core;
using TheKrystalShip.MovieBot.Ingest.Probe;

namespace TheKrystalShip.MovieBot.Ingest.Ffmpeg;

/// <summary>
/// The strip of frames the scrub bar previews from.
///
/// One image holding every frame, rather than a file each. A preview is wanted the instant a
/// pointer lands on the bar, and a request per frame would spend the whole hover fetching; a
/// browser holds one sheet and moves a window over it for nothing.
///
/// Only keyframes are decoded. Reading every frame of a two-hour film to throw away all but four
/// hundred of them costs minutes of GPU for pictures a hundred and sixty pixels wide, where
/// decoding the keyframes alone is bounded by how fast the file can be read. What comes out is the
/// nearest keyframe to each interval, which is what a preview wants anyway.
/// </summary>
public static class ThumbnailSheet
{
    /// <summary>Wide enough to recognise a scene, small enough that hundreds fit in one sheet.</summary>
    private const int FrameWidth = 160;

    /// <summary>Rows this long keep the sheet nearer square than wide, whatever the film's length.</summary>
    private const int Columns = 20;

    /// <summary>
    /// Beyond this the sheet grows without the preview getting more useful, and browsers have
    /// limits on how large an image they will decode.
    /// </summary>
    private const int MostFrames = 400;

    /// <summary>Closer together than this is more precision than a pointer on a scrub bar has.</summary>
    private const double ShortestIntervalSeconds = 5;

    public const string FileName = "thumbs.jpg";

    /// <summary>
    /// Writes the sheet and describes it, or returns null when there is nothing worth making one
    /// from. Never throws: a film without previews is worse than one with them and better than one
    /// that failed to ingest.
    /// </summary>
    public static async Task<ThumbnailStrip?> WriteAsync(
        string sourcePath, ProbeStream video, double durationSeconds,
        string outputDirectory, Action<string> log, CancellationToken ct = default)
    {
        if (durationSeconds <= 0 || video.Width is not > 0 || video.Height is not > 0) return null;

        var interval = Math.Max(ShortestIntervalSeconds, Math.Ceiling(durationSeconds / MostFrames));
        var count = (int)Math.Ceiling(durationSeconds / interval);
        if (count < 2) return null;

        var rows = (int)Math.Ceiling(count / (double)Columns);

        // Both dimensions are fixed rather than one derived, so the window the player moves over
        // the sheet is known exactly. A frame sliced half onto the next one is the failure here.
        var height = (int)Math.Round(FrameWidth * (double)video.Height.Value / video.Width.Value);
        if (height % 2 != 0) height++;

        var path = Path.Combine(outputDirectory, FileName);

        try
        {
            await FfmpegProcess.RunAsync(
            [
                "-y",
                "-skip_frame", "nokey",
                "-i", sourcePath,
                "-an", "-sn", "-dn",
                "-vf", $"fps=1/{interval.ToString(System.Globalization.CultureInfo.InvariantCulture)},"
                       + $"scale={FrameWidth}:{height},tile={Columns}x{rows}",
                "-frames:v", "1",
                "-q:v", "5",
                path
            ], ct: ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            log($"  no scrub previews: {ex.Message}");
            return null;
        }

        if (!File.Exists(path)) return null;

        log($"  {count} scrub previews, one every {interval:0}s");

        return new ThumbnailStrip
        {
            Uri = FileName,
            IntervalSeconds = interval,
            Columns = Columns,
            Rows = rows,
            Width = FrameWidth,
            Height = height,
            Count = count
        };
    }
}
