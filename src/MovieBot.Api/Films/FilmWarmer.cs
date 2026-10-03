using TheKrystalShip.MovieBot.Api.Acquisition;
using TheKrystalShip.MovieBot.Api.Library;

namespace TheKrystalShip.MovieBot.Api.Films;

/// <summary>
/// Keeps every film in the library, and every film somebody is waiting on, described.
///
/// A listing reads only what is kept, so it never waits on the network; this is what makes sure
/// something is kept. It runs shortly after start and then every few hours, asking the database
/// about each film whose description is missing or due, one at a time, so a large library costs a
/// slow trickle of requests rather than a burst.
/// </summary>
public sealed class FilmWarmer(
    FilmStore store,
    TitleLibrary library,
    WishList wishes,
    ILogger<FilmWarmer> logger) : BackgroundService
{
    private static readonly TimeSpan FirstPass = TimeSpan.FromSeconds(20);
    private static readonly TimeSpan Every = TimeSpan.FromHours(6);
    private static readonly TimeSpan Between = TimeSpan.FromMilliseconds(300);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await Task.Delay(FirstPass, stoppingToken);
            while (!stoppingToken.IsCancellationRequested)
            {
                await PassAsync(stoppingToken);
                await Task.Delay(Every, stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
    }

    public async Task PassAsync(CancellationToken ct)
    {
        var ids = library.List().Select(t => t.Film?.ImdbId)
            .Concat(wishes.All().Select(w => (string?)w.ImdbId))
            .Where(FilmStore.IsImdbId)
            .Select(id => id!)
            .Distinct()
            .ToList();

        var asked = 0;
        foreach (var id in ids)
        {
            if (store.Held(id) is { } held && !store.IsDue(held)) continue;
            await store.GetAsync(id, ct);
            asked++;
            await Task.Delay(Between, ct);
        }
        if (asked > 0) logger.LogInformation("Described {Asked} of {Films} films from the film database.", asked, ids.Count);
    }
}
