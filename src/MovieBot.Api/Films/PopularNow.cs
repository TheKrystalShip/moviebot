using System.Text.Json;
using System.Text.Json.Serialization;
using TheKrystalShip.MovieBot.Acquire.Tmdb;

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
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    WriteIndented = true)]
[JsonSerializable(typeof(StoredPopular))]
internal partial class StoredPopularJson : JsonSerializerContext;

/// <summary>A film or a show trending this week, as a poster on a shelf shows it.</summary>
public sealed record PopularTitleView
{
    public required string ImdbId { get; init; }
    public required string Title { get; init; }
    public int? Year { get; init; }
    public double? Rating { get; init; }
    public int Votes { get; init; }
    public string? PosterUrl { get; init; }
}

/// <summary>What <c>/api/popular</c> answers. <c>FetchedAt</c> is absent while nothing has been asked yet.</summary>
public sealed record PopularNowView(List<PopularTitleView> Films, List<PopularTitleView> Shows, DateTimeOffset? FetchedAt);

/// <summary>
/// The films and shows the film database counts as trending this week, for somebody looking for
/// something to watch beyond what the library holds.
///
/// The list and one poster per entry are kept under one directory and served from here, on the
/// terms <see cref="FilmStore"/> keeps a description: a page reads only what is kept and never waits
/// on the database. The list is asked for shortly after start when what is kept is due, and then
/// every <see cref="Every"/>; a database that cannot be reached costs freshness and nothing else.
/// Only the list and the posters are kept: an entry's page asks for the rest of it when it is opened.
/// </summary>
public sealed class PopularNow(
    string root,
    IServiceScopeFactory scopes,
    TimeProvider clock,
    ILogger<PopularNow> logger) : BackgroundService
{
    private const string ListFile = "popular.json";
    private static readonly TimeSpan FirstPass = TimeSpan.FromSeconds(40);
    private static readonly TimeSpan Every = TimeSpan.FromHours(6);

    private StoredPopular? _held;
    private bool _read;
    private readonly Lock _gate = new();

    /// <summary>What is kept, read from disk the first time it is asked for.</summary>
    public StoredPopular? Held()
    {
        lock (_gate)
        {
            if (!_read)
            {
                _held = Read();
                _read = true;
            }
            return _held;
        }
    }

    public PopularNowView View()
    {
        var held = Held();
        PopularTitleView ToView(TrendingTitle t) => new()
        {
            ImdbId = t.ImdbId,
            Title = t.Title,
            Year = t.Year,
            Rating = t.Rating,
            Votes = t.Votes,
            PosterUrl = File.Exists(PosterPath(t.ImdbId)) ? ImageUrl(t.ImdbId) : null,
        };
        return held is null
            ? new PopularNowView([], [], null)
            : new PopularNowView(held.Films.Select(ToView).ToList(), held.Shows.Select(ToView).ToList(), held.FetchedAt);
    }

    /// <summary>Where an entry's poster is served from. Artwork is not the film, so it needs no token.</summary>
    public static string ImageUrl(string imdbId) => $"/popular/{imdbId}.jpg";

    /// <summary>The kept poster a request names, or null for anything that is not one.</summary>
    public string? PosterFile(string file)
    {
        if (!file.EndsWith(".jpg", StringComparison.Ordinal) || !FilmStore.IsImdbId(file[..^4])) return null;
        var path = PosterPath(file[..^4]);
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

            Directory.CreateDirectory(root);
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
        var target = PosterPath(entry.ImdbId);
        var marker = target + ".src";
        if (entry.PosterPath is null)
        {
            File.Delete(target);
            File.Delete(marker);
            return;
        }
        // An image a path names never changes; a new image comes under a new path.
        if (File.Exists(target) && File.Exists(marker) && await File.ReadAllTextAsync(marker, ct) == entry.PosterPath) return;

        if (await tmdb.ImageAsync(entry.PosterPath, "w500", ct) is not { } bytes) return;
        await File.WriteAllBytesAsync(target + ".tmp", bytes, ct);
        File.Move(target + ".tmp", target, overwrite: true);
        await File.WriteAllTextAsync(marker, entry.PosterPath, ct);
    }

    /// <summary>Removes the posters of whatever is no longer trending.</summary>
    private void Prune(HashSet<string> kept)
    {
        foreach (var file in Directory.EnumerateFiles(root, "tt*.jpg*"))
        {
            var id = Path.GetFileName(file).Split('.')[0];
            if (!kept.Contains(id)) File.Delete(file);
        }
    }

    private string PosterPath(string imdbId) => Path.Combine(root, imdbId + ".jpg");

    private StoredPopular? Read()
    {
        var path = Path.Combine(root, ListFile);
        try
        {
            if (!File.Exists(path)) return null;
            using var stream = File.OpenRead(path);
            return JsonSerializer.Deserialize(stream, StoredPopularJson.Default.StoredPopular);
        }
        catch (Exception ex) when (ex is JsonException or IOException)
        {
            // A list that cannot be read is one not kept: the next pass writes a good one.
            logger.LogWarning("The kept trending list could not be read: {Reason}", ex.Message);
            return null;
        }
    }

    private void Write(StoredPopular popular)
    {
        var path = Path.Combine(root, ListFile);
        File.WriteAllText(path + ".tmp", JsonSerializer.Serialize(popular, StoredPopularJson.Default.StoredPopular));
        File.Move(path + ".tmp", path, overwrite: true);
    }
}
