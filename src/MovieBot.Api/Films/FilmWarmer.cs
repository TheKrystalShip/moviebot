using TheKrystalShip.MovieBot.Api.Acquisition;
using TheKrystalShip.MovieBot.Api.Library;

namespace TheKrystalShip.MovieBot.Api.Films;

/// <summary>
/// Keeps every film and show in the library, every film with a wish, and every title anybody wants
/// described.
///
/// A listing reads only what is kept, so it never waits on the network; this is what makes sure
/// something is kept. It runs shortly after start and then every few hours, asking the database
/// about each film whose description is missing or due, one at a time, so a large library costs a
/// slow trickle of requests rather than a burst. Each film's facts from Wikidata (<see cref="WikidataFacts"/>)
/// are kept the same way.
/// </summary>
public sealed class FilmWarmer(
    FilmStore store,
    Shows.ShowStore shows,
    TitleLibrary library,
    WishList wishes,
    Wants.WantStore wants,
    WikidataFacts facts,
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
        var titles = library.List();
        var wanted = wants.All();

        // An episode names its show, which is described as a show and never as a film.
        var ids = titles.Where(t => t.Kind != Core.TitleKind.Show).Select(t => t.Film?.ImdbId)
            .Concat(wishes.All().Select(w => (string?)w.ImdbId))
            .Concat(wanted.Where(w => w.Kind == Wants.WantKinds.Film).Select(w => (string?)w.ImdbId))
            .Where(FilmStore.IsImdbId)
            .Select(id => id!)
            .Distinct()
            .ToList();

        var asked = 0;
        foreach (var id in ids)
        {
            if (store.Held(id) is not { } held || store.IsDue(held))
            {
                await store.GetAsync(id, ct);
                asked++;
                await Task.Delay(Between, ct);
            }

            if (facts.IsDue(id))
            {
                await facts.AskAsync(id, ct);
                await Task.Delay(Between, ct);
            }
        }
        if (asked > 0) logger.LogInformation("Described {Asked} of {Films} films from the film database.", asked, ids.Count);

        var showIds = titles.Where(t => t.Kind == Core.TitleKind.Show).Select(t => t.Film?.ImdbId)
            .Concat(wanted.Where(w => w.Kind == Wants.WantKinds.Show).Select(w => (string?)w.ImdbId))
            .Where(FilmStore.IsImdbId)
            .Select(id => id!)
            .Distinct()
            .ToList();

        var askedShows = 0;
        foreach (var id in showIds)
        {
            if (shows.Held(id) is { } held && !shows.IsDue(held)) continue;
            await shows.GetAsync(id, ct);
            askedShows++;
            await Task.Delay(Between, ct);
        }
        if (askedShows > 0)
            logger.LogInformation("Described {Asked} of {Shows} shows from the film database.", askedShows, showIds.Count);
    }
}
