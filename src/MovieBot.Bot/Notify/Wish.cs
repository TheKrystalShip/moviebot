using TheKrystalShip.MovieBot.Core;

namespace TheKrystalShip.MovieBot.Bot.Notify;

/// <summary>One person waiting on a film, by the account the API knows them by.</summary>
public sealed record WishSubscriber(string UserId);

/// <summary>
/// A film somebody wants that cannot be downloaded yet, and everybody waiting on it, as the API
/// holds it. Keyed by the film: two people asking for the same film share one wish.
/// </summary>
public sealed record Wish
{
    /// <summary>The canonical <c>tt0458352</c> form.</summary>
    public required string ImdbId { get; init; }

    public required string Title { get; init; }

    public int? Year { get; init; }

    public string? Starring { get; init; }

    public string? PosterUrl { get; init; }

    public IReadOnlyList<WishSubscriber> Subscribers { get; init; } = [];

    /// <summary>The one spelling of the name and the year together.</summary>
    public string Display => Year is { } year ? $"{Title} ({year})" : Title;

    public string Url => $"https://www.imdb.com/title/{ImdbId}/";

    /// <summary>
    /// The wish as the API answered it. Its poster is served from the API at an address relative to
    /// it, and Discord fetches an embed's image from its own servers, so it is offered at the API's
    /// public address and left off when there is none.
    /// </summary>
    public static Wish From(WishView view, string? publicBaseUrl) => new()
    {
        ImdbId = view.ImdbId,
        Title = view.Title,
        Year = view.Year,
        Starring = view.Starring,
        PosterUrl = view.PosterUrl switch
        {
            ['/', ..] url => publicBaseUrl is { Length: > 0 } root ? root.TrimEnd('/') + url : null,
            { } url when Uri.TryCreate(url, UriKind.Absolute, out var absolute) && absolute.Scheme is "https" or "http" => url,
            _ => null,
        },
        Subscribers = view.Subscribers.Select(s => new WishSubscriber(s.UserId)).ToList(),
    };
}
