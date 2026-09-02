namespace TheKrystalShip.MovieBot.Ingest.Subtitles;

/// <summary>
/// A last look at a track once the repair has finished with it, so a track that is still wrong says
/// so at ingest rather than forty minutes into a film.
///
/// The test is that two non-ASCII characters do not stand next to each other in English. Curly
/// quotes, dashes and the accented letters that reach English through names and loanwords all
/// appear singly, between ASCII. Every form of this corruption produces the opposite: a multi-byte
/// character read one byte at a time becomes two or three adjacent non-ASCII characters, always.
/// That makes adjacency a sharper signal than any list of known-bad sequences, because it catches
/// mangling through codepages nobody here has seen yet.
/// </summary>
public static class SubtitleHealth
{
    public readonly record struct Report(int Count, IReadOnlyList<string> Samples)
    {
        public bool Clean => Count == 0;
    }

    private const int SamplesKept = 5;

    public static Report Inspect(string text)
    {
        var count = 0;
        var samples = new List<string>();

        var i = 0;
        while (i < text.Length)
        {
            if (text[i] < 0x80)
            {
                i++;
                continue;
            }

            var start = i;
            while (i < text.Length && text[i] >= 0x80) i++;
            var run = text.AsSpan(start, i - start);

            if (run.Length < 2 || !ContainsLatin1Letter(run)) continue;

            count++;

            var sample = new string(run);
            if (samples.Count < SamplesKept && !samples.Contains(sample, StringComparer.Ordinal))
                samples.Add(sample);
        }

        return new Report(count, samples);
    }

    /// <summary>
    /// A letter is what separates corruption from the handful of runs that are legitimately
    /// adjacent. Two music notes together are ordinary, and so is a dash beside a quote; a letter
    /// pressed against another non-ASCII character is not.
    /// </summary>
    private static bool ContainsLatin1Letter(ReadOnlySpan<char> run)
    {
        foreach (var c in run)
        {
            // The Latin-1 supplement is letters throughout except for the two mathematical signs
            // sitting in the middle of it.
            if (c is >= 'À' and <= 'ÿ' and not '×' and not '÷') return true;
        }

        return false;
    }
}
