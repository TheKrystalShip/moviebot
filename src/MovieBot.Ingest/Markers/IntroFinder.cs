namespace TheKrystalShip.MovieBot.Ingest.Markers;

/// <summary>
/// The title sequence as two episodes' audio places it: where it lies in each, in seconds.
/// </summary>
public readonly record struct IntroMatch(double StartA, double EndA, double StartB, double EndB);

/// <summary>
/// Finds a show's title sequence by comparing the openings of two of its episodes.
///
/// The cold open before the titles varies from one episode to the next, so the sequence is not at
/// a fixed time; what is fixed is the music, which is the same recording every week. The longest
/// stretch two openings share is the sequence, provided it is the length of one.
/// </summary>
public static class IntroFinder
{
    /// <summary>
    /// How much of an episode is searched for its titles, as a share of its length. A sequence that
    /// begins later than this is not a title sequence, it is the middle of the episode.
    /// </summary>
    public const double OpeningShare = 0.25;

    /// <summary>The most of an episode searched, however long it is.</summary>
    public const double OpeningCapSeconds = 600;

    /// <summary>Shorter than this is a sting or a logo, which is not worth a button.</summary>
    public const double MinSeconds = 15;

    /// <summary>
    /// Longer than this is not a title sequence. Two episodes can share a whole scene — a flashback
    /// replayed, a recap clip — and skipping it would skip the story.
    /// </summary>
    public const double MaxSeconds = 120;

    /// <summary>How many values from the start of a fingerprint are its opening.</summary>
    public static int OpeningItems(int totalItems) =>
        (int)Math.Min(totalItems * OpeningShare, Fpcalc.IndexAt(OpeningCapSeconds));

    /// <summary>
    /// The sequence the two episodes share, or null when they share none of the right length. When
    /// they share several stretches the longest is the one considered, and a longest that is too
    /// long or too short is no answer rather than a cue to look at the next.
    /// </summary>
    public static IntroMatch? Find(uint[] a, uint[] b)
    {
        var runs = RunFinder.Find(
            a, 0, OpeningItems(a.Length), b, 0, OpeningItems(b.Length), Fpcalc.IndexAt(MinSeconds));

        if (runs.Count == 0) return null;

        var run = runs[0];
        var seconds = SecondsOf(run);
        if (seconds < MinSeconds || seconds > MaxSeconds) return null;

        return new IntroMatch(
            StartOf(run.StartA), EndOf(run.EndA),
            StartOf(run.StartB), EndOf(run.EndB));
    }

    /// <summary>The time a stretch begins at, given the index of its first matching value.</summary>
    public static double StartOf(int startIndex) => Fpcalc.TimeOf(startIndex) + Fpcalc.EdgeSeconds;

    /// <summary>
    /// The time a stretch ends at, given the index one past its last matching value. It is placed
    /// early on purpose (<see cref="Fpcalc.EndAllowanceSeconds"/>).
    /// </summary>
    public static double EndOf(int endIndex) =>
        Fpcalc.TimeOf(endIndex - 1) + Fpcalc.EndAllowanceSeconds;

    private static double SecondsOf(SharedRun run) => Fpcalc.TimeOf(run.StartA + run.Length) - Fpcalc.TimeOf(run.StartA);
}
