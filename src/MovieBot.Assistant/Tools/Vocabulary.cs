using System.Text.RegularExpressions;
using TheKrystalShip.Agent.Plugin;
using TheKrystalShip.MovieBot.Assistant.Api;
using TheKrystalShip.MovieBot.Catalogue;
using TheKrystalShip.MovieBot.Client;

namespace TheKrystalShip.MovieBot.Assistant.Tools;

/// <summary>
/// The words a person says, turned into what the catalogue's list filters take: a genre into the IMDb
/// genre the catalogue holds, a language into its ISO code, a plot word into a keyword, a title into its
/// IMDb id. The genres and languages are the catalogue's own, read from its facets; a word that names
/// none of them is said back with what there is, never dropped.
/// </summary>
internal sealed partial class Vocabulary(Facets facets)
{
    /// <summary>The ways people say a genre the catalogue names otherwise, by <see cref="TitleText.Normal"/>.</summary>
    private static readonly Dictionary<string, string> Spoken = new(StringComparer.Ordinal)
    {
        ["sciencefiction"] = "Sci-Fi", ["sf"] = "Sci-Fi",
        ["romantic"] = "Romance",
        ["animated"] = "Animation", ["cartoon"] = "Animation", ["cartoons"] = "Animation", ["anime"] = "Animation",
        ["noir"] = "Film-Noir",
        ["kids"] = "Family", ["children"] = "Family", ["childrens"] = "Family",
    };

    public static async Task<Vocabulary> ReadAsync(CinemaApi.PersonApi database, CancellationToken ct) =>
        new(await database.FacetsAsync(ct));

    private IEnumerable<string> Genres => facets.Section("genre").Select(o => o.Value);

    /// <summary>The genre a word names, as the catalogue spells it; null when it names none.</summary>
    public string? Genre(string word)
    {
        string key = TitleText.Normal(word);
        string[] keys = [key, Spoken.GetValueOrDefault(key) is { } spoken ? TitleText.Normal(spoken) : key,
            key.EndsWith("ies", StringComparison.Ordinal) ? key[..^3] + "y" : key, key.TrimEnd('s')];
        return Genres.FirstOrDefault(g => keys.Contains(TitleText.Normal(g)));
    }

    /// <summary>The genres a list of words names; a word that names none is refused with the genres there are.</summary>
    public List<string> GenresOf(IEnumerable<string> words) =>
        [.. words.Select(w => Genre(w) ?? throw new ToolRefusal(
            $"The film database has no genre '{w}'. Its genres: {string.Join(", ", Genres)}."))];

    [GeneratedRegex("^[a-z]{2,3}$")]
    private static partial Regex LanguageCode();

    /// <summary>
    /// The original language a word names, as its code and its name: a language's name (Korean) or its
    /// code (ko). A word that names none is refused with the languages the catalogue holds most.
    /// </summary>
    public (string Code, string Name) Language(string word)
    {
        List<FacetOption> languages = facets.Section("language");
        string said = word.Trim();
        FacetOption? option = languages.FirstOrDefault(o => string.Equals(o.Label, said, StringComparison.OrdinalIgnoreCase))
            ?? languages.FirstOrDefault(o => string.Equals(o.Value, said, StringComparison.OrdinalIgnoreCase));
        if (option is not null) return (option.Value, option.Label ?? option.Value);
        if (LanguageCode().IsMatch(said)) return (said, said);
        throw new ToolRefusal(
            $"The film database has no original language '{word}'. Its languages: {string.Join(", ", languages.Select(o => o.Label ?? o.Value))}.");
    }

    /// <summary>The keyword words mean: the one named exactly that, else the one most titles carry; null when none matches.</summary>
    public static async Task<Term?> KeywordAsync(CinemaApi.PersonApi database, string words, CancellationToken ct)
    {
        List<Term> keywords = [.. (await database.TermsAsync(words, ct)).Where(t => t.Filter == "keyword")];
        return keywords.FirstOrDefault(k => string.Equals(k.Name, words.Trim(), StringComparison.OrdinalIgnoreCase)) ?? keywords.FirstOrDefault();
    }

    [GeneratedRegex(@"^(?<name>.*?)\s*\((?<year>\d{4})\)\s*$")]
    private static partial Regex NameWithYear();

    /// <summary>
    /// The title a name means, as "Heat" or "Heat (1995)": the catalogue's closest match by name, in the
    /// year where one is given. Null when there is none.
    /// </summary>
    public static async Task<TitleCard?> TitleAsync(CinemaApi.PersonApi database, string said, CancellationToken ct)
    {
        (string name, int? year) = (said, null);
        if (NameWithYear().Match(said) is { Success: true } m)
            (name, year) = (m.Groups["name"].Value, int.Parse(m.Groups["year"].Value));
        CardPage found = await database.TitlesAsync(new TitleFilter { Words = name, From = year, To = year, Size = 1 }, ct);
        return found.Titles.FirstOrDefault();
    }
}
