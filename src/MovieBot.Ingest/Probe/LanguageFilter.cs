namespace TheKrystalShip.MovieBot.Ingest.Probe;

/// <summary>
/// Decides which subtitle languages are worth keeping.
///
/// A Blu-ray rip routinely carries thirty of them and a room reads one. The rest are not a
/// resource problem — they all come out of a single demux pass — they are a menu forty-eight rows
/// long in which the three rows anybody wants are impossible to find.
///
/// The matching is the part that has to be right, because getting it wrong fails silently: a
/// filter that keeps nothing produces a film with no subtitles and no error. Containers write the
/// same language three different ways, and which one depends on who muxed it: this disc says
/// <c>rum</c>, <c>fre</c> and <c>ger</c> where another says <c>ron</c>, <c>fra</c> and <c>deu</c>,
/// and a third says <c>ro</c>, <c>fr</c> and <c>de</c>.
/// </summary>
public static class LanguageFilter
{
    /// <summary>
    /// Every spelling of one language, grouped. The bibliographic and terminological codes differ
    /// for twenty-odd languages and the two-letter code differs for nearly all of them.
    /// </summary>
    private static readonly string[][] Equivalent =
    [
        ["eng", "en"], ["rum", "ron", "ro"], ["cze", "ces", "cs"], ["dut", "nld", "nl"],
        ["fre", "fra", "fr"], ["ger", "deu", "de"], ["gre", "ell", "el"], ["per", "fas", "fa"],
        ["ice", "isl", "is"], ["chi", "zho", "zh"], ["alb", "sqi", "sq"], ["arm", "hye", "hy"],
        ["baq", "eus", "eu"], ["bur", "mya", "my"], ["geo", "kat", "ka"], ["mac", "mkd", "mk"],
        ["mao", "mri", "mi"], ["may", "msa", "ms"], ["slo", "slk", "sk"], ["tib", "bod", "bo"],
        ["wel", "cym", "cy"], ["spa", "es"], ["por", "pt"], ["ita", "it"], ["rus", "ru"],
        ["jpn", "ja"], ["kor", "ko"], ["ara", "ar"], ["heb", "he"], ["hin", "hi"], ["pol", "pl"],
        ["swe", "sv"], ["nor", "no"], ["dan", "da"], ["fin", "fi"], ["hun", "hu"], ["tur", "tr"],
        ["tha", "th"], ["vie", "vi"], ["ind", "id"], ["bul", "bg"], ["hrv", "hr"], ["srp", "sr"],
        ["slv", "sl"], ["ukr", "uk"], ["est", "et"], ["lav", "lv"], ["lit", "lt"], ["cat", "ca"],
    ];

    private static readonly Dictionary<string, string> Canonical = Build();

    private static Dictionary<string, string> Build()
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var group in Equivalent)
        {
            foreach (var spelling in group) map[spelling] = group[0];
        }

        return map;
    }

    /// <summary>One agreed spelling for a language tag, or the tag itself when it is not known.</summary>
    public static string Normalise(string? language)
    {
        var tag = language?.Trim() ?? "";
        if (tag.Length == 0) return "";

        // A container may qualify the tag, as in "pt-BR". The region is not what is being matched.
        var dash = tag.IndexOfAny(['-', '_']);
        if (dash > 0) tag = tag[..dash];

        return Canonical.TryGetValue(tag, out var canonical) ? canonical : tag.ToLowerInvariant();
    }

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
