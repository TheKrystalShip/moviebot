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
/// Only keyframes are decoded. Reading every frame of a two-hour film to throw all but a few
/// hundred of them away costs minutes of GPU, where decoding the keyframes alone is bounded by how
/// fast the file can be read. What comes out is the nearest keyframe to each interval, which is
/// what a preview wants anyway.
/// </summary>
public static class ThumbnailSheet
{
    /// <summary>
    /// Wide enough to actually recognise a scene. This is the thing somebody is looking at when
    /// they seek, so it is sized to be looked at rather than to keep the sheet small.
    /// </summary>
    private const int FrameWidth = 320;

    /// <summary>
    /// Rows this long keep both of the sheet's dimensions inside the four thousand pixels older
    /// hardware will hold as one texture. Wider rows and a taller sheet cost the same pixels; only
    /// one of them fails on a machine somebody actually owns.
    /// </summary>
    private const int Columns = 12;

    /// <summary>
    /// What one sheet may cost, in pixels, decoded. A browser holds four bytes for each of them
    /// the whole time the film is open, so this is the real limit rather than the file's size —
    /// and it is a budget rather than a count, because how many frames fit depends on how large
    /// each one is.
    /// </summary>
    private const int SheetPixelBudget = 12_000_000;

    /// <summary>More than this is finer than a pointer on a scrub bar can ask for.</summary>
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

        // Both dimensions are fixed rather than one derived, so the window the player moves over
        // the sheet is known exactly. A frame sliced half onto the next one is the failure here.
        var height = (int)Math.Round(FrameWidth * (double)video.Height.Value / video.Width.Value);
        if (height % 2 != 0) height++;

        var affordable = Math.Max(2, SheetPixelBudget / (FrameWidth * height));
        var most = Math.Min(MostFrames, affordable);

        var interval = Math.Max(ShortestIntervalSeconds, Math.Ceiling(durationSeconds / most));
        var count = (int)Math.Ceiling(durationSeconds / interval);
        if (count < 2) return null;

        var rows = (int)Math.Ceiling(count / (double)Columns);

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

        log($"  {count} scrub previews at {FrameWidth}x{height}, one every {interval:0}s");

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
