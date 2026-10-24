using Microsoft.Extensions.Options;

namespace TheKrystalShip.MovieBot.Api.Films;

/// <summary>
/// Keeps the sets of films the Discover shelves are made of from Wikidata. A pass every
/// <see cref="WikidataOptions.WarmMinutes"/> asks only for the sets that have come due, so each is asked again once every <see cref="WikidataOptions.RefreshDays"/>,
/// one after another and slowly. A page never asks Wikidata for a shelf: it reads what this kept.
/// </summary>
public sealed class WikidataWarmer(
    WikidataSets sets,
    IOptions<WikidataOptions> options,
    ILogger<WikidataWarmer> logger) : BackgroundService
{
    private static readonly TimeSpan FirstPass = TimeSpan.FromSeconds(45);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.Enabled || options.Value.WarmMinutes <= 0) return;
        try
        {
            await Task.Delay(FirstPass, stoppingToken);
            while (!stoppingToken.IsCancellationRequested)
            {
                var unanswered = await sets.WarmAsync(stoppingToken);
                if (unanswered > 0)
                    logger.LogWarning("{Unanswered} of {Sets} Wikidata sets could not be answered.", unanswered, WikidataSets.Fixed.Count);
                await Task.Delay(TimeSpan.FromMinutes(options.Value.WarmMinutes), stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
    }
}
