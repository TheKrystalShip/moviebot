using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;
using TheKrystalShip.MovieBot.Acquire.Tmdb;
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
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    WriteIndented = true)]
[JsonSerializable(typeof(StoredShow))]
internal partial class StoredShowJson : JsonSerializerContext;

/// <summary>
/// Shows as the film database describes them, kept on disk and served from here, on the same terms
/// as <see cref="FilmStore"/>: asked once per show, kept under one directory per show with every
/// image it names, refreshed when <see cref="FilmStoreOptions.RefreshDays"/> old, and the old copy
/// served when the database cannot be reached. A show is its description and its seasons' episode
/// lists, asked for together, so a page about a show never waits on a second question.
/// </summary>
public sealed class ShowStore(
    string root,
    IOptions<FilmStoreOptions> options,
    IServiceScopeFactory scopes,
    PosterPins pins,
    EpisodeNumbering numbering,
    TimeProvider clock,
    ILogger<ShowStore> logger)
{
    public const string Poster = "poster.jpg";
    public const string Backdrop = "backdrop.jpg";
    private const string DescriptionFile = "show.json";

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

    /// <summary>What is kept for a show, as the film database numbers it.</summary>
    private StoredShow? Kept(string imdbId)
    {
        if (!FilmStore.IsImdbId(imdbId)) return null;
        if (_held.TryGetValue(imdbId, out var held)) return held;
        var read = Read(imdbId);
        if (read is not null) _held[imdbId] = read;
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
        || show.Details is { Keywords: null };

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
            Write(imdbId, stored);
            _held[imdbId] = stored;
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
        var directory = Path.Combine(root, imdbId);
        var wanted = new List<(string? Path, string Size, string File)>
        {
            (pins.Pin(imdbId, PosterPins.Shown(Path.Combine(directory, Poster)), details.PosterPath), "w500", Poster),
            (details.BackdropPath, "w1280", Backdrop),
        };
        wanted.AddRange(details.Cast.Select((c, i) => (c.ProfilePath, "w185", Portrait(i))));
        wanted.AddRange(seasons.SelectMany(s => s.Episodes).Select(e => (e.StillPath, "w300", Still(e.Season, e.Number))));

        Directory.CreateDirectory(directory);
        // Asked side by side, as a film's are: a page opened on a show nothing is kept for waits on them.
        await Parallel.ForEachAsync(wanted, new ParallelOptions { MaxDegreeOfParallelism = FilmStore.ImagesAtOnce, CancellationToken = ct },
            async (image, token) =>
            {
                var (path, size, file) = image;
                var target = Path.Combine(directory, file);
                if (path is null)
                {
                    File.Delete(target);
                    return;
                }

                // An image a path names never changes; a refresh that keeps the path keeps the file.
                var marker = target + ".src";
                if (File.Exists(target) && File.Exists(marker) && await File.ReadAllTextAsync(marker, token) == path) return;

                if (await tmdb.ImageAsync(path, size, token) is not { } bytes) return;
                await File.WriteAllBytesAsync(target + ".tmp", bytes, token);
                File.Move(target + ".tmp", target, overwrite: true);
                await File.WriteAllTextAsync(marker, path, token);
            });
    }

    private StoredShow? Read(string imdbId)
    {
        var path = Path.Combine(root, imdbId, DescriptionFile);
        try
        {
            if (!File.Exists(path)) return null;
            using var stream = File.OpenRead(path);
            return JsonSerializer.Deserialize(stream, StoredShowJson.Default.StoredShow);
        }
        catch (Exception ex) when (ex is JsonException or IOException)
        {
            logger.LogWarning("The kept description of the show {Imdb} could not be read: {Reason}", imdbId, ex.Message);
            return null;
        }
    }

    private void Write(string imdbId, StoredShow show)
    {
        var directory = Path.Combine(root, imdbId);
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, DescriptionFile);
        File.WriteAllText(path + ".tmp", JsonSerializer.Serialize(show, StoredShowJson.Default.StoredShow));
        File.Move(path + ".tmp", path, overwrite: true);
    }

    private static bool IsImageName(string file)
    {
        if (file is Poster or Backdrop) return true;
        if (!file.EndsWith(".jpg", StringComparison.Ordinal)) return false;
        var stem = file[..^4];
        if (stem.StartsWith("cast-", StringComparison.Ordinal))
            return stem[5..] is { Length: > 0 and <= 2 } n && n.All(char.IsAsciiDigit);

        // s02e04
        return stem.Length is >= 6 and <= 8 && stem[0] == 's' && stem.IndexOf('e') is var e && e > 1
               && stem[1..e].All(char.IsAsciiDigit) && stem[(e + 1)..] is { Length: > 0 } ep && ep.All(char.IsAsciiDigit);
    }
}
