using System.Text;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TheKrystalShip.MovieBot.Acquire.Download;
using TheKrystalShip.MovieBot.Acquire.Tracker;
using TheKrystalShip.MovieBot.Acquire.Search;
using TheKrystalShip.MovieBot.Core;
using TheKrystalShip.MovieBot.Ingest.Pipeline;

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
    IOptions<HandoffOptions> options,
    ILogger<HandoffWorker> logger) : BackgroundService
{
    private readonly HandoffOptions _options = options.Value;

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
        if (string.IsNullOrWhiteSpace(_options.MediaRoot))
            throw new InvalidOperationException(
                "No media root. Set Handoff__MediaRoot to the directory the API serves from.");

        logger.LogInformation(
            "Watching for finished downloads, writing into {MediaRoot}.", _options.MediaRoot);

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

        var owed = downloads
            .Where(d => d.Tags.Contains(TorrentTags.NeedsIngest) && IsFarEnoughAlong(d))
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

            _ = Task.Run(async () =>
            {
                try
                {
                    await IngestAsync(download, ct);
                }
                finally
                {
                    _inFlight.TryRemove(download.Hash, out _);
                }
            }, ct);
        }
    }

    /// <summary>
    /// Whether there is enough of a download to start reading it. A finished one always is; an
    /// unfinished one needs its first pieces and its index, which the head start covers.
    /// </summary>
    private bool IsFarEnoughAlong(DownloadStatus download) =>
        download.IsFinished
        || (_options.StartAtProgress > 0 && download.Progress >= _options.StartAtProgress);

    private async Task IngestAsync(DownloadStatus download, CancellationToken ct)
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
        var title = LibraryId.TitleFor(download.Name);
        logger.LogInformation(
            "Ingesting {Name} from {Source} as {Id} ({Title}), download at {Progress:P0}.",
            download.Name, Path.GetFileName(source), id, title, download.Progress);

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
            OutputRoot = _options.MediaRoot,
            Id = id,
            Title = title,
            ImdbId = imdbId,
            Film = film is null ? null : FilmMetadata.Identify(film),
            PosterSource = poster,
            SubtitleLanguages = _options.SubtitleLanguages,
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
