using System.Text.Json.Serialization;
using TheKrystalShip.MovieBot.Api.Films;
using TheKrystalShip.MovieBot.Api.Shows;
using TheKrystalShip.MovieBot.Core;

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
/// A title's category, read from its description.
///
/// A title the film database files under Animation and that was made in Japanese is anime. Both facts
/// are on every description and on every title a catalogue answer lists, so the same rule shelves the
/// library and sorts a discovery. The database's own <c>anime</c> keyword is not the rule: it is
/// missing from most of the titles it describes. A title nothing describes yet has no category, which
/// a listing shelves with its kind.
/// </summary>
public sealed class TitleCategories(FilmStore films, ShowStore shows)
{
    public const string AnimeGenre = "Animation";
    public const string AnimeLanguage = "ja";

    /// <summary>
    /// What the rule says of a title by its genres and original language; null while its genres are
    /// not known, since a title not known to be animated is not known to be anything else either.
    /// </summary>
    public static TitleCategory? ByRule(IEnumerable<string>? genres, string? originalLanguage)
    {
        var named = genres?.ToList() ?? [];
        if (named.Count == 0) return null;
        return named.Contains(AnimeGenre, StringComparer.OrdinalIgnoreCase)
            && string.Equals(originalLanguage, AnimeLanguage, StringComparison.OrdinalIgnoreCase)
            ? TitleCategory.Anime
            : TitleCategory.General;
    }

    /// <summary>Reads a category as the wire spells it; null for anything else.</summary>
    public static TitleCategory? Parse(string? category) => category?.Trim().ToLowerInvariant() switch
    {
        "anime" => TitleCategory.Anime,
        "general" => TitleCategory.General,
        _ => null,
    };

    /// <summary>A library title's category, by its kind and the IMDb id it is catalogued under.</summary>
    public TitleCategory? Of(TitleKind kind, string? imdbId) => Of(kind == TitleKind.Show ? "show" : "film", imdbId);

    /// <summary>
    /// A title's category by its kind (<c>film</c> or <c>show</c>) and IMDb id: the rule over its kept
    /// description. Null while nothing describes it.
    /// </summary>
    public TitleCategory? Of(string kind, string? imdbId) => Of(kind, imdbId, null, null);

    /// <summary>
    /// A title's category where a catalogue answer carries its <paramref name="genres"/> and
    /// <paramref name="originalLanguage"/>: the rule over its kept description, else the rule over what
    /// the answer carries.
    /// </summary>
    public TitleCategory? Of(string kind, string? imdbId, IEnumerable<string>? genres, string? originalLanguage) =>
        Described(kind, imdbId) ?? ByRule(genres, originalLanguage);

    private TitleCategory? Described(string kind, string? imdbId)
    {
        if (imdbId is null) return null;
        if (kind == "show")
            return shows.Held(imdbId)?.Details is { } show ? ByRule(show.Genres, show.OriginalLanguage) : null;
        return films.Held(imdbId)?.Details is { } film ? ByRule(film.Genres, film.OriginalLanguage) : null;
    }
}
