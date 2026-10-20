using System.Globalization;
using TheKrystalShip.MovieBot.Acquire.Download;
using TheKrystalShip.MovieBot.Acquire.Search;
using TheKrystalShip.MovieBot.Acquire.Tracker;
using TheKrystalShip.MovieBot.Api.Library;
using TheKrystalShip.MovieBot.Core;

namespace TheKrystalShip.MovieBot.Api.Acquisition;

/// <summary>
/// Turns a picked release into a running download.
///
/// Every fact about the download is written on the torrent rather than held here: where to announce
/// it, who asked, which room it is for, that it is not watchable until something has transcoded it,
/// and which film it is. A download outlives a restart of every process that touches it, and the
/// watchers that announce it, the hand-off that ingests it and the retention rule that removes it all
/// read those notes, so a download wearing none of them is one nobody is waiting on.
/// </summary>
public sealed class Downloads(
    OfferedReleases offered,
    AcquisitionService acquisition,
    ILogger<Downloads> logger)
{
    /// <param name="asker">Who asked, who is told when it can be watched; null for a download nobody asked for.</param>
    /// <param name="watchRoom">
    /// The site room waiting to watch it, for Watch now, which loads it there once it can be opened.
    /// </param>
    /// <param name="watchEpisode">For a show's release, the episode of it the room waits on.</param>
    /// <param name="tell">
    /// Whether whoever asked is told once it can be watched. A download onto a device is not: the device
    /// is what they wait on, and it says when it has the film.
    /// </param>
    /// <param name="wanted">
    /// The film or show people want, for a download the prefetcher starts on their behalf: the torrent
    /// carries <see cref="TorrentTags.Want"/> and every wanter is told when it can be watched.
    /// </param>
    public async Task<StartDownloadReply> StartAsync(StartDownloadRequest request, Actor? asker, CancellationToken ct,
        string? watchRoom = null, EpisodeNumber? watchEpisode = null, bool tell = true, string? wanted = null)
    {
        if (offered.Resolve(request.TorrentId) is not { } release)
            return new StartDownloadReply { Outcome = "noLongerOffered" };

        var view = OfferedReleases.View(release);
        try
        {
            var tags = new List<string> { TorrentTags.NeedsIngest };

            if (Snowflake(request.NotifyChannelId) is { } announceIn) tags.Add(TorrentTags.Notify(announceIn));
            if (asker is { UserId.Length: > 0 })
            {
                // Whoever asked is told when it can be watched.
                tags.Add(TorrentTags.Requester(asker.UserId));
                // An episode watched now is not news: its season's torrent may be watchable already
                // for another episode, and the room the person is in shows the one they pressed.
                if (watchEpisode is null && tell) tags.Add(TorrentTags.Tell);
            }
            if (watchRoom is not null) tags.Add(TorrentTags.Watch(watchRoom, watchEpisode));
            if (wanted is not null) tags.Add(TorrentTags.Want(wanted));

            // The room is the reason for the download. Whichever pass sees the film become
            // watchable, in whichever process is running by then, loads it there.
            if (Snowflake(request.RoomId) is { } room) tags.Add(TorrentTags.Room(room));

            // The tracker states the film outright. Recording it here is the only chance to keep a
            // fact rather than something re-guessed from a release name later on.
            if (release.ImdbId is { Length: > 0 } imdbId) tags.Add(TorrentTags.Imdb(imdbId));

            var episodes = request.Episodes is { Count: > 0 } picked
                ? picked.Select(e => new EpisodeNumber(e.Season, e.Episode)).ToList()
                : null;

            // A picked episode no room waits on is fetched for a reason that outlasts any room: a
            // follow, a device, the next one ahead, a download asked for outright. Each says so,
            // so a room moving past an episode lets go of only what that room alone asked for.
            foreach (var episode in episodes ?? [])
                if (watchRoom is null || episode != watchEpisode) tags.Add(TorrentTags.Keep(episode));
            var result = await acquisition.StartAsync(release, episodes, tags, ct);
            if (!result.Started)
                return new StartDownloadReply { Outcome = "refused", Refusal = result.Refusal, Release = view };

            logger.LogInformation("{User} started {Release} as {Hash}{Room}.",
                asker?.DisplayName ?? "MovieBot", release.ReleaseName, result.Hash,
                request.RoomId is { Length: > 0 } r ? $" for room {r}" : "");

            return new StartDownloadReply { Outcome = "started", Release = view, Hash = result.Hash };
        }
        catch (Exception ex) when (ex is TrackerException or QBittorrentException)
        {
            logger.LogWarning(ex, "Could not start {Release}.", release.ReleaseName);
            return new StartDownloadReply { Outcome = "unavailable", Release = view };
        }
    }

    /// <summary>
    /// Every download, running or seeding, with the library's name for it where it has one. Null when
    /// the torrent client could not be asked, which is a different answer from none.
    /// </summary>
    public async Task<List<DownloadView>?> ListAsync(TitleLibrary library, CancellationToken ct)
    {
        IReadOnlyList<DownloadStatus> downloads;
        try
        {
            downloads = await acquisition.ListAsync(ct);
        }
        catch (QBittorrentException ex)
        {
            logger.LogWarning(ex, "The downloads could not be read.");
            return null;
        }

        var titles = library.List();
        return downloads.Select(d =>
        {
            var libraryId = d.Tags.Select(TorrentTags.ReadLibrary).FirstOrDefault(i => i is not null);
            var title = libraryId is null ? null : titles.FirstOrDefault(t => t.Id == libraryId);
            return new DownloadView
            {
                Hash = d.Hash,
                Name = title?.Film?.Display ?? title?.Title ?? ReleaseParser.ParseName(d.Name).Display,
                Release = d.Name,
                Summary = d.Summary,
                Progress = d.Progress,
                IsFinished = d.IsFinished,
                IsWatchable = d.Tags.Contains(TorrentTags.Watchable),
                LibraryId = libraryId,
                ImdbId = d.Tags.Select(TorrentTags.ReadImdb).FirstOrDefault(i => i is not null),
                RequestedBy = d.Tags.Select(TorrentTags.ReadRequester).FirstOrDefault(i => i is not null),
            };
        }).ToList();
    }

    /// <summary>A Discord channel or voice channel id as the tags carry it, or null when there is none to carry.</summary>
    internal static ulong? Snowflake(string? id) =>
        ulong.TryParse(id, NumberStyles.None, CultureInfo.InvariantCulture, out var value) ? value : null;
}
