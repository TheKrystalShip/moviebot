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

    public static string Rating(double? rating, int votes) =>
        rating is { } r ? $"Rated {r.ToString("0.0", Invariant)} on {Count(votes)} votes." : "";

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
    public static string Line(TitleHit hit, bool withDate = false)
    {
        var text = new StringBuilder("- ").Append(Named(hit.Title, hit.Year)).Append(", ").Append(KindWord(hit.Kind, hit.Category)).Append('.');
        if (withDate && hit.ReleaseDate is { Length: > 0 } date)
            text.Append(" Release date ").Append(date).Append('.');
        if (hit.MadeBy.Count > 0)
            text.Append(hit.Kind == "show" ? " Created by " : " Directed by ").Append(string.Join(", ", hit.MadeBy)).Append('.');
        if (hit.Starring.Count > 0)
            text.Append(" With ").Append(string.Join(", ", hit.Starring)).Append('.');
        if (hit.Genres.Count > 0)
            text.Append(' ').Append(string.Join(", ", hit.Genres)).Append('.');
        if (hit.Rating is not null)
            text.Append(' ').Append(Rating(hit.Rating, hit.Votes));
        text.Append(' ').Append(Library(hit.InLibrary, hit.Watched, hit.Kind));
        text.Append(hit.ImdbId is { Length: > 0 } id ? $" id {id}." : " No IMDb id: it has no page here.");
        if (hit.Plot is { Length: > 0 } plot)
            text.Append("\n  ").Append(plot);
        return text.ToString();
    }

    public static string Lines(IEnumerable<TitleHit> hits, bool withDate = false) =>
        string.Join('\n', hits.Select(h => Line(h, withDate)));

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

    /// <summary>The words of a status the API gave a credit, as the model should say them.</summary>
    public static string Status(string status) => status switch
    {
        "released" => "released",
        "upcoming" => "upcoming (dated in the future)",
        "announced" => "announced (no release date, or only planned)",
        _ => status,
    };
}
