using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TheKrystalShip.MovieBot.Catalogue.Data;

namespace TheKrystalShip.MovieBot.Catalogue.Tmdb;

/// <summary>A list the film database ranks: the catalogue's name for it, the database's path, and the kind it lists.</summary>
public sealed record Ranking(string Name, string Path, string Kind);

/// <summary>
/// Asks for every ranking every <see cref="CatalogueOptions.RankingHours"/>, to
/// <see cref="CatalogueOptions.RankingPages"/> pages, and keeps each as the catalogue's titles in the
/// database's order. A ranked title the catalogue lacks is described first and admitted, whatever its
/// votes, when the database names its IMDb id; one with no IMDb id is left out of the list. Each list is
/// swapped in whole, so a reader sees the last list or the new one.
/// </summary>
public sealed class RankingsWorker(
    TmdbSource source,
    TmdbLayer layer,
    CatalogueDatabase database,
    IOptions<CatalogueOptions> options,
    TimeProvider clock,
    ILogger<RankingsWorker> logger) : BackgroundService
{
    public static readonly IReadOnlyList<Ranking> Rankings =
    [
        new("trending-films", "trending/movie/week", "film"),
        new("popular-films", "movie/popular", "film"),
        new("now-playing", "movie/now_playing", "film"),
        new("upcoming", "movie/upcoming", "film"),
        new("top-rated-films", "movie/top_rated", "film"),
        new("trending-shows", "trending/tv/week", "show"),
        new("popular-shows", "tv/popular", "show"),
        new("on-the-air", "tv/on_the_air", "show"),
        new("airing-today", "tv/airing_today", "show"),
        new("top-rated-shows", "tv/top_rated", "show"),
    ];

    private static readonly TimeSpan Retry = TimeSpan.FromMinutes(30);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!source.IsConfigured) return;
        var every = TimeSpan.FromHours(Math.Max(1, options.Value.RankingHours));
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                var failed = 0;
                foreach (var ranking in Rankings)
                {
                    try
                    {
                        await RefreshAsync(ranking, stoppingToken);
                    }
                    catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException && !stoppingToken.IsCancellationRequested)
                    {
                        failed++;
                        logger.LogWarning("The ranking {Ranking} could not be asked for: {Reason}", ranking.Name, ex.Message);
                    }
                }
                await Task.Delay(failed > 0 ? Retry : every, clock, stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
    }

    public async Task RefreshAsync(Ranking ranking, CancellationToken ct)
    {
        var listed = new List<TmdbRef>();
        for (var page = 1; page <= Math.Max(1, options.Value.RankingPages); page++)
        {
            var titles = await source.RankingAsync(ranking.Path, ranking.Kind, page, ct);
            listed.AddRange(titles);
            if (titles.Count == 0) break;
        }

        var ids = new List<long>();
        var admitted = 0;
        foreach (var title in listed.Distinct())
        {
            if (await TitleOfAsync(title, ct) is not var (id, added)) continue;
            if (ids.Contains(id)) continue;
            ids.Add(id);
            if (added) admitted++;
        }

        var now = clock.GetUtcNow();
        database.Write(c =>
        {
            c.Execute("DELETE FROM rankings WHERE list = $list", ("$list", ranking.Name));
            for (var rank = 0; rank < ids.Count; rank++)
                c.Execute("INSERT INTO rankings (list, rank, title_id, fetched_at) VALUES ($list, $rank, $id, $at)",
                    ("$list", ranking.Name), ("$rank", rank + 1), ("$id", ids[rank]), ("$at", now));
        });
        logger.LogInformation("Ranking {Ranking}: {Count} titles, {Admitted} admitted by it.", ranking.Name, ids.Count, admitted);
    }

    /// <summary>The catalogue's title for a ranked one, described and admitted when it is new; null when the database names no IMDb id for it.</summary>
    private async Task<(long Id, bool Added)?> TitleOfAsync(TmdbRef title, CancellationToken ct)
    {
        var known = database.Read(c => c.First("SELECT id FROM titles WHERE kind = $kind AND tmdb_id = $id",
            r => (long?)r.GetInt64(0), ("$kind", title.Kind), ("$id", title.Id)));
        if (known is { } id) return (id, false);

        var description = await source.DescribeAsync(title, ct);
        if (description?["external_ids"]?["imdb_id"]?.GetValue<string>() is not { Length: > 2 } imdb || !imdb.StartsWith("tt", StringComparison.Ordinal))
            return null;

        var (titleId, added) = database.Write(c =>
        {
            if (c.First("SELECT id FROM titles WHERE imdb_id = $imdb", r => (long?)r.GetInt64(0), ("$imdb", imdb)) is { } existing)
                return (existing, false);
            c.Execute("INSERT INTO titles (imdb_id, kind, admitted) VALUES ($imdb, $kind, 'ranking')", ("$imdb", imdb), ("$kind", title.Kind));
            return (c.Count("SELECT last_insert_rowid()"), true);
        });

        var seasons = title.Kind == "show" ? await layer.SeasonsAsync(title.Id, description, ct) : [];
        return layer.Keep(titleId, title, description, seasons) == TmdbLayer.Outcome.Kept ? (titleId, added) : null;
    }
}
