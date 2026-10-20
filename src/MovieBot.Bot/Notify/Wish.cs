using TheKrystalShip.MovieBot.Core;

namespace TheKrystalShip.MovieBot.Bot.Notify;

/// <summary>
/// A film somebody wants that cannot be downloaded yet, as the API holds it. Keyed by the film: two
/// people wanting the same film share one wish.
/// </summary>
public sealed record Wish
{
    /// <summary>The canonical <c>tt0458352</c> form.</summary>
    public required string ImdbId { get; init; }

    public required string Title { get; init; }

    public int? Year { get; init; }

    public string? PosterUrl { get; init; }

    /// <summary>How many other people visibly want the film too.</summary>
    public int Others { get; init; }

    /// <summary>The one spelling of the name and the year together.</summary>
    public string Display => Year is { } year ? $"{Title} ({year})" : Title;

    public string Url => $"https://www.imdb.com/title/{ImdbId}/";

    /// <summary>
    /// The wish as the API answered it. Its poster is served from the API at an address relative to
    /// it, and Discord fetches an embed's image from its own servers, so it is offered at the API's
    /// public address and left off when there is none.
    /// </summary>
    public static Wish From(WantView view, string? publicBaseUrl) => new()
    {
        ImdbId = view.ImdbId,
        Title = view.Title,
        Year = view.Year,
        PosterUrl = view.PosterUrl switch
        {
            ['/', ..] url => publicBaseUrl is { Length: > 0 } root ? root.TrimEnd('/') + url : null,
            { } url when Uri.TryCreate(url, UriKind.Absolute, out var absolute) && absolute.Scheme is "https" or "http" => url,
            _ => null,
        },
        Others = view.Others,
    };
}
