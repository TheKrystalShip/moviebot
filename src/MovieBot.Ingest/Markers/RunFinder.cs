using System.Numerics;

namespace TheKrystalShip.MovieBot.Ingest.Markers;

/// <summary>
/// A stretch of one fingerprint that another fingerprint repeats: values
/// <see cref="StartA"/> to <see cref="StartA"/> + <see cref="Length"/> of the first are the values
/// from <see cref="StartB"/> of the second, give or take a few bits each.
/// </summary>
public readonly record struct SharedRun(int StartA, int StartB, int Length, double MeanBitErrors)
{
    public int EndA => StartA + Length;
    public int EndB => StartB + Length;
}

/// <summary>
/// Finds where two fingerprints say the same sound.
///
/// Two episodes of a show open with the same title music at different times, because the cold open
/// before it varies in length, so the alignment is not known and has to be found. Every value that
/// has a half in common with a value of the other is a vote for the offset between them; a real
/// shared stretch piles its votes on one offset while chance spreads them thinly. Each of the
/// offsets with the most votes is then walked value by value, counting the bits that differ, and
/// what survives is a run of values that agree to within the noise two encodes of the same audio
/// produce.
///
/// This is the method Jellyfin's intro skipper uses; the thresholds here were fitted to the episodes
/// in the tests rather than taken from it.
/// </summary>
public static class RunFinder
{
    /// <summary>
    /// A value that differs from its counterpart in at most this many of its 32 bits counts as the
    /// same sound. Unrelated audio differs in about sixteen, and one in three hundred pairs of it
    /// falls inside this.
    /// </summary>
    public const int MaxBitErrors = 8;

    /// <summary>
    /// How many consecutive values may differ by more than <see cref="MaxBitErrors"/> without
    /// ending a run: a cough over the music, a cut, a frame of the encode that came out differently.
    /// </summary>
    public const int MaxGapItems = 4;

    /// <summary>A run whose values differ by more than this on average is not the same audio.</summary>
    public const double MaxMeanBitErrors = 6.5;

    /// <summary>
    /// A value that occurs more often than this in the fingerprint it is looked up in says nothing
    /// about where: silence and a held tone produce the same value hundreds of times, and each
    /// would vote for every offset at once.
    /// </summary>
    public const int MaxOccurrences = 6;

    /// <summary>How many of the best-voted offsets are walked.</summary>
    public const int OffsetsTried = 6;

    /// <summary>Votes an offset needs before it is worth walking.</summary>
    public const int MinVotes = 4;

    /// <summary>
    /// The share of a run that may be one repeated value before it is taken for silence. The
    /// frequency filter keeps silence out of the votes; a run found by its neighbours can still
    /// have been padded with it.
    /// </summary>
    public const double MaxRepeatedShare = 0.3;

    /// <summary>
    /// Every run of at least <paramref name="minItems"/> values in the stated windows of the two
    /// fingerprints, longest first. Runs that overlap on the first fingerprint are reported once.
    /// </summary>
    public static IReadOnlyList<SharedRun> Find(
        uint[] a, int fromA, int toA, uint[] b, int fromB, int toB, int minItems)
    {
        toA = Math.Min(toA, a.Length);
        toB = Math.Min(toB, b.Length);
        if (toA - fromA < minItems || toB - fromB < minItems) return [];

        var index = new Dictionary<uint, List<int>>();
        for (var j = fromB; j < toB; j++)
        {
            foreach (var key in KeysOf(b[j]))
            {
                if (!index.TryGetValue(key, out var positions))
                    index[key] = positions = [];
                positions.Add(j);
            }
        }

        var votes = new Dictionary<int, int>();
        for (var i = fromA; i < toA; i++)
        {
            foreach (var key in KeysOf(a[i]))
            {
                if (!index.TryGetValue(key, out var positions)) continue;
                if (positions.Count > MaxOccurrences) continue;

                foreach (var j in positions)
                    votes[j - i] = votes.GetValueOrDefault(j - i) + 1;
            }
        }

        var runs = new List<SharedRun>();
        var offsets = votes
            .Where(v => v.Value >= MinVotes)
            .OrderByDescending(v => v.Value)
            .Take(OffsetsTried)
            .Select(v => v.Key);

        foreach (var offset in offsets)
            runs.AddRange(Walk(a, fromA, toA, b, fromB, toB, offset, minItems));

        return Disjoint(runs);
    }

    /// <summary>
    /// What a value is looked up by: each half of it on its own. Two encodes of one sound rarely
    /// agree on all 32 bits of a value, but they agree on one of the halves often enough for every
    /// shared stretch to pile its votes on one offset.
    /// </summary>
    private static IEnumerable<uint> KeysOf(uint value)
    {
        yield return value & 0xFFFF;
        yield return 0x10000 | (value >> 16);
    }

    private static IEnumerable<SharedRun> Walk(
        uint[] a, int fromA, int toA, uint[] b, int fromB, int toB, int offset, int minItems)
    {
        // Values of the first fingerprint whose counterpart, at this offset, is inside the window.
        var first = Math.Max(fromA, fromB - offset);
        var last = Math.Min(toA, toB - offset);

        int start = -1, lastGood = -1, gap = 0;

        for (var i = first; i <= last; i++)
        {
            var matched = i < last && BitOperations.PopCount(a[i] ^ b[i + offset]) <= MaxBitErrors;

            if (matched)
            {
                if (start < 0) start = i;
                lastGood = i;
                gap = 0;
                continue;
            }

            if (start < 0) continue;

            gap++;
            if (gap <= MaxGapItems && i < last) continue;

            var run = Measure(a, b, start, lastGood, offset);
            if (run.Length >= minItems && Accepts(run, a)) yield return run;

            start = -1;
            gap = 0;
        }
    }

    private static SharedRun Measure(uint[] a, uint[] b, int start, int end, int offset)
    {
        long errors = 0;
        for (var i = start; i <= end; i++)
            errors += BitOperations.PopCount(a[i] ^ b[i + offset]);

        var length = end - start + 1;
        return new SharedRun(start, start + offset, length, (double)errors / length);
    }

    private static bool Accepts(SharedRun run, uint[] a)
    {
        if (run.MeanBitErrors > MaxMeanBitErrors) return false;

        var counts = new Dictionary<uint, int>();
        var most = 0;
        for (var i = run.StartA; i < run.EndA; i++)
        {
            var count = counts.GetValueOrDefault(a[i]) + 1;
            counts[a[i]] = count;
            most = Math.Max(most, count);
        }

        return most <= run.Length * MaxRepeatedShare;
    }

    /// <summary>Longest first, dropping any run that overlaps one already kept on the first fingerprint.</summary>
    private static List<SharedRun> Disjoint(List<SharedRun> runs)
    {
        var kept = new List<SharedRun>();
        foreach (var run in runs.OrderByDescending(r => r.Length))
            if (!kept.Any(k => run.StartA < k.EndA && k.StartA < run.EndA))
                kept.Add(run);

        return kept;
    }
}
