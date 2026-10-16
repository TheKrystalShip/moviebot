using TheKrystalShip.MovieBot.Acquire.Download;
using TheKrystalShip.MovieBot.Acquire.Search;
using TheKrystalShip.MovieBot.Api.Acquisition;
using TheKrystalShip.MovieBot.Api.Films;
using TheKrystalShip.MovieBot.Api.Library;
using TheKrystalShip.MovieBot.Api.Shows;
using TheKrystalShip.MovieBot.Core;

namespace TheKrystalShip.MovieBot.Api.Offline;

/// <summary>
/// Download of a title the library does not hold yet: the want is recorded at once, and the release is
/// picked and the download started after the answer, as Watch now does, the slow step being the
/// tracker. Where a want stands is read from the library and the torrent client each time it is
/// asked, and never kept: the library holding it, its download's progress, or what stopped it.
///
/// The download is the library's like any other: it is ingested, kept and let go of on the library's
/// clock, and a film fetched for one device is there for everybody. Nobody is told it can be watched,
/// because the device is what the person waits on.
/// </summary>
public sealed class OfflineWanting(
    OfflineSaves saves,
    TitleLibrary library,
    WatchNow watch,
    Downloads downloads,
    AcquisitionService acquisition,
    ShowOfferCache showOffers,
    OfferedReleases offered,
    FilmStore films,
    ShowStore shows,
    TimeProvider clock,
    ILogger<OfflineWanting> logger)
{
    /// <summary>
    /// Records what the person asked the device to download, and starts fetching whatever of it the
    /// library neither holds nor is fetching already. Answers each want as it stands.
    /// </summary>
    public async Task<List<OfflineWant>> AskAsync(Actor asker, string imdbId, IReadOnlyList<EpisodePick>? episodes,
        string device, WantPick pick, CancellationToken ct)
    {
        var wants = saves.Want(asker.UserId, imdbId, episodes, device, pick, clock.GetUtcNow());
        var owed = wants.Where(w => w.Hash is null && w.Problem is null && Playable(w) is null).ToList();
        if (owed.Count > 0) _ = Task.Run(() => FetchAsync(asker, imdbId, owed));
        return await ViewsAsync(wants, ct);
    }

    /// <summary>A person's wants, the oldest first, each as it stands.</summary>
    public Task<List<OfflineWant>> ListAsync(string userId, CancellationToken ct) =>
        ViewsAsync(saves.WantsOf(userId, clock.GetUtcNow()), ct);

    /// <summary>
    /// The library's title a want is, once a device can download it: a film or an episode that is
    /// ready, or still being made with something made already. Null until then.
    /// </summary>
    public string? Playable(StoredWant want)
    {
        if (want.Episode is { } e)
            return WatchNow.PlayableEpisode(library, want.ImdbId, new EpisodeNumber(e.Season, e.Episode));

        return library.List()
            .Where(t => t.Kind != TitleKind.Show
                && string.Equals(t.Film?.ImdbId, want.ImdbId, StringComparison.OrdinalIgnoreCase))
            .Select(t => t.Id)
            .FirstOrDefault(id => library.Get(id) is { } m
                && (m.Status == TitleStatus.Ready || (m.Status == TitleStatus.Transcoding && m.HeadSeconds > 0)));
    }

    private async Task FetchAsync(Actor asker, string imdbId, List<StoredWant> wants)
    {
        var ct = CancellationToken.None;
        try
        {
            if (wants[0].Episode is null) await FetchFilmAsync(asker, imdbId, wants[0], ct);
            else await FetchEpisodesAsync(asker, imdbId, wants, ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Fetching {Imdb} to download onto a device failed.", imdbId);
            foreach (var want in wants) saves.Fetched(asker.UserId, want.Id, null, WantStages.Unavailable);
        }
    }

    /// <summary>A film arriving already is waited on; otherwise its best release at 1080p is fetched.</summary>
    private async Task FetchFilmAsync(Actor asker, string imdbId, StoredWant want, CancellationToken ct)
    {
        if (await DownloadsOrNullAsync(ct) is { } all && WatchNow.DownloadOf(all, imdbId) is { } arriving)
        {
            saves.Fetched(asker.UserId, want.Id, arriving.Hash);
            return;
        }

        if (await watch.BestReleaseAsync(imdbId, Resolution.Hd1080, ct, want.AudioLanguage) is not { } release)
        {
            saves.Fetched(asker.UserId, want.Id, null, WantStages.NotOffered);
            return;
        }
        offered.Remember([release]);
        Started(asker, [want], await downloads.StartAsync(new StartDownloadRequest { TorrentId = release.TorrentId }, asker, ct, tell: false));
    }

    /// <summary>
    /// Each episode out of its season's pack at 720p where there is one, so a season stays one torrent,
    /// else from a release of its own, as Watch now picks; one download per release.
    /// </summary>
    private async Task FetchEpisodesAsync(Actor asker, string imdbId, List<StoredWant> wants, CancellationToken ct)
    {
        var offers = await showOffers.GetAsync(imdbId, ct);
        var original = shows.Held(imdbId)?.Details?.OriginalLanguage;
        var picked = new List<(StoredWant Want, Release Release)>();
        foreach (var want in wants)
        {
            var e = want.Episode!;
            if (ShowOfferCache.Pick(offers, new EpisodeNumber(e.Season, e.Episode), Resolution.Hd720, want.AudioLanguage, original) is { } release)
                picked.Add((want, release));
            else
                saves.Fetched(asker.UserId, want.Id, null, WantStages.NotOffered);
        }

        foreach (var group in picked.GroupBy(p => p.Release.TorrentId))
        {
            var release = group.First().Release;
            offered.Remember([release]);
            var reply = await downloads.StartAsync(new StartDownloadRequest
            {
                TorrentId = release.TorrentId,
                Episodes = [.. group.Select(p => p.Want.Episode!)],
            }, asker, ct, tell: false);
            Started(asker, [.. group.Select(p => p.Want)], reply);
        }
    }

    private void Started(Actor asker, List<StoredWant> wants, StartDownloadReply reply)
    {
        foreach (var want in wants)
        {
            switch (reply.Outcome)
            {
                case "started":
                    saves.Fetched(asker.UserId, want.Id, reply.Hash);
                    break;
                case "refused":
                    saves.Fetched(asker.UserId, want.Id, null, WantStages.Refused, reply.Refusal);
                    break;
                case "noLongerOffered":
                    saves.Fetched(asker.UserId, want.Id, null, WantStages.NotOffered);
                    break;
                default:
                    saves.Fetched(asker.UserId, want.Id, null, WantStages.Unavailable);
                    break;
            }
        }
        if (reply.Outcome == "started")
            logger.LogInformation("{User} is downloading {Count} title(s) of {Release} onto a device.",
                asker.DisplayName, wants.Count, reply.Release?.ReleaseName);
    }

    private async Task<List<OfflineWant>> ViewsAsync(List<StoredWant> wants, CancellationToken ct)
    {
        var all = wants.Any(w => w.Hash is not null) ? await DownloadsOrNullAsync(ct) : null;
        var files = new Dictionary<string, IReadOnlyList<(string Path, double Progress, IReadOnlyCollection<EpisodeNumber> Episodes)>>();
        var views = new List<OfflineWant>(wants.Count);
        foreach (var want in wants) views.Add(await ViewAsync(want, all, files, ct));
        return views;
    }

    /// <summary>Where a want stands: the library first, then what stopped it, then its download.</summary>
    private async Task<OfflineWant> ViewAsync(StoredWant want, IReadOnlyList<DownloadStatus>? all,
        Dictionary<string, IReadOnlyList<(string Path, double Progress, IReadOnlyCollection<EpisodeNumber> Episodes)>> files,
        CancellationToken ct)
    {
        var name = want.Episode is null ? films.Held(want.ImdbId)?.Details?.Title : shows.Held(want.ImdbId)?.Details?.Title;
        OfflineWant View(string stage, double? progress = null, string? problem = null, string? refusal = null, string? titleId = null) =>
            new(want.Id, want.ImdbId, name, want.Episode, want.Device, want.Height, want.AudioLanguage, want.SubtitleLanguage,
                want.CreatedAt, stage, progress, problem, refusal, titleId);

        if (Playable(want) is { } titleId) return View(WantStages.Ready, titleId: titleId);
        if (want.Problem is { } problem) return View(WantStages.Failed, problem: problem, refusal: want.Refusal);
        if (want.Hash is not { } hash) return View(WantStages.Finding);
        // The torrent client could not be asked: the download is under way as far as anything here knows.
        if (all is null) return View(WantStages.Downloading);
        if (all.FirstOrDefault(d => d.Hash == hash) is not { } download) return View(WantStages.Failed, problem: WantStages.Unavailable);
        if (download.Tags.Contains(TorrentTags.IngestFailed)) return View(WantStages.Failed, problem: WantStages.NotWatchable);

        var progress = download.Progress;
        if (want.Episode is { } e)
        {
            if (!files.TryGetValue(hash, out var mapped))
            {
                mapped = await EpisodeFilesAsync(download, ct);
                files[hash] = mapped;
            }
            var episode = new EpisodeNumber(e.Season, e.Episode);
            if (mapped.FirstOrDefault(f => f.Episodes.Contains(episode)) is { Path: not null } file) progress = file.Progress;
        }

        // Hundredths: the page redraws no finer.
        return progress >= 1
            ? View(WantStages.Preparing, 1)
            : View(WantStages.Downloading, Math.Round(progress, 2));
    }

    /// <summary>A show's torrent's files, each with the episodes it holds and how much of it has arrived.</summary>
    private async Task<IReadOnlyList<(string Path, double Progress, IReadOnlyCollection<EpisodeNumber> Episodes)>> EpisodeFilesAsync(
        DownloadStatus download, CancellationToken ct)
    {
        try
        {
            var listed = await acquisition.FilesAsync(download.Hash, ct);
            var mapped = EpisodeFiles.Map(listed.Select(f => (f.Name, f.Size)), EpisodeMarker.Read(download.Name));
            return [.. mapped.Select(m => (m.Path, listed.FirstOrDefault(f => f.Name == m.Path)?.Progress ?? download.Progress,
                (IReadOnlyCollection<EpisodeNumber>)m.Episodes))];
        }
        catch (QBittorrentException ex)
        {
            logger.LogDebug(ex, "The files of {Hash} could not be read.", download.Hash);
            return [];
        }
    }

    private async Task<IReadOnlyList<DownloadStatus>?> DownloadsOrNullAsync(CancellationToken ct)
    {
        try
        {
            return await acquisition.ListAsync(ct);
        }
        catch (QBittorrentException ex)
        {
            logger.LogWarning(ex, "The downloads could not be read.");
            return null;
        }
    }
}
