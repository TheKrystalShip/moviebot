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

    public async Task<Manifest> RunAsync(CancellationToken ct = default)
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

        PrepareOutputDirectory(outputDirectory, ladder, audioStreams.Count, options.Force);

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
                // the main pass, so no output is ever appended to while a player reads it.
                if (textSubtitles.Count > 0)
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
                manifest, manifestPath, probe.Format.DurationSeconds, ct, makeGuard);
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
        catch (Exception ex)
        {
            manifest.Status = TitleStatus.Failed;
            manifest.Error = ex.Message;
            ManifestJson.WriteAtomic(manifestPath, manifest);
            throw;
        }
    }

    private static void PrepareOutputDirectory(
        string outputDirectory, IReadOnlyList<VideoRung> ladder, int audioCount, bool force)
    {
        if (Directory.Exists(outputDirectory) && Directory.EnumerateFileSystemEntries(outputDirectory).Any())
        {
            if (!force)
            {
                throw new InvalidOperationException(
                    $"{outputDirectory} already holds output. Pass --force to replace it.");
            }

            Directory.Delete(outputDirectory, recursive: true);
        }

        foreach (var rung in ladder)
            Directory.CreateDirectory(Path.Combine(outputDirectory, rung.Directory));
        for (var i = 0; i < audioCount; i++)
            Directory.CreateDirectory(Path.Combine(outputDirectory, $"a{i}"));
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
        Func<ReadAheadGuard>? makeGuard = null)
    {
        var units = BuildUnits(video, audioStreams, ladder, outputDirectory, toneMap, makeGuard);

        var playlists = ladder
            .Select(r => Path.Combine(outputDirectory, r.Directory, "index.m3u8"))
            .ToList();
        for (var i = 0; i < audioStreams.Count; i++)
            playlists.Add(Path.Combine(outputDirectory, $"a{i}", "index.m3u8"));

        using var polling = CancellationTokenSource.CreateLinkedTokenSource(ct);

        var headWatcher = Task.Run(async () =>
        {
            var announcedPlayable = false;
            while (!polling.Token.IsCancellationRequested)
            {
                var heads = playlists.Select(path => HlsPlaylist.PlayableSeconds(path)).ToList();
                var head = heads.Count == 0 ? 0 : heads.Min();
                if (head > 0)
                {
                    manifest.HeadSeconds = head;

                    // Rungs and audio advance at their own pace; a player picks among the rungs
                    // that cover its playhead, and the room's seek limit is the best of them.
                    for (var i = 0; i < ladder.Count && i < manifest.Video.Renditions.Count; i++)
                        manifest.Video.Renditions[i].HeadSeconds = heads[i];
                    manifest.AudioHeadSeconds = audioStreams.Count == 0 ? null : heads.Skip(ladder.Count).Min();

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
            await TranscodeUnit.RunAllAsync(units, ct);
        }
        finally
        {
            await polling.CancelAsync();
            await headWatcher;
        }
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
        Func<ReadAheadGuard>? makeGuard)
    {
        var units = new List<TranscodeUnit>();
        var decode = MainPassArguments.DecodeFor(video);

        foreach (var rung in ladder)
        {
            // The decoder that scales gives way to the generic hardware decode with a CPU scale
            // when it cannot open the source, so a source the cuvid decoder refuses still
            // transcodes.
            var attempts = new List<IReadOnlyList<string>>
            {
                arguments.Video(video, rung, outputDirectory, toneMap, decode)
            };
            if (decode == VideoDecode.Cuvid)
                attempts.Add(arguments.Video(video, rung, outputDirectory, toneMap, VideoDecode.Hwaccel));

            var directory = Path.Combine(outputDirectory, rung.Directory);
            units.Add(new TranscodeUnit(rung.Directory, rung.Directory, attempts,
                () => Directory.EnumerateFiles(directory, "*.m4s").Any(), makeGuard, log));
        }

        // All of a title's audio is one group, so whatever holds or resumes it can address its
        // processes together.
        var groups = MainPassArguments.AudioGroups(audioStreams.Count, AudioProcesses);
        for (var g = 0; g < groups.Count; g++)
        {
            var renditions = groups[g];
            var directories = renditions.Select(i => Path.Combine(outputDirectory, $"a{i}")).ToList();
            units.Add(new TranscodeUnit(groups.Count == 1 ? "audio" : $"audio.{g}", "audio",
                [arguments.Audio(audioStreams, renditions, outputDirectory)],
                () => directories.Any(d => Directory.EnumerateFiles(d, "*.m4s").Any()), makeGuard, log));
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
