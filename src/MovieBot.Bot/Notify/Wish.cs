using System.Globalization;
using TheKrystalShip.MovieBot.Core;

namespace TheKrystalShip.MovieBot.Bot.Notify;

/// <summary>One person waiting on a film, and the Discord channel they asked in, if they asked in one.</summary>
public sealed record WishSubscriber(ulong UserId, ulong? ChannelId);

/// <summary>
/// A film somebody wants that cannot be downloaded yet, and everybody waiting on it, as the API
/// holds it. Keyed by the film: two people asking for the same film share one wish and one
/// announcement in each channel they asked in.
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

    public static Wish From(WishView view) => new()
    {
        ImdbId = view.ImdbId,
        Title = view.Title,
        Year = view.Year,
        Starring = view.Starring,
        PosterUrl = view.PosterUrl,
        Subscribers = view.Subscribers
            .Select(s => new WishSubscriber(Snowflake(s.UserId) ?? 0, Snowflake(s.ChannelId)))
            .ToList(),
    };

    private static ulong? Snowflake(string? id) =>
        ulong.TryParse(id, NumberStyles.None, CultureInfo.InvariantCulture, out var value) ? value : null;
}
