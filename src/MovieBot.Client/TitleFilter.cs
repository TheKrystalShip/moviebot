using System.Globalization;
using System.Text;

namespace TheKrystalShip.MovieBot.Client;

/// <summary>
/// What one list of titles asks the catalogue for (<c>/api/catalogue/titles</c>, authority:
/// <c>docs/api-contract.md</c>). A filter left null or empty is not sent; the list filters
/// (<see cref="Genres"/>, <see cref="Keywords"/>, <see cref="People"/>) match all of their values,
/// <see cref="NotGenres"/> none of them.
/// </summary>
public sealed record TitleFilter
{
    public string? Words { get; init; }
    public string? Kind { get; init; }
    public string? Category { get; init; }
    public IReadOnlyList<string> Genres { get; init; } = [];
    public IReadOnlyList<string> NotGenres { get; init; } = [];
    public int? From { get; init; }
    public int? To { get; init; }

    /// <summary>Released on or after this day, <c>yyyy-MM-dd</c>.</summary>
    public string? Released { get; init; }

    /// <summary>Released before this day, <c>yyyy-MM-dd</c>.</summary>
    public string? ReleasedBefore { get; init; }

    /// <summary>ISO 639 codes.</summary>
    public IReadOnlyList<string> Languages { get; init; } = [];

    /// <summary>Keyword ids, as <c>k123</c>.</summary>
    public IReadOnlyList<string> Keywords { get; init; } = [];

    /// <summary>The catalogue's person ids.</summary>
    public IReadOnlyList<string> People { get; init; } = [];

    /// <summary><c>any</c>, <c>cast</c>, <c>directing</c> or <c>writing</c>: which of the people's credits count.</summary>
    public string? Role { get; init; }

    public double? MinRating { get; init; }
    public int? MinVotes { get; init; }
    public string? Ranking { get; init; }

    /// <summary>An IMDb id: the titles the film database counts as similar to it, in its order unless a sort is given.</summary>
    public string? SimilarTo { get; init; }

    public string? Sort { get; init; }
    public int? Page { get; init; }
    public int? Size { get; init; }

    /// <summary><c>ready</c>: only what the library holds ready to play, the newest added first.</summary>
    public string? Here { get; init; }

    /// <summary>The query parameters, each list filter repeated once a value.</summary>
    public IEnumerable<(string Name, object? Value)> Parameters()
    {
        yield return ("q", Words);
        yield return ("kind", Kind);
        yield return ("category", Category);
        foreach (string genre in Genres) yield return ("genre", genre);
        foreach (string genre in NotGenres) yield return ("notGenre", genre);
        yield return ("from", From);
        yield return ("to", To);
        yield return ("released", Released);
        yield return ("releasedBefore", ReleasedBefore);
        foreach (string language in Languages) yield return ("language", language);
        foreach (string keyword in Keywords) yield return ("keyword", keyword);
        foreach (string person in People) yield return ("person", person);
        yield return ("role", Role);
        yield return ("minRating", MinRating);
        yield return ("minVotes", MinVotes);
        yield return ("ranking", Ranking);
        yield return ("similarTo", SimilarTo);
        yield return ("sort", Sort);
        yield return ("page", Page);
        yield return ("size", Size);
        yield return ("here", Here);
    }

    /// <summary>The query string for the parameters that have a value, with its <c>?</c>; empty when none has.</summary>
    public string QueryString()
    {
        var text = new StringBuilder();
        foreach (var (name, value) in Parameters())
        {
            if (value is null) continue;
            text.Append(text.Length == 0 ? '?' : '&').Append(Uri.EscapeDataString(name)).Append('=')
                .Append(Uri.EscapeDataString(Convert.ToString(value, CultureInfo.InvariantCulture)!));
        }

        return text.ToString();
    }
}
