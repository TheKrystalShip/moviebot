using System.Diagnostics;
using System.Text;
using TheKrystalShip.MovieBot.Core;
using TheKrystalShip.MovieBot.Core.Subtitles;
using TheKrystalShip.MovieBot.Ingest.Ffmpeg;
using TheKrystalShip.MovieBot.Ingest.Probe;

namespace TheKrystalShip.MovieBot.Ingest.Pipeline;

/// <summary>
/// Turns one source film into something a room can start watching within seconds.
///
/// Order matters. Subtitles are extracted first, because a monolithic .vtt being
/// appended to while a player fetches it once gives subtitles that stop partway through the film.
/// Only then does the main pass start, writing EVENT playlists that grow as segments land, so
/// playback opens long before the transcode finishes.
/// </summary>
public sealed class IngestPipeline(IngestOptions options, Action<string> log)
{
    private const string ProbeWindow = "200M";

    /// <summary>
    /// How many processes the audio renditions are shared between. The host's four cores are
    /// already full with the two video processes, so more audio processes do not make the pass
    /// faster; two keep the encode of one track from queueing behind the others on one thread.
    /// </summary>
    private const int AudioProcesses = 2;

    private readonly MainPassArguments arguments = new(options);

    /// <summary>A resumed track that cannot be carried on after all, which the whole transcode then starts again for.</summary>
    private sealed class ResumeRefused(string message, Exception inner) : Exception(message, inner);

    public async Task<Manifest> RunAsync(CancellationToken ct = default)
    {
        try
        {
            return await RunOnceAsync(ct);
        }
        catch (ResumeRefused refused)
        {
            log($"  {refused.Message}; starting again from the beginning");
            return await new IngestPipeline(options with { Resume = false }, log).RunOnceAsync(ct);
        }
    }

    private async Task<Manifest> RunOnceAsync(CancellationToken ct)
    {
        log($"Probing {Path.GetFileName(options.SourcePath)}");
        var probe = await FfProbe.RunAsync(options.SourcePath, ct);

        var video = probe.Streams.FirstOrDefault(s => s.IsVideo && !s.IsAttachedPicture)
            ?? throw new InvalidOperationException("No video stream found.");
        var audioStreams = probe.Streams.Where(s => s.IsAudio).ToList();
        var subtitleStreams = probe.Streams.Where(s => s.IsSubtitle).ToList();

        if (audioStreams.Count == 0)
            throw new InvalidOperationException("No audio stream found.");

        // The film's own name where it is known. A title parsed out of a release keeps that
        // release's edition words and loses the punctuation a name has; the container's title tag
        // is routinely the release name again, so it is the last thing tried rather than the first.
        // An episode is the show's name and its place in it; the show's year would only say when
        // the show started.
        var title = (options.Episode is { } episode && options.Film?.Name is { Length: > 0 } show
                        ? $"{show} {episode.Label}" : null)
                    ?? options.Film?.Display
                    ?? options.Title
                    ?? probe.Format.Title
                    ?? Path.GetFileNameWithoutExtension(options.SourcePath);
        var id = options.Id ?? Slug(title);
        var outputDirectory = options.OutputDirectory(id);
        var dynamicRange = TrackClassifier.DescribeDynamicRange(video);
        var toneMap = options.ForceToneMap ?? TrackClassifier.NeedsToneMapping(dynamicRange);
        var ladder = VideoLadder.For(video.Width ?? 0, video.Height ?? 0, options);

        var allText = subtitleStreams.Where(TrackClassifier.IsTextSubtitle).ToList();
        var allBitmap = subtitleStreams.Where(TrackClassifier.IsBitmapSubtitle).ToList();

        var textSubtitles = allText
            .Where(t => LanguageFilter.Wanted(t.Language, options.SubtitleLanguages)).ToList();
        var bitmapSubtitles = allBitmap
            .Where(t => LanguageFilter.Wanted(t.Language, options.SubtitleLanguages)).ToList();

        // Named rather than merely counted: a language absent from a film that plainly has it
        // reads as a fault, and this is what lets the menu answer that in one line.
        var otherLanguages = allText.Concat(allBitmap)
            .Where(t => !LanguageFilter.Wanted(t.Language, options.SubtitleLanguages))
            .Select(t => LanguageFilter.Normalise(t.Language))
            .Where(l => l.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToList();

        log($"  {title}");
        log($"  {video.Width}x{video.Height} {video.CodecName} {dynamicRange}"
            + $" · {TimeSpan.FromSeconds(probe.Format.DurationSeconds):h\\:mm\\:ss}"
            + $" · tone-map {(toneMap ? "on" : "off")}");
        log("  " + string.Join(" · ", ladder.Select(r => $"{r.Width}x{r.Height} @ {r.Bitrate}")));
        log($"  {audioStreams.Count} audio, {textSubtitles.Count} text subtitles"
            + $"{(bitmapSubtitles.Count > 0 ? $", {bitmapSubtitles.Count} bitmap (need OCR)" : "")}"
            + $"{(otherLanguages.Count > 0 ? $", {otherLanguages.Count} other languages left out" : "")}");

        // Only the previews, for a title already in the library. Everything a preview is made from
        // is in the source file, so a sheet is corrected without re-encoding the film behind it.
        if (options.ThumbnailsOnly)
            return await RebuildThumbnailsAsync(id, video, probe.Format.DurationSeconds, log, ct);

        var manifest = BuildManifest(id, title, probe, video, dynamicRange, ladder,
            audioStreams, textSubtitles, bitmapSubtitles,
            sourceStillArriving: options.Availability is not null);
        manifest.OtherLanguages = otherLanguages;

        if (options.DryRun)
        {
            manifest.Poster = options.PosterSource is { Length: > 0 } ? "poster.jpg" : null;
            log(string.Empty);
            log(ManifestJson.Serialize(manifest));
            return manifest;
        }

        var resumes = FindResume(outputDirectory, manifest);
        if (resumes is null)
        {
            PrepareOutputDirectory(outputDirectory, ladder, audioStreams.Count, options.Force, log);
        }
        else
        {
            foreach (var track in resumes.Values.Where(t => !t.Complete)) track.Apply();
            log("  carrying on from where it stopped: " + string.Join(", ", resumes.Values.Select(t =>
                t.Complete ? $"{t.Name} finished" : $"{t.Name} from {Format(t.SourceSeconds)}")));
            UpdateHeads(manifest, ladder, Playlists(outputDirectory, ladder, audioStreams.Count));
        }

        // Put down before the manifest names it. A manifest is read the moment it exists — a film
        // is announced within seconds of the first segments landing, and an embed asking for a
        // poster that is not there yet gets a 404 which the surface then caches.
        CopyPoster(outputDirectory, log);
        if (File.Exists(Path.Combine(outputDirectory, "poster.jpg")))
            manifest.Poster = "poster.jpg";

        var manifestPath = Path.Combine(outputDirectory, "manifest.json");
        ManifestJson.WriteAtomic(manifestPath, manifest);

        // Written before the transcode starts, so the master exists for as long as the title is
        // discoverable at all. Its media playlists appear moments later as ffmpeg opens them.
        MasterPlaylist.Write(outputDirectory, manifest);

        try
        {
            // One guard per ffmpeg process: each reads the source through descriptors of its own
            // and is held and resumed on its own.
            var guards = new List<ReadAheadGuard>();
            Func<ReadAheadGuard>? makeGuard = null;

            if (options.Availability is null)
            {
                // The whole file is present. Everything that reads it end to end is done before
                // the main pass, so no output is ever appended to while a player reads it. A resumed
                // transcode of a whole file extracted them before it stopped, and a file a player may
                // be reading is not written again.
                var extracted = resumes is not null
                    && textSubtitles.All(t => File.Exists(Path.Combine(outputDirectory, SubtitleFileName(t))));
                if (textSubtitles.Count > 0 && !extracted)
                {
                    log($"Extracting {textSubtitles.Count} subtitle tracks");
                    await ExtractSubtitlesAsync(textSubtitles, outputDirectory, ct);
                }
            }
            else
            {
                // The file is still arriving, so nothing that has to reach its end can run yet.
                // The main pass can, because it reads forwards and can be held back.
                makeGuard = () =>
                {
                    var guard = new ReadAheadGuard(
                        options.Availability, options.SourcePath, options.ReadAheadMarginBytes, log);
                    lock (guards) guards.Add(guard);
                    return guard;
                };
            }

            log("Transcoding — playback opens as soon as the first segments land");
            var transcoding = Stopwatch.StartNew();
            await RunMainPassAsync(video, audioStreams, ladder, outputDirectory, toneMap,
                manifest, manifestPath, probe.Format.DurationSeconds, ct, makeGuard, resumes);
            log($"  transcoded in {transcoding.Elapsed:m\\:ss}"
                + $" ({probe.Format.DurationSeconds / Math.Max(transcoding.Elapsed.TotalSeconds, 1):F1}x)");

            if (makeGuard is not null)
            {
                var pauses = guards.Sum(g => g.Pauses);
                if (pauses > 0)
                    log($"  held back {pauses} times waiting for the download");

                // The main pass is held behind the download, so by the time it ends the file is
                // usually whole already. Usually is not always: a short film on a slow torrent
                // finishes transcoding what has arrived and gets here first.
                while (!await options.Availability!.IsCompleteAsync(ct))
                    await Task.Delay(TimeSpan.FromSeconds(2), ct);

                if (textSubtitles.Count > 0)
                {
                    log($"Extracting {textSubtitles.Count} subtitle tracks");
                    await ExtractSubtitlesAsync(textSubtitles, outputDirectory, ct);
                }

                manifest.Subtitles = Advertise(manifest.Subtitles, textSubtitles);
                manifest.Source = Fingerprint(video);
            }

            manifest.Thumbnails = await ThumbnailSheet.WriteAsync(
                options.SourcePath, video, probe.Format.DurationSeconds, outputDirectory, log, ct);

            // Before the status turns: a film that reports itself ready is one nothing will add
            // to, and settling it moves the directory this writes into.
            if (options.DialogueBoost)
                await AddDialogueBoostAsync(audioStreams, manifest, manifestPath, outputDirectory, ct);

            manifest.Status = TitleStatus.Ready;
            manifest.HeadSeconds = null;
            manifest.AudioHeadSeconds = null;
            foreach (var rendition in manifest.Video.Renditions) rendition.HeadSeconds = null;

            // Every segment is written, so what each track weighs is known: the master states it from
            // here on, before the manifest that says the film is ready.
            MeasuredRates.Measure(outputDirectory, manifest);
            MasterPlaylist.Write(outputDirectory, manifest);
            ManifestJson.WriteAtomic(manifestPath, manifest);
            log($"Ready: {outputDirectory}");
            return manifest;
        }
        // A transcode stopped with its process, or about to start again, has not failed: what it wrote
        // stays playable, and the next start carries it on.
        catch (Exception ex) when (ex is not ResumeRefused && !(ex is OperationCanceledException && ct.IsCancellationRequested))
        {
            manifest.Status = TitleStatus.Failed;
            manifest.Error = ex.Message;
            ManifestJson.WriteAtomic(manifestPath, manifest);
            throw;
        }
    }

    /// <summary>
    /// The tracks a stopped transcode of this same film left in <paramref name="outputDirectory"/>,
    /// each read for carrying on (<see cref="TrackResume"/>), or null when it starts from the beginning:
    /// resuming was not asked for, nothing was stopped here, what was stopped was made differently (another
    /// running time, ladder, quality or set of audio tracks), or a track of it cannot be carried on.
    /// A transcode a process stopped reports itself transcoding; one an earlier build marked failed for
    /// being cancelled was stopped too.
    /// </summary>
    private Dictionary<string, TrackResume>? FindResume(string outputDirectory, Manifest fresh)
    {
        if (!options.Resume || ReadManifest(outputDirectory) is not { } before) return null;

        var stopped = before.Status == TitleStatus.Transcoding
            || (before.Status == TitleStatus.Failed && before.Error is { } error
                && error.Contains("cancel", StringComparison.OrdinalIgnoreCase));
        static string Rung(Rendition r) => $"{r.Uri} {r.Width}x{r.Height} {r.BitrateKbps} {r.Cq}";
        var same = Math.Abs(before.DurationSeconds - fresh.DurationSeconds) < 0.5
            && before.Video.Renditions.Select(Rung).SequenceEqual(fresh.Video.Renditions.Select(Rung))
            && before.Audio.Select(a => a.Uri).SequenceEqual(fresh.Audio.Select(a => a.Uri));
        if (!stopped || !same) return null;

        var resumes = new Dictionary<string, TrackResume>(StringComparer.Ordinal);
        foreach (var uri in fresh.Video.Renditions.Select(r => r.Uri).Concat(fresh.Audio.Select(a => a.Uri)))
        {
            var directory = uri.Split('/')[0];
            try
            {
                if (TrackResume.Read(Path.Combine(outputDirectory, directory), options.SegmentPrefix, fresh.DurationSeconds) is not { } track)
                    return null;
                resumes[directory] = track;
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException)
            {
                log($"  {directory} cannot be carried on: {ex.Message}");
                return null;
            }
        }
        return resumes;
    }

    private static Manifest? ReadManifest(string outputDirectory)
    {
        try
        {
            return ManifestJson.Deserialize(File.ReadAllText(Path.Combine(outputDirectory, "manifest.json")));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
        {
            return null;
        }
    }

    /// <summary>Every track's playlist: the rungs', then the audio renditions'.</summary>
    private static List<string> Playlists(string outputDirectory, IReadOnlyList<VideoRung> ladder, int audioCount) =>
    [
        .. ladder.Select(r => Path.Combine(outputDirectory, r.Directory, "index.m3u8")),
        .. Enumerable.Range(0, audioCount).Select(i => Path.Combine(outputDirectory, $"a{i}", "index.m3u8"))
    ];

    /// <summary>
    /// The manifest's heads from what the playlists list: each rung's, the audio's, and the title's,
    /// which is the shortest of them. Returns the title's.
    /// </summary>
    private static double UpdateHeads(Manifest manifest, IReadOnlyList<VideoRung> ladder, IReadOnlyList<string> playlists)
    {
        var heads = playlists.Select(path => HlsPlaylist.PlayableSeconds(path)).ToList();
        var head = heads.Count == 0 ? 0 : heads.Min();
        manifest.HeadSeconds = head;

        // Rungs and audio advance at their own pace; a player picks among the rungs that cover its
        // playhead, and the room's seek limit is the best of them.
        for (var i = 0; i < ladder.Count && i < manifest.Video.Renditions.Count; i++)
            manifest.Video.Renditions[i].HeadSeconds = heads[i];
        manifest.AudioHeadSeconds = heads.Count == ladder.Count ? null : heads.Skip(ladder.Count).Min();
        return head;
    }

    private static void PrepareOutputDirectory(
        string outputDirectory, IReadOnlyList<VideoRung> ladder, int audioCount, bool force, Action<string> log)
    {
        if (Directory.Exists(outputDirectory) && Directory.EnumerateFileSystemEntries(outputDirectory).Any())
        {
            if (!force)
            {
                throw new InvalidOperationException(
                    $"{outputDirectory} already holds output. Pass --force to replace it.");
            }

            // Every playlist and segment goes with the directory, so a player reading them meets
            // nothing until the transcode writes them again: said, because nothing else says it.
            log(ReplacingLine(outputDirectory));
            Directory.Delete(outputDirectory, recursive: true);
        }

        foreach (var rung in ladder)
            Directory.CreateDirectory(Path.Combine(outputDirectory, rung.Directory));
        for (var i = 0; i < audioCount; i++)
            Directory.CreateDirectory(Path.Combine(outputDirectory, $"a{i}"));
    }

    /// <summary>What a transcode about to replace a title's output is throwing away, read from its manifest.</summary>
    private static string ReplacingLine(string outputDirectory)
    {
        Manifest? before;
        try
        {
            before = ManifestJson.Deserialize(File.ReadAllText(Path.Combine(outputDirectory, "manifest.json")));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
        {
            before = null;
        }
        return before switch
        {
            { Status: TitleStatus.Transcoding, HeadSeconds: { } head } =>
                $"  starting again from nothing: {TimeSpan.FromSeconds(head):h\\:mm\\:ss} already written is deleted, and anybody watching it waits until it is written again",
            { Status: var status } => $"  replacing the {status.ToString().ToLowerInvariant()} output already here",
            null => "  replacing output already here",
        };
    }

    // ---- Pre-passes -----------------------------------------------------------------

    private async Task ExtractSubtitlesAsync(
        List<ProbeStream> textSubtitles, string outputDirectory, CancellationToken ct)
    {
        var args = new List<string> { "-y", "-probesize", ProbeWindow, "-analyzeduration", ProbeWindow,
            "-i", options.SourcePath };

        foreach (var stream in textSubtitles)
        {
            args.Add("-map"); args.Add($"0:{stream.Index}");
            args.Add("-c:s"); args.Add("webvtt");
            args.Add(Path.Combine(outputDirectory, SubtitleFileName(stream)));
        }

        await FfmpegProcess.RunAsync(args, ct: ct);

        foreach (var stream in textSubtitles)
            await RepairSubtitleAsync(stream, Path.Combine(outputDirectory, SubtitleFileName(stream)), ct);
    }

    /// <summary>
    /// Some releases ship a subtitle track that was already mangled when the file was built, and
    /// the transcode carries it through untouched because nothing about it is malformed: it is
    /// valid UTF-8 and valid WebVTT, just wrong. Undo it here, where the track is whole and on
    /// disk, because a player has no way to tell that it should.
    /// </summary>
    private async Task RepairSubtitleAsync(ProbeStream stream, string path, CancellationToken ct)
    {
        if (!File.Exists(path)) return;

        var name = Path.GetFileName(path);
        var english = IsEnglish(stream);

        var result = MojibakeRepair.Repair(
            await File.ReadAllTextAsync(path, Encoding.UTF8, ct), completeDiscardedBytes: english);

        if (result.Changed)
        {
            await File.WriteAllTextAsync(path, result.Text, new UTF8Encoding(false), ct);
            log($"  {name}: repaired {result.Repaired} mangled characters"
                + (result.Unrepairable > 0
                    ? $", left {result.Unrepairable} that could not be read back"
                    : string.Empty));
        }

        // Only English is inspected afterwards. It is the track that gets selected, and warning
        // about one nobody opens teaches the reader to skip the warnings that matter.
        if (!english) return;

        var health = SubtitleHealth.Inspect(result.Text);
        if (health.Clean) return;

        log($"  {name}: English subtitles still hold {health.Count} suspect sequences"
            + $" ({string.Join(" ", health.Samples)}) — this track needs looking at");
    }

    /// <summary>ffprobe reports ISO 639-2, and a disc occasionally carries the two-letter code.</summary>
    private static bool IsEnglish(ProbeStream stream) => stream.Language is "eng" or "en";

    /// <summary>
    /// Puts a new sheet of previews under a title that already exists, and says so in its manifest.
    /// </summary>
    private async Task<Manifest> RebuildThumbnailsAsync(
        string id, ProbeStream video, double durationSeconds, Action<string> log, CancellationToken ct)
    {
        var outputDirectory = options.OutputDirectory(id);
        var manifestPath = Path.Combine(outputDirectory, "manifest.json");

        if (!File.Exists(manifestPath))
            throw new InvalidOperationException($"No title at {outputDirectory} to rebuild previews for.");

        var manifest = ManifestJson.Deserialize(await File.ReadAllTextAsync(manifestPath, ct))
                       ?? throw new InvalidOperationException($"The manifest at {manifestPath} is unreadable.");

        manifest.Thumbnails = await ThumbnailSheet.WriteAsync(
            options.SourcePath, video, durationSeconds, outputDirectory, log, ct);

        ManifestJson.WriteAtomic(manifestPath, manifest);
        log($"Previews rebuilt: {outputDirectory}");
        return manifest;
    }

    /// <summary>
    /// The title index's poster, which the hand-off fetched before the ingest started. A source's own
    /// cover art is never used: it is whatever the packer put there, often a landscape still. The API
    /// shows a title by the film database's poster and serves this one only for a title the database
    /// holds none for. Never throws: a film without artwork is worse than one with it, and better than
    /// an ingest that failed over it.
    /// </summary>
    private void CopyPoster(string outputDirectory, Action<string> log)
    {
        if (options.PosterSource is not { Length: > 0 } source || !File.Exists(source)) return;

        try
        {
            File.Copy(source, Path.Combine(outputDirectory, "poster.jpg"), overwrite: true);
            log("  poster from the title index");
        }
        catch (Exception ex)
        {
            log($"  no poster: {ex.Message}");
        }
    }

    // ---- Main pass ------------------------------------------------------------------

    private async Task RunMainPassAsync(
        ProbeStream video,
        List<ProbeStream> audioStreams,
        IReadOnlyList<VideoRung> ladder,
        string outputDirectory,
        bool toneMap,
        Manifest manifest,
        string manifestPath,
        double durationSeconds,
        CancellationToken ct,
        Func<ReadAheadGuard>? makeGuard = null,
        IReadOnlyDictionary<string, TrackResume>? resumes = null)
    {
        var owed = resumes?.Values.Where(t => !t.Complete).ToList() ?? [];
        var units = BuildUnits(video, audioStreams, ladder, outputDirectory, toneMap, makeGuard, resumes);
        var playlists = Playlists(outputDirectory, ladder, audioStreams.Count);

        // The units are ended early, and the transcode started again, when a resumed track turns out
        // to be encoded differently from what it continues.
        using var running = CancellationTokenSource.CreateLinkedTokenSource(ct);
        Exception? refused = null;
        void Publish()
        {
            foreach (var track in owed)
            {
                try
                {
                    track.Publish();
                }
                catch (InvalidDataException ex) when (refused is null)
                {
                    refused = ex;
                    running.Cancel();
                }
                catch (IOException)
                {
                    // A file being renamed into place as it was read; the next look finds it there.
                }
            }
        }

        using var polling = CancellationTokenSource.CreateLinkedTokenSource(ct);

        var headWatcher = Task.Run(async () =>
        {
            var announcedPlayable = false;
            while (!polling.Token.IsCancellationRequested)
            {
                Publish();
                var head = UpdateHeads(manifest, ladder, playlists);
                if (head > 0)
                {
                    ManifestJson.WriteAtomic(manifestPath, manifest);

                    if (!announcedPlayable)
                    {
                        announcedPlayable = true;
                        log($"  playable now — {Format(head)} available");
                    }

                    log($"  head {Format(head)} / {Format(durationSeconds)}"
                        + $"  ({head / Math.Max(durationSeconds, 1) * 100:F0}%)"
                        + (Slowest(units) is > 0 and var speed ? $"  {speed:F1}x" : ""));
                }

                try { await Task.Delay(TimeSpan.FromSeconds(2), polling.Token); }
                catch (OperationCanceledException) { break; }
            }
        }, CancellationToken.None);

        try
        {
            await TranscodeUnit.RunAllAsync(units, running.Token);
        }
        catch (OperationCanceledException) when (refused is not null && !ct.IsCancellationRequested)
        {
            // Said below, as the reason.
        }
        finally
        {
            await polling.CancelAsync();
            await headWatcher;
        }

        if (refused is null)
        {
            try
            {
                foreach (var track in owed) track.Finish();
            }
            catch (InvalidDataException ex)
            {
                refused = ex;
            }
        }
        if (refused is not null) throw new ResumeRefused(refused.Message, refused);
    }

    /// <summary>
    /// The units of the main pass: a process for each video rung and one for every audio
    /// rendition. They run side by side and share nothing but the source file, each reading it
    /// through its own demuxer.
    /// </summary>
    private List<TranscodeUnit> BuildUnits(
        ProbeStream video,
        List<ProbeStream> audioStreams,
        IReadOnlyList<VideoRung> ladder,
        string outputDirectory,
        bool toneMap,
        Func<ReadAheadGuard>? makeGuard,
        IReadOnlyDictionary<string, TrackResume>? resumes = null)
    {
        var units = new List<TranscodeUnit>();
        var decode = MainPassArguments.DecodeFor(video);

        // A track the stopped transcode finished is not made again, and one it left part way is
        // carried on from where its playlist was rewound to.
        TrackResume? ResumeOf(string directory) => resumes?.GetValueOrDefault(directory);
        bool Owed(string directory) => ResumeOf(directory) is not { Complete: true };

        foreach (var rung in ladder.Where(r => Owed(r.Directory)))
        {
            // The decoder that scales gives way to the generic hardware decode with a CPU scale
            // when it cannot open the source, so a source the cuvid decoder refuses still
            // transcodes.
            var resume = ResumeOf(rung.Directory);
            var attempts = new List<IReadOnlyList<string>>
            {
                arguments.Video(video, rung, outputDirectory, toneMap, decode, resume)
            };
            if (decode == VideoDecode.Cuvid)
                attempts.Add(arguments.Video(video, rung, outputDirectory, toneMap, VideoDecode.Hwaccel, resume));

            var directory = Path.Combine(outputDirectory, rung.Directory);
            units.Add(new TranscodeUnit(rung.Directory, rung.Directory, attempts,
                () => resume is null
                    ? Directory.EnumerateFiles(directory, "*.m4s").Any()
                    : File.Exists(Path.Combine(directory, TrackResume.ResumePlaylist)),
                makeGuard, log));
        }

        // All of a title's audio is one group, so whatever holds or resumes it can address its
        // processes together.
        var groups = MainPassArguments.AudioGroups(audioStreams.Count, AudioProcesses);
        var audioResumes = Enumerable.Range(0, audioStreams.Count)
            .Where(i => ResumeOf($"a{i}") is { Complete: false })
            .ToDictionary(i => i, i => ResumeOf($"a{i}")!);
        for (var g = 0; g < groups.Count; g++)
        {
            var renditions = groups[g].Where(i => Owed($"a{i}")).ToArray();
            if (renditions.Length == 0) continue;
            var directories = renditions.Select(i => Path.Combine(outputDirectory, $"a{i}")).ToList();
            units.Add(new TranscodeUnit(groups.Count == 1 ? "audio" : $"audio.{g}", "audio",
                [arguments.Audio(audioStreams, renditions, outputDirectory, audioResumes)],
                () => directories.Any(d => audioResumes.Count == 0
                    ? Directory.EnumerateFiles(d, "*.m4s").Any()
                    : File.Exists(Path.Combine(d, TrackResume.ResumePlaylist))),
                makeGuard, log));
        }

        return units;
    }

    /// <summary>The slowest of the running units, which is the one every head waits on.</summary>
    private static double Slowest(IEnumerable<TranscodeUnit> units)
    {
        var slowest = 0d;
        foreach (var unit in units)
        {
            if (unit.Speed <= 0) continue;
            slowest = slowest == 0 ? unit.Speed : Math.Min(slowest, unit.Speed);
        }

        return slowest;
    }

    /// <summary>
    /// Mixes a dialogue-boosted rendition of every feature track and lists each one once its
    /// playlist is complete.
    ///
    /// Listed only when whole: a track offered while it is being written plays until it runs out
    /// and then stops, partway through a film whose other tracks carry on. The boosted renditions
    /// are numbered after the source's own, so the ids a viewer's saved choice names stay where
    /// they are. A mix that fails leaves the film without it: the film is whole, and nothing about
    /// it is worse for lacking a second mix.
    /// </summary>
    private async Task AddDialogueBoostAsync(
        List<ProbeStream> audioStreams, Manifest manifest, string manifestPath,
        string outputDirectory, CancellationToken ct)
    {
        var feature = audioStreams
            .Select((stream, i) => (Stream: stream, Track: manifest.Audio[i]))
            .Where(t => t.Track.Kind == TrackKind.Feature)
            .ToList();
        if (feature.Count == 0) return;

        var boosted = new List<AudioTrack>();
        var args = new List<string> { "-y" };

        // One demuxer each, for the same reason as the main pass's renditions.
        foreach (var _ in feature)
            arguments.SourceInput(args, VideoDecode.Software, video: null, scaledTo: null);

        for (var j = 0; j < feature.Count; j++)
        {
            var (stream, track) = feature[j];
            var id = $"a{audioStreams.Count + j}";
            var directory = Path.Combine(outputDirectory, id);
            Directory.CreateDirectory(directory);

            args.Add("-map"); args.Add($"{j}:{stream.Index}");
            args.Add("-af"); args.Add(DialogueBoost.FilterFor(stream.ChannelLayout));
            args.Add("-c:a"); args.Add("aac");
            args.Add("-b:a"); args.Add(options.PrimaryAudioBitrate);
            args.Add("-ac"); args.Add("2");
            args.AddRange(arguments.HlsOutput(directory));

            boosted.Add(track with
            {
                Id = id,
                Label = track.Label + DialogueBoost.LabelSuffix,
                Default = false,
                Uri = $"{id}/index.m3u8"
            });
        }

        log($"Mixing a dialogue boost for {feature.Count} feature track{(feature.Count == 1 ? "" : "s")}");
        var started = Stopwatch.StartNew();

        try
        {
            // The film is already playable, so the mix waits behind everything else on the host:
            // the services rooms are using and the next film's main pass.
            await FfmpegProcess.RunAsync(args, ct: ct, yieldCpu: true);
        }
        catch (FfmpegException ex)
        {
            log($"  no dialogue boost: {ex.Message}");
            foreach (var track in boosted)
                Directory.Delete(Path.Combine(outputDirectory, track.Id), recursive: true);
            return;
        }

        manifest.Audio = [.. manifest.Audio, .. boosted];

        // The master first, so the manifest never lists a track the master cannot play.
        MasterPlaylist.Write(outputDirectory, manifest);
        ManifestJson.WriteAtomic(manifestPath, manifest);
        log($"  dialogue boost mixed in {started.Elapsed:m\\:ss}");
    }

    // ---- Manifest -------------------------------------------------------------------

    private Manifest BuildManifest(
        string id, string title, ProbeResult probe, ProbeStream video, string dynamicRange,
        IReadOnlyList<VideoRung> ladder,
        List<ProbeStream> audioStreams, List<ProbeStream> textSubtitles,
        List<ProbeStream> bitmapSubtitles, bool sourceStillArriving = false)
    {
        var audio = new List<AudioTrack>();
        var primaryAssigned = false;

        for (var i = 0; i < audioStreams.Count; i++)
        {
            var stream = audioStreams[i];
            var kind = TrackClassifier.KindOf(stream);
            var isDefault = !primaryAssigned && kind == TrackKind.Feature;
            if (isDefault) primaryAssigned = true;

            audio.Add(new AudioTrack
            {
                Id = $"a{i}",
                Kind = kind,
                Language = stream.Language ?? "und",
                Label = TrackClassifier.BuildLabel(stream),
                Channels = 2,
                Default = isDefault,
                Uri = $"a{i}/index.m3u8"
            });
        }

        var subtitles = new List<SubtitleTrack>();

        foreach (var stream in textSubtitles)
        {
            subtitles.Add(new SubtitleTrack
            {
                Id = $"s{stream.Index}",
                Kind = TrackClassifier.KindOf(stream),
                Language = stream.Language ?? "und",
                Label = TrackClassifier.BuildLabel(stream),
                HearingImpaired = stream.IsHearingImpaired,
                Forced = stream.IsForced,
                Source = SubtitleSource.EmbeddedText,

                // A track is advertised only once its file is whole. Offering one that is still
                // being written gives subtitles that stop partway through the film, which is the
                // reason they are not extracted alongside the transcode in the first place.
                Available = !sourceStillArriving,
                Reason = sourceStillArriving ? "extracting" : null,
                Uri = sourceStillArriving ? null : SubtitleFileName(stream)
            });
        }

        // Listed rather than dropped, so a missing language is explained instead of mysterious.
        foreach (var stream in bitmapSubtitles)
        {
            subtitles.Add(new SubtitleTrack
            {
                Id = $"s{stream.Index}",
                Kind = TrackClassifier.KindOf(stream),
                Language = stream.Language ?? "und",
                Label = TrackClassifier.BuildLabel(stream),
                HearingImpaired = stream.IsHearingImpaired,
                Forced = stream.IsForced,
                Source = SubtitleSource.Bitmap,
                Available = false,
                Reason = "needs-ocr"
            });
        }

        return new Manifest
        {
            Id = id,
            Title = title,
            DurationSeconds = probe.Format.DurationSeconds,
            Status = TitleStatus.Transcoding,
            HeadSeconds = 0,
            Master = MasterPlaylist.FileName,
            Video = new VideoInfo
            {
                Width = video.Width ?? 0,
                Height = video.Height ?? 0,
                SourceCodec = video.CodecName ?? "unknown",
                SourceHdr = dynamicRange,
                Renditions = ladder
                    .Select(r => new Rendition
                    {
                        Name = r.Name,
                        BitrateKbps = r.BitrateKbps,
                        Width = r.Width,
                        Height = r.Height,
                        Uri = r.Uri,
                        Cq = options.Cq
                    })
                    .ToList()
            },
            Audio = audio,
            // Known from the moment the film was chosen, so unlike the fingerprint it does not
            // wait on the file being whole.
            Chapters = probe.Chapters
                .Where(c => c.EndSeconds > c.StartSeconds)
                .Select(c => new Chapter { StartSeconds = c.StartSeconds, Title = c.Title })
                .ToList(),
            Film = options.Film
                   ?? (options.ImdbId is { Length: > 0 } imdbId
                       ? new FilmIdentity { ImdbId = imdbId }
                       : null),
            Kind = options.Episode is null ? TitleKind.Film : TitleKind.Show,
            Episode = options.Episode,
            Source = sourceStillArriving ? null : Fingerprint(video),
            Subtitles = subtitles
        };
    }

    /// <summary>
    /// Turns the deferred subtitle entries into advertised ones, now that their files are whole.
    /// Bitmap tracks are left as they are: theirs is a permanent absence, not a wait.
    /// </summary>
    private static IReadOnlyList<SubtitleTrack> Advertise(
        IReadOnlyList<SubtitleTrack> tracks, List<ProbeStream> textSubtitles) =>
        tracks.Select(track =>
        {
            var stream = textSubtitles.FirstOrDefault(s => $"s{s.Index}" == track.Id);
            return stream is null
                ? track
                : track with { Available = true, Reason = null, Uri = SubtitleFileName(stream) };
        }).ToList();

    private static string SubtitleFileName(ProbeStream stream) => $"s{stream.Index}.vtt";

    /// <summary>
    /// What a subtitle found elsewhere is matched against. Read from the source rather than from
    /// the transcode, because that is what an uploaded subtitle was timed to.
    /// </summary>
    private SourceFingerprint? Fingerprint(ProbeStream video)
    {
        var file = new FileInfo(options.SourcePath);
        if (!file.Exists) return null;

        return new SourceFingerprint
        {
            Release = Path.GetFileNameWithoutExtension(options.SourcePath),
            SizeBytes = file.Length,
            MovieHash = SourceHash.Compute(options.SourcePath),
            FrameRate = video.FrameRate()
        };
    }

    private static string Format(double seconds) =>
        TimeSpan.FromSeconds(seconds).ToString(@"h\:mm\:ss");

    private static string Slug(string value)
    {
        var builder = new StringBuilder(value.Length);
        var lastWasDash = false;

        foreach (var c in value.ToLowerInvariant())
        {
            if (char.IsAsciiLetterOrDigit(c))
            {
                builder.Append(c);
                lastWasDash = false;
            }
            else if (!lastWasDash && builder.Length > 0)
            {
                builder.Append('-');
                lastWasDash = true;
            }
        }

        return builder.ToString().Trim('-') is { Length: > 0 } slug ? slug : "untitled";
    }
}
