using System.Collections.Concurrent;
using TheKrystalShip.MovieBot.Acquire.Search;
using TheKrystalShip.MovieBot.Api.Acquisition;

namespace TheKrystalShip.MovieBot.Api.Shows;

/// <summary>
/// What the tracker offers for each show, kept a few minutes.
///
/// A show's page asks on every visit, every episode on it can be played from what it says, and the
/// prefetcher asks while somebody watches: one search per show per few minutes answers all of them
/// without spending the tracker's per-member call allowance on the same question. Every release in
/// an answer is remembered as offered, so a download can name it.
/// </summary>
public sealed class ShowOfferCache(IShowSearch search, OfferedReleases offered, TimeProvider clock)
{
    public static readonly TimeSpan KeptFor = TimeSpan.FromMinutes(10);

    private readonly ConcurrentDictionary<string, (ShowReleases Releases, DateTimeOffset At)> _byShow =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The show's releases, asked of the tracker when none are kept or they are old.</summary>
    public async Task<ShowReleases> GetAsync(string showImdbId, CancellationToken ct)
    {
        if (!_byShow.TryGetValue(showImdbId, out var kept) || clock.GetUtcNow() - kept.At > KeptFor)
        {
            kept = (await search.ByShowAsync(showImdbId, ct), clock.GetUtcNow());
            _byShow[showImdbId] = kept;
        }

        offered.Remember(kept.Releases.Seasons.SelectMany(s => s.Packs.Concat(s.Episodes.SelectMany(e => e.Releases))));
        return kept.Releases;
    }

    /// <summary>
    /// The release one episode is fetched from at a quality: the season's pack at it, picking the
    /// one file; else a release of the episode alone at it; else either at whatever quality the
    /// tracker has. Null when nothing offered holds the episode.
    /// </summary>
    public static Release? Pick(ShowReleases releases, EpisodeNumber episode, Resolution quality)
    {
        if (releases.Seasons.FirstOrDefault(s => s.Season == episode.Season) is not { } season) return null;
        return season.PackAt(quality)
            ?? season.EpisodeAt(episode.Episode, quality)
            ?? season.Packs.FirstOrDefault()
            ?? season.Episodes.FirstOrDefault(e => e.Episode == episode.Episode)?.Releases.FirstOrDefault();
    }

    /// <summary>Whether the tracker offers anything holding the episode.</summary>
    public static bool Offers(ShowReleases releases, EpisodeNumber episode) =>
        Pick(releases, episode, Resolution.Hd720) is not null;
}
