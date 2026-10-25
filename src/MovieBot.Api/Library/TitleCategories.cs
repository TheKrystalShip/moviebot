using System.Text.Json.Serialization;

namespace TheKrystalShip.MovieBot.Api.Library;

/// <summary>
/// Which part of the site a title is shelved in, across both kinds: anime, films and shows alike, or
/// everything else. A title's kind says how it is built and its category says where people look for
/// it, so an anime film is a film on the Anime shelf. It carries its own converter, so every reader of
/// the wire, a test's or a client's, spells it as the API does.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<TitleCategory>))]
public enum TitleCategory
{
    [JsonStringEnumMemberName("general")] General,
    [JsonStringEnumMemberName("anime")] Anime
}

/// <summary>
/// A title's category: the catalogue's, which files a title made in Japanese under Animation as anime.
/// A title the catalogue does not list has none, which a listing shelves with its kind.
/// </summary>
public static class TitleCategories
{
    /// <summary>The category of a title by its card; null with no card.</summary>
    public static TitleCategory? Of(TheKrystalShip.MovieBot.Catalogue.TitleCard? card) =>
        card is null ? null : card.Category == "anime" ? TitleCategory.Anime : TitleCategory.General;

    /// <summary>Reads a category as the wire spells it; null for anything else.</summary>
    public static TitleCategory? Parse(string? category) => category?.Trim().ToLowerInvariant() switch
    {
        "anime" => TitleCategory.Anime,
        "general" => TitleCategory.General,
        _ => null,
    };
}
