using System.Collections.Concurrent;
using System.Text.Json.Serialization;
using TheKrystalShip.MovieBot.Api.Data;
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
/// A title's category: the one somebody set for it, else the rule read from its description.
///
/// The rule is that a title the film database files under Animation and that was made in Japanese is
/// anime. Both facts are on every description and on every title a catalogue answer lists, so the
/// same rule shelves the library and sorts a discovery. The database's own <c>anime</c> keyword is
/// not the rule: it is missing from most of the titles it describes. A title nothing describes yet
/// has no category, which a listing shelves with its kind.
///
/// Where the rule is wrong for a title (an anime made in English, an animation from elsewhere that
/// people shelve with anime) somebody who fetches titles sets its category, kept by IMDb id, so a
/// show's every episode moves with it. A setting that agrees with the rule is not kept, so the title
/// follows its description again. What is set is held in memory beside the database, since every
/// listing asks it of every title.
/// </summary>
public sealed class TitleCategories
{
    public const string AnimeGenre = "Animation";
    public const string AnimeLanguage = "ja";

    private readonly CinemaDatabase _database;
    private readonly FilmStore _films;
    private readonly ShowStore _shows;
    private readonly ConcurrentDictionary<string, TitleCategory> _set;

    public TitleCategories(CinemaDatabase database, FilmStore films, ShowStore shows)
    {
        _database = database;
        _films = films;
        _shows = shows;
        _set = new ConcurrentDictionary<string, TitleCategory>(
            database.Read(c => c.Query("SELECT imdb_id, category FROM title_categories",
                r => KeyValuePair.Create(r.GetString(0), Parse(r.GetString(1)) ?? TitleCategory.General))),
            StringComparer.Ordinal);
    }

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
    /// A title's category by its kind (<c>film</c> or <c>show</c>) and IMDb id: the one set for it, else
    /// the rule over its kept description. Null when neither says.
    /// </summary>
    public TitleCategory? Of(string kind, string? imdbId) => Of(kind, imdbId, null, null);

    /// <summary>
    /// A title's category where a catalogue answer carries its <paramref name="genres"/> and
    /// <paramref name="originalLanguage"/>: the one set for it, else the rule over its kept
    /// description, else the rule over what the answer carries.
    /// </summary>
    public TitleCategory? Of(string kind, string? imdbId, IEnumerable<string>? genres, string? originalLanguage)
    {
        if (imdbId is not null && _set.TryGetValue(imdbId, out var set)) return set;
        return Described(kind, imdbId) ?? ByRule(genres, originalLanguage);
    }

    /// <summary>Whether a title's category is one somebody set rather than the rule's.</summary>
    public bool IsSet(string imdbId) => _set.ContainsKey(imdbId);

    /// <summary>
    /// Shelves a title in <paramref name="category"/>. Kept only where the rule over its description
    /// says otherwise or cannot say; answers the title's category as it now stands.
    /// </summary>
    public TitleCategory Set(string kind, string imdbId, TitleCategory category)
    {
        _database.Write(c =>
        {
            if (Described(kind, imdbId) == category)
            {
                c.Execute("DELETE FROM title_categories WHERE imdb_id = $imdb", ("$imdb", imdbId));
                _set.TryRemove(imdbId, out _);
            }
            else
            {
                c.Execute("INSERT OR REPLACE INTO title_categories (imdb_id, category) VALUES ($imdb, $category)",
                    ("$imdb", imdbId), ("$category", Spelling(category)));
                _set[imdbId] = category;
            }
        });
        return category;
    }

    /// <summary>A category as the wire spells it.</summary>
    public static string Spelling(TitleCategory category) => category == TitleCategory.Anime ? "anime" : "general";

    private TitleCategory? Described(string kind, string? imdbId)
    {
        if (imdbId is null) return null;
        if (kind == "show")
            return _shows.Held(imdbId)?.Details is { } show ? ByRule(show.Genres, show.OriginalLanguage) : null;
        return _films.Held(imdbId)?.Details is { } film ? ByRule(film.Genres, film.OriginalLanguage) : null;
    }
}

/// <summary>What <c>PUT /api/categories/{imdbId}</c> takes.</summary>
public sealed record CategoryRequest
{
    /// <summary><c>film</c> or <c>show</c>.</summary>
    public string? Kind { get; init; }

    /// <summary><c>anime</c> or <c>general</c>.</summary>
    public string? Category { get; init; }
}

/// <summary>A title's category, and whether somebody set it rather than the rule.</summary>
public sealed record CategoryView(string ImdbId, TitleCategory Category, bool Set);
