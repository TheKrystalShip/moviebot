using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace TheKrystalShip.MovieBot.Core.Subtitles;

/// <summary>
/// Turns a subtitle fetched from outside into the one format a browser will play.
///
/// The conversion itself is small; what surrounds it is not. A downloaded subtitle arrives in
/// whatever encoding its uploader had a decade ago, frequently mangled on top of that, and
/// sometimes already in WebVTT. Each of those is handled here so that nothing further down has to
/// know which it was given.
/// </summary>
public static partial class SubRip
{
    [GeneratedRegex(
        @"(\d+):(\d{2}):(\d{2})[.,](\d{1,3})\s*-->\s*(\d+):(\d{2}):(\d{2})[.,](\d{1,3})([^\r\n]*)",
        RegexOptions.Compiled)]
    private static partial Regex CueLine();

    /// <summary>A numbering line, which WebVTT has no use for.</summary>
    [GeneratedRegex(@"^\d+$", RegexOptions.Compiled)]
    private static partial Regex CueNumber();

    /// <summary>What a conversion produced, and what had to be done to get there.</summary>
    public readonly record struct Result(string WebVtt, int Cues, int Repaired, SubtitleHealth.Report Health)
    {
        public bool LooksWrong => !Health.Clean;
    }

    /// <summary>
    /// Reads bytes as text, undoes any double-encoding, shifts every cue and writes WebVTT.
    /// </summary>
    /// <param name="shiftSeconds">
    /// Added to every timestamp. This is where a measured offset is applied, so that what lands on
    /// disk already fits the film rather than needing every viewer to correct it themselves.
    /// </param>
    public static Result Convert(ReadOnlySpan<byte> bytes, double shiftSeconds = 0)
    {
        var text = Decode(bytes);

        // Fetched subtitles are mangled at least as often as embedded ones, and for the same
        // reason: somebody's editor read one encoding and wrote another.
        var repair = MojibakeRepair.Repair(text, completeDiscardedBytes: true);

        var body = new StringBuilder("WEBVTT\n\n");
        var cues = 0;

        foreach (var line in repair.Text.ReplaceLineEndings("\n").Split('\n'))
        {
            var match = CueLine().Match(line);
            if (match.Success)
            {
                body.Append(Timestamp(match, 1, shiftSeconds))
                    .Append(" --> ")
                    .Append(Timestamp(match, 5, shiftSeconds));

                // Cue settings ride on the same line and mean the same thing in both formats.
                var settings = match.Groups[9].Value.Trim();
                if (settings.Length > 0) body.Append(' ').Append(settings);

                body.Append('\n');
                cues++;
                continue;
            }

            // A bare number is SubRip's cue counter. WebVTT treats one as a cue identifier, which
            // is harmless but pointless, and dropping it keeps the output to what it means.
            if (CueNumber().IsMatch(line.Trim())) continue;

            body.Append(line).Append('\n');
        }

        return new Result(
            body.ToString(), cues, repair.Repaired, SubtitleHealth.Inspect(repair.Text));
    }

    /// <summary>
    /// Text out of bytes, without guessing more than it has to.
    ///
    /// UTF-8 is tried strictly first, so a file that is valid UTF-8 is read as UTF-8 and nothing
    /// else is considered. Anything else is read as Windows-1252, which cannot fail and is what
    /// the overwhelming majority of older uploads actually are.
    /// </summary>
    private static string Decode(ReadOnlySpan<byte> bytes)
    {
        if (bytes.StartsWith("﻿"u8)) bytes = bytes[3..];

        try
        {
            return new UTF8Encoding(false, throwOnInvalidBytes: true).GetString(bytes);
        }
        catch (DecoderFallbackException)
        {
            return Windows1252.Decode(bytes);
        }
    }

    private static string Timestamp(Match match, int group, double shiftSeconds)
    {
        var fraction = match.Groups[group + 3].Value;

        var seconds =
            int.Parse(match.Groups[group].Value) * 3600
            + int.Parse(match.Groups[group + 1].Value) * 60
            + int.Parse(match.Groups[group + 2].Value)
            + int.Parse(fraction) * Math.Pow(10, -fraction.Length);

        // A cue shifted before the start of the film is pinned to it rather than dropped: the line
        // is still spoken, and losing it would be a worse answer than showing it a moment early.
        var shifted = Math.Max(0, seconds + shiftSeconds);
        var time = TimeSpan.FromSeconds(shifted);

        return string.Create(CultureInfo.InvariantCulture,
            $"{(int)time.TotalHours:D2}:{time.Minutes:D2}:{time.Seconds:D2}.{time.Milliseconds:D3}");
    }
}
