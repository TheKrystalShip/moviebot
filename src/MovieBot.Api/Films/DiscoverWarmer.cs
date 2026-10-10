using System.Diagnostics;
using Microsoft.Extensions.Options;

namespace TheKrystalShip.MovieBot.Api.Films;

/// <summary>
/// Asks every Discover shelf that is the same for everybody again before anybody does, so the page
/// reads what is kept and never waits on the database. A shelf's answer is kept for a day or for six
/// hours, so a pass every <see cref="FilmStoreOptions.ShelvesWarmMinutes"/> asks only for what has
/// come due, one shelf at a time. Zero minutes asks nothing ahead.
/// </summary>
public sealed class DiscoverWarmer(
    DiscoverShelves shelves,
    IOptions<FilmStoreOptions> options,
    ILogger<DiscoverWarmer> logger) : BackgroundService
{
    /// <summary>After the week's trending list has had its own first pass.</summary>
    private static readonly TimeSpan FirstPass = TimeSpan.FromMinutes(2);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var minutes = options.Value.ShelvesWarmMinutes;
        if (minutes <= 0) return;
        try
        {
            await Task.Delay(FirstPass, stoppingToken);
            while (!stoppingToken.IsCancellationRequested)
            {
                var clock = Stopwatch.StartNew();
                var unanswered = await shelves.WarmAsync(stoppingToken);
                if (unanswered > 0)
                    logger.LogWarning("{Unanswered} of {Shelves} Discover shelves could not be answered by the film database.", unanswered, DiscoverShelves.Fixed.Count);
                else
                    logger.LogDebug("Kept the Discover shelves fresh in {Seconds:0.0}s.", clock.Elapsed.TotalSeconds);
                await Task.Delay(TimeSpan.FromMinutes(minutes), stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
    }
}
