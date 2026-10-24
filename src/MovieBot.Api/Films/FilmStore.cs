using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;
using TheKrystalShip.MovieBot.Acquire.Tmdb;
using TheKrystalShip.MovieBot.Api.Data;

namespace TheKrystalShip.MovieBot.Api.Films;

public sealed class FilmStoreOptions
{
    /// <summary>Where films' artwork is kept. Empty is <c>films/</c> in the state directory.</summary>
    public string Directory { get; set; } = "";

    /// <summary>Where shows' artwork is kept. Empty is <c>shows/</c> in the state directory.</summary>
    public string ShowsDirectory { get; set; } = "";

    /// <summary>Where the posters of the week's trending films and shows are kept. Empty is <c>popular/</c> in the state directory.</summary>
    public string PopularDirectory { get; set; } = "";

    /// <summary>Where search results' posters are kept. Empty is <c>glances/</c> in the state directory.</summary>
    public string GlancesDirectory { get; set; } = "";

    /// <summary>Where people's portraits are kept. Empty is <c>catalogue/</c> in the state directory.</summary>
    public string CatalogueDirectory { get; set; } = "";

    /// <summary>How old a description may get before the database is asked again.</summary>
    public int RefreshDays { get; set; } = 14;

    /// <summary>How old the description of a film released within the last sixty days may get before the database is asked again: its rating moves daily.</summary>
    public int RecentRefreshDays { get; set; } = 1;

    /// <summary>How often the Discover shelves the same for everybody are asked for again ahead of anybody; zero asks nothing ahead.</summary>
    public int ShelvesWarmMinutes { get; set; } = 60;
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
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(FilmDetails))]
internal partial class FilmDetailsJson : JsonSerializerContext;

/// <summary>
/// Films as the film database describes them, kept and served from here.
///
/// The site never reaches the database: this asks it once per film, keeps the description and every
/// image it names, the images under one directory per film, and answers from that copy until it is
/// <see cref="FilmStoreOptions.RefreshDays"/> old. A database that cannot be reached when a copy is
/// due costs nothing but freshness: the old copy is served. Artwork is fetched with the description,
/// so serving an image is reading a file.
/// </summary>
public sealed class FilmStore(
    string root,
    IOptions<FilmStoreOptions> options,
    IServiceScopeFactory scopes,
    PosterPins pins,
    KeptImages images,
    CinemaDatabase database,
    TitleScores scores,
    TimeProvider clock,
    ILogger<FilmStore> logger)
{
    public const string Poster = "poster.jpg";
    public const string Backdrop = "backdrop.jpg";

    /// <summary>The title set as a logo: a transparent PNG.</summary>
    public const string Logo = "logo.png";

    /// <summary>What the images this keeps are named under in <see cref="KeptImages"/>.</summary>
    private const string Images = "films";

    private readonly ConcurrentDictionary<string, StoredFilm> _held = new();
    private readonly ConcurrentDictionary<string, Task<StoredFilm?>> _asking = new();

    public string Root => root;

    /// <summary>The rating every view of a film shows.</summary>
    public TitleScores Scores => scores;

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
        if (read is not null)
        {
            _held[imdbId] = read;
            Observe(read);
        }
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
    /// Whether a kept description is asked for again: once it is old, a film released within
    /// <see cref="RecentWindow"/> after <see cref="FilmStoreOptions.RecentRefreshDays"/> since its
    /// rating moves daily, and at once when its cast names nobody by the database's id, which is
    /// what leads from a cast member to their other work, or when the database was never asked for
    /// its keywords, trailers and the rest of what a description carries.
    /// </summary>
    public bool IsDue(StoredFilm film) =>
        clock.GetUtcNow() - film.FetchedAt > Keep(film)
        || film.Details is { Cast: [{ TmdbId: null }, ..] }
        || film.Details is { Keywords: null }
        || film.Details is { Trailers: null };

    /// <summary>How recently a film must have been released for its description to be kept the shorter time.</summary>
    internal static readonly TimeSpan RecentWindow = TimeSpan.FromDays(60);

    private TimeSpan Keep(StoredFilm film)
    {
        var keep = TimeSpan.FromDays(Math.Max(1, options.Value.RefreshDays));
        var recent = TimeSpan.FromDays(Math.Max(1, options.Value.RecentRefreshDays));
        var age = film.Details is null ? null : clock.GetUtcNow() - Released(film.Details);
        return recent < keep && age is { } since && since >= TimeSpan.Zero && since <= RecentWindow ? recent : keep;
    }

    private static DateTimeOffset? Released(FilmDetails details) =>
        details.ReleaseDate is { } text && DateOnly.TryParseExact(text, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var day)
            ? new DateTimeOffset(day.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero)
            : null;

    private void Observe(StoredFilm film)
    {
        if (film.Details is not { } d) return;
        scores.Observe(d.TmdbId, "film", d.Rating, d.Votes, film.FetchedAt);
        scores.Observe(TitleScores.ReadingsOf(d.Recommendations), film.FetchedAt);
    }

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
            Observe(stored);
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
            (pins.Pin(imdbId, images.Source(Images, root, $"{imdbId}/{Poster}"), details.PosterPath), "w500", Poster),
            (details.BackdropPath, "w1280", Backdrop),
            (details.LogoPath, "w500", Logo),
        };
        wanted.AddRange(details.Cast.Select((c, i) => (c.ProfilePath, "w185", Portrait(i))));

        // A page opened on a film nothing is kept for waits on these, so they are asked side by side:
        // the wait is the slowest image rather than the sum of them.
        await Parallel.ForEachAsync(wanted, new ParallelOptions { MaxDegreeOfParallelism = ImagesAtOnce, CancellationToken = ct },
            async (image, token) => await images.KeepAsync(tmdb, Images, root, $"{imdbId}/{image.File}", image.Path, image.Size, token));
    }

    /// <summary>How many images are asked of the database's store at once.</summary>
    internal const int ImagesAtOnce = 8;

    private StoredFilm? Read(string imdbId)
    {
        try
        {
            return database.Read(c => c.First("SELECT fetched_at, details FROM film_descriptions WHERE imdb_id = $imdb",
                r => new StoredFilm
                {
                    FetchedAt = r.GetInstant(0),
                    Details = r.GetStringOrNull(1) is { } details ? JsonSerializer.Deserialize(details, FilmDetailsJson.Default.FilmDetails) : null,
                },
                ("$imdb", imdbId)));
        }
        catch (JsonException ex)
        {
            // A description that cannot be read is one not kept: the next ask keeps a good one.
            logger.LogWarning("The kept description of {Imdb} could not be read: {Reason}", imdbId, ex.Message);
            return null;
        }
    }

    private void Write(string imdbId, StoredFilm film) =>
        database.Write(c => c.Execute(
            "INSERT OR REPLACE INTO film_descriptions (imdb_id, fetched_at, details) VALUES ($imdb, $at, $details)",
            ("$imdb", imdbId), ("$at", film.FetchedAt),
            ("$details", film.Details is null ? null : JsonSerializer.Serialize(film.Details, FilmDetailsJson.Default.FilmDetails))));

    /// <summary>The canonical <c>tt</c> form and nothing else, since it becomes a directory name.</summary>
    public static bool IsImdbId(string? id) =>
        id is { Length: >= 9 and <= 12 } && id.StartsWith("tt", StringComparison.Ordinal) && id[2..].All(char.IsAsciiDigit);

    private static bool IsImageName(string file) =>
        file is Poster or Backdrop or Logo
        || (file.StartsWith("cast-", StringComparison.Ordinal) && file.EndsWith(".jpg", StringComparison.Ordinal)
            && file[5..^4] is { Length: > 0 and <= 2 } n && n.All(char.IsAsciiDigit));
}
