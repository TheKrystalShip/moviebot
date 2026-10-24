using System.Collections.Concurrent;
using Microsoft.Extensions.Options;

namespace TheKrystalShip.MovieBot.Api.Films;

/// <summary>
/// What Wikidata says of a film, by its IMDb id: the awards it won, how many it was nominated for and
/// the work it is based on. A page reads what is kept (<see cref="Of"/>) and never waits: a film with
/// nothing kept, or kept longer than <see cref="WikidataOptions.RefreshDays"/>, is asked about in the
/// background (<see cref="Want"/>), and <see cref="FilmWarmer"/> asks for every film it describes.
/// A host that cannot reach Wikidata shows a film without them.
/// </summary>
public sealed class WikidataFacts(
    AnswerKeeper keeper,
    IOptions<WikidataOptions> options,
    IServiceScopeFactory scopes,
    TimeProvider clock,
    ILogger<WikidataFacts> logger)
{
    /// <summary>How long after a question went unanswered a film is asked about again.</summary>
    private static readonly TimeSpan RetryAfter = TimeSpan.FromHours(1);

    private readonly ConcurrentDictionary<string, DateTimeOffset> _tried = new(StringComparer.Ordinal);

    public bool Enabled => options.Value.Enabled;

    private static string NameOf(string imdbId) => $"wikidata/facts-{imdbId}";

    private TimeSpan Keep => TimeSpan.FromDays(Math.Max(1, options.Value.RefreshDays));

    /// <summary>What is kept for the film; null while nothing is.</summary>
    public WikidataTitleFacts? Of(string imdbId) =>
        Enabled && FilmStore.IsImdbId(imdbId) ? keeper.Kept(NameOf(imdbId), WikidataJson.Default.WikidataTitleFacts)?.Value : null;

    /// <summary>Whether the film has no facts kept or they are old, and a question for it has not just gone unanswered.</summary>
    public bool IsDue(string imdbId)
    {
        if (!Enabled || !FilmStore.IsImdbId(imdbId)) return false;
        var now = clock.GetUtcNow();
        if (_tried.TryGetValue(imdbId, out var tried) && now - tried < RetryAfter) return false;
        var held = keeper.Kept(NameOf(imdbId), WikidataJson.Default.WikidataTitleFacts);
        return held?.Value is null || now - held.FetchedAt > Keep;
    }

    /// <summary>Asks Wikidata about the film now and keeps the answer.</summary>
    public async Task AskAsync(string imdbId, CancellationToken ct)
    {
        _tried[imdbId] = clock.GetUtcNow();
        var kept = await keeper.KeptAsync(NameOf(imdbId), WikidataJson.Default.WikidataTitleFacts, Keep,
            async (_, token) =>
            {
                using var scope = scopes.CreateScope();
                return await scope.ServiceProvider.GetRequiredService<WikidataClient>().FactsAsync(imdbId, token);
            }, null, ct);
        if (kept?.Value is not null) _tried.TryRemove(imdbId, out _);
    }

    /// <summary>Has the film's facts asked for in the background if they are due; the caller does not wait.</summary>
    public void Want(string imdbId)
    {
        if (!IsDue(imdbId)) return;
        _ = Task.Run(async () =>
        {
            try
            {
                await AskAsync(imdbId, CancellationToken.None);
            }
            catch (Exception ex)
            {
                logger.LogWarning("Wikidata could not be asked about {Imdb}: {Reason}", imdbId, ex.Message);
            }
        });
    }
}
