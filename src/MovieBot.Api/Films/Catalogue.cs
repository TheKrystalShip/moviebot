using System.Text.Json.Serialization;
using TheKrystalShip.MovieBot.Acquire.Tmdb;

namespace TheKrystalShip.MovieBot.Api.Films;

/// <summary>What is kept of one answer from the database, and when it was asked. A null value is the database having nothing under the id.</summary>
public sealed record KeptAnswer<T> where T : class
{
    public T? Value { get; init; }

    public required DateTimeOffset FetchedAt { get; init; }
}

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(PersonCredits))]
[JsonSerializable(typeof(List<CatalogueGenre>))]
[JsonSerializable(typeof(List<CatalogueTitle>))]
[JsonSerializable(typeof(List<ListedTitle>))]
[JsonSerializable(typeof(ListedPage))]
[JsonSerializable(typeof(List<PersonMatch>))]
[JsonSerializable(typeof(List<KeywordMatch>))]
[JsonSerializable(typeof(List<CompanyMatch>))]
[JsonSerializable(typeof(List<LanguageName>))]
[JsonSerializable(typeof(Filmography))]
[JsonSerializable(typeof(TitleCard))]
[JsonSerializable(typeof(TitleKey))]
[JsonSerializable(typeof(TitleTraits))]
[JsonSerializable(typeof(TitleCollection))]
internal partial class AnswerJson : JsonSerializerContext;

/// <summary>A film or a show as a browse of the catalogue lists it, by its poster.</summary>
public sealed record CatalogueTitleView
{
    public required string ImdbId { get; init; }

    /// <summary><c>film</c> or <c>show</c>.</summary>
    public required string Kind { get; init; }

    public required string Title { get; init; }
    public int? Year { get; init; }
    public double? Rating { get; init; }
    public int Votes { get; init; }
    /// <summary>Whose <see cref="Rating"/> is, <c>imdb</c> or <c>tmdb</c>; absent with no rating (<see cref="TitleScores.Serve"/>).</summary>
    public string? RatedBy { get; init; }

    public string? Overview { get; init; }

    /// <summary>The genres the database files it under, by name.</summary>
    public List<string> Genres { get; init; } = [];

    /// <summary>The language it was made in, an ISO 639-1 code.</summary>
    public string? OriginalLanguage { get; init; }

    /// <summary>The poster its page shows, served from here; absent when the database has none.</summary>
    public string? PosterUrl { get; init; }

    /// <summary>Among a person's credits, the part they played or the job they did.</summary>
    public string? Role { get; init; }

    /// <summary>The shelf it is on beyond its kind, <c>anime</c> or <c>general</c>; absent while nothing says.</summary>
    public Library.TitleCategory? Category { get; init; }
}

/// <summary>What <c>/api/catalogue/people/{personId}</c> answers.</summary>
public sealed record CataloguePersonView
{
    public required int PersonId { get; init; }
    public required string Name { get; init; }
    public string? KnownFor { get; init; }
    public string? PortraitUrl { get; init; }
    public required List<CatalogueTitleView> Titles { get; init; }
}

/// <summary>What <c>/api/catalogue/genre</c> answers: the genre as the database spells it, and its titles.</summary>
public sealed record CatalogueGenreView
{
    public required string Kind { get; init; }
    public required string Genre { get; init; }

    /// <summary><c>anime</c> for a genre's anime alone; absent for the whole genre.</summary>
    public Library.TitleCategory? Category { get; init; }

    public required List<CatalogueTitleView> Titles { get; init; }
}

/// <summary>
/// The film database browsed by a person and by a genre: everything a person is credited on, and the
/// most popular films or shows in a genre, each leading to its own page by its IMDb id.
///
/// Each answer is kept on disk under one directory and served from here, on the terms
/// <see cref="FilmStore"/> keeps a description: asked for when nothing is kept or what is kept is
/// due, a person's after <see cref="PersonKept"/> and a genre's after <see cref="GenreKept"/>, and a
/// database that cannot be reached then costs freshness and nothing else. A title's poster is the
/// one its page shows: each title listed is offered to <see cref="TitleGlances"/>, which serves it at
/// <c>/glances/{imdbId}/poster.jpg</c> as it does a search result's. A person's portrait is fetched
/// with their credits and served at <c>/catalogue/people/{personId}.jpg</c>.
/// </summary>
public sealed class Catalogue(
    string root,
    AnswerKeeper keeper,
    TitleGlances glances,
    TitleScores scores,
    KeptImages images)
{
    /// <summary>How many titles a person's credits or a genre lists.</summary>
    public const int TitlesListed = 40;

    internal static readonly TimeSpan PersonKept = TimeSpan.FromDays(7);
    internal static readonly TimeSpan GenreKept = TimeSpan.FromDays(1);

    /// <summary>How long the database's list of genres is kept; it changes about never.</summary>
    internal static readonly TimeSpan GenresKept = TimeSpan.FromDays(30);

    private const string PortraitSize = "w185";

    /// <summary>What the portraits this keeps are named under in <see cref="KeptImages"/>.</summary>
    private const string Images = "catalogue";

    /// <summary>The kinds a genre is browsed in.</summary>
    public static bool IsKind(string? kind) => kind is "film" or "show";

    /// <summary>
    /// A person and their credits. Null when the database cannot be asked and nothing is kept; a kept
    /// answer with no value when the database has nobody under the id.
    /// </summary>
    public Task<KeptAnswer<PersonCredits>?> PersonAsync(int personId, CancellationToken ct) =>
        keeper.KeptAsync($"person-titles/{personId}", AnswerJson.Default.PersonCredits, PersonKept,
            async (tmdb, token) =>
            {
                var person = await tmdb.PersonAsync(personId, TitlesListed, token);
                if (person is not null) await FetchPortraitAsync(tmdb, person.TmdbId, person.ProfilePath, token);
                return person;
            },
            person => Offer(person.Titles), ct);

    /// <summary>
    /// The database's genres for films or for shows. Null when the database cannot be asked and
    /// nothing is kept.
    /// </summary>
    public async Task<IReadOnlyList<CatalogueGenre>?> GenresAsync(string kind, CancellationToken ct)
    {
        var genres = await keeper.KeptAsync($"genres/{kind}", AnswerJson.Default.ListCatalogueGenre, GenresKept,
            async (tmdb, token) => (await tmdb.GenresAsync(kind, token)) is { Count: > 0 } list ? list.ToList() : null,
            null, ct);
        return genres is null ? null : genres.Value ?? [];
    }

    /// <summary>
    /// A genre of films or shows by its name, any case, and its most popular titles; with
    /// <paramref name="anime"/>, the most popular of them that are anime (<see cref="Library.TitleCategories"/>).
    /// Null when the database cannot be asked and nothing is kept; a kept answer with no value when the
    /// database has no genre by that name for the kind.
    /// </summary>
    public async Task<(CatalogueGenre? Genre, KeptAnswer<List<CatalogueTitle>>? Titles)?> GenreAsync(string kind, string name, bool anime, CancellationToken ct)
    {
        var genres = await GenresAsync(kind, ct);
        if (genres is null) return null;
        if (genres.FirstOrDefault(g => string.Equals(g.Name, name.Trim(), StringComparison.OrdinalIgnoreCase)) is not { } genre)
            return (null, null);

        List<int> ids = [genre.Id];
        string? language = null;
        if (anime)
        {
            if (genres.FirstOrDefault(g => string.Equals(g.Name, Library.TitleCategories.AnimeGenre, StringComparison.OrdinalIgnoreCase)) is not { } animation)
                return (null, null);
            if (animation.Id != genre.Id) ids.Add(animation.Id);
            language = Library.TitleCategories.AnimeLanguage;
        }

        var titles = await keeper.KeptAsync($"genre-titles/{kind}-{genre.Id}{(anime ? "-anime" : "")}", AnswerJson.Default.ListCatalogueTitle, GenreKept,
            async (tmdb, token) => (await tmdb.GenreAsync(kind, ids, language, TitlesListed, token)).ToList(),
            Offer, ct);
        return titles is null ? null : (genre, titles);
    }

    public async Task<CataloguePersonView> ViewAsync(PersonCredits person, CancellationToken ct) => new()
    {
        PersonId = person.TmdbId,
        Name = person.Name,
        KnownFor = person.KnownFor,
        PortraitUrl = PortraitUrlIfKept(person.TmdbId),
        Titles = await ViewsAsync(person.Titles, ct),
    };

    /// <summary>Each title as a browse lists it, its genres named in its kind's list; a list the database cannot be asked for leaves them unnamed.</summary>
    public async Task<List<CatalogueTitleView>> ViewsAsync(IReadOnlyList<CatalogueTitle> titles, CancellationToken ct)
    {
        var names = new Dictionary<string, Dictionary<int, string>>();
        foreach (var kind in titles.Select(t => t.Kind).Distinct())
            names[kind] = (await GenresAsync(kind, ct) ?? []).ToDictionary(g => g.Id, g => g.Name);
        return [.. titles.Select(t => View(t) with
        {
            Genres = [.. t.GenreIds.Select(id => names[t.Kind].GetValueOrDefault(id)).OfType<string>()],
        })];
    }

    public CatalogueTitleView View(CatalogueTitle title)
    {
        var (rating, votes, ratedBy) = scores.Serve(title.ImdbId, title.TmdbId, title.Kind, title.Rating, title.Votes);
        return new CatalogueTitleView
        {
            ImdbId = title.ImdbId,
            Kind = title.Kind,
            Title = title.Title,
            Year = title.Released?.Year,
            Rating = rating,
            Votes = votes,
            RatedBy = ratedBy,
            Overview = title.Overview,
            OriginalLanguage = title.OriginalLanguage,
            PosterUrl = glances.PosterUrl(title.ImdbId, Glance(title)),
            Role = title.Role,
        };
    }

    /// <summary>Where a person's portrait is served from. Artwork is not the film, so it needs no token.</summary>
    public static string PortraitUrl(int personId) => $"/catalogue/people/{personId}.jpg";

    /// <summary>Where a person's portrait is served from, or null while none is kept.</summary>
    public string? PortraitUrlIfKept(int personId) => File.Exists(PortraitPath(personId)) ? PortraitUrl(personId) : null;

    /// <summary>The kept portrait a request names, or null for anything that is not one.</summary>
    public string? PortraitFile(string file)
    {
        if (!file.EndsWith(".jpg", StringComparison.Ordinal) || !int.TryParse(file[..^4], out var id) || id <= 0
            || file[..^4] != id.ToString(System.Globalization.CultureInfo.InvariantCulture)) return null;
        var path = PortraitPath(id);
        return File.Exists(path) ? path : null;
    }

    private string PortraitPath(int personId) => Path.Combine(root, "people", personId + ".jpg");

    private static TitleGlance Glance(CatalogueTitle title) => new()
    {
        TmdbId = title.TmdbId,
        Kind = title.Kind,
        Rating = title.Rating,
        Votes = title.Votes,
        Overview = title.Overview,
        Released = title.Released,
        PosterPath = title.PosterPath,
    };

    private void Offer(IEnumerable<CatalogueTitle> titles)
    {
        foreach (var title in titles) glances.Offer(title.ImdbId, Glance(title));
    }

    /// <summary>Keeps a person's portrait, or removes it when the database holds none.</summary>
    internal Task FetchPortraitAsync(TmdbClient tmdb, int personId, string? profilePath, CancellationToken ct) =>
        images.KeepAsync(tmdb, Images, root, $"people/{personId}.jpg", profilePath, PortraitSize, ct);
}
