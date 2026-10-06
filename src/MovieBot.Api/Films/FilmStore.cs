using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;
using TheKrystalShip.MovieBot.Acquire.Tmdb;

namespace TheKrystalShip.MovieBot.Api.Films;

public sealed class FilmStoreOptions
{
    /// <summary>Where descriptions and artwork are kept. Empty is <c>films/</c> in the state directory.</summary>
    public string Directory { get; set; } = "";

    /// <summary>Where shows are kept, the same way. Empty is <c>shows/</c> in the state directory.</summary>
    public string ShowsDirectory { get; set; } = "";

    /// <summary>Where the week's trending films and shows are kept. Empty is <c>popular/</c> in the state directory.</summary>
    public string PopularDirectory { get; set; } = "";

    /// <summary>Where search results' ratings and overviews are kept. Empty is <c>glances/</c> in the state directory.</summary>
    public string GlancesDirectory { get; set; } = "";

    /// <summary>Where people's credits and genres' titles are kept. Empty is <c>catalogue/</c> in the state directory.</summary>
    public string CatalogueDirectory { get; set; } = "";

    /// <summary>How old a description may get before the database is asked again.</summary>
    public int RefreshDays { get; set; } = 14;
}

/// <summary>What is kept for one film: what the database said, and when it was asked.</summary>
public sealed record StoredFilm
{
    /// <summary>Null when the database holds no film under the id, which is an answer worth keeping.</summary>
    public FilmDetails? Details { get; init; }

    public required DateTimeOffset FetchedAt { get; init; }
}

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    WriteIndented = true)]
[JsonSerializable(typeof(StoredFilm))]
internal partial class StoredFilmJson : JsonSerializerContext;

/// <summary>
/// Films as the film database describes them, kept on disk and served from here.
///
/// The site never reaches the database: this asks it once per film, keeps the description and every
/// image it names under one directory per film, and answers from that copy until it is
/// <see cref="FilmStoreOptions.RefreshDays"/> old. A database that cannot be reached when a copy is
/// due costs nothing but freshness: the old copy is served. Artwork is fetched with the description,
/// so serving an image is reading a file.
/// </summary>
public sealed class FilmStore(
    string root,
    IOptions<FilmStoreOptions> options,
    IServiceScopeFactory scopes,
    TimeProvider clock,
    ILogger<FilmStore> logger)
{
    public const string Poster = "poster.jpg";
    public const string Backdrop = "backdrop.jpg";
    private const string DescriptionFile = "film.json";

    private readonly ConcurrentDictionary<string, StoredFilm> _held = new();
    private readonly ConcurrentDictionary<string, Task<StoredFilm?>> _asking = new();

    public string Root => root;

    /// <summary>A portrait's file name, by the cast member's place in the billing.</summary>
    public static string Portrait(int billing) => $"cast-{billing}.jpg";

    /// <summary>
    /// What is kept for a film, without asking the database: what a listing shows, which must not
    /// wait on the network. Null when nothing has been asked yet.
    /// </summary>
    public StoredFilm? Held(string imdbId)
    {
        if (!IsImdbId(imdbId)) return null;
        if (_held.TryGetValue(imdbId, out var held)) return held;
        var read = Read(imdbId);
        if (read is not null) _held[imdbId] = read;
        return read;
    }

    /// <summary>
    /// The film, asking the database when nothing is kept or what is kept is due. Null when the
    /// database is not configured and nothing is kept, or cannot be reached and nothing is kept.
    /// </summary>
    public async Task<StoredFilm?> GetAsync(string imdbId, CancellationToken ct)
    {
        if (!IsImdbId(imdbId)) return null;
        var held = Held(imdbId);
        if (held is not null && !IsDue(held)) return held;

        // Two pages asking for one film at once share one question.
        var asking = _asking.GetOrAdd(imdbId, id => FetchAsync(id, held));
        try
        {
            return await asking.WaitAsync(ct);
        }
        finally
        {
            if (asking.IsCompleted) _asking.TryRemove(new KeyValuePair<string, Task<StoredFilm?>>(imdbId, asking));
        }
    }

    /// <summary>
    /// Whether a kept description is asked for again: once it is old, and at once when its cast names
    /// nobody by the database's id, which is what leads from a cast member to their other work.
    /// </summary>
    public bool IsDue(StoredFilm film) =>
        clock.GetUtcNow() - film.FetchedAt > TimeSpan.FromDays(Math.Max(1, options.Value.RefreshDays))
        || film.Details is { Cast: [{ TmdbId: null }, ..] };

    /// <summary>The kept image a request names, or null for anything that is not one.</summary>
    public string? ImageFile(string imdbId, string file)
    {
        if (!IsImdbId(imdbId) || !IsImageName(file)) return null;
        var path = Path.Combine(root, imdbId, file);
        return File.Exists(path) ? path : null;
    }

    /// <summary>Whether a kept image exists, for building the URLs a view carries.</summary>
    public bool HasImage(string imdbId, string file) => File.Exists(Path.Combine(root, imdbId, file));

    private async Task<StoredFilm?> FetchAsync(string imdbId, StoredFilm? held)
    {
        using var scope = scopes.CreateScope();
        var tmdb = scope.ServiceProvider.GetRequiredService<TmdbClient>();
        if (!tmdb.IsConfigured) return held;

        try
        {
            // Not the caller's token: a page that stops waiting does not cancel the question for
            // the next one, and what comes back is worth keeping either way.
            using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
            var details = await tmdb.DetailsAsync(imdbId, timeout.Token);
            if (details is not null) await FetchImagesAsync(tmdb, imdbId, details, timeout.Token);

            var stored = new StoredFilm { Details = details, FetchedAt = clock.GetUtcNow() };
            Write(imdbId, stored);
            _held[imdbId] = stored;
            return stored;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or IOException)
        {
            logger.LogWarning("The film database could not describe {Imdb}: {Reason}{Kept}", imdbId, ex.Message,
                held is null ? "" : "; the kept description is served");
            return held;
        }
    }

    private async Task FetchImagesAsync(TmdbClient tmdb, string imdbId, FilmDetails details, CancellationToken ct)
    {
        var wanted = new List<(string? Path, string Size, string File)>
        {
            (details.PosterPath, "w500", Poster),
            (details.BackdropPath, "w1280", Backdrop),
        };
        wanted.AddRange(details.Cast.Select((c, i) => (c.ProfilePath, "w185", Portrait(i))));

        var directory = Path.Combine(root, imdbId);
        Directory.CreateDirectory(directory);
        // A page opened on a film nothing is kept for waits on these, so they are asked side by side:
        // the wait is the slowest image rather than the sum of them.
        await Parallel.ForEachAsync(wanted, new ParallelOptions { MaxDegreeOfParallelism = ImagesAtOnce, CancellationToken = ct },
            async (image, token) =>
            {
                var (path, size, file) = image;
                var target = Path.Combine(directory, file);
                if (path is null)
                {
                    File.Delete(target);
                    return;
                }
                // An image a path names never changes; a new image comes under a new path, and a
                // refresh that keeps the path keeps the file.
                var marker = target + ".src";
                if (File.Exists(target) && File.Exists(marker) && await File.ReadAllTextAsync(marker, token) == path) return;

                if (await tmdb.ImageAsync(path, size, token) is not { } bytes) return;
                await File.WriteAllBytesAsync(target + ".tmp", bytes, token);
                File.Move(target + ".tmp", target, overwrite: true);
                await File.WriteAllTextAsync(marker, path, token);
            });
    }

    /// <summary>How many images are asked of the database's store at once.</summary>
    internal const int ImagesAtOnce = 8;

    private StoredFilm? Read(string imdbId)
    {
        var path = Path.Combine(root, imdbId, DescriptionFile);
        try
        {
            if (!File.Exists(path)) return null;
            using var stream = File.OpenRead(path);
            return JsonSerializer.Deserialize(stream, StoredFilmJson.Default.StoredFilm);
        }
        catch (Exception ex) when (ex is JsonException or IOException)
        {
            // A description that cannot be read is one not kept: the next ask writes a good one.
            logger.LogWarning("The kept description of {Imdb} could not be read: {Reason}", imdbId, ex.Message);
            return null;
        }
    }

    private void Write(string imdbId, StoredFilm film)
    {
        var directory = Path.Combine(root, imdbId);
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, DescriptionFile);
        File.WriteAllText(path + ".tmp", JsonSerializer.Serialize(film, StoredFilmJson.Default.StoredFilm));
        File.Move(path + ".tmp", path, overwrite: true);
    }

    /// <summary>The canonical <c>tt</c> form and nothing else, since it becomes a directory name.</summary>
    public static bool IsImdbId(string? id) =>
        id is { Length: >= 9 and <= 12 } && id.StartsWith("tt", StringComparison.Ordinal) && id[2..].All(char.IsAsciiDigit);

    private static bool IsImageName(string file) =>
        file is Poster or Backdrop
        || (file.StartsWith("cast-", StringComparison.Ordinal) && file.EndsWith(".jpg", StringComparison.Ordinal)
            && file[5..^4] is { Length: > 0 and <= 2 } n && n.All(char.IsAsciiDigit));
}
