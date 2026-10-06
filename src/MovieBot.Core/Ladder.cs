namespace TheKrystalShip.MovieBot.Core;

/// <summary>One quality of a film as the ladder defines it: where it is written and what it is.</summary>
public sealed record LadderRung(string Directory, int Width, int Height, int BitrateKbps)
{
    /// <summary>The playlist URI, relative to the title's directory.</summary>
    public string Uri => $"{Directory}/index.m3u8";
}

/// <summary>A rung below the top one: the width it is scaled to and the bitrate it is encoded at.</summary>
public sealed record LadderStep(string Directory, int Width, int BitrateKbps);

/// <summary>
/// The set of qualities a source of a given size is encoded into. It is written once, here, because
/// two processes need the answer: the ingest makes the rungs, and the planner compares a finished
/// title against what a source of its size should have to find what it is owed.
///
/// The top rung is the source's own dimensions. Each step-down joins it only where the source is
/// wider: scaling up spends another copy of the film on no more picture than the source carries.
/// </summary>
public static class Ladder
{
    public const string TopDirectory = "v0";

    /// <summary>
    /// What the ingest encodes at unless told otherwise, and so what the planner judges a finished
    /// title against and the hand-off sizes a transcode's reservation from. Why each value is what it
    /// is lives on <c>IngestOptions</c>, which takes its defaults from here.
    /// </summary>
    public const int TopKbps = 9000;
    public const int SegmentSeconds = 2;

    /// <summary>The rungs under the top one, widest first.</summary>
    public static readonly IReadOnlyList<LadderStep> StepDowns =
    [
        new("v1", 1280, 4000),
        new("v2", 854, 1600)
    ];

    /// <summary>The ladder a source of this size is owed at the default bitrates.</summary>
    public static List<LadderRung> For(int sourceWidth, int sourceHeight) =>
        For(sourceWidth, sourceHeight, TopKbps, StepDowns);

    /// <summary>
    /// The top rung, then every step-down narrower than the source, widest first. A step-down at
    /// no bitrate is left out.
    /// </summary>
    public static List<LadderRung> For(
        int sourceWidth, int sourceHeight, int topKbps, IReadOnlyList<LadderStep> stepDowns)
    {
        var rungs = new List<LadderRung> { new(TopDirectory, sourceWidth, sourceHeight, topKbps) };

        if (sourceWidth <= 0 || sourceHeight <= 0) return rungs;

        foreach (var step in stepDowns)
        {
            if (sourceWidth <= step.Width || step.BitrateKbps <= 0) continue;

            rungs.Add(new LadderRung(
                step.Directory, step.Width, ScaledHeight(sourceWidth, sourceHeight, step.Width), step.BitrateKbps));
        }

        return rungs;
    }

    /// <summary>
    /// What <c>scale=w:-2</c> produces: the height that keeps the source's aspect ratio, rounded to
    /// the nearest even number, since H.264 requires even dimensions. The manifest advertises this,
    /// so it is computed the way ffmpeg computes it and the two cannot disagree.
    /// </summary>
    public static int ScaledHeight(int sourceWidth, int sourceHeight, int targetWidth)
    {
        var exact = (double)sourceHeight * targetWidth / sourceWidth;
        return Math.Max(2, (int)Math.Round(exact / 2, MidpointRounding.AwayFromZero) * 2);
    }
}
