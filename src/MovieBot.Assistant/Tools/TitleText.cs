using System.Globalization;
using System.Text;
using TheKrystalShip.MovieBot.Assistant.Api;

namespace TheKrystalShip.MovieBot.Assistant.Tools;

/// <summary>
/// How the tools write a title for the model to read: one title to a line, every figure as the API gave
/// it and nothing it did not. A value the API left out is not written, so the model has no blank to fill.
/// </summary>
internal static class TitleText
{
    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

    public static string Count(int n) => n.ToString("N0", Invariant);

    /// <summary>"Rated 7.5 on 61,000 votes at IMDb.", or nothing when the card shows no rating.</summary>
    public static string Rating(CardRating? rating) =>
        rating is { Value: { } r } ? $"Rated {r.ToString("0.0", Invariant)} on {Count(rating.Votes)} votes at {Source(rating.By)}." : "";

    private static string Source(string by) => by switch
    {
        "imdb" => "IMDb",
        "tmdb" => "TMDB",
        _ => by,
    };

    public static string KindWord(string kind) => kind == "show" ? "show" : "film";

    /// <summary>"film", or "anime film" for a title the site shelves as anime.</summary>
    public static string KindWord(string kind, string? category) =>
        category == "anime" ? "anime " + KindWord(kind) : KindWord(kind);

    /// <summary>"Heat (1995)", or "Heat" when the database dates it no year.</summary>
    public static string Named(string title, int? year) => year is { } y ? $"{title} ({y})" : title;

    public static string Library(bool inLibrary, bool watched, string kind)
    {
        var text = inLibrary ? "In the library." : "Not in the library.";
        return watched && kind != "show" ? text + " The person has watched it." : text;
    }

    /// <summary>The line a search, a discovery or a list of the week gives a title.</summary>
    public static string Line(CatalogueCard card, bool withDate = false)
    {
        var text = new StringBuilder("- ").Append(Named(card.Title, card.Year)).Append(", ").Append(KindWord(card.Kind, card.Category)).Append('.');
        if (withDate && card.Released is { Length: > 0 } date)
            text.Append(" Release date ").Append(date).Append('.');
        if (card.MadeBy.Count > 0)
            text.Append(card.Kind == "show" ? " Created by " : " Directed by ").Append(string.Join(", ", card.MadeBy)).Append('.');
        if (card.Starring.Count > 0)
            text.Append(" With ").Append(string.Join(", ", card.Starring)).Append('.');
        if (card.Genres.Count > 0)
            text.Append(' ').Append(string.Join(", ", card.Genres)).Append('.');
        if (Rating(card.Rating) is { Length: > 0 } rated)
            text.Append(' ').Append(rated);
        text.Append(' ').Append(Library(card.InLibrary, card.Watched, card.Kind));
        text.Append(" id ").Append(card.Id).Append('.');
        if (card.Plot is { Length: > 0 } plot)
            text.Append("\n  ").Append(plot);
        return text.ToString();
    }

    public static string Lines(IEnumerable<CatalogueCard> cards, bool withDate = false) =>
        string.Join('\n', cards.Select(c => Line(c, withDate)));

    /// <summary>The name a title is matched by: letters and digits only, in lower case.</summary>
    public static string Normal(string title)
    {
        var text = new StringBuilder();
        foreach (char c in title.Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark) continue;
            if (char.IsLetterOrDigit(c)) text.Append(char.ToLowerInvariant(c));
        }

        return text.ToString();
    }

    /// <summary>
    /// Whether a title is out on <paramref name="today"/>: <c>released</c> on or before it, <c>upcoming</c>
    /// when dated after it, and <c>announced</c> when it has no date. A title dated only by a year before
    /// this one is released; one dated only by this year or a later one is announced, since its day is not
    /// known.
    /// </summary>
    public static string Status(CatalogueCard card, DateOnly today)
    {
        if (DateOnly.TryParseExact(card.Released, "yyyy-MM-dd", Invariant, DateTimeStyles.None, out DateOnly day))
            return day > today ? "upcoming" : "released";
        return card.Year is { } year && year < today.Year ? "released" : "announced";
    }

    /// <summary>The words of a status, as the model should say them.</summary>
    public static string Status(string status) => status switch
    {
        "released" => "released",
        "upcoming" => "upcoming (dated in the future)",
        "announced" => "announced (no release date, or only planned)",
        _ => status,
    };
}
