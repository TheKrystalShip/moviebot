using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
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
    TitleGlances glances,
    IServiceScopeFactory scopes,
    TimeProvider clock,
    ILogger<Catalogue> logger)
{
    /// <summary>How many titles a person's credits or a genre lists.</summary>
    public const int TitlesListed = 40;

    internal static readonly TimeSpan PersonKept = TimeSpan.FromDays(7);
    internal static readonly TimeSpan GenreKept = TimeSpan.FromDays(1);

    /// <summary>How long the database's list of genres is kept; it changes about never.</summary>
    internal static readonly TimeSpan GenresKept = TimeSpan.FromDays(30);

    private const string PortraitSize = "w185";

    private readonly ConcurrentDictionary<string, object> _held = new();
    private readonly ConcurrentDictionary<string, Task> _asking = new();

    /// <summary>The kinds a genre is browsed in.</summary>
    public static bool IsKind(string? kind) => kind is "film" or "show";

    /// <summary>
    /// A person and their credits. Null when the database cannot be asked and nothing is kept; a kept
    /// answer with no value when the database has nobody under the id.
    /// </summary>
    public Task<KeptAnswer<PersonCredits>?> PersonAsync(int personId, CancellationToken ct) =>
        KeptAsync($"people/{personId}", KeptAnswerJson.Default.KeptAnswerPersonCredits, PersonKept,
            async (tmdb, token) =>
            {
                var person = await tmdb.PersonAsync(personId, TitlesListed, token);
                if (person is not null) await FetchPortraitAsync(tmdb, person, token);
                return person;
            },
            person => Offer(person.Titles), ct);

    /// <summary>
    /// A genre of films or shows by its name, any case, and its most popular titles. Null when the
    /// database cannot be asked and nothing is kept; a kept answer with no value when the database has
    /// no genre by that name for the kind.
    /// </summary>
    public async Task<(CatalogueGenre? Genre, KeptAnswer<List<CatalogueTitle>>? Titles)?> GenreAsync(string kind, string name, CancellationToken ct)
    {
        var genres = await KeptAsync($"genres/{kind}", KeptAnswerJson.Default.KeptAnswerListCatalogueGenre, GenresKept,
            async (tmdb, token) => (await tmdb.GenresAsync(kind, token)) is { Count: > 0 } list ? list.ToList() : null,
            null, ct);
        if (genres is null) return null;
        if (genres.Value?.FirstOrDefault(g => string.Equals(g.Name, name.Trim(), StringComparison.OrdinalIgnoreCase)) is not { } genre)
            return (null, null);

        var titles = await KeptAsync($"genres/{kind}-{genre.Id}", KeptAnswerJson.Default.KeptAnswerListCatalogueTitle, GenreKept,
            async (tmdb, token) => (await tmdb.GenreAsync(kind, genre.Id, TitlesListed, token)).ToList(),
            Offer, ct);
        return titles is null ? null : (genre, titles);
    }

    public CataloguePersonView View(PersonCredits person) => new()
    {
        PersonId = person.TmdbId,
        Name = person.Name,
        KnownFor = person.KnownFor,
        PortraitUrl = File.Exists(PortraitPath(person.TmdbId)) ? PortraitUrl(person.TmdbId) : null,
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

    private async Task FetchPortraitAsync(TmdbClient tmdb, PersonCredits person, CancellationToken ct)
    {
        var target = PortraitPath(person.TmdbId);
        var marker = target + ".src";
        if (person.ProfilePath is null)
        {
            File.Delete(target);
            File.Delete(marker);
            return;
        }
        // An image a path names never changes; a new image comes under a new path.
        if (File.Exists(target) && File.Exists(marker) && await File.ReadAllTextAsync(marker, ct) == person.ProfilePath) return;

        if (await tmdb.ImageAsync(person.ProfilePath, PortraitSize, ct) is not { } bytes) return;
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        await File.WriteAllBytesAsync(target + ".tmp", bytes, ct);
        File.Move(target + ".tmp", target, overwrite: true);
        await File.WriteAllTextAsync(marker, person.ProfilePath, ct);
    }

    /// <summary>
    /// The answer kept under <paramref name="name"/>, asking the database when nothing is kept or what
    /// is kept is older than <paramref name="keep"/>; two requests for one answer share one question.
    /// <paramref name="received"/> sees each new value before it is kept.
    /// </summary>
    private async Task<KeptAnswer<T>?> KeptAsync<T>(
        string name, JsonTypeInfo<KeptAnswer<T>> type, TimeSpan keep,
        Func<TmdbClient, CancellationToken, Task<T?>> ask, Action<T>? received, CancellationToken ct) where T : class
    {
        var held = Held(name, type);
        if (held is not null && clock.GetUtcNow() - held.FetchedAt <= keep) return held;

        var asking = (Task<KeptAnswer<T>?>)_asking.GetOrAdd(name, _ => FetchAsync(name, type, held, ask, received));
        try
        {
            return await asking.WaitAsync(ct);
        }
        finally
        {
            if (asking.IsCompleted) _asking.TryRemove(new KeyValuePair<string, Task>(name, asking));
        }
    }

    private async Task<KeptAnswer<T>?> FetchAsync<T>(
        string name, JsonTypeInfo<KeptAnswer<T>> type, KeptAnswer<T>? held,
        Func<TmdbClient, CancellationToken, Task<T?>> ask, Action<T>? received) where T : class
    {
        await Task.Yield();
        using var scope = scopes.CreateScope();
        var tmdb = scope.ServiceProvider.GetRequiredService<TmdbClient>();
        if (!tmdb.IsConfigured) return held;

        try
        {
            // Not the caller's token: a page that stops waiting does not cancel the question for the
            // next one, and what comes back is worth keeping either way.
            using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
            var value = await ask(tmdb, timeout.Token);
            if (value is not null) received?.Invoke(value);

            var kept = new KeptAnswer<T> { Value = value, FetchedAt = clock.GetUtcNow() };
            Write(name, type, kept);
            _held[name] = kept;
            return kept;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or IOException)
        {
            logger.LogWarning("The film database could not answer for {Name}: {Reason}{Kept}", name, ex.Message,
                held is null ? "" : "; the kept answer is served");
            return held;
        }
    }

    private KeptAnswer<T>? Held<T>(string name, JsonTypeInfo<KeptAnswer<T>> type) where T : class
    {
        if (_held.TryGetValue(name, out var held)) return (KeptAnswer<T>)held;
        var path = Path.Combine(root, name + ".json");
        try
        {
            if (!File.Exists(path)) return null;
            using var stream = File.OpenRead(path);
            if (JsonSerializer.Deserialize(stream, type) is not { } read) return null;
            _held[name] = read;
            return read;
        }
        catch (Exception ex) when (ex is JsonException or IOException)
        {
            // An answer that cannot be read is one not kept: the next ask writes a good one.
            logger.LogWarning("The kept answer for {Name} could not be read: {Reason}", name, ex.Message);
            return null;
        }
    }

    private void Write<T>(string name, JsonTypeInfo<KeptAnswer<T>> type, KeptAnswer<T> kept) where T : class
    {
        var path = Path.Combine(root, name + ".json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path + ".tmp", JsonSerializer.Serialize(kept, type));
        File.Move(path + ".tmp", path, overwrite: true);
    }
}
