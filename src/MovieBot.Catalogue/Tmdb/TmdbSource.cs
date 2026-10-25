using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TheKrystalShip.MovieBot.Acquire.Configuration;

namespace TheKrystalShip.MovieBot.Catalogue.Tmdb;

/// <summary>
/// The film database's request budget for the whole process: at most
/// <see cref="CatalogueOptions.TmdbAtOnce"/> requests in flight, started no faster than
/// <see cref="CatalogueOptions.TmdbPerSecond"/>, and after a 429 nothing starts until the time the
/// database named has passed.
/// </summary>
public sealed class TmdbGate
{
    private readonly SemaphoreSlim _slots;
    private readonly TimeSpan _spacing;
    private readonly TimeProvider _clock;
    private readonly Lock _gate = new();
    private DateTimeOffset _next;

    public TmdbGate(IOptions<CatalogueOptions> options, TimeProvider clock)
    {
        _slots = new SemaphoreSlim(Math.Max(1, options.Value.TmdbAtOnce));
        _spacing = TimeSpan.FromSeconds(1.0 / Math.Max(1, options.Value.TmdbPerSecond));
        _clock = clock;
    }

    public async Task<IDisposable> EnterAsync(CancellationToken ct)
    {
        await _slots.WaitAsync(ct);
        try
        {
            TimeSpan wait;
            lock (_gate)
            {
                var now = _clock.GetUtcNow();
                var start = _next > now ? _next : now;
                _next = start + _spacing;
                wait = start - now;
            }
            if (wait > TimeSpan.Zero) await Task.Delay(wait, _clock, ct);
            return new Slot(_slots);
        }
        catch
        {
            _slots.Release();
            throw;
        }
    }

    /// <summary>Holds every request back until <paramref name="wait"/> has passed.</summary>
    public void BackOff(TimeSpan wait)
    {
        lock (_gate)
        {
            var until = _clock.GetUtcNow() + wait;
            if (until > _next) _next = until;
        }
    }

    private sealed class Slot(SemaphoreSlim slots) : IDisposable
    {
        private int _released;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _released, 1) == 0) slots.Release();
        }
    }
}

/// <summary>A title the film database lists: its id and whether it is a film or a show.</summary>
public readonly record struct TmdbRef(int Id, string Kind);

/// <summary>
/// The film database, as the catalogue asks it: a title found by IMDb id, a title's description with
/// everything the catalogue keeps appended, a show's season, the ids it changed, its rankings, and an
/// image's bytes. Answers are the database's own JSON, which <see cref="TmdbLayer"/> prunes and keeps.
/// A 404 is null: the database holds nothing there. Anything else that is not an answer throws
/// <see cref="HttpRequestException"/>, so a caller holding an older layer keeps it.
/// </summary>
public sealed class TmdbSource(HttpClient http, TmdbGate gate, IOptions<TmdbOptions> options, ILogger<TmdbSource> logger)
{
    /// <summary>What a film's description asks for beside the film's own facts, in the same call.</summary>
    public const string FilmExtras = "credits,keywords,release_dates,alternative_titles,recommendations,images,videos,external_ids";

    public const string ShowExtras = "aggregate_credits,keywords,content_ratings,alternative_titles,recommendations,images,videos,external_ids";

    private const int Attempts = 4;

    /// <summary>The last page any of the database's lists answers.</summary>
    private const int MaxPages = 500;

    public bool IsConfigured => options.Value.IsConfigured;

    /// <summary>
    /// The film or show of <paramref name="kind"/> an IMDb id names. IMDb's kind is the title's: an id
    /// the database files as the other kind is a title it does not hold.
    /// </summary>
    public async Task<TmdbRef?> FindAsync(string imdbId, string kind, CancellationToken ct)
    {
        var found = await GetAsync($"find/{imdbId}?external_source=imdb_id", ct);
        var results = found?[kind == "show" ? "tv_results" : "movie_results"]?.AsArray();
        return results is { Count: > 0 } && results[0]?["id"]?.GetValue<int>() is { } id ? new TmdbRef(id, kind) : null;
    }

    public Task<JsonNode?> DescribeAsync(TmdbRef title, CancellationToken ct) => GetAsync(
        title.Kind == "show"
            ? $"tv/{title.Id}?language=en-US&append_to_response={ShowExtras}&include_image_language=en,null&include_video_language=en,null"
            : $"movie/{title.Id}?language=en-US&append_to_response={FilmExtras}&include_image_language=en,null&include_video_language=en,null",
        ct);

    public Task<JsonNode?> SeasonAsync(int showId, int season, CancellationToken ct) =>
        GetAsync($"tv/{showId}/season/{season}?language=en-US", ct);

    /// <summary>
    /// The ids of every film (or show) the database changed from <paramref name="from"/> to
    /// <paramref name="to"/>, at most fourteen days apart. The feed answers at most 500 pages, so a caller
    /// asks for a day at a time.
    /// </summary>
    public async Task<List<int>> ChangedAsync(string kind, DateOnly from, DateOnly to, CancellationToken ct)
    {
        var ids = new List<int>();
        var path = kind == "show" ? "tv" : "movie";
        for (var page = 1; ; page++)
        {
            var answer = await GetAsync(
                $"{path}/changes?start_date={from.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}&end_date={to.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}&page={page}", ct);
            if (answer?["results"]?.AsArray() is not { } results) break;
            foreach (var result in results)
                if (result?["id"]?.GetValue<int>() is { } id && result["adult"]?.GetValue<bool>() != true) ids.Add(id);
            if (page >= Math.Min(MaxPages, answer["total_pages"]?.GetValue<int>() ?? 1)) break;
        }
        return ids;
    }

    /// <summary>One page of a ranking, by its path (<c>trending/movie/week</c>, <c>movie/upcoming</c>).</summary>
    public async Task<List<TmdbRef>> RankingAsync(string path, string kind, int page, CancellationToken ct)
    {
        var answer = await GetAsync($"{path}?language=en-US&page={page}", ct);
        var titles = new List<TmdbRef>();
        foreach (var result in answer?["results"]?.AsArray() ?? [])
            if (result?["id"]?.GetValue<int>() is { } id && result["adult"]?.GetValue<bool>() != true) titles.Add(new TmdbRef(id, kind));
        return titles;
    }

    /// <summary>An image's bytes at a size (<c>w500</c>), or null when the store has none at the path.</summary>
    public async Task<byte[]?> ImageAsync(string path, string size, CancellationToken ct)
    {
        var url = $"{options.Value.ImageBaseUrl.TrimEnd('/')}/{size}{path}";
        for (var attempt = 1; ; attempt++)
        {
            using var response = await http.GetAsync(url, ct);
            if (response.StatusCode == HttpStatusCode.NotFound) return null;
            if ((int)response.StatusCode is 429 or >= 500 && attempt < Attempts)
            {
                await Task.Delay(TimeSpan.FromSeconds(attempt * 2), ct);
                continue;
            }
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadAsByteArrayAsync(ct);
        }
    }

    private async Task<JsonNode?> GetAsync(string path, CancellationToken ct)
    {
        var settings = options.Value;
        for (var attempt = 1; ; attempt++)
        {
            using var slot = await gate.EnterAsync(ct);
            using var request = new HttpRequestMessage(HttpMethod.Get, $"{settings.BaseUrl.TrimEnd('/')}/{path}")
            {
                Version = http.DefaultRequestVersion,
                VersionPolicy = http.DefaultVersionPolicy,
            };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", settings.ReadAccessToken);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

            HttpResponseMessage response;
            try
            {
                response = await http.SendAsync(request, ct);
            }
            catch (HttpRequestException) when (attempt < Attempts)
            {
                await Task.Delay(TimeSpan.FromSeconds(attempt * 2), ct);
                continue;
            }
            using (response)
            {
                if (response.StatusCode == HttpStatusCode.NotFound) return null;
                if (response.StatusCode == HttpStatusCode.TooManyRequests && attempt < Attempts)
                {
                    // The database says how long to wait; asking again sooner is what it is refusing.
                    var wait = response.Headers.RetryAfter?.Delta ?? TimeSpan.FromSeconds(2);
                    logger.LogInformation("The film database asked for {Seconds} s before the next request.", wait.TotalSeconds);
                    gate.BackOff(wait);
                    continue;
                }
                if ((int)response.StatusCode >= 500 && attempt < Attempts)
                {
                    await Task.Delay(TimeSpan.FromSeconds(attempt * 2), ct);
                    continue;
                }
                response.EnsureSuccessStatusCode();
                await using var body = await response.Content.ReadAsStreamAsync(ct);
                return await JsonNode.ParseAsync(body, cancellationToken: ct);
            }
        }
    }
}
