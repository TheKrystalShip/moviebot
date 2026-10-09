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
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    WriteIndented = true)]
[JsonSerializable(typeof(KeptAnswer<PersonCredits>))]
[JsonSerializable(typeof(KeptAnswer<List<CatalogueGenre>>))]
[JsonSerializable(typeof(KeptAnswer<List<CatalogueTitle>>))]
[JsonSerializable(typeof(KeptAnswer<List<ListedTitle>>))]
[JsonSerializable(typeof(KeptAnswer<List<PersonMatch>>))]
[JsonSerializable(typeof(KeptAnswer<List<KeywordMatch>>))]
[JsonSerializable(typeof(KeptAnswer<List<LanguageName>>))]
[JsonSerializable(typeof(KeptAnswer<Filmography>))]
[JsonSerializable(typeof(KeptAnswer<TitleCard>))]
[JsonSerializable(typeof(KeptAnswer<TitleKey>))]
[JsonSerializable(typeof(KeptAnswer<TitleTraits>))]
internal partial class KeptAnswerJson : JsonSerializerContext;

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
    public string? Overview { get; init; }

    /// <summary>The poster its page shows, served from here; absent when the database has none.</summary>
    public string? PosterUrl { get; init; }

    /// <summary>Among a person's credits, the part they played or the job they did.</summary>
    public string? Role { get; init; }
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
    TitleGlances glances)
{
    /// <summary>How many titles a person's credits or a genre lists.</summary>
    public const int TitlesListed = 40;

    internal static readonly TimeSpan PersonKept = TimeSpan.FromDays(7);
    internal static readonly TimeSpan GenreKept = TimeSpan.FromDays(1);

    /// <summary>How long the database's list of genres is kept; it changes about never.</summary>
    internal static readonly TimeSpan GenresKept = TimeSpan.FromDays(30);

    private const string PortraitSize = "w185";

    /// <summary>The kinds a genre is browsed in.</summary>
    public static bool IsKind(string? kind) => kind is "film" or "show";

    /// <summary>
    /// A person and their credits. Null when the database cannot be asked and nothing is kept; a kept
    /// answer with no value when the database has nobody under the id.
    /// </summary>
    public Task<KeptAnswer<PersonCredits>?> PersonAsync(int personId, CancellationToken ct) =>
        keeper.KeptAsync($"people/{personId}", KeptAnswerJson.Default.KeptAnswerPersonCredits, PersonKept,
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
        var genres = await keeper.KeptAsync($"genres/{kind}", KeptAnswerJson.Default.KeptAnswerListCatalogueGenre, GenresKept,
            async (tmdb, token) => (await tmdb.GenresAsync(kind, token)) is { Count: > 0 } list ? list.ToList() : null,
            null, ct);
        return genres is null ? null : genres.Value ?? [];
    }

    /// <summary>
    /// A genre of films or shows by its name, any case, and its most popular titles. Null when the
    /// database cannot be asked and nothing is kept; a kept answer with no value when the database has
    /// no genre by that name for the kind.
    /// </summary>
    public async Task<(CatalogueGenre? Genre, KeptAnswer<List<CatalogueTitle>>? Titles)?> GenreAsync(string kind, string name, CancellationToken ct)
    {
        var genres = await GenresAsync(kind, ct);
        if (genres is null) return null;
        if (genres.FirstOrDefault(g => string.Equals(g.Name, name.Trim(), StringComparison.OrdinalIgnoreCase)) is not { } genre)
            return (null, null);

        var titles = await keeper.KeptAsync($"genres/{kind}-{genre.Id}", KeptAnswerJson.Default.KeptAnswerListCatalogueTitle, GenreKept,
            async (tmdb, token) => (await tmdb.GenreAsync(kind, genre.Id, TitlesListed, token)).ToList(),
            Offer, ct);
        return titles is null ? null : (genre, titles);
    }

    public CataloguePersonView View(PersonCredits person) => new()
    {
        PersonId = person.TmdbId,
        Name = person.Name,
        KnownFor = person.KnownFor,
        PortraitUrl = PortraitUrlIfKept(person.TmdbId),
        Titles = person.Titles.Select(View).ToList(),
    };

    public CatalogueTitleView View(CatalogueTitle title) => new()
    {
        ImdbId = title.ImdbId,
        Kind = title.Kind,
        Title = title.Title,
        Year = title.Released?.Year,
        Rating = title.Rating,
        Votes = title.Votes,
        Overview = title.Overview,
        PosterUrl = glances.PosterUrl(title.ImdbId, Glance(title)),
        Role = title.Role,
    };

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

    /// <summary>Keeps a person's portrait beside their credits, or removes it when the database holds none.</summary>
    internal async Task FetchPortraitAsync(TmdbClient tmdb, int personId, string? profilePath, CancellationToken ct)
    {
        var target = PortraitPath(personId);
        var marker = target + ".src";
        if (profilePath is null)
        {
            File.Delete(target);
            File.Delete(marker);
            return;
        }
        // An image a path names never changes; a new image comes under a new path.
        if (File.Exists(target) && File.Exists(marker) && await File.ReadAllTextAsync(marker, ct) == profilePath) return;

        if (await tmdb.ImageAsync(profilePath, PortraitSize, ct) is not { } bytes) return;
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        await File.WriteAllBytesAsync(target + ".tmp", bytes, ct);
        File.Move(target + ".tmp", target, overwrite: true);
        await File.WriteAllTextAsync(marker, profilePath, ct);
    }
}
