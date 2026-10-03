using System.Globalization;
using System.Text.RegularExpressions;
using TheKrystalShip.MovieBot.Core;

namespace TheKrystalShip.MovieBot.Ingest.Markers;

/// <summary>One cue of a subtitle track: what is said, and when.</summary>
public readonly record struct Cue(double StartSeconds, double EndSeconds, string Text);

/// <summary>
/// Keeps a title sequence from skipping speech.
///
/// A title sequence has music and no dialogue, so a line spoken inside a stretch marked as one means
/// the stretch is wrong: a chapter that was named loosely, or sound shared with another episode that
/// runs on under the scene after the titles. The marker is ended just before the line, or dropped
/// when what is left is too short to be a sequence. The first line after the titles is not an end
/// to aim for — the scene before it is often silent, and skipping to the line would take it — so
/// cues only ever shorten a marker.
/// </summary>
public static partial class IntroGuard
{
    /// <summary>How long before the first spoken line the marker ends.</summary>
    public const double MarginSeconds = 0.5;

    /// <summary>
    /// The marker kept clear of speech, or null when what is left of it is shorter than
    /// <see cref="IntroFinder.MinSeconds"/>. A marker with no dialogue inside it is returned as it is.
    /// </summary>
    public static Marker? Guard(Marker intro, IReadOnlyList<Cue> cues)
    {
        var spoken = cues
            .Where(c => c.StartSeconds >= intro.StartSeconds && c.StartSeconds < intro.EndSeconds)
            .Where(c => IsDialogue(c.Text))
            .OrderBy(c => c.StartSeconds)
            .Cast<Cue?>()
            .FirstOrDefault();

        if (spoken is not { } cue) return intro;

        var end = cue.StartSeconds - MarginSeconds;
        return end - intro.StartSeconds >= IntroFinder.MinSeconds
            ? intro with { EndSeconds = Math.Round(end, 3) }
            : null;
    }

    /// <summary>
    /// Whether a cue is somebody speaking. Lyrics and music marks, and descriptions of sound in
    /// brackets or parentheses, are what a title sequence has, so a cue made only of those is not.
    /// A line with a music mark in it is lyrics whatever else is on it.
    /// </summary>
    public static bool IsDialogue(string text)
    {
        foreach (var raw in text.Split('\n'))
        {
            var line = Tags().Replace(raw, "").Trim();
            line = LeadingDash().Replace(line, "");
            if (MusicLine().IsMatch(line)) continue;

            line = Description().Replace(line, "").Trim();
            if (line.Length > 0) return true;
        }

        return false;
    }

    /// <summary>The cues of a WebVTT document, in the order they appear.</summary>
    public static IReadOnlyList<Cue> ParseWebVtt(string document)
    {
        var cues = new List<Cue>();
        var lines = document.Replace("\r\n", "\n").Split('\n');

        for (var i = 0; i < lines.Length; i++)
        {
            var timing = Timing().Match(lines[i]);
            if (!timing.Success) continue;

            var text = new List<string>();
            while (i + 1 < lines.Length && lines[i + 1].Trim().Length > 0)
                text.Add(lines[++i]);

            cues.Add(new Cue(
                Seconds(timing.Groups[1].Value), Seconds(timing.Groups[2].Value), string.Join('\n', text)));
        }

        return cues;
    }

    private static double Seconds(string stamp)
    {
        var parts = stamp.Split(':');
        var seconds = double.Parse(parts[^1], CultureInfo.InvariantCulture);
        var minutes = double.Parse(parts[^2], CultureInfo.InvariantCulture);
        var hours = parts.Length == 3 ? double.Parse(parts[0], CultureInfo.InvariantCulture) : 0;
        return hours * 3600 + minutes * 60 + seconds;
    }

    [GeneratedRegex(@"^\s*((?:\d+:)?\d{1,2}:\d{2}\.\d{3})\s*-->\s*((?:\d+:)?\d{1,2}:\d{2}\.\d{3})")]
    private static partial Regex Timing();

    [GeneratedRegex(@"<[^>]*>")]
    private static partial Regex Tags();

    [GeneratedRegex(@"^\s*-\s*")]
    private static partial Regex LeadingDash();

    [GeneratedRegex(@"\[[^\]]*\]|\([^)]*\)")]
    private static partial Regex Description();

    /// <summary>A line sung or played: it carries a music mark, or begins with the one some tracks use.</summary>
    [GeneratedRegex(@"[♪♫♬]|^#")]
    private static partial Regex MusicLine();
}
