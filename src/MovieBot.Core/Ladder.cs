namespace TheKrystalShip.MovieBot.Core;

/// <summary>One quality of a film as the ladder defines it: where it is written and what it is.</summary>
public sealed record LadderRung(string Directory, int Width, int Height, int BitrateKbps)
{
    /// <summary>The playlist URI, relative to the title's directory.</summary>
    public string Uri => $"{Directory}/index.m3u8";
}

/// <summary>
/// The set of qualities a source of a given size is encoded into. It is written once, here, because
/// two processes need the answer: the ingest makes the rungs, and the planner compares a finished
/// title against what a source of its size should have to find what it is owed.
///
/// The top rung is the source's own dimensions. The step-down joins it only where the source is
/// wider: scaling up spends a second copy of the film on no more picture than the first carries.
/// </summary>
public static class Ladder
{
    public const string TopDirectory = "v0";
    public const string StepDownDirectory = "v1";

    /// <summary>
    /// What the ingest encodes at unless told otherwise, and so what the planner judges a finished
    /// title against and the hand-off sizes a transcode's reservation from. Why each value is what it
    /// is lives on <c>IngestOptions</c>, which takes its defaults from here.
    /// </summary>
    public const int TopKbps = 9000;
    public const int StepDownWidth = 1280;
    public const int StepDownKbps = 4000;
    public const int SegmentSeconds = 2;

    public static List<LadderRung> For(
        int sourceWidth, int sourceHeight, int topKbps, int stepDownWidth, int stepDownKbps)
    {
        var rungs = new List<LadderRung> { new(TopDirectory, sourceWidth, sourceHeight, topKbps) };

        if (sourceWidth <= 0 || sourceHeight <= 0 || sourceWidth <= stepDownWidth || stepDownKbps <= 0)
            return rungs;

        rungs.Add(new LadderRung(
            StepDownDirectory, stepDownWidth, ScaledHeight(sourceWidth, sourceHeight, stepDownWidth), stepDownKbps));
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
