using System.Text;
using System.Text.RegularExpressions;
using TheKrystalShip.MovieBot.Core;

namespace TheKrystalShip.MovieBot.Ingest.Markers;

/// <summary>
/// Reads skippable stretches out of the chapters a container already carries.
///
/// A release that names its title sequence or its recap has said exactly where each begins and ends,
/// so nothing is measured for it. The whole of a chapter's title has to name the thing: a chapter
/// called "Opening Heist" or "Chapter 3" is an act of the episode, and skipping it skips the story.
/// </summary>
public static partial class ChapterMarkers
{
    /// <summary>
    /// The longest a chapter can be and still be a title sequence. A chapter called "Opening" that
    /// runs for ten minutes is the first act.
    /// </summary>
    public const double MaxIntroSeconds = 150;

    /// <summary>The longest a chapter can be and still be a recap.</summary>
    public const double MaxRecapSeconds = 300;

    /// <summary>The shortest worth a button: a chapter shorter than this is a marker on a cut.</summary>
    public const double MinSeconds = 5;

    /// <summary>
    /// The markers the chapters name, each running from its chapter's start to the next chapter's
    /// start, or to the end of the episode for the last.
    /// </summary>
    public static IReadOnlyList<Marker> From(IReadOnlyList<Chapter> chapters, double durationSeconds)
    {
        var markers = new List<Marker>();

        for (var i = 0; i < chapters.Count; i++)
        {
            if (Classify(chapters[i].Title) is not { } kind) continue;

            var start = chapters[i].StartSeconds;
            var end = i + 1 < chapters.Count ? chapters[i + 1].StartSeconds : durationSeconds;
            var length = end - start;
            var cap = kind == MarkerKind.Intro ? MaxIntroSeconds : MaxRecapSeconds;

            if (length < MinSeconds || length > cap) continue;

            // An episode has one title sequence and one recap; a second chapter of a kind is the
            // first one's own parts.
            if (markers.Any(m => m.Kind == kind)) continue;

            markers.Add(new Marker
            {
                Kind = kind,
                StartSeconds = start,
                EndSeconds = end,
                From = MarkerSource.Chapter
            });
        }

        return markers;
    }

    /// <summary>What a chapter's title says the chapter is, or null when it names neither.</summary>
    public static MarkerKind? Classify(string? title)
    {
        var name = Normalize(title);
        if (name.Length == 0) return null;

        if (IntroTitle().IsMatch(name)) return MarkerKind.Intro;
        if (RecapTitle().IsMatch(name)) return MarkerKind.Recap;
        return null;
    }

    /// <summary>
    /// Lower case words separated by single spaces, with the punctuation, the brackets and a
    /// trailing number ("Intro 2", "OP1") taken away, so each spelling of a name reads the same.
    /// </summary>
    private static string Normalize(string? title)
    {
        if (string.IsNullOrWhiteSpace(title)) return "";

        var builder = new StringBuilder(title.Length);
        foreach (var character in title)
            builder.Append(char.IsLetterOrDigit(character) ? char.ToLowerInvariant(character) : ' ');

        return TrailingNumber().Replace(Spaces().Replace(builder.ToString(), " ").Trim(), "");
    }

    [GeneratedRegex(@"^(intro|introduction|opening|opening (credits|titles|title|sequence|theme)|main titles?|title sequence|title credits|op)$")]
    private static partial Regex IntroTitle();

    [GeneratedRegex(@"^(recap|previously( on.*)?|the story so far|last time( on.*)?)$")]
    private static partial Regex RecapTitle();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Spaces();

    [GeneratedRegex(@"\s*\d+$")]
    private static partial Regex TrailingNumber();
}
