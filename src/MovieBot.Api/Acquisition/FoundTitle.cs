using TheKrystalShip.MovieBot.Acquire.Imdb;

namespace TheKrystalShip.MovieBot.Api.Acquisition;

/// <summary>
/// A film or show the title index offers for a name, as a search result shows it: enough to tell it
/// apart from everything else called the same — its poster, its year and who is in it.
/// <see cref="Kind"/> is <c>film</c> or <c>show</c>.
/// </summary>
public sealed record FoundTitle
{
    /// <summary>The width posters are asked for at: a result's card, on a screen of twice the density.</summary>
    internal const int PosterWidth = 500;

    public required string ImdbId { get; init; }

    public required string Title { get; init; }

    public int? Year { get; init; }

    public required string Display { get; init; }

    public required string Kind { get; init; }

    /// <summary>Top-billed cast as one line, absent when the index names nobody.</summary>
    public string? Starring { get; init; }

    /// <summary>The poster on the index's own image host, absent when it holds none.</summary>
    public string? PosterUrl { get; init; }

    public static FoundTitle From(ImdbTitle title) => new()
    {
        ImdbId = title.ImdbId,
        Title = title.Title,
        Year = title.Year,
        Display = title.Display,
        Kind = title.IsSeries ? "show" : "film",
        Starring = title.Starring,
        PosterUrl = title.PosterAt(PosterWidth)?.ToString(),
    };
}
