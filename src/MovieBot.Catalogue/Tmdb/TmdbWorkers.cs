using System.Globalization;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TheKrystalShip.MovieBot.Catalogue.Data;

namespace TheKrystalShip.MovieBot.Catalogue.Tmdb;

/// <summary>
/// Keeps every title's film database layer current: asks first about the titles marked due (changed,
/// or their rating come due), then about the titles never asked, most voted first, then about the
/// layers a sweep old, oldest first. A title the database holds nothing for is asked after again once
/// that answer is a sweep old. With nothing due it waits a minute.
/// </summary>
public sealed class TmdbWorker(
    TmdbLayer layer,
    TmdbSource source,
    CatalogueDatabase database,
    IOptions<CatalogueOptions> options,
    TimeProvider clock,
    ILogger<TmdbWorker> logger) : BackgroundService
{
    private const int Batch = 400;
    private static readonly TimeSpan Idle = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan Report = TimeSpan.FromMinutes(10);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!source.IsConfigured)
        {
            logger.LogWarning("No film database token (Tmdb__ReadAccessToken): no title is described and no entry is listed.");
            return;
        }

        var kept = 0;
        var missing = 0;
        var failed = 0;
        var reported = clock.GetUtcNow();
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                var due = Due();
                if (due.Count == 0)
                {
                    await Task.Delay(Idle, clock, stoppingToken);
                    continue;
                }

                await Parallel.ForEachAsync(due, new ParallelOptions
                {
                    MaxDegreeOfParallelism = Math.Max(1, options.Value.TmdbAtOnce),
                    CancellationToken = stoppingToken,
                }, async (titleId, ct) =>
                {
                    try
                    {
                        if (await layer.FetchAsync(titleId, ct) == TmdbLayer.Outcome.Kept) Interlocked.Increment(ref kept);
                        else Interlocked.Increment(ref missing);
                    }
                    catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException && !ct.IsCancellationRequested)
                    {
                        // Left as it was: due, never asked or old, so a later batch asks again.
                        if (Interlocked.Increment(ref failed) <= 3)
                            logger.LogWarning("The film database could not be asked about title {Title}: {Reason}", titleId, ex.Message);
                    }
                });

                if (clock.GetUtcNow() - reported >= Report)
                {
                    reported = clock.GetUtcNow();
                    var left = Remaining();
                    logger.LogInformation(
                        "Film database layer: {Kept} kept, {Missing} not held there, {Failed} failed since the last report; {Unasked} never asked, {Due} due.",
                        kept, missing, failed, left.Unasked, left.Due);
                    kept = missing = failed = 0;
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
    }

    /// <summary>The next titles to ask about, in the order the database is asked.</summary>
    private List<long> Due()
    {
        var sweep = Sql.Instant(clock.GetUtcNow() - TimeSpan.FromDays(Math.Max(1, options.Value.SweepDays)));
        return database.Read(c =>
        {
            var due = c.Query("SELECT title_id FROM tmdb_due ORDER BY since LIMIT $n", r => r.GetInt64(0), ("$n", Batch));
            if (due.Count < Batch)
                due.AddRange(c.Query("""
                    SELECT t.id FROM titles t
                    LEFT JOIN ratings r ON r.title_id = t.id AND r.source = 'imdb'
                    WHERE NOT EXISTS (SELECT 1 FROM tmdb_layer l WHERE l.title_id = t.id)
                      AND NOT EXISTS (SELECT 1 FROM tmdb_missing m WHERE m.title_id = t.id AND m.asked_at > $sweep)
                      AND NOT EXISTS (SELECT 1 FROM tmdb_due d WHERE d.title_id = t.id)
                    ORDER BY r.votes DESC NULLS LAST
                    LIMIT $n
                    """, r => r.GetInt64(0), ("$sweep", sweep), ("$n", Batch - due.Count)));
            if (due.Count < Batch)
                due.AddRange(c.Query("""
                    SELECT l.title_id FROM tmdb_layer l
                    WHERE l.fetched_at < $sweep AND NOT EXISTS (SELECT 1 FROM tmdb_due d WHERE d.title_id = l.title_id)
                    ORDER BY l.fetched_at
                    LIMIT $n
                    """, r => r.GetInt64(0), ("$sweep", sweep), ("$n", Batch - due.Count)));
            return due;
        });
    }

    private (long Unasked, long Due) Remaining() => database.Read(c => (
        c.Count("SELECT count(*) FROM titles t WHERE NOT EXISTS (SELECT 1 FROM tmdb_layer l WHERE l.title_id = t.id) AND NOT EXISTS (SELECT 1 FROM tmdb_missing m WHERE m.title_id = t.id)"),
        c.Count("SELECT count(*) FROM tmdb_due")));
}

/// <summary>
/// Once a day, marks due every title the film database's changes feed names since the last pass, and
/// every title whose database rating has come due: what a ranking holds or came out within
/// <see cref="CatalogueOptions.RecentDays"/>, after <see cref="CatalogueOptions.RatingDays"/>, since the
/// feed does not carry votes. The feed reaches back fourteen days at most; anything older is caught by
/// the sweep.
/// </summary>
public sealed class ChangesWorker(
    TmdbSource source,
    CatalogueDatabase database,
    IOptions<CatalogueOptions> options,
    TimeProvider clock,
    ILogger<ChangesWorker> logger) : BackgroundService
{
    public const string ThroughKey = "tmdb_changes_through";

    private static readonly TimeSpan Every = TimeSpan.FromHours(24);
    private static readonly TimeSpan Retry = TimeSpan.FromHours(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!source.IsConfigured) return;
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                var worked = true;
                try
                {
                    await RunOnceAsync(stoppingToken);
                }
                catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !stoppingToken.IsCancellationRequested)
                {
                    worked = false;
                    logger.LogWarning("The film database's changes could not be read: {Reason}", ex.Message);
                }
                await Task.Delay(worked ? Every : Retry, clock, stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
    }

    public async Task RunOnceAsync(CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var today = DateOnly.FromDateTime(now.UtcDateTime);
        var earliest = today.AddDays(-14);
        var from = database.Meta(ThroughKey) is { } through && DateOnly.TryParseExact(through, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day)
            ? day : earliest;
        if (from < earliest) from = earliest;

        // A day at a time: the feed answers at most 500 pages, and a fortnight of films is more. A title
        // is due when its layer is older than the end of the day it changed on.
        var marked = 0;
        foreach (var kind in new[] { "film", "show" })
        for (var date = from; date < today; date = date.AddDays(1))
        {
            var ids = await source.ChangedAsync(kind, date, date.AddDays(1), ct);
            var dayEnd = new DateTimeOffset(date.AddDays(1).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
            marked += database.Write(c =>
            {
                c.Execute("CREATE TEMP TABLE IF NOT EXISTS changed (id INTEGER PRIMARY KEY)");
                c.Execute("DELETE FROM changed");
                using (var insert = c.Command("INSERT OR IGNORE INTO changed (id) VALUES ($id)"))
                {
                    var id = insert.Parameters.Add("$id", Microsoft.Data.Sqlite.SqliteType.Integer);
                    foreach (var changed in ids)
                    {
                        id.Value = changed;
                        insert.ExecuteNonQuery();
                    }
                }
                return c.Execute("""
                    INSERT INTO tmdb_due (title_id, reason, since)
                    SELECT t.id, 'changed', $at FROM titles t JOIN changed ch ON ch.id = t.tmdb_id
                    JOIN tmdb_layer l ON l.title_id = t.id
                    WHERE t.kind = $kind AND l.fetched_at < $dayEnd
                    ON CONFLICT (title_id) DO NOTHING
                    """, ("$at", now), ("$kind", kind), ("$dayEnd", dayEnd));
            });
        }

        var settings = options.Value;
        var rated = database.Write(c => c.Execute("""
            INSERT INTO tmdb_due (title_id, reason, since)
            SELECT t.id, 'rating', $at FROM titles t JOIN tmdb_layer l ON l.title_id = t.id
            WHERE l.fetched_at < $stale
              AND (EXISTS (SELECT 1 FROM rankings k WHERE k.title_id = t.id) OR t.released >= $recent)
            ON CONFLICT (title_id) DO NOTHING
            """,
            ("$at", now),
            ("$stale", now - TimeSpan.FromDays(Math.Max(1, settings.RatingDays))),
            ("$recent", today.AddDays(-Math.Max(1, settings.RecentDays)).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture))));

        database.SetMeta(ThroughKey, today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        logger.LogInformation("Changes from {From} to {To}: {Changed} titles due as changed, {Rated} for their rating.", from, today, marked, rated);
    }
}
