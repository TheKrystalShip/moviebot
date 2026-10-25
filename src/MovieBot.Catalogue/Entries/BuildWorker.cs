using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TheKrystalShip.MovieBot.Catalogue.Data;

namespace TheKrystalShip.MovieBot.Catalogue.Entries;

/// <summary>
/// Builds again the entries a layer change marked due without building them itself (an IMDb import
/// marks every title whose basics moved), a small batch to a transaction with a pause after each, so
/// the layers being fetched meanwhile are written between batches rather than after the last.
/// </summary>
public sealed class BuildWorker(CatalogueDatabase database, EntryBuilder builder, TimeProvider clock, ILogger<BuildWorker> logger) : BackgroundService
{
    private const int Batch = 100;
    private static readonly TimeSpan Idle = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan Between = TimeSpan.FromMilliseconds(250);

    public const string RulesKey = "entry_rules";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        QueueForRules();
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                var built = 0;
                while (DrainOnce() is var count and > 0)
                {
                    built += count;
                    await Task.Delay(Between, clock, stoppingToken);
                }
                if (built > 0) logger.LogInformation("Built {Count} entries again.", built);
                await Task.Delay(Idle, clock, stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
    }

    /// <summary>
    /// Marks every title due when the entries were built under other rules (<see cref="EntryBuilder.Rules"/>);
    /// returns whether it did. Each build then runs, since the rules are part of every fingerprint.
    /// </summary>
    public bool QueueForRules() => database.Write(c =>
    {
        if (c.First("SELECT value FROM meta WHERE key = $key", r => r.GetString(0), ("$key", RulesKey)) == EntryBuilder.Rules) return false;
        var queued = c.Execute("INSERT OR IGNORE INTO build_due (title_id) SELECT id FROM titles");
        CatalogueDatabase.SetMeta(c, RulesKey, EntryBuilder.Rules);
        logger.LogInformation("Entry rules {Rules}: {Count} entries due to be built again.", EntryBuilder.Rules, queued);
        return true;
    });

    /// <summary>Builds one batch of due entries; returns how many.</summary>
    public int DrainOnce() => database.Write(c =>
    {
        var due = c.Query("SELECT title_id FROM build_due LIMIT $n", r => r.GetInt64(0), ("$n", Batch));
        // A build whose inputs did not move is skipped by the builder itself.
        foreach (var id in due)
        {
            builder.Build(c, id);
            c.Execute("DELETE FROM build_due WHERE title_id = $id", ("$id", id));
        }
        return due.Count;
    });
}
