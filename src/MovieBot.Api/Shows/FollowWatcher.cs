using System.Collections.Concurrent;
using System.Globalization;
using Microsoft.Extensions.Options;
using TheKrystalShip.MovieBot.Acquire.Download;
using TheKrystalShip.MovieBot.Acquire.Search;
using TheKrystalShip.MovieBot.Acquire.Tracker;
using TheKrystalShip.MovieBot.Api.Acquisition;
using TheKrystalShip.MovieBot.Api.Library;
using TheKrystalShip.MovieBot.Api.Notifications;
using TheKrystalShip.MovieBot.Api.People;
using TheKrystalShip.MovieBot.Core;

namespace TheKrystalShip.MovieBot.Api.Shows;

public sealed class FollowOptions
{
    public const string Section = "Follow";

    /// <summary>How old a followed show's description may be before the film database is asked again.</summary>
    public int RefreshHours { get; set; } = 24;

    /// <summary>How long after asking the tracker for an episode it is not asked for again.</summary>
    public int AskMinutes { get; set; } = 60;

    /// <summary>How many days past its air date an episode is still looked for.</summary>
    public int GiveUpDays { get; set; } = 14;
}

/// <summary>
/// Tells the people who follow a show that a new episode is out, and fetches it for those who asked
/// for that.
///
/// An episode is due from the day after its air date (<see cref="FollowViews.IsDue"/>) and looked for
/// until <see cref="FollowOptions.GiveUpDays"/> days past it. Only a show with an episode due is asked
/// of the tracker, once per <see cref="FollowOptions.AskMinutes"/> per episode, so the load on the
/// tracker is the handful of episodes airing this fortnight and not the number of follows. An episode
/// counts for a follower only when it comes after the baseline their follow began with, so following a
/// long-running show announces nothing that was already out.
///
/// Once an episode is offered, everybody following it is told and one download is started for the
/// ones who want it fetched, at the best quality any of them watches at. The person the download is
/// started as is told by the download's own notice when it can be watched, so they are not also told
/// the episode is out. What was announced is kept (<see cref="FollowStore"/>), so a restart tells nobody
/// twice, and a notification carries a key besides, which is what makes a pass that stopped halfway
/// safe to run again.
/// </summary>
public sealed class FollowWatcher(
    FollowStore follows,
    ShowStore shows,
    ShowOfferCache offers,
    TitleLibrary library,
    Downloads downloads,
    AcquisitionService acquisition,
    WatchRecord record,
    PersonDirectory people,
    Notifier notifier,
    IOptions<FollowOptions> options,
    TimeProvider clock,
    ILogger<FollowWatcher> logger) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(5);

    private readonly ConcurrentDictionary<string, DateTimeOffset> _asked = new(StringComparer.Ordinal);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval);
        while (await ReadyNotices.WaitAsync(timer, stoppingToken))
        {
            try
            {
                await PassAsync(stoppingToken);
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                logger.LogWarning(ex, "A pass over the shows people follow failed.");
            }
        }
    }

    public async Task PassAsync(CancellationToken ct)
    {
        foreach (var show in follows.Shows())
        {
            try
            {
                await PassAsync(show, ct);
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                // The episode is still due and the next pass finds it.
                logger.LogWarning(ex, "The followers of {Show} could not be told.", show);
            }
        }
    }

    private async Task PassAsync(string show, CancellationToken ct)
    {
        var settings = options.Value;
        var stored = await shows.GetAsync(show, ct, TimeSpan.FromHours(Math.Max(1, settings.RefreshHours)));
        if (stored?.Details is not { } details) return;

        var today = FollowViews.Today(clock);
        var followers = follows.Followers(show);
        var due = stored.Seasons
            .Where(s => s.Number > 0)
            .SelectMany(s => s.Episodes)
            .Where(e => FollowViews.AirDate(e.AirDate) is { } date
                        && FollowViews.IsDue(date, today)
                        && today.DayNumber - date.DayNumber <= Math.Max(1, settings.GiveUpDays)
                        && !follows.IsAnnounced(show, e.Season, e.Number)
                        && followers.Any(f => f.Follow.Covers(e.Season, e.Number)))
            .OrderBy(e => e.Season).ThenBy(e => e.Number)
            .ToList();
        if (due.Count == 0) return;

        IReadOnlyList<DownloadStatus>? running = null;
        ShowReleases? offered = null;
        foreach (var episode in due)
        {
            var number = new EpisodeNumber(episode.Season, episode.Number);
            var audience = followers.Where(f => f.Follow.Covers(episode.Season, episode.Number)).ToList();

            // Somebody else's download, or a film already here: nothing is left to fetch, and the news
            // does not wait on the tracker.
            var here = library.EpisodesOf(show).Any(e => e.Manifest.Episode!.Season == number.Season
                && e.Manifest.Episode.Number == number.Episode && e.Manifest.Status != TitleStatus.Failed);
            if (!here)
            {
                try
                {
                    running ??= await acquisition.ListAsync(ct);
                    here = await NextEpisodes.ArrivingAsync(acquisition, running, show, number, ct);
                }
                catch (QBittorrentException ex)
                {
                    logger.LogDebug(ex, "The downloads could not be read for {Show} {Episode}.", show, number);
                    return;
                }
            }

            if (here)
            {
                await TellAsync(show, details.Title, episode, audience.Select(a => a.UserId), ct);
                follows.Announce(show, episode.Season, episode.Number);
                _asked.TryRemove($"{show}:{number}", out _);
                continue;
            }

            var key = $"{show}:{number}";
            if (_asked.TryGetValue(key, out var at) && clock.GetUtcNow() - at < TimeSpan.FromMinutes(Math.Max(1, settings.AskMinutes)))
                continue;
            _asked[key] = clock.GetUtcNow();

            try
            {
                offered ??= await offers.GetAsync(show, ct);
            }
            catch (TrackerException ex)
            {
                logger.LogDebug(ex, "The tracker could not be asked for {Show} {Episode}.", show, number);
                return;
            }
            if (!ShowOfferCache.Offers(offered, number)) continue;

            var fetchedFor = await FetchAsync(show, details.Title, number, offered, audience, ct);
            await TellAsync(show, details.Title, episode, audience.Select(a => a.UserId).Where(u => u != fetchedFor), ct);
            follows.Announce(show, episode.Season, episode.Number);
            _asked.TryRemove(key, out _);
        }
    }

    /// <summary>
    /// Starts the one download of an episode for the followers who want it fetched, at the best
    /// quality any of them watches at, as the one who followed first. Returns who the download is
    /// for, or null when nobody asked for one or it was refused: a refusal leaves those followers
    /// with the plain notice that the episode is out.
    /// </summary>
    private async Task<string?> FetchAsync(
        string show, string title, EpisodeNumber number, ShowReleases offered,
        List<(string UserId, StoredFollow Follow)> audience, CancellationToken ct)
    {
        var wanting = audience.Where(a => a.Follow.AutoDownload).Select(a => a.UserId).ToList();
        if (wanting.Count == 0) return null;

        var quality = wanting.Select(u => QualityOf(u, show)).Max();
        if (ShowOfferCache.Pick(offered, number, quality) is not { } release) return null;

        // The download's own notice reaches whoever it is started as, so it has to be somebody the
        // torrent can carry: an id the torrent client's tags can name.
        var requester = wanting.FirstOrDefault(u => Downloads.Snowflake(u) is not null);
        if (requester is null) return null;

        var reply = await downloads.StartAsync(new StartDownloadRequest
        {
            TorrentId = release.TorrentId,
            RequestedBy = new Actor(requester, people.NameOf(requester) ?? requester),
            Episodes = [new EpisodePick(number.Season, number.Episode)],
        }, ct);
        logger.LogInformation("Fetching {Show} {Episode} for {Count} follower(s) at {Quality}: {Outcome}{Refusal}.",
            title, number, wanting.Count, quality, reply.Outcome, reply.Refusal is { } why ? $" ({why})" : "");

        return reply.Outcome == "started" ? requester : null;
    }

    /// <summary>
    /// The quality a person watches a show at: 1080p when the latest episode of it they watched has
    /// a picture at least 1800 wide, else 720p, which is also what somebody who has watched none gets.
    /// </summary>
    private Resolution QualityOf(string userId, string show)
    {
        Manifest? latest = null;
        var at = DateTimeOffset.MinValue;
        foreach (var (titleId, entry) in record.Entries(userId))
        {
            if (entry.LastWatchedAt < at || library.Get(titleId) is not { Episode: not null } manifest
                || manifest.Film?.ImdbId != show) continue;
            latest = manifest;
            at = entry.LastWatchedAt;
        }
        return latest?.Video?.Width >= 1800 ? Resolution.Hd1080 : Resolution.Hd720;
    }

    private async Task TellAsync(string show, string title, Acquire.Tmdb.EpisodeDetails episode, IEnumerable<string> recipients, CancellationToken ct)
    {
        var place = $"S{episode.Season.ToString(CultureInfo.InvariantCulture)} · E{episode.Number.ToString(CultureInfo.InvariantCulture)}";
        foreach (var person in recipients)
        {
            await notifier.TellAsync(person, new NotificationDraft
            {
                Kind = NotificationKinds.Episode,
                Key = $"episode:{show}:{FollowStore.Label(episode.Season, episode.Number)}",
                Text = $"{title} {place} is out.",
                Url = $"/show/{Uri.EscapeDataString(show)}?season={episode.Season.ToString(CultureInfo.InvariantCulture)}",
                Action = "Watch",
            }, ct);
        }
    }
}
