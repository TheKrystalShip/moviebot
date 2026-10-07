using System.Text.RegularExpressions;
using TheKrystalShip.MovieBot.Acquire.Search;
using TheKrystalShip.MovieBot.Core;

namespace TheKrystalShip.MovieBot.Api.Acquisition;

/// <summary>
/// Which audio a release says it carries, read from its name: the only description of it there is
/// before it is downloaded. A release made from the original carries the original language without
/// saying so, so a name says something only about a dub: a token naming the language
/// (<c>Castellano</c>, <c>TRUEFRENCH</c>), or <c>MULTI</c> and <c>DUAL</c>, which name no language and
/// say there is more than the original.
/// </summary>
public static partial class ReleaseLanguages
{
    /// <summary>How a release name advertises a language.</summary>
    public enum Advertised
    {
        None,

        /// <summary><c>MULTI</c> or <c>DUAL</c>: more than one audio language, not saying which.</summary>
        Several,

        /// <summary>A token naming the language.</summary>
        Named,
    }

    /// <summary>The tokens a release names a language's dub with, by the language's canonical spelling.</summary>
    private static readonly Dictionary<string, string[]> Tokens = new()
    {
        ["eng"] = ["ENGLISH", "ENG"],
        ["spa"] = ["SPANISH", "SPA", "ESP", "CASTELLANO", "LATINO", "LAT"],
        ["fre"] = ["FRENCH", "TRUEFRENCH", "VFF", "VFQ", "VFI", "VF2", "VF"],
        ["ger"] = ["GERMAN", "GER", "DEU"],
        ["ita"] = ["ITALIAN", "ITA"],
        ["por"] = ["PORTUGUESE", "POR", "PTBR", "PT-BR", "DUBLADO"],
        ["cat"] = ["CATALAN", "CAT", "CATALA"],
        ["dut"] = ["DUTCH", "NL", "FLEMISH"],
        ["pol"] = ["POLISH", "PL", "PLDUB", "DUBBINGPL"],
        ["rus"] = ["RUSSIAN", "RUS"],
        ["jpn"] = ["JAPANESE", "JPN", "JAP"],
        ["kor"] = ["KOREAN", "KOR"],
        ["chi"] = ["CHINESE", "CHI", "MANDARIN", "CANTONESE"],
        ["ara"] = ["ARABIC", "ARA"],
        ["tur"] = ["TURKISH", "TUR"],
    };

    [GeneratedRegex(@"[A-Za-z0-9\-]+", RegexOptions.CultureInvariant)]
    private static partial Regex Word();

    /// <summary>How <paramref name="releaseName"/> advertises <paramref name="language"/>.</summary>
    public static Advertised Of(string releaseName, string language)
    {
        var words = Word().Matches(releaseName).Select(m => m.Value.ToUpperInvariant()).ToHashSet();
        if (Tokens.TryGetValue(Languages.Normalise(language), out var named) && named.Any(words.Contains))
            return Advertised.Named;
        return words.Contains("MULTI") || words.Contains("DUAL") ? Advertised.Several : Advertised.None;
    }

    /// <summary>
    /// The first of <paramref name="ranked"/> naming <paramref name="language"/>, else the first that
    /// carries several; null when none says anything of it. The order is the ranker's, so the best
    /// release among those saying the same wins.
    /// </summary>
    public static Release? Preferring(IEnumerable<Release> ranked, string language)
    {
        var list = ranked as IReadOnlyList<Release> ?? ranked.ToList();
        return list.FirstOrDefault(r => Of(r.ReleaseName, language) == Advertised.Named)
            ?? list.FirstOrDefault(r => Of(r.ReleaseName, language) == Advertised.Several);
    }

    /// <summary>
    /// The language a release is worth preferring for: the one asked for, unless it is the title's own,
    /// which every release made from the original carries. Null when there is nothing to prefer.
    /// </summary>
    public static string? WorthPreferring(string? asked, string? original) =>
        string.IsNullOrWhiteSpace(asked) || Languages.Same(asked, original) ? null : asked;
}
