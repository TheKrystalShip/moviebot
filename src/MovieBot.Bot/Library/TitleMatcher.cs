namespace TheKrystalShip.MovieBot.Bot.Library;

using TheKrystalShip.MovieBot.Bot.Api;

public enum TitleMatchKind
{
    /// <summary>Exactly one title answers to what was typed.</summary>
    Resolved,

    /// <summary>Nothing answers to it.</summary>
    NotFound,

    /// <summary>Several do, and picking one for the room would be a guess.</summary>
    Ambiguous
}

/// <summary>
/// What a query resolved to. <see cref="Candidates"/> carries the titles worth showing back:
/// the shortlist when the query was ambiguous, and what the library holds when it matched
/// nothing.
/// </summary>
public sealed record TitleMatch(
    TitleMatchKind Kind,
    LibraryTitle? Title,
    IReadOnlyList<LibraryTitle> Candidates)
{
    public static TitleMatch Resolved(LibraryTitle title) => new(TitleMatchKind.Resolved, title, []);
}

/// <summary>
/// Turns what somebody typed into a title, or into an answer about why it could not.
///
/// Kept free of Discord and of HTTP so the rules are exercised directly: this is where a movie
/// night goes wrong in the way people notice, and a wrong film is worse than a refusal.
/// </summary>
public static class TitleMatcher
{
    /// <summary>Discord accepts at most this many autocomplete choices in one response.</summary>
    public const int MaxChoices = 25;

    public static TitleMatch Resolve(IReadOnlyList<LibraryTitle> library, string query)
    {
        var needle = Normalize(query);
        if (needle.Length == 0 || library.Count == 0)
            return new TitleMatch(TitleMatchKind.NotFound, null, Shortlist(library));

        // An id is what the launch link carries and what a repeat request is likely to be
        // pasted from, so it wins outright over anything the title text happens to contain.
        if (library.FirstOrDefault(t => t.Id.Equals(query.Trim(), StringComparison.OrdinalIgnoreCase))
            is { } byId)
            return TitleMatch.Resolved(byId);

        if (library.FirstOrDefault(t => Normalize(t.Name) == needle || Normalize(t.Title) == needle)
            is { } byTitle)
            return TitleMatch.Resolved(byTitle);

        var matches = Search(library, needle);
        if (matches.Count == 1) return TitleMatch.Resolved(matches[0]);
        if (matches.Count == 0) return new TitleMatch(TitleMatchKind.NotFound, null, Shortlist(library));

        // "gladiator" against a director's cut and an extended cut is a real ambiguity; the same
        // word against one film whose title merely begins with it is not.
        var leading = matches
            .Where(t => Normalize(t.Name).StartsWith(needle, StringComparison.Ordinal)
                        || Normalize(t.Title).StartsWith(needle, StringComparison.Ordinal))
            .ToList();
        if (leading.Count == 1) return TitleMatch.Resolved(leading[0]);

        return new TitleMatch(TitleMatchKind.Ambiguous, null, Shortlist(matches));
    }

    /// <summary>
    /// The choices offered while somebody is still typing. An empty query offers the library, so
    /// the menu opens with something in it rather than waiting for a first keystroke.
    /// </summary>
    public static IReadOnlyList<LibraryTitle> Suggest(IReadOnlyList<LibraryTitle> library, string query)
    {
        var needle = Normalize(query);
        var matches = needle.Length == 0 ? library : Search(library, needle);
        return [.. matches.Take(MaxChoices)];
    }

    /// <summary>
    /// Every word has to begin a word in the id or the title. Matching words in any order is
    /// what catches "gladiator extended" against "Gladiator 2000 Extended Cut", which is how a
    /// person names a film they have watched once; requiring a word boundary is what stops
    /// "heat" from also meaning "Gladiator 2000 Theatrical Cut" and refusing both as ambiguous.
    /// </summary>
    private static List<LibraryTitle> Search(IReadOnlyList<LibraryTitle> library, string needle)
    {
        var words = needle.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return [.. library.Where(t =>
        {
            // Both names, because either is a reasonable thing for somebody to type: the
            // catalogue's, and whatever the release called it before the catalogue was asked.
            var haystack = Normalize($"{t.Id} {t.Title} {t.Name}");
            return words.All(w => StartsAWord(haystack, w));
        })];
    }

    /// <summary>
    /// Whether the haystack carries the word at the start of one of its own, so a partly typed
    /// word still matches the word it is the start of.
    /// </summary>
    private static bool StartsAWord(string haystack, string word)
    {
        for (var index = haystack.IndexOf(word, StringComparison.Ordinal);
             index >= 0;
             index = haystack.IndexOf(word, index + 1, StringComparison.Ordinal))
        {
            if (index == 0 || !char.IsLetterOrDigit(haystack[index - 1])) return true;
        }

        return false;
    }

    private static IReadOnlyList<LibraryTitle> Shortlist(IReadOnlyList<LibraryTitle> titles) =>
        [.. titles.Take(MaxChoices)];

    private static string Normalize(string value) =>
        string.Join(' ', value.ToLowerInvariant().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
}
