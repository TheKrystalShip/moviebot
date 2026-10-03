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
    /// <param name="watchRoom">
    /// The site room waiting to watch it, for Watch now, which loads it there once it can be opened.
    /// </param>
    public async Task<StartDownloadReply> StartAsync(StartDownloadRequest request, CancellationToken ct, string? watchRoom = null)
    {
        if (offered.Resolve(request.TorrentId) is not { } release)
            return new StartDownloadReply { Outcome = "noLongerOffered" };

        var view = OfferedReleases.View(release);
        try
        {
            var tags = new List<string> { TorrentTags.NeedsIngest };

            var channel = Snowflake(request.NotifyChannelId);
            if (channel is { } announceIn) tags.Add(TorrentTags.Notify(announceIn));
            if (Snowflake(request.RequestedBy.UserId) is { } requester)
            {
                // Whoever asked is told when it can be watched; on Discord directly only where no
                // channel will announce it to them.
                tags.Add(TorrentTags.Requester(requester));
                tags.Add(TorrentTags.Tell);
                if (channel is null) tags.Add(TorrentTags.TellDirect);
            }
            if (watchRoom is not null) tags.Add(TorrentTags.Watch(watchRoom));

            // The room is the reason for the download. Whichever pass sees the film become
            // watchable, in whichever process is running by then, loads it there.
            if (Snowflake(request.RoomId) is { } room) tags.Add(TorrentTags.Room(room));

            // The tracker states the film outright. Recording it here is the only chance to keep a
            // fact rather than something re-guessed from a release name later on.
            if (release.ImdbId is { Length: > 0 } imdbId) tags.Add(TorrentTags.Imdb(imdbId));

            var episodes = request.Episodes is { Count: > 0 } picked
                ? picked.Select(e => new EpisodeNumber(e.Season, e.Episode)).ToList()
                : null;
            var result = await acquisition.StartAsync(release, episodes, tags, ct);
            if (!result.Started)
                return new StartDownloadReply { Outcome = "refused", Refusal = result.Refusal, Release = view };

            logger.LogInformation("{User} started {Release} as {Hash}{Room}.",
                request.RequestedBy.DisplayName, release.ReleaseName, result.Hash,
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
                IsFinished = d.IsFinished,
                IsWatchable = d.Tags.Contains(TorrentTags.Watchable),
                LibraryId = libraryId,
                ImdbId = d.Tags.Select(TorrentTags.ReadImdb).FirstOrDefault(i => i is not null),
                RequestedBy = d.Tags.Select(TorrentTags.ReadRequester).FirstOrDefault(i => i is not null)
                    ?.ToString(System.Globalization.CultureInfo.InvariantCulture),
            };
        }).ToList();
    }

    /// <summary>A Discord id as the tags carry it, or null when there is none to carry.</summary>
    internal static ulong? Snowflake(string? id) =>
        ulong.TryParse(id, NumberStyles.None, CultureInfo.InvariantCulture, out var value) ? value : null;
}
