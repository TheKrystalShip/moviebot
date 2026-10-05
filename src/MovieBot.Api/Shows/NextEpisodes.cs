using System.Collections.Concurrent;
using TheKrystalShip.MovieBot.Acquire.Download;
using TheKrystalShip.MovieBot.Acquire.Search;
using TheKrystalShip.MovieBot.Acquire.Tracker;
using TheKrystalShip.MovieBot.Api.Acquisition;
using TheKrystalShip.MovieBot.Api.Library;
using TheKrystalShip.MovieBot.Api.Sessions;
using TheKrystalShip.MovieBot.Core;

namespace TheKrystalShip.MovieBot.Api.Shows;

/// <summary>
/// Fetches the episode after the one a room is watching, so next has something to play.
///
/// One episode ahead and no further: that is enough on a connection that lands an episode well within
/// one, and an episode fetched and never watched is disk spent for a season's stay. It comes at the
/// quality of the episode being watched, read from the picture's width, out of the season's pack where
/// there is one so a season stays one torrent. Nobody is named as having asked, so nobody is told it is
/// ready: next is how it is found.
/// </summary>
public sealed class NextEpisodes(
    SessionStore sessions,
    TitleLibrary library,
    ShowStore shows,
    ShowOfferCache offers,
    Downloads downloads,
    AcquisitionService acquisition,
    TimeProvider clock,
    ILogger<NextEpisodes> logger) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(30);

    /// <summary>How long an episode asked for is not asked for again while the hand-off catches up with it.</summary>
    private static readonly TimeSpan Asked = TimeSpan.FromMinutes(30);

    private readonly ConcurrentDictionary<string, DateTimeOffset> _asked = new(StringComparer.Ordinal);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval);
        while (await Notifications.ReadyNotices.WaitAsync(timer, stoppingToken))
        {
            try
            {
                await PassAsync(stoppingToken);
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                logger.LogWarning(ex, "A pass for the episodes after the ones being watched failed.");
            }
        }
    }

    public async Task PassAsync(CancellationToken ct)
    {
        IReadOnlyList<DownloadStatus>? all = null;
        foreach (var room in sessions.Viewing())
        {
            if (library.Get(room.TitleId) is not { Episode: { } episode, Film.ImdbId: { Length: > 0 } show } manifest) continue;
            if (shows.EpisodeAfter(show, episode) is not { } after) continue;

            var next = new EpisodeNumber(after.Season, after.Number);
            var key = $"{show}:{next}";
            if (_asked.TryGetValue(key, out var at) && clock.GetUtcNow() - at < Asked) continue;
            if (library.EpisodesOf(show).Any(e => e.Manifest.Episode!.Season == next.Season
                    && e.Manifest.Episode.Number == next.Episode && e.Manifest.Status != TitleStatus.Failed)) continue;

            all ??= await acquisition.ListAsync(ct);
            if (await ArrivingAsync(acquisition, all, show, next, ct)) continue;

            var quality = manifest.Video?.Width >= 1800 ? Resolution.Hd1080 : Resolution.Hd720;
            Release? release;
            try
            {
                release = ShowOfferCache.Pick(await offers.GetAsync(show, ct), next, quality);
            }
            catch (TrackerException ex)
            {
                logger.LogDebug(ex, "The tracker could not be asked for {Show} {Episode}.", show, next);
                continue;
            }

            _asked[key] = clock.GetUtcNow();
            if (release is null) continue;

            var reply = await downloads.StartAsync(new StartDownloadRequest
            {
                TorrentId = release.TorrentId,
                Episodes = [new EpisodePick(next.Season, next.Episode)],
            }, asker: null, ct);
            logger.LogInformation("Fetching {Show} {Episode} ahead of room {Room}: {Outcome}.",
                manifest.Film.Name ?? show, next, room.SessionId, reply.Outcome);
        }
    }

    /// <summary>Whether a torrent of the show is already fetching the episode.</summary>
    internal static async Task<bool> ArrivingAsync(
        AcquisitionService acquisition, IReadOnlyList<DownloadStatus> all, string show, EpisodeNumber episode, CancellationToken ct)
    {
        foreach (var download in all.Where(d => d.Tags.Select(TorrentTags.ReadImdb).Any(i => i == show)))
        {
            var files = await acquisition.FilesAsync(download.Hash, ct);
            var mapped = EpisodeFiles.Map(files.Select(f => (f.Name, f.Size)), EpisodeMarker.Read(download.Name));
            if (mapped.FirstOrDefault(f => f.Episodes.Contains(episode)) is { } file
                && files.Any(f => f.Name == file.Path && f.Priority > 0))
                return true;
        }
        return false;
    }
}
