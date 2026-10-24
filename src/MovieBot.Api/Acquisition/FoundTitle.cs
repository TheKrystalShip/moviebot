using TheKrystalShip.MovieBot.Acquire.Imdb;
using TheKrystalShip.MovieBot.Acquire.Tmdb;

namespace TheKrystalShip.MovieBot.Api.Acquisition;

/// <summary>
/// A film or show the title index offers for a name, as a search result shows it: enough to tell it
/// apart from everything else called the same — its poster, its year and who is in it — from the
/// index, and its rating and what it is about from the film database.
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

    /// <summary>
    /// The poster the title's page shows, served from here; the index's own, on its image host, for a
    /// title the film database has no poster for; absent when neither has one.
    /// </summary>
    public string? PosterUrl { get; init; }

    /// <summary>Out of ten, to one decimal; absent when nobody has voted or the database has not answered.</summary>
    public double? Rating { get; init; }

    public int Votes { get; init; }
    /// <summary>Whose <see cref="Rating"/> is, <c>imdb</c> or <c>tmdb</c>; absent with no rating (<see cref="TitleScores.Serve"/>).</summary>
    public string? RatedBy { get; init; }

    public string? Overview { get; init; }

    /// <summary>The shelf it is on beyond its kind, <c>anime</c> or <c>general</c>; absent while nothing says.</summary>
    public Library.TitleCategory? Category { get; init; }

    public static FoundTitle From(ImdbTitle title, TitleGlance? glance = null, string? poster = null) => new()
    {
        ImdbId = title.ImdbId,
        Title = title.Title,
        Year = title.Year,
        Display = title.Display,
        Kind = title.IsSeries ? "show" : "film",
        Starring = title.Starring,
        PosterUrl = poster ?? title.PosterAt(PosterWidth)?.ToString(),
        Rating = glance?.Rating,
        Votes = glance?.Votes ?? 0,
        Overview = glance?.Overview,
    };
}
