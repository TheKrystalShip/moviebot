using Microsoft.Extensions.Options;
using TheKrystalShip.MovieBot.Acquire.Download;
using TheKrystalShip.MovieBot.Acquire.Search;
using TheKrystalShip.MovieBot.Acquire.Tracker;
using TheKrystalShip.MovieBot.Api.Acquisition;
using TheKrystalShip.MovieBot.Api.Library;
using TheKrystalShip.MovieBot.Api.Notifications;
using TheKrystalShip.MovieBot.Api.People;
using TheKrystalShip.MovieBot.Api.Sessions;
using TheKrystalShip.MovieBot.Api.Shows;
using TheKrystalShip.MovieBot.Core;

namespace TheKrystalShip.MovieBot.Api.Wants;

public sealed class WantOptions
{
    public const string Section = "Wants";

    /// <summary>
    /// Whether what people want is fetched ahead of them. Off, the prefetcher fetches only the titles of
    /// fixed plans.
    /// </summary>
    public bool Prefetch { get; set; } = true;

    /// <summary>Seconds between passes of the prefetcher.</summary>
    public int PassSeconds { get; set; } = 60;

    /// <summary>How many downloads the prefetcher may have arriving at once.</summary>
    public int Concurrent { get; set; } = 1;

    /// <summary>
    /// Days a want keeps its title in the library for the person who has not watched it. A want older
    /// than this keeps nothing, and fetches nothing, by itself.
    /// </summary>
    public int HoldDays { get; set; } = 90;

    /// <summary>How long after the tracker offered a title nothing it is not asked about it again.</summary>
    public int AskMinutes { get; set; } = 60;
}

/// <summary>A title people want, and the people it can be fetched for.</summary>
/// <param name="Wanters">How many people want it, the ones who keep their want to themselves included.</param>
/// <param name="Fetchers">The people whose want lets the server fetch it for them, in the order they wanted it.</param>
/// <param name="Oldest">When the first of the wants was made.</param>
/// <param name="PlannedAt">When the earliest plan fixed for the title begins; null when none is fixed.</param>
/// <param name="Episode">For a show, the episode that plan starts at; null leaves it to the wanters' next episodes.</param>
public sealed record WantCandidate(
    string ImdbId, string Kind, int Wanters, IReadOnlyList<string> Fetchers, DateTimeOffset Oldest,
    DateTimeOffset? PlannedAt = null, WantEpisode? Episode = null);

/// <summary>
/// The rules that decide whether, and what, the prefetcher fetches. Pure, so each is settled by
/// the facts handed in and nothing is assumed.
/// </summary>
public static class WantFetchRules
{
    /// <summary>
    /// The titles that can be fetched: those with at least one want whose token let the server spend.
    /// </summary>
    public static List<WantCandidate> Candidates(IEnumerable<StoredWant> wants) => [.. wants
        .GroupBy(w => w.ImdbId, StringComparer.Ordinal)
        .Where(g => g.Any(w => w.MayFetch))
        .Select(g => new WantCandidate(
            g.Key, g.First().Kind, g.Count(),
            [.. g.Where(w => w.MayFetch).OrderBy(w => w.Since).ThenBy(w => w.UserId, StringComparer.Ordinal).Select(w => w.UserId)],
            g.Min(w => w.Since)))];

    /// <summary>
    /// The candidates with the titles of fixed plans in them. A title somebody wants carries the
    /// earliest time a plan fixed for it begins, and the episode that plan starts at; a title only a
    /// plan names is a candidate in its own right, fetched for the plan's maker, whose permission the
    /// plan captured. The maker is a fetcher of every title their plan names.
    /// </summary>
    public static List<WantCandidate> WithPlans(IEnumerable<WantCandidate> candidates, IEnumerable<Plans.PlannedTitle> planned)
    {
        var all = candidates.ToDictionary(c => c.ImdbId, StringComparer.Ordinal);
        foreach (var plan in planned.OrderBy(p => p.At).ThenBy(p => p.MadeAt))
        {
            var episode = plan is { Kind: WantKinds.Show, Season: { } season, Number: { } number }
                ? new WantEpisode { Season = season, Episode = number }
                : null;
            if (!all.TryGetValue(plan.ImdbId, out var held))
            {
                all[plan.ImdbId] = new WantCandidate(plan.ImdbId, plan.Kind, 0, [plan.MadeBy], plan.MadeAt, plan.At, episode);
                continue;
            }
            if (held.PlannedAt is not null) continue;
            all[plan.ImdbId] = held with
            {
                PlannedAt = plan.At, Episode = episode,
                Fetchers = held.Fetchers.Contains(plan.MadeBy) ? held.Fetchers : [.. held.Fetchers, plan.MadeBy],
            };
        }
        return [.. all.Values];
    }

    /// <summary>
    /// The order titles are tried in: one with a fixed plan first, the earliest plan first; then the
    /// most wanted; then the one wanted longest.
    /// </summary>
    public static List<WantCandidate> Order(IEnumerable<WantCandidate> candidates) => [.. candidates
        .OrderBy(c => c.PlannedAt is null)
        .ThenBy(c => c.PlannedAt)
        .ThenByDescending(c => c.Wanters)
        .ThenBy(c => c.Oldest)
        .ThenBy(c => c.ImdbId, StringComparer.Ordinal)];

    /// <summary>
    /// Why nothing may be started now, or null when the pass may go on: somebody is watching, a Watch
    /// now download is arriving, or the prefetcher already has as many arriving as it may.
    /// </summary>
    public static string? Withhold(bool somebodyPlaying, int watchNowArriving, int prefetchArriving, int concurrent)
    {
        if (somebodyPlaying) return "a room is playing";
        if (watchNowArriving > 0) return "a download is arriving for a room";
        if (prefetchArriving >= Math.Max(1, concurrent)) return "the prefetcher's downloads are still arriving";
        return null;
    }

    /// <summary>
    /// Whether the disk takes <paramref name="bytes"/> more: the budget reads Ok and the headroom it
    /// reports, which already keeps the transcode reserve and what running transcodes have still to
    /// write, holds the release.
    /// </summary>
    public static bool Fits(BudgetReading budget, long bytes) =>
        budget.State == BudgetState.Ok && bytes <= budget.HeadroomBytes;

    /// <summary>The best quality any of the people watches at; 720p when there is nobody to ask.</summary>
    public static Resolution Quality(IEnumerable<Resolution> watchedAt) =>
        watchedAt.DefaultIfEmpty(Resolution.Hd720).Max();

    /// <summary>The earliest episode any of the people is up to; null when none has one.</summary>
    public static WantEpisode? Earliest(IEnumerable<WantEpisode?> next) => next
        .OfType<WantEpisode>()
        .OrderBy(e => e.Season).ThenBy(e => e.Episode)
        .FirstOrDefault();
}

/// <summary>
/// Fetches what people want to watch, ahead of them, when it costs nobody anything: no room is playing,
/// the disk has room for it and the tracker offers a digital release. At most one download is started
/// per pass, for the first title in <see cref="WantFetchRules.Order"/> that can be fetched.
///
/// Only wants whose token held <c>cinema:download</c> are fetched (<see cref="StoredWant.MayFetch"/>),
/// the way a follow's permission is captured when it is made. A film is fetched at the best quality any
/// of its wanters watches at, as <see cref="WatchNow"/> picks it; a show, the episode that comes first
/// among its wanters' next episodes, out of the season's pack where there is one so the season stays
/// one torrent. The torrent carries <see cref="TorrentTags.Want"/> and the first wanter as its
/// requester, and <see cref="ReadyNotices"/> tells every wanter once it can be watched.
///
/// A title the tracker offers nothing for is not asked about again for <see cref="WantOptions.AskMinutes"/>,
/// and a show is asked of the tracker through <see cref="ShowOfferCache"/>, so prefetch adds no tracker
/// load beyond what the rate limits allow. A download already running carries on when somebody starts
/// watching: the transcode planner puts rooms first.
/// </summary>
public sealed class WantFetcher(
    WantStore wants,
    Plans.PlanStore plans,
    LibraryListing listing,
    TitleLibrary library,
    WatchRecord record,
    WatchNow watchNow,
    ShowOfferCache showOffers,
    ShowStore shows,
    OfferedReleases offered,
    Downloads downloads,
    AcquisitionService acquisition,
    SessionStore sessions,
    PersonDirectory people,
    IOptions<WantOptions> options,
    TimeProvider clock,
    ILogger<WantFetcher> logger) : BackgroundService
{
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, DateTimeOffset> _askedEmpty = new(StringComparer.Ordinal);

    /// <summary>Whether the tracker was asked about the title lately and offered nothing at the source floor.</summary>
    public bool AskedEmpty(string imdbId) =>
        _askedEmpty.TryGetValue(imdbId, out var at) && clock.GetUtcNow() - at < TimeSpan.FromMinutes(Math.Max(1, options.Value.AskMinutes));

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(Math.Max(5, options.Value.PassSeconds)));
        while (await ReadyNotices.WaitAsync(timer, stoppingToken))
        {
            try
            {
                await PassAsync(stoppingToken);
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                logger.LogWarning(ex, "A pass over the wants to fetch failed.");
            }
        }
    }

    /// <summary>One pass. Returns the title a download was started for, or null when none was.</summary>
    public async Task<string?> PassAsync(CancellationToken ct)
    {
        var settings = options.Value;
        var candidates = WantFetchRules.Order(WantFetchRules.WithPlans(
            settings.Prefetch ? WantFetchRules.Candidates(wants.All()) : [], plans.Planned(clock.GetUtcNow())));
        if (candidates.Count == 0) return null;

        IReadOnlyList<DownloadStatus> all;
        BudgetReading budget;
        try
        {
            all = await acquisition.ListAsync(ct);
            budget = acquisition.Budget();
        }
        catch (Exception ex) when (ex is QBittorrentException or IOException or UnauthorizedAccessException)
        {
            // A state that cannot be measured is not assumed to be fine.
            logger.LogDebug(ex, "The downloads or the disk could not be read for the wants.");
            return null;
        }

        var arriving = all.Count(d => !d.IsFinished && d.Tags.Any(t => TorrentTags.ReadWant(t) is not null));
        if (WantFetchRules.Withhold(
                sessions.Viewing().Any(r => r.Playing), watchNow.Slots(all).Used, arriving, settings.Concurrent) is { } reason)
        {
            logger.LogDebug("The wants wait: {Reason}.", reason);
            return null;
        }

        var titles = listing.Titles();
        var now = clock.GetUtcNow();
        foreach (var candidate in candidates)
        {
            if (_askedEmpty.TryGetValue(candidate.ImdbId, out var at)
                && now - at < TimeSpan.FromMinutes(Math.Max(1, settings.AskMinutes))) continue;

            try
            {
                if (await TryFetchAsync(candidate, titles, all, budget, ct)) return candidate.ImdbId;
            }
            catch (TrackerException ex)
            {
                // One tracker failure is every tracker failure; the rest of the pass would repeat it.
                logger.LogDebug(ex, "The tracker could not be asked about {Imdb}.", candidate.ImdbId);
                return null;
            }
        }
        return null;
    }

    private async Task<bool> TryFetchAsync(
        WantCandidate candidate, IReadOnlyList<TitleSummary> titles, IReadOnlyList<DownloadStatus> all,
        BudgetReading budget, CancellationToken ct)
    {
        var imdb = candidate.ImdbId;
        var isShow = candidate.Kind == WantKinds.Show;

        EpisodeNumber? episode = null;
        if (isShow)
        {
            var stored = shows.Held(imdb);
            var next = candidate.Episode;
            if (next is null)
            {
                var finished = candidate.Fetchers.ToDictionary(
                    u => u, u => ShowProgress.Finished(titles, record.Entries(u)).GetValueOrDefault(imdb), StringComparer.Ordinal);
                next = WantFetchRules.Earliest(candidate.Fetchers.Select(u => ShowProgress.Next(stored, finished[u])));
            }
            if (next is null) return false;

            episode = new EpisodeNumber(next.Season, next.Episode);
            if (titles.Any(t => t is { Kind: TitleKind.Show, Episode: { } e } && t.Status != TitleStatus.Failed
                    && string.Equals(t.Film?.ImdbId, imdb, StringComparison.Ordinal)
                    && e.Season == next.Season && e.Number == next.Episode)) return false;
            if (await NextEpisodes.ArrivingAsync(acquisition, all, imdb, episode.Value, ct)) return false;
        }
        else
        {
            if (titles.Any(t => t.Kind != TitleKind.Show && t.Status != TitleStatus.Failed
                    && string.Equals(t.Film?.ImdbId, imdb, StringComparison.OrdinalIgnoreCase))) return false;
            if (WatchNow.DownloadOf(all, imdb) is not null) return false;
        }

        var quality = WantFetchRules.Quality(candidate.Fetchers.Select(WatchedAt));
        var release = episode is { } wanted
            ? ShowOfferCache.Pick(await showOffers.GetAsync(imdb, ct), wanted, quality,
                original: shows.Held(imdb)?.Details?.OriginalLanguage)
            : await watchNow.BestReleaseAsync(imdb, quality, ct);
        if (release is null)
        {
            _askedEmpty[imdb] = clock.GetUtcNow();
            return false;
        }
        _askedEmpty.TryRemove(imdb, out _);

        if (!WantFetchRules.Fits(budget, release.IsEpisodic ? release.SizePerEpisode : release.SizeBytes))
        {
            logger.LogDebug("{Release} does not fit the disk now.", release.ReleaseName);
            return false;
        }

        offered.Remember([release]);
        var requester = candidate.Fetchers[0];
        var reply = await downloads.StartAsync(new StartDownloadRequest
        {
            TorrentId = release.TorrentId,
            Episodes = episode is { } e ? [new EpisodePick(e.Season, e.Episode)] : null,
        }, new Actor(requester, people.NameOf(requester) ?? requester), ct, wanted: imdb);

        logger.LogInformation("Fetching {Release} for {Count} wanter(s) of {Imdb} at {Quality}: {Outcome}{Refusal}.",
            release.ReleaseName, candidate.Fetchers.Count, imdb, quality, reply.Outcome,
            reply.Refusal is { } why ? $" ({why})" : "");
        return reply.Outcome == "started";
    }

    /// <summary>
    /// The quality a person watches at: 1080p when the latest title of theirs the library holds has a
    /// picture at least 1800 wide, else 720p, which is also what somebody who has watched nothing gets.
    /// </summary>
    private Resolution WatchedAt(string userId)
    {
        Manifest? latest = null;
        var at = DateTimeOffset.MinValue;
        foreach (var (titleId, entry) in record.Entries(userId))
        {
            if (entry.LastWatchedAt < at || library.Get(titleId) is not { } manifest) continue;
            latest = manifest;
            at = entry.LastWatchedAt;
        }
        return latest?.Video?.Width >= 1800 ? Resolution.Hd1080 : Resolution.Hd720;
    }
}
