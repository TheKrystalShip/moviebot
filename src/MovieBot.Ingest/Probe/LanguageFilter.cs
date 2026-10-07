using TheKrystalShip.MovieBot.Core;

namespace TheKrystalShip.MovieBot.Ingest.Probe;

/// <summary>
/// Decides which subtitle languages are worth keeping.
///
/// A Blu-ray rip routinely carries thirty of them and a room reads one. The rest are not a
/// resource problem — they all come out of a single demux pass — they are a menu forty-eight rows
/// long in which the three rows anybody wants are impossible to find.
///
/// The matching is the part that has to be right, because getting it wrong fails silently: a
/// filter that keeps nothing produces a film with no subtitles and no error. Every spelling of a
/// language is matched (<see cref="Languages"/>).
/// </summary>
public static class LanguageFilter
{
    /// <summary>One agreed spelling for a language tag, or the tag itself when it is not known.</summary>
    public static string Normalise(string? language) => Languages.Normalise(language);

    /// <summary>
    /// Whether a track is worth extracting.
    ///
    /// An empty set of wanted languages keeps everything, which is what an ingest run by hand
    /// against an unknown film should do.
    ///
    /// A track with no language tag is always kept. Releases ship one untagged subtitle far more
    /// often than they should, and it is usually the one the room wants; dropping it on a filter
    /// leaves a film with no subtitles at all and nothing anywhere to say why.
    /// </summary>
    public static bool Wanted(string? language, IReadOnlyCollection<string> wanted)
    {
        if (wanted.Count == 0) return true;
        if (string.IsNullOrWhiteSpace(language)) return true;

        var tag = Normalise(language);
        return wanted.Any(w => Normalise(w) == tag);
    }
}
