using System.Text;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TheKrystalShip.MovieBot.Acquire.Download;
using TheKrystalShip.MovieBot.Acquire.Tracker;
using TheKrystalShip.MovieBot.Acquire.Search;
using TheKrystalShip.MovieBot.Core;
using TheKrystalShip.MovieBot.Ingest.Pipeline;
using TheKrystalShip.MovieBot.Ingest.Probe;

namespace TheKrystalShip.MovieBot.Handoff;

/// <summary>
/// Carries a finished download into the library.
///
/// It runs apart from the bot for three reasons, all of which are about what a transcode is: it
/// writes into the media root, which the bot is not permitted to do; it takes minutes, so a bot
/// restart would abandon it halfway; and it holds the GPU, which has no business inside a gateway
/// connection.
///
/// It holds nothing between passes. What is owed is read from the torrents' own tags every time,
/// so stopping this process at any moment loses no work beyond the transcode in flight, and that
/// one is simply done again.
/// </summary>
public sealed class HandoffWorker(
    AcquisitionService acquisition,
    FilmMetadata metadata,
    MediaRoots roots,
    ColdStore cold,
    MarkerPass markers,
    DiskBudget budget,
    TranscodeLedger transcodes,
    TitleLocks locks,
    IOptions<HandoffOptions> options,
    ILogger<HandoffWorker> logger) : BackgroundService
{
    private readonly HandoffOptions _options = options.Value;

    /// <summary>
    /// What each transcode is expected to take, by the same key as the in-flight set. Probing a
    /// source costs seconds, and a film that has to wait for room is asked about every pass.
    /// </summary>
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, long> _estimates = new();

    /// <summary>
    /// The films waiting for room, so the reason is said once when a film begins to wait and not
    /// on every poll for as long as it does.
    /// </summary>
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, byte> _waiting = new();

    /// <summary>
    /// Held while a placement is decided and its claim recorded. Two films deciding at once would
    /// each read the room before the other had taken any of it.
    /// </summary>
    private readonly SemaphoreSlim _placing = new(1, 1);

    /// <summary>
    /// What is being ingested right now, by download hash.
    ///
    /// Sweeps carry on every half minute while a transcode runs, so without this the next one
    /// would start the same film again — and the ingest replaces a title's directory wholesale,
    /// so the second start would delete what the first was still writing into.
    /// </summary>
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, byte> _inFlight = new();

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation(
            "Watching for finished downloads, writing into {MediaRoot}.", roots.Hot);

        // This process holds no transcode when it starts, so any claim on the ledger is one a
        // process that died left behind.
        transcodes.Clear();

        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(_options.PollSeconds));

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (!await timer.WaitForNextTickAsync(stoppingToken)) return;
                await SweepAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                // The work is described by the tags, not by this loop, so a failed pass costs
                // nothing but the wait until the next one.
                logger.LogWarning(ex, "A hand-off sweep failed.");
            }
        }
    }

    private async Task SweepAsync(CancellationToken ct)
    {
        var downloads = await acquisition.ListAsync(ct);
        var tagged = downloads.Where(d => d.Tags.Contains(TorrentTags.NeedsIngest)).ToList();

        // A show's torrent is many titles, one per episode, and is swept file by file.
        foreach (var download in tagged.Where(IsShow))
        {
            if (ct.IsCancellationRequested) return;
            await SweepShowAsync(download, ct);
        }

        var owed = tagged
            .Where(d => !IsShow(d) && IsFarEnoughAlong(d))
            .Where(d => !_inFlight.ContainsKey(d.Hash))
            .ToList();

        // Started rather than awaited, up to the ceiling. A film is watchable seconds after its
        // own transcode starts and not before, so a film held behind another's is a film that
        // does not exist yet — not in the library, not by name, not however much of it has
        // arrived. They share one card and each runs slower for it; every one of them still
        // outruns a person watching it several times over, which is the only rate that matters.
        foreach (var download in owed)
        {
            if (ct.IsCancellationRequested) return;
            if (_inFlight.Count >= Math.Max(1, _options.MaxConcurrentIngests))
            {
                logger.LogInformation(
                    "{Name} waits: {Running} already transcoding.", download.Name, _inFlight.Count);
                continue;
            }

            if (!_inFlight.TryAdd(download.Hash, 0)) continue;

            Start(download.Hash, () => PlaceAndIngestAsync(download, ct));
        }
    }

    /// <summary>
    /// Runs one ingest in the background, holding its slot in the in-flight set for as long as it
    /// takes. A failure that reaches here is logged: nothing awaits the task, so an exception left
    /// in it would be seen by nobody.
    /// </summary>
    private void Start(string key, Func<Task> work)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                await work();
            }
            catch (OperationCanceledException)
            {
                // The process is stopping; what is owed is owed again on the next start.
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "An ingest stopped unexpectedly.");
            }
            finally
            {
                _inFlight.TryRemove(key, out _);
            }
        });
    }

    /// <summary>
    /// Finds the film's file, chooses the disk it is made on, and ingests it there. A film that
    /// has no room anywhere is left owed and asked about again on the next pass.
    /// </summary>
    private async Task PlaceAndIngestAsync(DownloadStatus download, CancellationToken ct)
    {
        var source = FilmFile.Locate(
            download.ContentPath, (long)_options.MinimumFilmSizeMiB * (1 << 20));

        if (source is null)
        {
            logger.LogError(
                "Nothing that looks like a film in {Path} for {Name}.",
                download.ContentPath, download.Name);

            await FailAsync(download, ct);
            return;
        }

        var id = LibraryId.For(download.Name);

        Disk? disk;
        try
        {
            disk = await PlaceAsync(download.Hash, download.Name, source, id, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // The probe is what the ingest's own would be, and fails the same way for the same
            // reasons: a source that cannot be read is a failed film, not one waiting for room.
            logger.LogError(ex, "Could not size {Name} for placement.", download.Name);
            await FailAsync(download, ct);
            return;
        }

        if (disk is null) return;

        try
        {
            await IngestAsync(download, source, id, disk.Value, ct);
        }
        finally
        {
            transcodes.Release(download.Hash);
        }
    }

    /// <summary>
    /// Chooses the disk a transcode is written to and takes its claim there, or returns null when
    /// neither disk has the room.
    ///
    /// The room is read, decided on and claimed under one lock: each film's claim is subtracted
    /// from what the next is judged against, which is what stops two films that each fit from
    /// being placed on a disk that holds only one of them.
    /// </summary>
    private async Task<Disk?> PlaceAsync(
        string key, string name, string source, string id, CancellationToken ct)
    {
        var needed = await EstimateAsync(key, source, ct);

        await _placing.WaitAsync(ct);
        try
        {
            // Read fresh each time: the downloads still arriving are part of what the disk has
            // promised, and they change between passes.
            var pending = DiskBudget.PendingBytes(await acquisition.ListAsync(ct));

            var media = budget.ReadVolume(roots.Hot, pending);
            var coldRoom = roots.ColdReady ? budget.ReadVolume(roots.Cold!, pending) : null;

            var placement = TranscodePlacement.Decide(needed, media, coldRoom);
            if (placement.Disk is not { } disk)
            {
                if (_waiting.TryAdd(key, 0))
                    logger.LogWarning(
                        "{Name} waits for disk and is asked about again each pass. {Reason}",
                        name, placement.Reason);

                return null;
            }

            _waiting.TryRemove(key, out _);

            var root = disk == Disk.Cold ? roots.Cold! : roots.Hot;
            transcodes.Reserve(new TranscodeReservation
            {
                Key = key,
                Root = root,
                TitleDirectory = Path.Combine(root, id),
                Bytes = needed
            });

            logger.LogInformation(
                "{Name} is made on {Disk}: {Needed:0.#} GiB reserved under {Root}.",
                name, disk == Disk.Cold ? "cold storage" : "the media root",
                needed / (double)(1L << 30), root);

            return disk;
        }
        finally
        {
            _placing.Release();
        }
    }

    /// <summary>
    /// What a source's transcode will take, from the ladder and the running time. Probed once per
    /// download or episode: the answer is a property of the file, and a probe of one that is still
    /// arriving waits for its header.
    /// </summary>
    private async Task<long> EstimateAsync(string key, string source, CancellationToken ct)
    {
        if (_estimates.TryGetValue(key, out var known)) return known;

        var probe = await FfProbe.RunAsync(source, ct);
        var bytes = TranscodeEstimate.For(probe, new IngestOptions
        {
            SourcePath = source,
            OutputRoot = roots.Hot,
            SubtitleLanguages = _options.SubtitleLanguages,
            DialogueBoost = _options.DialogueBoost
        });

        _estimates[key] = bytes;
        return bytes;
    }

    /// <summary>
    /// Makes the chosen root the only one holding the title, under the title's lock so a settle in
    /// the middle of its copy finishes first, and returns the directory root to write into. Null
    /// when the cold volume went away between the decision and now: the film stays owed rather
    /// than being made somewhere it was not placed.
    /// </summary>
    private async Task<string?> PrepareAsync(string id, Disk disk, CancellationToken ct)
    {
        using (await locks.AcquireAsync(id, ct))
        {
            if (disk == Disk.Media)
            {
                // A copy that had settled onto cold storage would outlive the transcode and, being
                // resolved first, be the one every room opened.
                cold.ClearSettled(id);
                return roots.Hot;
            }

            // The reverse: a copy under the media root would be invisible while the transcode ran
            // and would reappear as the film the moment the cold directory went.
            if (!cold.ClearWorking(id))
            {
                logger.LogWarning(
                    "Cold storage is not there, so {Id} is not made on it now; it stays owed.", id);
                return null;
            }

            return roots.Cold;
        }
    }

    /// <summary>
    /// Whether there is enough of a download to start reading it. A finished one always is; an
    /// unfinished one needs its first pieces and its index, which the head start covers.
    /// </summary>
    private bool IsFarEnoughAlong(DownloadStatus download) =>
        download.IsFinished
        || (_options.StartAtProgress > 0 && download.Progress >= _options.StartAtProgress);

    /// <summary>Whether a download is a show's, which is ingested an episode at a time.</summary>
    private static bool IsShow(DownloadStatus download) =>
        ReleaseParser.ParseName(download.Name).IsEpisodic;

    /// <summary>
    /// Episodes whose ingest failed, by download and file, so a pass does not start them again
    /// every half minute. Held only for this process's life: a restart tries each once more.
    /// </summary>
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, byte> _failedEpisodes = new();

    private static string EpisodeKey(DownloadStatus download, EpisodeFile file) =>
        $"{download.Hash}/{file.Path}";

    /// <summary>
    /// Starts the picked episodes of a show's torrent that are not in the library yet, in episode
    /// order and up to the ceiling, each once enough of its own file has arrived; and once none is
    /// left owing, says the torrent owes nothing.
    ///
    /// Which episodes are owed is read from the torrent's picked files and the library, never
    /// remembered: an episode is done when its title is ready, so a pass after a restart, or after
    /// more episodes are picked from the same pack, finds exactly what is left.
    /// </summary>
    private async Task SweepShowAsync(DownloadStatus download, CancellationToken ct)
    {
        var picked = (await acquisition.FilesAsync(download.Hash, ct)).Where(f => f.Priority > 0).ToList();

        // A torrent whose files the client has not listed yet has nothing to say about them.
        if (picked.Count == 0) return;

        var arrived = picked.ToDictionary(f => f.Name, f => f.Progress, StringComparer.Ordinal);
        var episodes = EpisodeFiles.Map(
            picked.Select(f => (f.Name, f.Size)), ReleaseParser.ParseName(download.Name).Episodic);

        var owed = episodes
            .Where(e => !IsReady(LibraryId.ForEpisode(download.Name, e.Episode)))
            .Where(e => !_failedEpisodes.ContainsKey(EpisodeKey(download, e)))
            .ToList();

        if (owed.Count == 0)
        {
            var anyReady = episodes.Any(e => IsReady(LibraryId.ForEpisode(download.Name, e.Episode)));
            if (anyReady)
            {
                await acquisition.ClearTagAsync(download.Hash, TorrentTags.NeedsIngest, ct);
                logger.LogInformation("Every picked episode of {Name} is in the library.", download.Name);
            }
            else
            {
                logger.LogError("No episode of {Name} could be made watchable.", download.Name);
                await FailAsync(download, ct);
            }

            return;
        }

        foreach (var episode in owed)
        {
            var key = EpisodeKey(download, episode);
            if (_inFlight.ContainsKey(key)) continue;

            var progress = arrived.GetValueOrDefault(episode.Path);
            var farEnough = progress >= 1
                            || (_options.StartAtProgress > 0 && progress >= _options.StartAtProgress);
            if (!farEnough) continue;

            if (_inFlight.Count >= Math.Max(1, _options.MaxConcurrentIngests))
            {
                logger.LogInformation(
                    "{Episode} of {Name} waits: {Running} already transcoding.",
                    episode.Episode, download.Name, _inFlight.Count);
                return;
            }

            if (!_inFlight.TryAdd(key, 0)) continue;

            Start(key, () => PlaceAndIngestEpisodeAsync(download, episode, progress >= 1, ct));
        }
    }

    /// <summary>
    /// Chooses the disk an episode is made on, and ingests it there. An episode with no room
    /// anywhere is left owed, as a film is.
    /// </summary>
    private async Task PlaceAndIngestEpisodeAsync(
        DownloadStatus download, EpisodeFile file, bool whole, CancellationToken ct)
    {
        var key = EpisodeKey(download, file);

        // The client names a file under the torrent's own folder, which sits in the directory the
        // torrent's contents are in; a single-file torrent's contents are the file.
        var source = Path.Combine(Path.GetDirectoryName(download.ContentPath) ?? "", file.Path);
        var id = LibraryId.ForEpisode(download.Name, file.Episode);

        Disk? disk;
        try
        {
            disk = await PlaceAsync(key, $"{file.Episode} of {download.Name}", source, id, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Could not size {Episode} of {Name} for placement.", file.Episode, download.Name);
            _failedEpisodes.TryAdd(key, 0);
            return;
        }

        if (disk is null) return;

        try
        {
            await IngestEpisodeAsync(download, file, whole, source, id, disk.Value, ct);
        }
        finally
        {
            transcodes.Release(key);
        }
    }

    /// <summary>Whether a title is in the library and finished, on either root.</summary>
    private bool IsReady(string id)
    {
        if (roots.DirectoryOf(id) is not { } directory) return false;

        try
        {
            var path = Path.Combine(directory, "manifest.json");
            return File.Exists(path)
                   && ManifestJson.Deserialize(File.ReadAllText(path))?.Status == TitleStatus.Ready;
        }
        catch (Exception ex) when (ex is IOException or System.Text.Json.JsonException)
        {
            return false;
        }
    }

    /// <summary>
    /// Makes one episode a title of its own: named for the show and its place in it, tagged on
    /// the torrent like a film's, and announced as watchable the same way. The torrent owes its
    /// transcode until every picked episode is done, which the sweep decides.
    /// </summary>
    private async Task IngestEpisodeAsync(
        DownloadStatus download, EpisodeFile file, bool whole, string source, string id,
        Disk disk, CancellationToken ct)
    {
        var release = ReleaseParser.ParseName(download.Name);

        logger.LogInformation(
            "Ingesting {Episode} of {Name} from {Source} as {Id}.",
            file.Episode, download.Name, Path.GetFileName(source), id);

        try
        {
            if (await PrepareAsync(id, disk, ct) is not { } outputRoot) return;
            await acquisition.TagAsync(download.Hash, TorrentTags.Library(id), ct);

            var imdbId = download.Tags.Select(TorrentTags.ReadImdb).FirstOrDefault(i => i is not null);
            var show = await metadata.ResolveShowAsync(imdbId, ct);

            string? poster = null;
            if (show is not null)
                poster = await metadata.WritePosterAsync(
                    show, Path.Combine(Path.GetTempPath(), "moviebot-posters", id), ct);

            var episode = new EpisodeIdentity { Season = file.Episode.Season, Number = file.Episode.Episode };
            var options = new IngestOptions
            {
                SourcePath = source,
                OutputRoot = outputRoot,
                Id = id,
                Title = $"{release.Title} {episode.Label}",
                ImdbId = imdbId,
                Film = show is null ? null : FilmMetadata.Identify(show),
                Episode = episode,
                PosterSource = poster,
                SubtitleLanguages = _options.SubtitleLanguages,
                DialogueBoost = _options.DialogueBoost,
                Availability = whole ? null : new TorrentAvailability(acquisition, download.Hash, source, file.Path),
                Force = true,
            };

            var pipeline = new IngestPipeline(options, line => logger.LogInformation("  {Line}", line));
            var transcode = pipeline.RunAsync(ct);
            await ReleaseWhenPlayableAsync(download, options.OutputDirectory(id), transcode, ct);

            var manifest = await transcode;
            logger.LogInformation("{Title} finished transcoding as {Id}.", manifest.Title, manifest.Id);

            // The episode is ready and watchable already; what a viewer can skip in it is found
            // now and written when known. The pass reports its own failures and leaves the
            // markers empty, so nothing here can fail the episode.
            await markers.RunAsync(id, reuseStored: false, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            logger.LogInformation("Stopped mid-transcode of {Id}; it stays owed.", id);
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Could not ingest {Episode} of {Name}.", file.Episode, download.Name);
            _failedEpisodes.TryAdd(EpisodeKey(download, file), 0);
        }
    }

    private async Task IngestAsync(
        DownloadStatus download, string source, string id, Disk disk, CancellationToken ct)
    {
        var title = LibraryId.TitleFor(download.Name);
        logger.LogInformation(
            "Ingesting {Name} from {Source} as {Id} ({Title}), download at {Progress:P0}.",
            download.Name, Path.GetFileName(source), id, title, download.Progress);

        // A film made again under an id the library already holds replaces what was there, and
        // the ingest clears only the directory it writes into. A copy on the other root would
        // outlive it and, if it is the cold one resolved first, be the one every room opened:
        // the previous release, under the name of the one somebody just asked for.
        if (await PrepareAsync(id, disk, ct) is not { } outputRoot) return;

        // Written on the torrent before anything else happens, so whatever waits for the film
        // to become watchable is holding the id it will go under by the time it does. A surface
        // that had to derive it would be parsing the release name a second time, and two parsers
        // do not disagree loudly: they open nothing.
        await acquisition.TagAsync(download.Hash, TorrentTags.Library(id), ct);

        // Supplied only while the file is still arriving. A whole file is ingested exactly as
        // it always was, so the ordinary path keeps the behaviour that was measured against it.
        var availability = download.IsFinished
            ? null
            : new TorrentAvailability(acquisition, download.Hash, source);

        var imdbId = download.Tags.Select(TorrentTags.ReadImdb).FirstOrDefault(i => i is not null);

        // Before the transcode rather than after it. A film is watchable, and announced, long
        // before the last segment lands, so anything added afterwards arrives after every message
        // that would have shown it.
        var release = ReleaseParser.ParseName(download.Name);
        var film = await metadata.ResolveAsync(imdbId, release.Title, release.Year, ct);

        string? poster = null;
        if (film is not null)
        {
            logger.LogInformation(
                "{Name} is {Film} ({Imdb}).", download.Name, film.Title, film.ImdbId);
            poster = await metadata.WritePosterAsync(
                film, Path.Combine(Path.GetTempPath(), "moviebot-posters", id), ct);
        }

        var options = new IngestOptions
        {
            SourcePath = source,
            OutputRoot = outputRoot,
            Id = id,
            Title = title,
            ImdbId = imdbId,
            Film = film is null ? null : FilmMetadata.Identify(film),
            PosterSource = poster,
            SubtitleLanguages = _options.SubtitleLanguages,
            DialogueBoost = _options.DialogueBoost,
            Availability = availability,

            // Reaching here means the previous attempt did not finish, since the tag is removed
            // as soon as one becomes watchable. Whatever it left behind is incomplete.
            Force = true,
        };

        try
        {
            var pipeline = new IngestPipeline(options, line => logger.LogInformation("  {Line}", line));
            var transcode = pipeline.RunAsync(ct);

            // The playlists grow as segments land, so a film is watchable long before it is
            // finished — seconds in, against roughly ten times realtime. Waiting for the whole
            // transcode before saying so would hold a room for a quarter of an hour in front of
            // a film that was already playable.
            await ReleaseWhenPlayableAsync(download, options.OutputDirectory(id), transcode, ct);

            var manifest = await transcode;

            // Only now. Everything up to here — playable, announced, most of the film written —
            // is work in progress, and a process that stops during it leaves the rest owed.
            await acquisition.ClearTagAsync(download.Hash, TorrentTags.NeedsIngest, ct);

            logger.LogInformation(
                "{Title} finished transcoding as {Id}.", manifest.Title, manifest.Id);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Left tagged, so the next start picks it up again — which is now true rather than
            // merely intended.
            logger.LogInformation("Stopped mid-transcode of {Name}; it stays owed.", download.Name);
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Could not ingest {Name}.", download.Name);
            await FailAsync(download, ct);
        }
    }

    /// <summary>
    /// Marks the film watchable, the moment there is something to watch.
    ///
    /// The manifest is written before the main pass starts and gains a head as segments land, so
    /// what is waited on is a head rather than the transcode: the head is the point at which the
    /// player has something to open. A transcode that finishes before the first check is caught
    /// by its own status, since a completed one reports no head at all.
    /// </summary>
    private async Task ReleaseWhenPlayableAsync(
        DownloadStatus download, string outputDirectory, Task transcode, CancellationToken ct)
    {
        var manifestPath = Path.Combine(outputDirectory, "manifest.json");

        while (!ct.IsCancellationRequested)
        {
            if (IsPlayable(manifestPath))
            {
                // Marked, not unmarked. What is owed is still owed — an hour of transcoding is
                // still to come, and something has to say so if this process stops before it ends.
                await acquisition.TagAsync(download.Hash, TorrentTags.Watchable, ct);
                logger.LogInformation("{Name} is watchable; announcing it.", download.Name);
                return;
            }

            // A transcode that ended without ever becoming playable failed, and the failure path
            // is what should report it rather than this waiting forever for a head.
            if (transcode.IsCompleted) return;

            await Task.Delay(TimeSpan.FromSeconds(2), ct);
        }
    }

    private bool IsPlayable(string manifestPath)
    {
        try
        {
            if (!File.Exists(manifestPath)) return false;

            var manifest = ManifestJson.Deserialize(File.ReadAllText(manifestPath));
            if (manifest is null) return false;

            // A finished transcode carries no head, having no growing edge left to report.
            return manifest.Status == TitleStatus.Ready || manifest.HeadSeconds > 0;
        }
        catch (Exception ex) when (ex is IOException or System.Text.Json.JsonException)
        {
            // The manifest is written through a temp file and moved, so a partial read is not
            // expected — but a torn read here would only mean waiting one more turn.
            logger.LogDebug(ex, "Could not read {Path} yet.", manifestPath);
            return false;
        }
    }

    /// <summary>
    /// Marks a download as having failed to become watchable, and stops it being retried forever.
    ///
    /// A failure that simply left the tag alone would be retried every pass, and would look from
    /// the outside exactly like a transcode still running — so the person who asked would be told
    /// nothing at all, indefinitely.
    /// </summary>
    private async Task FailAsync(DownloadStatus download, CancellationToken ct)
    {
        try
        {
            await acquisition.TagAsync(download.Hash, TorrentTags.IngestFailed, ct);
            await acquisition.ClearTagAsync(download.Hash, TorrentTags.NeedsIngest, ct);
        }
        catch (QBittorrentException ex)
        {
            logger.LogWarning(ex, "Could not mark {Name} as failed.", download.Name);
        }
    }

}
