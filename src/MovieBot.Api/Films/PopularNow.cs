using System.Text.Json;
using System.Text.Json.Serialization;
using TheKrystalShip.MovieBot.Acquire.Tmdb;
using TheKrystalShip.MovieBot.Api.Data;

namespace TheKrystalShip.MovieBot.Api.Films;

/// <summary>What is kept of the week's trending list: each entry as the database named it, and when it was asked.</summary>
public sealed record StoredPopular
{
    public List<TrendingTitle> Films { get; init; } = [];
    public List<TrendingTitle> Shows { get; init; } = [];
    public required DateTimeOffset FetchedAt { get; init; }
}

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(TrendingTitle))]
internal partial class TrendingTitleJson : JsonSerializerContext;

/// <summary>A film or a show trending this week, as a poster on a shelf shows it.</summary>
public sealed record PopularTitleView
{
    public required string ImdbId { get; init; }
    public required string Title { get; init; }
    public int? Year { get; init; }
    public double? Rating { get; init; }
    public int Votes { get; init; }
    public string? PosterUrl { get; init; }

    /// <summary>The shelf it is on beyond its kind, <c>anime</c> or <c>general</c>; absent while nothing says.</summary>
    public Library.TitleCategory? Category { get; init; }
}

/// <summary>What <c>/api/popular</c> answers. <c>FetchedAt</c> is absent while nothing has been asked yet.</summary>
public sealed record PopularNowView(List<PopularTitleView> Films, List<PopularTitleView> Shows, DateTimeOffset? FetchedAt);

/// <summary>
/// The films and shows the film database counts as trending this week, for somebody looking for
/// something to watch beyond what the library holds.
///
/// The list is kept, and one poster per entry under one directory, both served from here, on the
/// terms <see cref="FilmStore"/> keeps a description: a page reads only what is kept and never waits
/// on the database. The list is asked for shortly after start when what is kept is due, and then
/// every <see cref="Every"/>; a database that cannot be reached costs freshness and nothing else.
/// Only the list and the posters are kept: an entry's page asks for the rest of it when it is opened.
/// </summary>
public sealed class PopularNow(
    string root,
    IServiceScopeFactory scopes,
    PosterPins pins,
    KeptImages images,
    CinemaDatabase database,
    TitleScores scores,
    TimeProvider clock,
    ILogger<PopularNow> logger) : BackgroundService
{
    /// <summary>What the posters this keeps are named under in <see cref="KeptImages"/>.</summary>
    private const string Images = "popular";

    private const string FetchedKey = "trending_fetched_at";

    private static readonly TimeSpan FirstPass = TimeSpan.FromSeconds(40);
    private static readonly TimeSpan Every = TimeSpan.FromHours(6);

    private StoredPopular? _held;
    private bool _read;
    private readonly Lock _gate = new();

    /// <summary>What is kept, read the first time it is asked for.</summary>
    public StoredPopular? Held()
    {
        lock (_gate)
        {
            if (!_read)
            {
                _held = Read();
                _read = true;
                if (_held is not null) Observe(_held);
            }
            return _held;
        }
    }

    public PopularNowView View()
    {
        var held = Held();
        PopularTitleView ToView(TrendingTitle t, string kind)
        {
            var (rating, votes) = scores.Serve(t.TmdbId, kind, t.Rating, t.Votes);
            return new PopularTitleView
            {
                ImdbId = t.ImdbId,
                Title = t.Title,
                Year = t.Year,
                Rating = rating,
                Votes = votes,
                PosterUrl = File.Exists(Path.Combine(root, PosterName(t.ImdbId))) ? ImageUrl(t.ImdbId) : null,
            };
        }
        return held is null
            ? new PopularNowView([], [], null)
            : new PopularNowView(held.Films.Select(t => ToView(t, "film")).ToList(), held.Shows.Select(t => ToView(t, "show")).ToList(), held.FetchedAt);
    }

    /// <summary>Where an entry's poster is served from. Artwork is not the film, so it needs no token.</summary>
    public static string ImageUrl(string imdbId) => $"/popular/{imdbId}.jpg";

    /// <summary>The kept poster a request names, or null for anything that is not one.</summary>
    public string? PosterFile(string file)
    {
        if (!file.EndsWith(".jpg", StringComparison.Ordinal) || !FilmStore.IsImdbId(file[..^4])) return null;
        var path = Path.Combine(root, PosterName(file[..^4]));
        return File.Exists(path) ? path : null;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await Task.Delay(FirstPass, stoppingToken);
            while (!stoppingToken.IsCancellationRequested)
            {
                if (Held() is not { } held || clock.GetUtcNow() - held.FetchedAt >= Every)
                    await PassAsync(stoppingToken);
                await Task.Delay(Every, stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
    }

    /// <summary>Asks for the week's lists and their posters, and keeps them in place of what was kept.</summary>
    public async Task PassAsync(CancellationToken ct)
    {
        using var scope = scopes.CreateScope();
        var tmdb = scope.ServiceProvider.GetRequiredService<TmdbClient>();
        if (!tmdb.IsConfigured) return;

        try
        {
            var films = await tmdb.TrendingFilmsAsync(ct);
            var shows = await tmdb.TrendingShowsAsync(ct);
            // An empty answer is the database having nothing to say this time, never a week in which
            // nothing was watched: what is kept stays.
            if (films.Count == 0 && shows.Count == 0) return;

            var entries = films.Concat(shows).ToList();
            foreach (var entry in entries)
                await FetchPosterAsync(tmdb, entry, ct);

            var stored = new StoredPopular { Films = [.. films], Shows = [.. shows], FetchedAt = clock.GetUtcNow() };
            Write(stored);
            lock (_gate)
            {
                _held = stored;
                _read = true;
            }
            Observe(stored);
            Prune(entries.Select(e => e.ImdbId).ToHashSet());
            logger.LogInformation("Kept {Films} trending films and {Shows} trending shows from the film database.", films.Count, shows.Count);
        }
        catch (Exception ex) when ((ex is HttpRequestException or TaskCanceledException or JsonException or IOException)
                                   && !ct.IsCancellationRequested)
        {
            logger.LogWarning("The film database could not say what is trending: {Reason}", ex.Message);
        }
    }

    private async Task FetchPosterAsync(TmdbClient tmdb, TrendingTitle entry, CancellationToken ct)
    {
        var file = PosterName(entry.ImdbId);
        await images.KeepAsync(tmdb, Images, root, file,
            pins.Pin(entry.ImdbId, images.Source(Images, root, file), entry.PosterPath), "w500", ct);
    }

    /// <summary>Removes the posters of whatever is no longer trending.</summary>
    private void Prune(HashSet<string> kept)
    {
        if (!Directory.Exists(root)) return;
        foreach (var path in Directory.EnumerateFiles(root, "tt*.jpg"))
        {
            var file = Path.GetFileName(path);
            if (!kept.Contains(file[..^4])) images.Forget(Images, root, file);
        }
    }

    private void Observe(StoredPopular popular) => scores.Observe(
        [.. popular.Films.Select(t => new TitleScores.Reading(t.TmdbId, "film", t.Rating, t.Votes)),
         .. popular.Shows.Select(t => new TitleScores.Reading(t.TmdbId, "show", t.Rating, t.Votes))],
        popular.FetchedAt);

    private static string PosterName(string imdbId) => imdbId + ".jpg";

    private StoredPopular? Read()
    {
        try
        {
            return database.Read(c =>
            {
                if (c.First("SELECT value FROM meta WHERE key = $key", r => r.GetString(0), ("$key", FetchedKey)) is not { } fetched)
                    return null;
                var titles = c.Query("SELECT kind, title FROM trending ORDER BY kind, rank",
                    r => (Kind: r.GetString(0), Title: JsonSerializer.Deserialize(r.GetString(1), TrendingTitleJson.Default.TrendingTitle)!));
                return new StoredPopular
                {
                    Films = [.. titles.Where(t => t.Kind == "film").Select(t => t.Title)],
                    Shows = [.. titles.Where(t => t.Kind == "show").Select(t => t.Title)],
                    FetchedAt = Sql.ReadInstant(fetched),
                };
            });
        }
        catch (JsonException ex)
        {
            // A list that cannot be read is one not kept: the next pass keeps a good one.
            logger.LogWarning("The kept trending list could not be read: {Reason}", ex.Message);
            return null;
        }
    }

    private void Write(StoredPopular popular) => database.Write(c =>
    {
        c.Execute("DELETE FROM trending");
        foreach (var (kind, titles) in new[] { ("film", popular.Films), ("show", popular.Shows) })
            for (var rank = 0; rank < titles.Count; rank++)
                c.Execute("INSERT INTO trending (kind, rank, title) VALUES ($kind, $rank, $title)",
                    ("$kind", kind), ("$rank", rank), ("$title", JsonSerializer.Serialize(titles[rank], TrendingTitleJson.Default.TrendingTitle)));
        c.Execute("INSERT OR REPLACE INTO meta (key, value) VALUES ($key, $value)", ("$key", FetchedKey), ("$value", Sql.Instant(popular.FetchedAt)));
    });
}
