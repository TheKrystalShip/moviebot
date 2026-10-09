using System.Globalization;
using Microsoft.Extensions.Options;
using TheKrystalShip.MovieBot.Acquire;
using TheKrystalShip.MovieBot.Acquire.Download;
using TheKrystalShip.MovieBot.Acquire.Imdb;
using TheKrystalShip.MovieBot.Acquire.Search;
using TheKrystalShip.MovieBot.Acquire.Tracker;
using TheKrystalShip.MovieBot.Api.Library;
using TheKrystalShip.MovieBot.Api.People;
using TheKrystalShip.MovieBot.Api.Sessions;
using TheKrystalShip.MovieBot.Api.Shows;
using TheKrystalShip.MovieBot.Core;

namespace TheKrystalShip.MovieBot.Api.Acquisition;

/// <summary>How films are watched while they arrive.</summary>
public sealed class WatchOptions
{
    public const string Section = "Watch";

    /// <summary>
    /// How many films may be watched while they are still arriving. Each is a download and a live
    /// transcode at once, and the transcode is what the machine has only so much of.
    /// </summary>
    public int MaxStreams { get; set; } = 4;
}

/// <summary>
/// Watch now: a film that is not here is fetched and played in a room as soon as it can be opened.
///
/// The room is told what it waits on (<see cref="SessionState.Preparing"/>) and every viewer sees the
/// same panel. The download carries the room on a tag (<see cref="TorrentTags.Watch"/>), and
/// <see cref="WatchNowLoader"/> loads the film into it the moment the hand-off marks it watchable, so a
/// restart in between loses nothing. Picking a release is the one slow step, and it happens after the
/// answer: the person is in the room, watching it be found.
/// </summary>
public sealed class WatchNow(
    IReleaseSearch search,
    ImdbClient catalogue,
    OfferedReleases offered,
    Downloads downloads,
    AcquisitionService acquisition,
    TitleLibrary library,
    SessionStore sessions,
    RoomControls controls,
    WishList wishes,
    ShowOfferCache showOffers,
    IOptions<NotifyOptions> notify,
    IOptions<WatchOptions> options,
    ILogger<WatchNow> logger)
{
    /// <summary>The server's own name on what it does to a room, for the log.</summary>
    internal static readonly Actor Server = new("moviebot", "MovieBot");

    /// <summary>Whether a film can be watched, and how. The tracker is asked only for a film neither here nor arriving.</summary>
    public async Task<FilmAvailability> AvailabilityAsync(string imdbId, string? userId, CancellationToken ct)
    {
        var all = await DownloadsOrNullAsync(ct);
        var streams = Slots(all);
        var wish = wishes.Find(imdbId);
        var answer = new FilmAvailability
        {
            ImdbId = imdbId,
            Streams = streams,
            Waiters = wish?.Subscribers.Count ?? 0,
            Waiting = wish is not null && userId is not null && wish.Subscribers.Any(s => s.UserId == userId),
        };

        if (InLibrary(imdbId) is { } title) return answer with { LibraryId = title.Id };

        if (all is not null && DownloadOf(all, imdbId) is { } download)
            return answer with
            {
                Download = new FilmDownload
                {
                    Hash = download.Hash,
                    Progress = download.Progress,
                    IsWatchable = download.Tags.Contains(TorrentTags.Watchable),
                    LibraryId = download.Tags.Select(TorrentTags.ReadLibrary).FirstOrDefault(i => i is not null),
                },
            };

        RankedReleases ranked;
        try
        {
            ranked = await search.ByImdbAsync(imdbId, ct);
        }
        catch (TrackerException ex)
        {
            logger.LogWarning(ex, "The tracker could not be asked about {Imdb}.", imdbId);
            return answer with { TrackerUnanswered = true };
        }

        var floor = notify.Value.MinimumSource;
        var digital = ranked.Candidates.Where(r => r.Source >= floor).ToList();
        var hd1080 = digital.FirstOrDefault(r => r.Resolution == Resolution.Hd1080);
        var hd720 = digital.FirstOrDefault(r => r.Resolution == Resolution.Hd720);
        offered.Remember([.. new[] { hd1080, hd720 }.OfType<Release>()]);

        // Out digitally is about the film, not this server's policy: a digital release turned down
        // for its category or its size is a film that is out and not offered here.
        var none = hd1080 is null && hd720 is null;
        var outDigitally = ranked.Candidates.Concat(ranked.Rejected.Select(r => r.Release)).Any(r => r.Source >= floor);
        return answer with
        {
            Hd1080 = hd1080 is null ? null : OfferedReleases.View(hd1080),
            Hd720 = hd720 is null ? null : OfferedReleases.View(hd720),
            NotOut = none && !outDigitally,
            NotOffered = none && outDigitally,
            EmptyExplanation = none ? ranked.EmptyExplanation : null,
        };
    }

    /// <summary>
    /// Starts watching: a film already here is loaded at once; one on its way has the room added to its
    /// download; anything else is fetched, in the background, at the release asked for or the best one
    /// at the quality asked for.
    /// </summary>
    public async Task<WatchNowReply> StartAsync(WatchNowRequest request, Actor asker, CancellationToken ct)
    {
        var room = request.Room is { Length: > 0 } named ? named : NewRoom();
        if (!TorrentTags.IsRoomId(room)) return new WatchNowReply { Outcome = "badRoom" };
        // The asker comes into the room the film is played in, and a room made for them is theirs.
        if (!sessions.Enter(room, asker.UserId)) return new WatchNowReply { Outcome = "private" };

        if (request.Episodes is null && InLibrary(request.ImdbId) is { } title)
        {
            await controls.ApplyAsync(room, "load", asker, a => sessions.LoadTitle(room, title.Id, a), ct: ct);
            return new WatchNowReply { Outcome = "inLibrary", Room = room, LibraryId = title.Id };
        }

        // A show is watched an episode at a time: the first picked is the one the room waits on.
        EpisodeNumber? episode = request.Episodes is { Count: > 0 } picked
            ? new EpisodeNumber(picked[0].Season, picked[0].Episode) : null;
        if (episode is { } wanted && PlayableEpisode(library, request.ImdbId, wanted) is { } here)
        {
            await controls.ApplyAsync(room, "load", asker, a => sessions.LoadTitle(room, here, a), ct: ct);
            return new WatchNowReply { Outcome = "inLibrary", Room = room, LibraryId = here };
        }

        var all = await DownloadsOrNullAsync(ct);
        if (all is not null && request.Episodes is null && DownloadOf(all, request.ImdbId) is { } arriving)
        {
            // Already on its way: the room waits on the same download rather than starting another.
            await HoldAsync(room, new RoomPreparing
            {
                ImdbId = request.ImdbId, Name = await NameAsync(request.ImdbId, null, null, ct),
                Stage = PreparingStages.Downloading, Progress = arriving.Progress,
            }, asker, ct);
            await acquisition.TagAsync(arriving.Hash, TorrentTags.Watch(room), ct);
            return new WatchNowReply { Outcome = "preparing", Room = room };
        }

        var streams = Slots(all);
        if (streams.Used >= streams.Limit) return new WatchNowReply { Outcome = "full", Streams = streams };

        var chosen = request.TorrentId is { } id ? offered.Resolve(id) : null;
        await HoldAsync(room, new RoomPreparing
        {
            ImdbId = request.ImdbId, Season = episode?.Season, Episode = episode?.Episode,
            Name = await NameAsync(request.ImdbId, chosen, episode, ct),
            Stage = PreparingStages.Finding,
            Release = chosen is null ? null : Label(chosen),
        }, asker, ct);

        // The person is in the room by the time this is decided, watching it be found.
        _ = Task.Run(() => FetchAsync(room, request, episode, chosen, asker));
        return new WatchNowReply { Outcome = "preparing", Room = room };
    }

    /// <summary>
    /// An episode the library holds and can play now, as the hand-off judges it: finished, or with a
    /// head to play up to. Null when it is not here or has nothing to play yet.
    /// </summary>
    internal static string? PlayableEpisode(TitleLibrary library, string showImdbId, EpisodeNumber episode) =>
        library.EpisodesOf(showImdbId)
            .Where(e => e.Manifest.Episode!.Season == episode.Season && e.Manifest.Episode.Number == episode.Episode)
            .Where(e => e.Manifest.Status == TitleStatus.Ready || (e.Manifest.Status == TitleStatus.Transcoding && e.Manifest.HeadSeconds > 0))
            .Select(e => e.Id)
            .FirstOrDefault();

    private async Task FetchAsync(string room, WatchNowRequest request, EpisodeNumber? episode, Release? chosen, Actor asker)
    {
        var ct = CancellationToken.None;
        var name = await NameAsync(request.ImdbId, chosen, episode, ct);
        RoomPreparing Failed(string problem, string? refusal = null) => new()
        {
            ImdbId = request.ImdbId, Season = episode?.Season, Episode = episode?.Episode,
            Name = name, Stage = PreparingStages.Failed, Problem = problem, Refusal = refusal,
        };

        try
        {
            var release = chosen ?? (episode is { } wanted
                ? ShowOfferCache.Pick(await showOffers.GetAsync(request.ImdbId, ct), wanted,
                    request.Quality == "1080p" ? Resolution.Hd1080 : Resolution.Hd720)
                : await PickAsync(request, ct));
            if (release is null)
            {
                await PrepareAsync(room, Failed("notOffered"), ct);
                return;
            }

            offered.Remember([release]);
            // A release picked here is one more source of a name the catalogue could not give.
            if (chosen is null) name = await NameAsync(request.ImdbId, release, episode, ct);
            var reply = await downloads.StartAsync(new StartDownloadRequest
            {
                TorrentId = release.TorrentId,
                Episodes = request.Episodes,
            }, asker, ct, watchRoom: room, watchEpisode: episode);

            switch (reply.Outcome)
            {
                case "started":
                    logger.LogInformation("{User} is watching {Film} while it arrives, in room {Room}.", asker.DisplayName, name, room);
                    await PrepareAsync(room, new RoomPreparing
                    {
                        ImdbId = request.ImdbId, Season = episode?.Season, Episode = episode?.Episode,
                        Name = name, Stage = PreparingStages.Downloading,
                        Release = Label(release), Progress = 0,
                    }, ct);
                    break;
                case "refused":
                    await PrepareAsync(room, Failed("refused", reply.Refusal), ct);
                    break;
                case "noLongerOffered":
                    await PrepareAsync(room, Failed("notOffered"), ct);
                    break;
                default:
                    await PrepareAsync(room, Failed("unavailable"), ct);
                    break;
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Watch now for {Film} in room {Room} failed.", name, room);
            await PrepareAsync(room, Failed("unavailable"), ct);
        }
    }

    /// <summary>The best release at the quality asked for, at a digital source or better; a film's default is 1080p.</summary>
    private Task<Release?> PickAsync(WatchNowRequest request, CancellationToken ct) =>
        BestReleaseAsync(request.ImdbId, request.Quality == "720p" ? Resolution.Hd720 : Resolution.Hd1080, ct);

    /// <summary>
    /// The best release of a film at <paramref name="wanted"/>, else the best at any resolution, from a
    /// digital source up; null when the tracker offers none. With a <paramref name="language"/> to
    /// prefer (<see cref="ReleaseLanguages.WorthPreferring"/>), a release whose name carries it comes
    /// first, at that resolution and then at any.
    /// </summary>
    internal async Task<Release?> BestReleaseAsync(string imdbId, Resolution wanted, CancellationToken ct, string? language = null)
    {
        var ranked = await search.ByImdbAsync(imdbId, ct);
        var digital = ranked.Candidates.Where(r => r.Source >= notify.Value.MinimumSource).ToList();
        var dubbed = language is null
            ? null
            : ReleaseLanguages.Preferring(digital.Where(r => r.Resolution == wanted), language) ?? ReleaseLanguages.Preferring(digital, language);
        return dubbed ?? digital.FirstOrDefault(r => r.Resolution == wanted) ?? digital.FirstOrDefault();
    }

    /// <summary>
    /// Puts what is about to arrive into the room in place of whatever it held, as the person who
    /// asked: every viewer's player moves to it at once.
    /// </summary>
    private Task HoldAsync(string room, RoomPreparing preparing, Actor asker, CancellationToken ct) =>
        controls.ApplyAsync(room, "load", asker, a => sessions.Prepare(room, preparing, a), ct: ct);

    /// <summary>
    /// How the fetch is getting on, said in the room only while the room still holds it: somebody may
    /// have put something else on since.
    /// </summary>
    private Task PrepareAsync(string room, RoomPreparing preparing, CancellationToken ct) =>
        preparing.SameItem(sessions.GetOrCreate(room).Preparing) ? controls.ReportAsync(room, preparing, ct) : Task.CompletedTask;

    /// <summary>
    /// What the room calls the film while it waits: the catalogue's name, which is the one every page
    /// shows, else the release's, which is often the film's name in another language.
    /// </summary>
    /// <remarks>An episode is its show's name and its place, <c>Slow Horses S05E03</c>, as the library names it.</remarks>
    private async Task<string> NameAsync(string imdbId, Release? release, EpisodeNumber? episode, CancellationToken ct)
    {
        try
        {
            if (await catalogue.LookupAsync(imdbId, ct) is { } found)
            {
                if (episode is { } e) return $"{found.Title} {e}";
                if (found.Display is { Length: > 0 } named) return named;
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
        {
            logger.LogDebug(ex, "The title index could not name {Imdb}.", imdbId);
        }
        return episode is { } fallback && release is not null ? $"{release.Title} {fallback}" : release?.Display ?? imdbId;
    }

    /// <summary>A release as the panel names it: <c>1080p WEB</c>.</summary>
    public static string Label(Release release)
    {
        var resolution = OfferedReleases.View(release).Resolution;
        var source = release.Source switch
        {
            Source.Web => "WEB",
            Source.BluRay => "Blu-ray",
            Source.Remux => "Remux",
            Source.Dvd => "DVD",
            Source.Cam => "Cam",
            _ => null,
        };
        return string.Join(' ', new[] { resolution, source }.OfType<string>());
    }

    /// <summary>
    /// How many films are being watched while they arrive: every download still arriving that a room
    /// waits on, or that a room with somebody in it is playing.
    /// </summary>
    public StreamSlots Slots(IReadOnlyList<DownloadStatus>? all)
    {
        if (all is null) return new StreamSlots(0, options.Value.MaxStreams);
        var playing = sessions.Viewing().Select(v => v.TitleId).ToHashSet(StringComparer.Ordinal);
        var used = all.Count(d => !d.IsFinished && d.Tags.Any(t =>
            t.StartsWith(TorrentTags.WatchPrefix, StringComparison.Ordinal)
            || t.StartsWith(TorrentTags.RoomPrefix, StringComparison.Ordinal)
            || TorrentTags.ReadLibrary(t) is { } id && playing.Contains(id)));
        return new StreamSlots(used, options.Value.MaxStreams);
    }

    private TitleSummary? InLibrary(string imdbId) =>
        library.List().FirstOrDefault(t => t.Kind != TitleKind.Show && t.Status != TitleStatus.Failed
            && string.Equals(t.Film?.ImdbId, imdbId, StringComparison.OrdinalIgnoreCase));

    internal static DownloadStatus? DownloadOf(IReadOnlyList<DownloadStatus> all, string imdbId) =>
        all.FirstOrDefault(d => d.Tags.Select(TorrentTags.ReadImdb)
            .Any(i => string.Equals(i, imdbId, StringComparison.OrdinalIgnoreCase)));

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

    /// <summary>A room for one Watch now, named the way the site names the rooms it opens.</summary>
    private static string NewRoom() => Guid.NewGuid().ToString("n")[..8];
}

/// <summary>
/// Loads a film into the room waiting on it, the moment it can be opened, and keeps the room told how
/// far it has got until then.
///
/// The download's tag is the record of which room waits on it, so this reads the torrent client on
/// every pass and holds nothing: a restart picks every waiting room back up. A film that could not be
/// made watchable is said so in the room. Once loaded, the room plays: the person pressed Watch, and
/// the panel says it starts on its own.
/// </summary>
public sealed class WatchNowLoader(
    AcquisitionService acquisition,
    TitleLibrary library,
    SessionStore sessions,
    RoomControls controls,
    PersonDirectory people,
    ILogger<WatchNowLoader> logger) : BackgroundService
{
    /// <summary>Somebody is looking at the panel; this is how stale its percentage may be.</summary>
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(3);

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
                logger.LogDebug(ex, "A pass over the rooms waiting on downloads failed.");
            }
        }
    }

    public async Task PassAsync(CancellationToken ct)
    {
        foreach (var download in await acquisition.ListAsync(ct))
        {
            foreach (var tag in download.Tags.Where(t => t.StartsWith(TorrentTags.WatchPrefix, StringComparison.Ordinal)).ToList())
            {
                if (TorrentTags.ReadWatch(tag) is not { } room)
                {
                    await acquisition.ClearTagAsync(download.Hash, tag, ct);
                    continue;
                }
                await TendAsync(download, tag, room, ct);
            }
        }
    }

    private async Task TendAsync(DownloadStatus download, string tag, WatchTag watch, CancellationToken ct)
    {
        var room = watch.Room;
        var current = sessions.GetOrCreate(room).Preparing;
        var imdbId = download.Tags.Select(TorrentTags.ReadImdb).FirstOrDefault(i => i is not null) ?? current?.ImdbId ?? "";

        // A room holds one thing. A room that has been given something else since it asked for this
        // is no longer waiting on it, so the download stops naming it.
        var item = new RoomPreparing
        {
            ImdbId = imdbId, Season = watch.Episode?.Season, Episode = watch.Episode?.Episode, Name = "", Stage = "",
        };
        if (!item.SameItem(current))
        {
            await acquisition.ClearTagAsync(download.Hash, tag, ct);
            return;
        }
        var name = current!.Name;

        if (download.Tags.Contains(TorrentTags.IngestFailed))
        {
            await PrepareAsync(room, current with { Stage = PreparingStages.Failed, Problem = "notWatchable", Progress = null }, ct);
            await acquisition.ClearTagAsync(download.Hash, tag, ct);
            return;
        }

        // A film is ready when the hand-off marks its torrent; an episode when its own title can
        // play, since its season's torrent may have been marked for another episode long before.
        var libraryId = watch.Episode is { } episode
            ? WatchNow.PlayableEpisode(library, imdbId, episode)
            : download.Tags.Contains(TorrentTags.Watchable)
                ? download.Tags.Select(TorrentTags.ReadLibrary).FirstOrDefault(i => i is not null)
                : null;
        if (libraryId is not null && library.Get(libraryId) is not null)
        {
            var requester = download.Tags.Select(TorrentTags.ReadRequester).FirstOrDefault(r => r is not null);
            var actor = requester is null ? WatchNow.Server : new Actor(requester, people.NameOf(requester) ?? "Somebody");

            await controls.ApplyAsync(room, "load", actor, a => sessions.LoadTitle(room, libraryId, a), ct: ct);
            await controls.ApplyAsync(room, "play", actor, a => sessions.Play(room, 0, a), ct: ct);
            await acquisition.ClearTagAsync(download.Hash, tag, ct);
            logger.LogInformation("Loaded {Title} into room {Room}, which was waiting on it.", libraryId, room);
            return;
        }

        await PrepareAsync(room, current with
        {
            Stage = PreparingStages.Downloading,
            // Hundredths: a push per pass is as often as the panel is redrawn, and no oftener.
            Progress = Math.Round(watch.Episode is { } wanted ? await EpisodeProgressAsync(download, wanted, ct) : download.Progress, 2),
        }, ct);
    }

    /// <summary>
    /// How much of one episode's file has arrived. A season's torrent reports its picked files
    /// together, and the room waits on one of them.
    /// </summary>
    private async Task<double> EpisodeProgressAsync(DownloadStatus download, EpisodeNumber episode, CancellationToken ct)
    {
        var files = await acquisition.FilesAsync(download.Hash, ct);
        var mapped = EpisodeFiles.Map(files.Select(f => (f.Name, f.Size)), EpisodeMarker.Read(download.Name));
        var path = mapped.FirstOrDefault(f => f.Episodes.Contains(episode))?.Path;
        return files.FirstOrDefault(f => f.Name == path)?.Progress ?? download.Progress;
    }

    private Task PrepareAsync(string room, RoomPreparing preparing, CancellationToken ct) =>
        controls.ReportAsync(room, preparing, ct);
}
