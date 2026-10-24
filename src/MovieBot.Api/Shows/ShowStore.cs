using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;
using TheKrystalShip.MovieBot.Acquire.Tmdb;
using TheKrystalShip.MovieBot.Api.Data;
using TheKrystalShip.MovieBot.Api.Films;

namespace TheKrystalShip.MovieBot.Api.Shows;

/// <summary>What is kept for one show: the show, each of its seasons with their episodes, and when.</summary>
public sealed record StoredShow
{
    /// <summary>Null when the database holds no show under the id, which is an answer worth keeping.</summary>
    public ShowDetails? Details { get; init; }

    /// <summary>Every numbered season with its episodes, in order. Specials (season 0) are not kept.</summary>
    public List<SeasonDetails> Seasons { get; init; } = [];

    public required DateTimeOffset FetchedAt { get; init; }
}

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(ShowDetails))]
[JsonSerializable(typeof(List<SeasonDetails>))]
internal partial class ShowDetailsJson : JsonSerializerContext;

/// <summary>
/// Shows as the film database describes them, kept and served from here, on the same terms as
/// <see cref="FilmStore"/>: asked once per show, kept with every image it names under one directory
/// per show, refreshed when <see cref="FilmStoreOptions.RefreshDays"/> old, and the old copy
/// served when the database cannot be reached. A show is its description and its seasons' episode
/// lists, asked for together, so a page about a show never waits on a second question.
/// </summary>
public sealed class ShowStore(
    string root,
    IOptions<FilmStoreOptions> options,
    IServiceScopeFactory scopes,
    PosterPins pins,
    KeptImages images,
    StreamingServices streaming,
    CinemaDatabase database,
    TitleScores scores,
    EpisodeNumbering numbering,
    TimeProvider clock,
    ILogger<ShowStore> logger)
{
    public const string Poster = "poster.jpg";
    public const string Backdrop = "backdrop.jpg";

    /// <summary>The title set as a logo: a transparent PNG.</summary>
    public const string Logo = "logo.png";

    /// <summary>What the images this keeps are named under in <see cref="KeptImages"/>.</summary>
    private const string Images = "shows";

    private readonly ConcurrentDictionary<string, StoredShow> _held = new();
    private readonly ConcurrentDictionary<string, Task<StoredShow?>> _asking = new();

    /// <summary>A portrait's file name, by the cast member's place in the billing.</summary>
    public static string Portrait(int billing) => $"cast-{billing}.jpg";

    /// <summary>An episode's still, <c>s02e04.jpg</c>.</summary>
    public static string Still(int season, int episode) =>
        string.Create(System.Globalization.CultureInfo.InvariantCulture, $"s{season:00}e{episode:00}.jpg");

    /// <summary>
    /// What is kept for a show, without asking: what a listing shows, its episodes numbered as the
    /// show's releases number them (<see cref="EpisodeNumbering"/>). Null when nothing is kept.
    /// </summary>
    public StoredShow? Held(string imdbId) => Kept(imdbId) is { } kept ? numbering.Apply(imdbId, kept) : null;

    /// <summary>The rating every view of a show shows.</summary>
    public TitleScores Scores => scores;

    private void Observe(StoredShow show)
    {
        if (show.Details is not { } d) return;
        scores.Observe(d.TmdbId, "show", d.Rating, d.Votes, show.FetchedAt);
        scores.Observe(TitleScores.ReadingsOf(d.Recommendations), show.FetchedAt);
    }

    /// <summary>What is kept for a show, as the film database numbers it.</summary>
    private StoredShow? Kept(string imdbId)
    {
        if (!FilmStore.IsImdbId(imdbId)) return null;
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
    /// The show, asking the database when nothing is kept or what is kept is due: older than
    /// <paramref name="maxAge"/> when one is given, else than the store's own refresh interval. A
    /// caller that needs a show's airing episodes current asks with a shorter age.
    /// </summary>
    public async Task<StoredShow?> GetAsync(string imdbId, CancellationToken ct, TimeSpan? maxAge = null)
    {
        if (!FilmStore.IsImdbId(imdbId)) return null;
        var held = Kept(imdbId);
        if (held is not null && !(maxAge is { } age ? clock.GetUtcNow() - held.FetchedAt > age : IsDue(held)))
            return numbering.Apply(imdbId, held);

        var asking = _asking.GetOrAdd(imdbId, id => FetchAsync(id, held));
        try
        {
            return await asking.WaitAsync(ct) is { } got ? numbering.Apply(imdbId, got) : null;
        }
        finally
        {
            if (asking.IsCompleted) _asking.TryRemove(new KeyValuePair<string, Task<StoredShow?>>(imdbId, asking));
        }
    }

    /// <summary>
    /// Whether a kept description is asked for again: once it is old, and at once when its cast names
    /// nobody by the database's id or the database was never asked for its keywords, as for a film.
    /// </summary>
    public bool IsDue(StoredShow show) =>
        clock.GetUtcNow() - show.FetchedAt > TimeSpan.FromDays(Math.Max(1, options.Value.RefreshDays))
        || show.Details is { Cast: [{ TmdbId: null }, ..] }
        || show.Details is { Keywords: null }
        || show.Details is { Trailers: null };

    /// <summary>
    /// The kept image a request names, or null for anything that is not one. A still is named by the
    /// episode as its release numbers it, and is the film database's still for that episode.
    /// </summary>
    public string? ImageFile(string imdbId, string file)
    {
        if (!FilmStore.IsImdbId(imdbId) || !IsImageName(file)) return null;
        var path = Path.Combine(root, imdbId, numbering.StillFile(imdbId, Kept(imdbId), file));
        return File.Exists(path) ? path : null;
    }

    public bool HasImage(string imdbId, string file) =>
        File.Exists(Path.Combine(root, imdbId, numbering.StillFile(imdbId, Kept(imdbId), file)));

    private async Task<StoredShow?> FetchAsync(string imdbId, StoredShow? held)
    {
        using var scope = scopes.CreateScope();
        var tmdb = scope.ServiceProvider.GetRequiredService<TmdbClient>();
        if (!tmdb.IsConfigured) return held;

        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(5));
            var details = await tmdb.ShowAsync(imdbId, timeout.Token);

            var seasons = new List<SeasonDetails>();
            if (details is not null)
            {
                // Every season at once, kept in the show's order.
                using var gate = new SemaphoreSlim(FilmStore.ImagesAtOnce);
                var described = await Task.WhenAll(details.Seasons.Where(s => s.Number > 0).Select(async season =>
                {
                    await gate.WaitAsync(timeout.Token);
                    try { return await tmdb.SeasonAsync(details.TmdbId, season.Number, timeout.Token); }
                    finally { gate.Release(); }
                }));
                seasons.AddRange(described.OfType<SeasonDetails>());

                await FetchImagesAsync(tmdb, imdbId, details, seasons, timeout.Token);
            }

            var stored = new StoredShow { Details = details, Seasons = seasons, FetchedAt = clock.GetUtcNow() };
            Keep(imdbId, stored);
            _held[imdbId] = stored;
            Observe(stored);
            return stored;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or IOException)
        {
            logger.LogWarning("The film database could not describe the show {Imdb}: {Reason}{Kept}", imdbId, ex.Message,
                held is null ? "" : "; the kept description is served");
            return held;
        }
    }

    private async Task FetchImagesAsync(
        TmdbClient tmdb, string imdbId, ShowDetails details, List<SeasonDetails> seasons, CancellationToken ct)
    {
        var wanted = new List<(string? Path, string Size, string File)>
        {
            (pins.Pin(imdbId, images.Source(Images, root, $"{imdbId}/{Poster}"), details.PosterPath), "w500", Poster),
            (details.BackdropPath, "w1280", Backdrop),
            (details.LogoPath, "w500", Logo),
        };
        wanted.AddRange(details.Cast.Select((c, i) => (c.ProfilePath, "w185", Portrait(i))));
        wanted.AddRange(seasons.SelectMany(s => s.Episodes).Select(e => (e.StillPath, "w300", Still(e.Season, e.Number))));

        // Asked side by side, as a film's are: a page opened on a show nothing is kept for waits on them.
        await Parallel.ForEachAsync(wanted, new ParallelOptions { MaxDegreeOfParallelism = FilmStore.ImagesAtOnce, CancellationToken = ct },
            async (image, token) => await images.KeepAsync(tmdb, Images, root, $"{imdbId}/{image.File}", image.Path, image.Size, token));
        await streaming.KeepLogosAsync(tmdb, details.Providers, ct);
    }

    private StoredShow? Read(string imdbId)
    {
        try
        {
            return database.Read(c => c.First("SELECT fetched_at, details, seasons FROM show_descriptions WHERE imdb_id = $imdb",
                r => new StoredShow
                {
                    FetchedAt = r.GetInstant(0),
                    Details = r.GetStringOrNull(1) is { } details ? JsonSerializer.Deserialize(details, ShowDetailsJson.Default.ShowDetails) : null,
                    Seasons = JsonSerializer.Deserialize(r.GetString(2), ShowDetailsJson.Default.ListSeasonDetails) ?? [],
                },
                ("$imdb", imdbId)));
        }
        catch (JsonException ex)
        {
            logger.LogWarning("The kept description of the show {Imdb} could not be read: {Reason}", imdbId, ex.Message);
            return null;
        }
    }

    /// <summary>Keeps what the database said of a show in place of what was kept.</summary>
    internal void Keep(string imdbId, StoredShow show)
    {
        Keep(database, imdbId, show);
        _held.TryRemove(imdbId, out _);
    }

    internal static void Keep(CinemaDatabase database, string imdbId, StoredShow show) =>
        database.Write(c => c.Execute(
            "INSERT OR REPLACE INTO show_descriptions (imdb_id, fetched_at, details, seasons) VALUES ($imdb, $at, $details, $seasons)",
            ("$imdb", imdbId), ("$at", show.FetchedAt),
            ("$details", show.Details is null ? null : JsonSerializer.Serialize(show.Details, ShowDetailsJson.Default.ShowDetails)),
            ("$seasons", JsonSerializer.Serialize(show.Seasons, ShowDetailsJson.Default.ListSeasonDetails))));

    private static bool IsImageName(string file)
    {
        if (file is Poster or Backdrop or Logo) return true;
        if (!file.EndsWith(".jpg", StringComparison.Ordinal)) return false;
        var stem = file[..^4];
        if (stem.StartsWith("cast-", StringComparison.Ordinal))
            return stem[5..] is { Length: > 0 and <= 2 } n && n.All(char.IsAsciiDigit);

        // s02e04
        return stem.Length is >= 6 and <= 8 && stem[0] == 's' && stem.IndexOf('e') is var e && e > 1
               && stem[1..e].All(char.IsAsciiDigit) && stem[(e + 1)..] is { Length: > 0 } ep && ep.All(char.IsAsciiDigit);
    }
}
