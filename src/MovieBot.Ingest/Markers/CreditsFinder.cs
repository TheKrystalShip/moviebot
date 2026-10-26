namespace TheKrystalShip.MovieBot.Ingest.Markers;

/// <summary>The closing credits as two episodes' audio places them: where they lie in each, in seconds.</summary>
public readonly record struct CreditsMatch(double StartA, double EndA, double StartB, double EndB);

/// <summary>
/// Finds a show's closing credits by comparing the closing stretches of two of its episodes.
///
/// The credits are played over the same music every week, ending the episode or followed by a
/// preview of the next; the scene before them is the episode's own. So the longest stretch two
/// closings share is the credits, provided it ends close enough to the end to be them. A show whose
/// credits run over a different song each week shares nothing there, and has none.
/// </summary>
public static class CreditsFinder
{
    /// <summary>How much of an episode, from its end, is searched, as a share of its length.</summary>
    public const double ClosingShare = 0.25;

    /// <summary>The most of an episode searched, however long it is.</summary>
    public const double ClosingCapSeconds = 600;

    /// <summary>Shorter than this is a logo or a sting, which is not worth a button.</summary>
    public const double MinSeconds = 15;

    /// <summary>
    /// The most that may follow the credits. A preview of the next episode or a scene after the
    /// credits is a minute or so; a shared stretch further from the end is a cue of the show's score
    /// played again, and taking it for the credits would skip the scene it scores.
    /// </summary>
    public const double MaxAfterSeconds = 120;

    /// <summary>
    /// Shared stretches this close together are one. A run breaks on a moment the two encodes
    /// disagree about, and the credits measured split into two runs a few seconds apart.
    /// </summary>
    public const double MergeGapSeconds = 10;

    /// <summary>
    /// Credits that end this close to the end of the episode run to its end: what is left is a
    /// logo, not anything a viewer would skip to.
    /// </summary>
    public const double SnapToEndSeconds = 15;

    /// <summary>Two partners place the credits alike when their starts or their ends are this close.</summary>
    public const double AgreementSeconds = 5;

    /// <summary>Where the closing stretch of an episode this long begins.</summary>
    public static double ClosingStart(double durationSeconds) =>
        durationSeconds - Math.Min(durationSeconds * ClosingShare, ClosingCapSeconds);

    /// <summary>How many values from the end of a fingerprint are its closing.</summary>
    public static int ClosingItems(int totalItems) =>
        (int)Math.Min(totalItems * ClosingShare, Fpcalc.IndexAt(ClosingCapSeconds));

    /// <summary>
    /// The credits the two episodes share, or null when their closings share no stretch long enough
    /// and near enough the end of both.
    /// </summary>
    public static CreditsMatch? Find(uint[] a, uint[] b)
    {
        var fromA = a.Length - ClosingItems(a.Length);
        var fromB = b.Length - ClosingItems(b.Length);
        var runs = RunFinder.Find(a, fromA, a.Length, b, fromB, b.Length, Fpcalc.IndexAt(MinSeconds / 3));
        if (runs.Count == 0) return null;

        var merged = Merge(runs);
        var longest = merged.MaxBy(r => r.EndA - r.StartA);

        var startA = IntroFinder.StartOf(longest.StartA);
        var endA = IntroFinder.EndOf(longest.EndA);
        if (endA - startA < MinSeconds) return null;

        var startB = IntroFinder.StartOf(longest.StartB);
        var endB = IntroFinder.EndOf(longest.EndB);

        var lengthA = Fpcalc.TimeOf(a.Length);
        var lengthB = Fpcalc.TimeOf(b.Length);
        if (lengthA - endA > MaxAfterSeconds || lengthB - endB > MaxAfterSeconds) return null;

        return new CreditsMatch(
            startA, lengthA - endA <= SnapToEndSeconds ? lengthA : endA,
            startB, lengthB - endB <= SnapToEndSeconds ? lengthB : endB);
    }

    /// <summary>
    /// Where the credits are in the episode, from what each partner found in it, nearest partner
    /// first. One partner is believed alone. With more, two have to place the credits alike, and the
    /// stretch is what every partner that agrees with the nearest of those two has in common: it
    /// starts late and ends early, since a skip that leaves a moment of the credits costs nothing and
    /// one that takes the last line of the scene, or the first of the one after, costs the story.
    /// Null when no two agree. Answers the indices of the partners that agreed, so each can be given
    /// the credits too.
    /// </summary>
    public static (double Start, double End, IReadOnlyList<int> Agreeing)? Agree(IReadOnlyList<CreditsMatch?> matches)
    {
        var found = matches
            .Select((m, i) => (Match: m, Index: i))
            .Where(m => m.Match is not null)
            .Select(m => (Match: m.Match!.Value, m.Index))
            .ToList();

        if (found.Count == 0) return null;
        if (matches.Count == 1) return (found[0].Match.StartA, found[0].Match.EndA, [found[0].Index]);

        foreach (var anchor in found)
        {
            var agreeing = found.Where(f => Alike(f.Match, anchor.Match)).ToList();
            if (agreeing.Count < 2) continue;

            var start = agreeing.Max(f => f.Match.StartA);
            var end = agreeing.Min(f => f.Match.EndA);
            if (end - start < MinSeconds) continue;

            return (start, end, agreeing.Select(f => f.Index).ToList());
        }

        return null;
    }

    private static bool Alike(CreditsMatch a, CreditsMatch b) =>
        Math.Abs(a.StartA - b.StartA) <= AgreementSeconds || Math.Abs(a.EndA - b.EndA) <= AgreementSeconds;

    /// <summary>Runs close together on both fingerprints, joined into one, in the order of the first.</summary>
    private static List<SharedRun> Merge(IReadOnlyList<SharedRun> runs)
    {
        var gap = Fpcalc.IndexAt(MergeGapSeconds);
        var merged = new List<SharedRun>();

        foreach (var run in runs.OrderBy(r => r.StartA))
        {
            if (merged.Count > 0 && merged[^1] is var last
                && run.StartA - last.EndA <= gap && run.StartB - last.EndB is var gapB && gapB >= 0 && gapB <= gap)
            {
                merged[^1] = last with { Length = run.EndA - last.StartA };
                continue;
            }

            merged.Add(run);
        }

        return merged;
    }
}
