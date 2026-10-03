namespace TheKrystalShip.MovieBot.Ingest.Markers;

/// <summary>The recap's extent in seconds, and how much of that extent is audio found in earlier episodes.</summary>
public readonly record struct RecapMatch(double StartSeconds, double EndSeconds, double MatchedSeconds);

/// <summary>
/// Finds the "previously on" at the head of an episode by looking for its clips in the episodes
/// that came before.
///
/// A recap is a montage: short pieces of earlier episodes, with their own dialogue, one after the
/// other. So it is recognised by what it is made of rather than by what it sounds like, and what
/// it is made of is audio that is somewhere in an earlier episode. A false recap skips story, which
/// is far worse than no button, so every test here leans toward saying nothing.
/// </summary>
public static class RecapFinder
{
    /// <summary>
    /// How far into an episode a recap is looked for when nothing says where the titles begin.
    /// </summary>
    public const double DefaultSearchSeconds = 240;

    /// <summary>A clip shorter than this is as likely to be a coincidence of music as a scene.</summary>
    public const double MinClipSeconds = 2.5;

    /// <summary>The clips together have to add up to at least this much, or it is not a montage.</summary>
    public const double MinMatchedSeconds = 15;

    /// <summary>At least this many separate clips: one long match is a scene replayed, not a recap.</summary>
    public const int MinClips = 2;

    /// <summary>The first clip has to start this close to the beginning, since a recap opens the episode.</summary>
    public const double MaxFirstClipSeconds = 45;

    /// <summary>
    /// The share of the recap's span that the clips found account for. Clips in a montage often sit
    /// under a music bed that hides them from the fingerprint, so this is well under one, and it is
    /// what stops a few matches far apart from being read as one long recap.
    /// </summary>
    public const double MinDensity = 0.4;

    /// <summary>
    /// A first clip this close to the start is taken to be preceded only by the "previously on" card,
    /// so the recap is marked from the first second.
    /// </summary>
    public const double SnapToStartSeconds = 10;

    /// <summary>A recap that ends this close before the titles runs up to them.</summary>
    public const double SnapToIntroSeconds = 10;

    /// <summary>
    /// The recap in the opening of <paramref name="a"/>, or null when nothing there is made of
    /// <paramref name="earlier"/>. <paramref name="searchSeconds"/> is where the opening ends: the
    /// start of the title sequence when it is known, because the sequence is made of audio the
    /// earlier episodes have too and is not a recap.
    ///
    /// Only the bodies of the earlier episodes are searched, never their openings, for the same
    /// reason: an earlier episode's own title sequence is in its opening.
    /// </summary>
    public static RecapMatch? Find(uint[] a, IEnumerable<uint[]> earlier, double searchSeconds, double? introStart = null)
    {
        var to = Math.Min(Fpcalc.IndexAt(searchSeconds), a.Length);
        var minItems = Fpcalc.IndexAt(MinClipSeconds);

        var clips = new List<(int Start, int End)>();
        foreach (var other in earlier)
        {
            var body = IntroFinder.OpeningItems(other.Length);
            foreach (var run in RunFinder.Find(a, 0, to, other, body, other.Length, minItems))
                clips.Add((run.StartA, run.EndA));
        }

        var merged = Merge(clips);
        if (merged.Count < MinClips) return null;

        var start = IntroFinder.StartOf(merged[0].Start);
        var end = IntroFinder.EndOf(merged[^1].End);
        var matched = merged.Sum(m => Fpcalc.TimeOf(m.End) - Fpcalc.TimeOf(m.Start));

        if (matched < MinMatchedSeconds) return null;
        if (start > MaxFirstClipSeconds) return null;
        if (matched / (end - start) < MinDensity) return null;

        if (start <= SnapToStartSeconds) start = 0;
        if (introStart is { } intro && intro - end is >= 0 and <= SnapToIntroSeconds) end = intro;
        if (introStart is { } limit) end = Math.Min(end, limit);

        return end > start ? new RecapMatch(start, end, matched) : null;
    }

    /// <summary>Overlapping clips, from several earlier episodes or one, counted once.</summary>
    private static List<(int Start, int End)> Merge(List<(int Start, int End)> clips)
    {
        var merged = new List<(int Start, int End)>();
        foreach (var clip in clips.OrderBy(c => c.Start))
        {
            if (merged.Count > 0 && clip.Start <= merged[^1].End)
                merged[^1] = (merged[^1].Start, Math.Max(merged[^1].End, clip.End));
            else
                merged.Add(clip);
        }

        return merged;
    }
}
