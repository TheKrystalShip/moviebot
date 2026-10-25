using System.Text.Json;
using System.Text.Json.Serialization;
using TheKrystalShip.MovieBot.Assistant.Api;

namespace TheKrystalShip.MovieBot.Assistant.Tools;

/// <summary>One title a card offers: enough for a poster, a name and a link to its page.</summary>
/// <param name="ImdbId">The title's page is at this id.</param>
/// <param name="PosterUrl">The API's address for the poster, relative to the site that serves it; absent when the database has none.</param>
public sealed record TitleCard(string ImdbId, string Kind, string Title, int? Year, string? PosterUrl, bool InLibrary, bool Watched);

/// <summary>The card a result carries: the titles it names, which the site draws as cards linking to each title's page.</summary>
public sealed record TitlesCard(string Kind, IReadOnlyList<TitleCard> Titles);

/// <summary>The card types as JSON, in the camel case the site reads.</summary>
[JsonSourceGenerationOptions(JsonSerializerDefaults.Web)]
[JsonSerializable(typeof(TitlesCard))]
public sealed partial class CardJson : JsonSerializerContext;

/// <summary>
/// The structured result a tool returns beside its text. A title with no IMDb id has no page, so it is
/// never in a card and is named in words alone. A result with no title to show carries no card.
/// </summary>
internal static class TitleCards
{
    public const string Kind = "titles";

    public static TitleCard Of(CatalogueCard card) =>
        new(card.Id, card.Kind, card.Title, card.Year, card.Poster, card.InLibrary, card.Watched);

    public static TitlesCard? Of(IEnumerable<CatalogueCard> cards) => Of(cards.Select(Of));

    public static TitlesCard? Of(IEnumerable<TitleCard> cards)
    {
        TitleCard[] distinct = [.. cards.Where(c => c.ImdbId is { Length: > 0 }).DistinctBy(c => c.ImdbId)];
        return distinct.Length == 0 ? null : new TitlesCard(Kind, distinct);
    }
}
