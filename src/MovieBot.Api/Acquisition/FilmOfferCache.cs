using System.Collections.Concurrent;
using TheKrystalShip.MovieBot.Acquire.Search;

namespace TheKrystalShip.MovieBot.Api.Acquisition;

/// <summary>
/// What the tracker offers for each film, kept a few minutes.
///
/// A film's page asks whether it can be watched on every visit, and so does every want made for it:
/// asked of the tracker each time, the answer costs up to two seconds and a call from the tracker's
/// per-member allowance. One search per film per few minutes answers all of them. The wish sweep asks
/// the tracker itself, since noticing a release the moment it appears is its whole purpose, and
/// keeps what it heard here, so a page read after a pass is as fresh as the pass.
/// </summary>
public sealed class FilmOfferCache(IReleaseSearch search, TimeProvider clock)
{
    public static readonly TimeSpan KeptFor = TimeSpan.FromMinutes(10);

    private readonly ConcurrentDictionary<string, (RankedReleases Ranked, DateTimeOffset At)> _byFilm =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The film's releases, asked of the tracker when none are kept or they are old.</summary>
    public async Task<RankedReleases> GetAsync(string imdbId, CancellationToken ct)
    {
        if (_byFilm.TryGetValue(imdbId, out var kept) && clock.GetUtcNow() - kept.At <= KeptFor) return kept.Ranked;

        var ranked = await search.ByImdbAsync(imdbId, ct);
        Keep(imdbId, ranked);
        return ranked;
    }

    /// <summary>Keeps an answer the tracker just gave for the film, and forgets every answer gone stale.</summary>
    public void Keep(string imdbId, RankedReleases ranked)
    {
        var now = clock.GetUtcNow();
        _byFilm[imdbId] = (ranked, now);
        foreach (var (film, (_, at)) in _byFilm)
            if (now - at > KeptFor) _byFilm.TryRemove(film, out _);
    }
}
