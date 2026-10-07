namespace TheKrystalShip.MovieBot.Core;

/// <summary>
/// One spelling for every language tag. Containers write the same language three different ways,
/// and which one depends on who muxed it: one disc says <c>rum</c>, <c>fre</c> and <c>ger</c> where
/// another says <c>ron</c>, <c>fra</c> and <c>deu</c>, a third <c>ro</c>, <c>fr</c> and <c>de</c>,
/// and a person's preference and the film database say the two-letter code.
/// </summary>
public static class Languages
{
    /// <summary>
    /// Every spelling of one language, grouped, the canonical one first. The bibliographic and
    /// terminological codes differ for twenty-odd languages and the two-letter code differs for nearly
    /// all of them.
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

    /// <summary>Whether two tags name the same language; an empty tag names none.</summary>
    public static bool Same(string? a, string? b)
    {
        var left = Normalise(a);
        return left.Length > 0 && left == Normalise(b);
    }
}
