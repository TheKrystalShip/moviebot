using System.Text.RegularExpressions;
using TheKrystalShip.MovieBot.Acquire.Search;
using TheKrystalShip.MovieBot.Core;

namespace TheKrystalShip.MovieBot.Api.Acquisition;

/// <summary>
/// Which audio a release says it carries, read from its name: the only description of it there is
/// before it is downloaded. A release made from the original carries the original language without
/// saying so, so a name says something only about a dub: a token naming the language
/// (<c>Castellano</c>, <c>TRUEFRENCH</c>), or <c>MULTI</c> and <c>DUAL</c>, which name no language and
/// say there is more than the original. A release naming a dub in a language other than the title's
/// own ranks below one that names none, since its default audio is usually that dub.
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

    /// <summary>The words that say a language token before them names subtitles rather than audio.</summary>
    private static readonly HashSet<string> SubtitleWords = ["SUB", "SUBS", "SUBBED", "SUBT", "SUBTITLES"];

    [GeneratedRegex(@"[A-Za-z0-9\-]+", RegexOptions.CultureInvariant)]
    private static partial Regex Word();

    /// <summary>How <paramref name="releaseName"/> advertises <paramref name="language"/>.</summary>
    public static Advertised Of(string releaseName, string language) => Of(ReleaseParser.ParseName(releaseName), language);

    /// <summary>How <paramref name="release"/> advertises <paramref name="language"/>.</summary>
    public static Advertised Of(Release release, string language)
    {
        var words = AudioWords(release);
        if (Tokens.TryGetValue(Languages.Normalise(language), out var named) && named.Any(words.Contains))
            return Advertised.Named;
        return words.Contains("MULTI") || words.Contains("DUAL") ? Advertised.Several : Advertised.None;
    }

    /// <summary>
    /// Whether <paramref name="release"/> names a dub in a language other than <paramref name="original"/>,
    /// the title's own. Such a release is usually a foreign one whose default audio is the dub, with the
    /// original second or absent. An unknown original makes no release a dub of something else.
    /// </summary>
    public static bool NamesAnotherDub(Release release, string? original)
    {
        var own = Languages.Normalise(original);
        if (own.Length == 0) return false;
        var words = AudioWords(release);
        return Tokens.Any(t => t.Key != own && t.Value.Any(words.Contains));
    }

    /// <summary>
    /// <paramref name="ranked"/> with every release naming another language's dub after those that do
    /// not, the ranker's order kept within each.
    /// </summary>
    public static IEnumerable<Release> OriginalFirst(IEnumerable<Release> ranked, string? original) =>
        ranked.OrderBy(r => NamesAnotherDub(r, original));

    /// <summary>
    /// The words of a release's name that can say what audio it carries: those after its title, so a film
    /// called <c>The Cat</c> or <c>Ita</c> names no language, less any naming a subtitle
    /// (<c>ENG.SUBS</c>).
    /// </summary>
    private static HashSet<string> AudioWords(Release release)
    {
        var words = Word().Matches(release.ReleaseName).Select(m => m.Value.ToUpperInvariant()).ToList();
        var title = Word().Matches(release.Title).Select(m => m.Value.ToUpperInvariant()).ToList();
        var from = title.Count <= words.Count && words.Take(title.Count).SequenceEqual(title) ? title.Count : 0;
        var audio = new HashSet<string>();
        for (var i = from; i < words.Count; i++)
        {
            if (i + 1 < words.Count && SubtitleWords.Contains(words[i + 1])) continue;
            audio.Add(words[i]);
        }
        return audio;
    }

    /// <summary>
    /// The first of <paramref name="ranked"/> naming <paramref name="language"/>, else the first that
    /// carries several; null when none says anything of it. The order is the ranker's, so the best
    /// release among those saying the same wins.
    /// </summary>
    public static Release? Preferring(IEnumerable<Release> ranked, string language)
    {
        var list = ranked as IReadOnlyList<Release> ?? ranked.ToList();
        return list.FirstOrDefault(r => Of(r, language) == Advertised.Named)
            ?? list.FirstOrDefault(r => Of(r, language) == Advertised.Several);
    }

    /// <summary>
    /// The release of a film to fetch from <paramref name="ranked"/>, best first: one naming the
    /// <paramref name="asked"/> language where it is worth preferring (<see cref="WorthPreferring"/>), at
    /// <paramref name="wanted"/> and then at any resolution; else the best at <paramref name="wanted"/>,
    /// else the best at any, a release naming another language's dub coming after every one at the same
    /// step that names none. Null when <paramref name="ranked"/> is empty.
    /// </summary>
    public static Release? Choose(IEnumerable<Release> ranked, Resolution wanted, string? asked, string? original)
    {
        var list = OriginalFirst(ranked, original).ToList();
        var language = WorthPreferring(asked, original);
        var dubbed = language is null
            ? null
            : Preferring(list.Where(r => r.Resolution == wanted), language) ?? Preferring(list, language);
        return dubbed ?? list.FirstOrDefault(r => r.Resolution == wanted) ?? list.FirstOrDefault();
    }

    /// <summary>
    /// The language a release is worth preferring for: the one asked for, unless it is the title's own,
    /// which every release made from the original carries. Null when there is nothing to prefer.
    /// </summary>
    public static string? WorthPreferring(string? asked, string? original) =>
        string.IsNullOrWhiteSpace(asked) || Languages.Same(asked, original) ? null : asked;
}
