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
            Waiting = wish is not null && userId is not null && wish.Subscribers.Any(s => s.UserId.ToString(CultureInfo.InvariantCulture) == userId),
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

        var digital = ranked.Candidates.Where(r => r.Source >= notify.Value.MinimumSource).ToList();
        var hd1080 = digital.FirstOrDefault(r => r.Resolution == Resolution.Hd1080);
        var hd720 = digital.FirstOrDefault(r => r.Resolution == Resolution.Hd720);
        offered.Remember([.. new[] { hd1080, hd720 }.OfType<Release>()]);

        return answer with
        {
            Hd1080 = hd1080 is null ? null : OfferedReleases.View(hd1080),
            Hd720 = hd720 is null ? null : OfferedReleases.View(hd720),
            NotOut = hd1080 is null && hd720 is null,
            EmptyExplanation = hd1080 is null && hd720 is null ? ranked.EmptyExplanation : null,
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

        if (request.Episodes is null && InLibrary(request.ImdbId) is { } title)
        {
            await controls.ApplyAsync(room, "load", asker, a => sessions.LoadTitle(room, title.Id, a), ct: ct);
            return new WatchNowReply { Outcome = "inLibrary", Room = room, LibraryId = title.Id };
        }

        var all = await DownloadsOrNullAsync(ct);
        if (all is not null && request.Episodes is null && DownloadOf(all, request.ImdbId) is { } arriving)
        {
            // Already on its way: the room waits on the same download rather than starting another.
            await acquisition.TagAsync(arriving.Hash, TorrentTags.Watch(room), ct);
            await PrepareAsync(room, new RoomPreparing
            {
                ImdbId = request.ImdbId, Name = await NameAsync(request.ImdbId, null, ct),
                Stage = PreparingStages.Downloading, Progress = arriving.Progress,
            }, ct);
            return new WatchNowReply { Outcome = "preparing", Room = room };
        }

        var streams = Slots(all);
        if (streams.Used >= streams.Limit) return new WatchNowReply { Outcome = "full", Streams = streams };

        var chosen = request.TorrentId is { } id ? offered.Resolve(id) : null;
        await PrepareAsync(room, new RoomPreparing
        {
            ImdbId = request.ImdbId,
            Name = await NameAsync(request.ImdbId, chosen, ct),
            Stage = PreparingStages.Finding,
            Release = chosen is null ? null : Label(chosen),
        }, ct);

        // The person is in the room by the time this is decided, watching it be found.
        _ = Task.Run(() => FetchAsync(room, request, chosen, asker));
        return new WatchNowReply { Outcome = "preparing", Room = room };
    }

    private async Task FetchAsync(string room, WatchNowRequest request, Release? chosen, Actor asker)
    {
        var ct = CancellationToken.None;
        var name = await NameAsync(request.ImdbId, chosen, ct);
        RoomPreparing Failed(string problem, string? refusal = null) => new()
        {
            ImdbId = request.ImdbId, Name = name, Stage = PreparingStages.Failed, Problem = problem, Refusal = refusal,
        };

        try
        {
            var release = chosen ?? await PickAsync(request, ct);
            if (release is null)
            {
                await PrepareAsync(room, Failed("notOffered"), ct);
                return;
            }

            offered.Remember([release]);
            var reply = await downloads.StartAsync(new StartDownloadRequest
            {
                TorrentId = release.TorrentId,
                RequestedBy = asker,
                Episodes = request.Episodes,
            }, ct, watchRoom: room);

            switch (reply.Outcome)
            {
                case "started":
                    logger.LogInformation("{User} is watching {Film} while it arrives, in room {Room}.", asker.DisplayName, name, room);
                    await PrepareAsync(room, new RoomPreparing
                    {
                        ImdbId = request.ImdbId, Name = name, Stage = PreparingStages.Downloading,
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
    private async Task<Release?> PickAsync(WatchNowRequest request, CancellationToken ct)
    {
        var ranked = await search.ByImdbAsync(request.ImdbId, ct);
        var wanted = request.Quality == "720p" ? Resolution.Hd720 : Resolution.Hd1080;
        var digital = ranked.Candidates.Where(r => r.Source >= notify.Value.MinimumSource).ToList();
        return digital.FirstOrDefault(r => r.Resolution == wanted) ?? digital.FirstOrDefault();
    }

    private Task PrepareAsync(string room, RoomPreparing preparing, CancellationToken ct) =>
        controls.ReportAsync(room, preparing, ct);

    /// <summary>What the room calls the film while it waits: the release's name, else the catalogue's.</summary>
    private async Task<string> NameAsync(string imdbId, Release? release, CancellationToken ct)
    {
        if (release is not null) return release.Display;
        try
        {
            return (await catalogue.LookupAsync(imdbId, ct))?.Display ?? imdbId;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
        {
            return imdbId;
        }
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

    private static DownloadStatus? DownloadOf(IReadOnlyList<DownloadStatus> all, string imdbId) =>
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

    private async Task TendAsync(DownloadStatus download, string tag, string room, CancellationToken ct)
    {
        var current = sessions.GetOrCreate(room).Preparing;
        var imdbId = download.Tags.Select(TorrentTags.ReadImdb).FirstOrDefault(i => i is not null) ?? current?.ImdbId ?? "";
        var name = current?.Name ?? ReleaseParser.ParseName(download.Name).Display;

        if (download.Tags.Contains(TorrentTags.IngestFailed))
        {
            await PrepareAsync(room, new RoomPreparing
            {
                ImdbId = imdbId, Name = name, Stage = PreparingStages.Failed, Problem = "notWatchable", Release = current?.Release,
            }, ct);
            await acquisition.ClearTagAsync(download.Hash, tag, ct);
            return;
        }

        var libraryId = download.Tags.Select(TorrentTags.ReadLibrary).FirstOrDefault(i => i is not null);
        if (download.Tags.Contains(TorrentTags.Watchable) && libraryId is not null && library.Get(libraryId) is not null)
        {
            var requester = download.Tags.Select(TorrentTags.ReadRequester).FirstOrDefault(r => r is not null)
                ?.ToString(CultureInfo.InvariantCulture);
            var actor = requester is null ? WatchNow.Server : new Actor(requester, people.NameOf(requester) ?? "Somebody");

            await controls.ApplyAsync(room, "load", actor, a => sessions.LoadTitle(room, libraryId, a), ct: ct);
            await controls.ApplyAsync(room, "play", actor, a => sessions.Play(room, 0, a), ct: ct);
            await acquisition.ClearTagAsync(download.Hash, tag, ct);
            logger.LogInformation("Loaded {Title} into room {Room}, which was waiting on it.", libraryId, room);
            return;
        }

        await PrepareAsync(room, new RoomPreparing
        {
            ImdbId = imdbId, Name = name, Stage = PreparingStages.Downloading, Release = current?.Release,
            // Hundredths: a push per pass is as often as the panel is redrawn, and no oftener.
            Progress = Math.Round(download.Progress, 2),
        }, ct);
    }

    private Task PrepareAsync(string room, RoomPreparing preparing, CancellationToken ct) =>
        controls.ReportAsync(room, preparing, ct);
}
