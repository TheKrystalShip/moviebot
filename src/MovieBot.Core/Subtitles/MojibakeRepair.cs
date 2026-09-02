using System.Text;

namespace TheKrystalShip.MovieBot.Core.Subtitles;

/// <summary>
/// Undoes text that was written as UTF-8, read back as Windows-1252, and written as UTF-8 again.
/// A right single quote arrives as three characters instead of one, and the subtitle reads
/// "Sheâ€™ll" where it should read "She'll".
///
/// Releases ship subtitles in this state and nothing downstream can tell the difference: the file
/// is valid UTF-8, it is valid WebVTT, and every byte survives the transcode intact. The damage is
/// only visible as text.
/// </summary>
public static class MojibakeRepair
{
    /// <summary>What a repair pass did, so a caller can say so rather than repair silently.</summary>
    public readonly record struct Result(string Text, int Repaired, int Unrepairable)
    {
        public bool Changed => Repaired > 0;
    }

    /// <summary>
    /// Five bytes have no character in Windows-1252. An encoder that discards them rather than
    /// substituting drops the last byte of a three-byte sequence, and the run arrives one character
    /// short of anything that can be decoded.
    /// </summary>
    private const string TruncatedRightQuote = "â€";

    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    /// <param name="completeDiscardedBytes">
    /// Whether to restore a closing double quote whose last byte was thrown away even with nothing
    /// in the document to prove that is what it was. Worth doing on a track people actually read
    /// and not on one they never open, because the guess is only nearly always right.
    /// </param>
    public static Result Repair(string text, bool completeDiscardedBytes = false)
    {
        var output = new StringBuilder(text.Length);
        var repaired = 0;
        var unrepairable = 0;
        var openingQuotes = 0;

        var i = 0;
        while (i < text.Length)
        {
            if (text[i] < 0x80)
            {
                output.Append(text[i]);
                i++;
                continue;
            }

            // Every byte of a multi-byte UTF-8 sequence is >= 0x80, so a mangled character is always
            // a run of non-ASCII characters and never straddles an ASCII one.
            var start = i;
            while (i < text.Length && text[i] >= 0x80) i++;
            var run = text.AsSpan(start, i - start);

            if (TryDecodeRun(run, out var decoded))
            {
                output.Append(decoded);
                repaired++;
                openingQuotes += Count(decoded, '“');
            }
            else
            {
                output.Append(run);
                unrepairable++;
            }
        }

        var result = output.ToString();

        // A dropped byte is unrecoverable by decoding, so this is the one place the repair guesses.
        // The guess is narrow: of the five bytes Windows-1252 has no character for, only the one
        // behind a closing double quote yields ordinary text. Surviving opening quotes prove the
        // encoder produced them, and without that proof the run is left alone unless the caller
        // says the track matters more than the risk of being wrong about twelve characters.
        if ((openingQuotes > 0 || completeDiscardedBytes)
            && result.Contains(TruncatedRightQuote, StringComparison.Ordinal))
        {
            var truncated = CountOccurrences(result, TruncatedRightQuote);
            result = result.Replace(TruncatedRightQuote, "”", StringComparison.Ordinal);
            unrepairable -= truncated;
            repaired += truncated;
        }

        return new Result(result, repaired, unrepairable);
    }

    /// <summary>
    /// Reverses one run: back to the bytes Windows-1252 would have produced, then a strict UTF-8
    /// read. Strictness is the whole safety argument. Text that was never mangled almost never
    /// forms valid UTF-8 when re-encoded this way, so a legitimate "NAO" in Portuguese or a
    /// Romanian diacritic fails here and is left exactly as it was.
    /// </summary>
    private static bool TryDecodeRun(ReadOnlySpan<char> run, out string decoded)
    {
        decoded = string.Empty;
        Span<byte> bytes = run.Length <= 128 ? stackalloc byte[run.Length] : new byte[run.Length];

        for (var i = 0; i < run.Length; i++)
        {
            if (!TryEncodeChar(run[i], out bytes[i])) return false;
        }

        try
        {
            decoded = StrictUtf8.GetString(bytes);
        }
        catch (DecoderFallbackException)
        {
            return false;
        }

        return !decoded.AsSpan().SequenceEqual(run);
    }

    private static bool TryEncodeChar(char c, out byte b)
    {
        return Windows1252.TryEncode(c, out b);
    }

    private static int Count(string s, char c)
    {
        var n = 0;
        foreach (var ch in s) if (ch == c) n++;
        return n;
    }

    private static int CountOccurrences(string haystack, string needle)
    {
        var n = 0;
        var at = 0;
        while ((at = haystack.IndexOf(needle, at, StringComparison.Ordinal)) >= 0)
        {
            n++;
            at += needle.Length;
        }
        return n;
    }
}
