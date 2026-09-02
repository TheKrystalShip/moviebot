using System.Text.Json.Serialization;

namespace TheKrystalShip.MovieBot.Bot.Notify;

/// <summary>One person waiting on a film, and where they asked to be told.</summary>
public sealed record WishSubscriber
{
    public required ulong UserId { get; init; }

    /// <summary>The channel the command was run in, which is where the announcement goes.</summary>
    public required ulong ChannelId { get; init; }

    public required DateTimeOffset AskedAt { get; init; }
}

/// <summary>
/// A film somebody wants that cannot be downloaded yet, and everybody waiting on it.
///
/// Keyed by the film rather than by the person: two people asking for the same film share one
/// wish, one tracker call per sweep, and one announcement in each channel they asked in. What is
/// kept about the film is what the catalogue said when it was asked, so the announcement can name
/// it and show its poster without asking again.
/// </summary>
public sealed record Wish
{
    /// <summary>The canonical <c>tt0458352</c> form. It is the key and what the tracker is asked.</summary>
    public required string ImdbId { get; init; }

    public required string Title { get; init; }

    public int? Year { get; init; }

    public string? Starring { get; init; }

    public string? PosterUrl { get; init; }

    public IReadOnlyList<WishSubscriber> Subscribers { get; init; } = [];

    /// <summary>The one spelling of the name and the year together.</summary>
    [JsonIgnore]
    public string Display => Year is { } year ? $"{Title} ({year})" : Title;

    [JsonIgnore]
    public string Url => $"https://www.imdb.com/title/{ImdbId}/";
}

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    WriteIndented = true)]
[JsonSerializable(typeof(List<Wish>))]
internal partial class WishListJson : JsonSerializerContext;
