using System.Diagnostics;
using System.Globalization;
using System.Text;
using TheKrystalShip.MovieBot.Core;
using TheKrystalShip.MovieBot.Core.Subtitles;
using TheKrystalShip.MovieBot.Ingest.Ffmpeg;
using TheKrystalShip.MovieBot.Ingest.Probe;

namespace TheKrystalShip.MovieBot.Ingest.Pipeline;

/// <summary>
/// Turns one source film into something a room can start watching within seconds.
///
/// Order matters. Subtitles and the poster are extracted first, because a monolithic .vtt being
/// appended to while a player fetches it once gives subtitles that stop partway through the film.
/// Only then does the main pass start, writing EVENT playlists that grow as segments land, so
/// playback opens long before the transcode finishes.
/// </summary>
public sealed class IngestPipeline(IngestOptions options, Action<string> log)
{
    private const string ProbeWindow = "200M";

    public async Task<Manifest> RunAsync(CancellationToken ct = default)
    {
        log($"Probing {Path.GetFileName(options.SourcePath)}");
        var probe = await FfProbe.RunAsync(options.SourcePath, ct);

        var video = probe.Streams.FirstOrDefault(s => s.IsVideo && !s.IsAttachedPicture)
            ?? throw new InvalidOperationException("No video stream found.");
        var attachedPicture = probe.Streams.FirstOrDefault(s => s.IsVideo && s.IsAttachedPicture);
        var audioStreams = probe.Streams.Where(s => s.IsAudio).ToList();
        var subtitleStreams = probe.Streams.Where(s => s.IsSubtitle).ToList();

        if (audioStreams.Count == 0)
            throw new InvalidOperationException("No audio stream found.");

        var title = options.Title
                    ?? probe.Format.Title
                    ?? Path.GetFileNameWithoutExtension(options.SourcePath);
        var id = options.Id ?? Slug(title);
        var outputDirectory = options.OutputDirectory(id);
        var dynamicRange = TrackClassifier.DescribeDynamicRange(video);
        var toneMap = options.ForceToneMap ?? TrackClassifier.NeedsToneMapping(dynamicRange);

        var textSubtitles = subtitleStreams.Where(TrackClassifier.IsTextSubtitle).ToList();
        var bitmapSubtitles = subtitleStreams.Where(TrackClassifier.IsBitmapSubtitle).ToList();

        log($"  {title}");
        log($"  {video.Width}x{video.Height} {video.CodecName} {dynamicRange}"
            + $" · {TimeSpan.FromSeconds(probe.Format.DurationSeconds):h\\:mm\\:ss}"
            + $" · tone-map {(toneMap ? "on" : "off")}");
        log($"  {audioStreams.Count} audio, {textSubtitles.Count} text subtitles"
            + $"{(bitmapSubtitles.Count > 0 ? $", {bitmapSubtitles.Count} bitmap (need OCR)" : "")}");

        var manifest = BuildManifest(id, title, probe, video, dynamicRange,
            audioStreams, textSubtitles, bitmapSubtitles, attachedPicture is not null,
            sourceStillArriving: options.Availability is not null);

        if (options.DryRun)
        {
            log(string.Empty);
            log(ManifestJson.Serialize(manifest));
            return manifest;
        }

        PrepareOutputDirectory(outputDirectory, audioStreams.Count, options.Force);

        var manifestPath = Path.Combine(outputDirectory, "manifest.json");
        ManifestJson.WriteAtomic(manifestPath, manifest);

        // Written before the transcode starts, so the master exists for as long as the title is
        // discoverable at all. Its media playlists appear moments later as ffmpeg opens them.
        MasterPlaylist.Write(outputDirectory, manifest);

        try
        {
            ReadAheadGuard? guard = null;

            if (options.Availability is null)
            {
                // The whole file is present. Everything that reads it end to end is done before
                // the main pass, so no output is ever appended to while a player reads it.
                if (textSubtitles.Count > 0)
                {
                    log($"Extracting {textSubtitles.Count} subtitle tracks");
                    await ExtractSubtitlesAsync(textSubtitles, outputDirectory, ct);
                }

                if (attachedPicture is not null)
                {
                    log("Extracting cover art");
                    await ExtractPosterAsync(attachedPicture, outputDirectory, ct);
                }
            }
            else
            {
                // The file is still arriving, so nothing that has to reach its end can run yet.
                // The main pass can, because it reads forwards and can be held back.
                guard = new ReadAheadGuard(
                    options.Availability, options.SourcePath, options.ReadAheadMarginBytes, log);
            }

            log("Transcoding — playback opens as soon as the first segments land");
            await RunMainPassAsync(video, audioStreams, outputDirectory, toneMap,
                manifest, manifestPath, probe.Format.DurationSeconds, ct, guard);

            if (guard is not null)
            {
                if (guard.Pauses > 0)
                    log($"  held back {guard.Pauses} times waiting for the download");

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

                if (attachedPicture is not null)
                {
                    log("Extracting cover art");
                    await ExtractPosterAsync(attachedPicture, outputDirectory, ct);
                }

                manifest.Subtitles = Advertise(manifest.Subtitles, textSubtitles);
                manifest.Source = Fingerprint(video);
            }

            manifest.Status = TitleStatus.Ready;
            manifest.HeadSeconds = null;
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

    private static void PrepareOutputDirectory(string outputDirectory, int audioCount, bool force)
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

        Directory.CreateDirectory(Path.Combine(outputDirectory, "v0"));
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

    private async Task ExtractPosterAsync(ProbeStream attachedPicture, string outputDirectory, CancellationToken ct)
    {
        await FfmpegProcess.RunAsync(
        [
            "-y", "-probesize", ProbeWindow, "-analyzeduration", ProbeWindow,
            "-i", options.SourcePath,
            "-map", $"0:{attachedPicture.Index}",
            "-c", "copy", "-frames:v", "1",
            Path.Combine(outputDirectory, "poster.jpg")
        ], ct: ct);
    }

    // ---- Main pass ------------------------------------------------------------------

    private async Task RunMainPassAsync(
        ProbeStream video,
        List<ProbeStream> audioStreams,
        string outputDirectory,
        bool toneMap,
        Manifest manifest,
        string manifestPath,
        double durationSeconds,
        CancellationToken ct,
        ReadAheadGuard? guard = null)
    {
        var arguments = BuildMainPassArguments(video, audioStreams, outputDirectory, toneMap);

        var playlists = new List<string> { Path.Combine(outputDirectory, "v0", "index.m3u8") };
        for (var i = 0; i < audioStreams.Count; i++)
            playlists.Add(Path.Combine(outputDirectory, $"a{i}", "index.m3u8"));

        using var polling = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var speed = 0d;

        Task? guarding = null;

        // The guard is attached the moment the process exists, because what it protects against
        // begins with the first read. A guard that cannot do its job kills the transcode rather
        // than letting it run on unwatched: the output of an unwatched run over a part-downloaded
        // file is a film with silence and stillness in it and nothing to say so.
        var transcode = FfmpegProcess.RunAsync(arguments, p => speed = p.Speed, ct,
            onStarted: process =>
            {
                if (guard is not null) guarding = GuardAsync(guard, process);
            });

        async Task GuardAsync(ReadAheadGuard watching, Process process)
        {
            try
            {
                await watching.WatchAsync(process, ct);
            }
            catch
            {
                try
                {
                    if (!process.HasExited) process.Kill(entireProcessTree: true);
                }
                catch (Exception ex) when (ex is InvalidOperationException or NotSupportedException)
                {
                    // Already gone, which is the outcome that was wanted.
                }

                throw;
            }
        }

        var headWatcher = Task.Run(async () =>
        {
            var announcedPlayable = false;
            while (!polling.Token.IsCancellationRequested)
            {
                var head = HlsPlaylist.PlayableSeconds(playlists);
                if (head > 0)
                {
                    manifest.HeadSeconds = head;
                    ManifestJson.WriteAtomic(manifestPath, manifest);

                    if (!announcedPlayable)
                    {
                        announcedPlayable = true;
                        log($"  playable now — {Format(head)} available");
                    }

                    log($"  head {Format(head)} / {Format(durationSeconds)}"
                        + $"  ({head / Math.Max(durationSeconds, 1) * 100:F0}%)"
                        + (speed > 0 ? $"  {speed:F1}x" : ""));
                }

                try { await Task.Delay(TimeSpan.FromSeconds(2), polling.Token); }
                catch (OperationCanceledException) { break; }
            }
        }, CancellationToken.None);

        try
        {
            await transcode;
        }
        finally
        {
            await polling.CancelAsync();
            await headWatcher;

            // Awaited last and deliberately allowed to replace whatever the transcode reported.
            // When the guard is the reason the transcode died, its message is the one that says
            // what actually happened; ffmpeg's is that it was killed.
            if (guarding is not null) await guarding;
        }
    }

    private List<string> BuildMainPassArguments(
        ProbeStream video, List<ProbeStream> audioStreams, string outputDirectory, bool toneMap)
    {
        var args = new List<string>();

        if (toneMap)
        {
            // libplacebo is the better filter but fails to initialise its graph on this ffmpeg
            // build; tonemap_opencl runs on the GPU and is what the measured 8.9x path uses.
            args.Add("-init_hw_device"); args.Add("opencl=ocl");
            args.Add("-filter_hw_device"); args.Add("ocl");
        }

        args.Add("-y");
        args.Add("-probesize"); args.Add(ProbeWindow);
        args.Add("-analyzeduration"); args.Add(ProbeWindow);

        // Decode on NVDEC. Software-decoding a 16 Mbps HEVC Main 10 stream costs 35 s of CPU per
        // 40 s of film against 5.5 s here, and saturates every core for the length of a feature
        // while the GPU sits half idle. Frames land in system memory, which is where the OpenCL
        // tone-map filter wants them anyway.
        args.Add("-hwaccel"); args.Add("cuda");

        args.Add("-i"); args.Add(options.SourcePath);

        // --- video ---
        args.Add("-map"); args.Add($"0:{video.Index}");
        args.Add("-vf"); args.Add(toneMap
            ? "format=p010le,hwupload,tonemap_opencl=tonemap=hable:desat=0"
              + ":transfer=bt709:matrix=bt709:primaries=bt709:format=nv12,hwdownload,format=nv12"
            : "format=yuv420p");

        var gop = GopSize(video.FrameRate(), options.SegmentSeconds);
        args.AddRange([
            "-c:v", "h264_nvenc", "-preset", "p5", "-rc", "vbr",
            "-cq", options.Cq.ToString(CultureInfo.InvariantCulture),
            "-b:v", options.VideoBitrate,
            "-maxrate", options.VideoMaxrate,
            "-bufsize", options.VideoBufsize,
            "-profile:v", "high", "-level", "4.2",
            "-g", gop.ToString(CultureInfo.InvariantCulture),
            "-keyint_min", gop.ToString(CultureInfo.InvariantCulture),
            "-sc_threshold", "0",
            // Guarantees a keyframe exactly on each segment boundary whatever the frame rate,
            // so a seek lands on the frame it was asked for rather than the nearest GOP.
            "-force_key_frames", $"expr:gte(t,n_forced*{options.SegmentSeconds})",
            "-an", "-sn"
        ]);
        args.AddRange(HlsOutput(Path.Combine(outputDirectory, "v0")));

        // --- audio, one rendition per source track ---
        for (var i = 0; i < audioStreams.Count; i++)
        {
            var stream = audioStreams[i];
            args.Add("-map"); args.Add($"0:{stream.Index}");
            args.Add("-c:a"); args.Add("aac");
            args.Add("-b:a"); args.Add(stream.IsCommentary
                ? options.CommentaryAudioBitrate
                : options.PrimaryAudioBitrate);
            // Stereo everywhere: multichannel AAC over MSE is inconsistent across browsers, and
            // most of a movie night is on headphones or laptop speakers.
            args.Add("-ac"); args.Add("2");
            args.AddRange(HlsOutput(Path.Combine(outputDirectory, $"a{i}")));
        }

        return args;
    }

    private IEnumerable<string> HlsOutput(string directory) =>
    [
        "-f", "hls",
        "-hls_time", options.SegmentSeconds.ToString(CultureInfo.InvariantCulture),
        // EVENT rather than VOD: the playlist only ever appends, so a player can join and seek
        // within what exists while ffmpeg is still writing. ffmpeg appends #EXT-X-ENDLIST on
        // completion, which turns it into an ordinary complete VOD with nothing to clean up.
        "-hls_playlist_type", "event",
        "-hls_segment_type", "fmp4",
        "-hls_fmp4_init_filename", "init.mp4",
        "-hls_segment_filename", Path.Combine(directory, "seg%05d.m4s"),
        Path.Combine(directory, "index.m3u8")
    ];

    private static int GopSize(double frameRate, int segmentSeconds)
    {
        if (frameRate <= 0) return 96;
        return Math.Max(1, (int)Math.Round(frameRate * segmentSeconds));
    }

    // ---- Manifest -------------------------------------------------------------------

    private Manifest BuildManifest(
        string id, string title, ProbeResult probe, ProbeStream video, string dynamicRange,
        List<ProbeStream> audioStreams, List<ProbeStream> textSubtitles,
        List<ProbeStream> bitmapSubtitles, bool hasPoster, bool sourceStillArriving = false)
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

        var bitrateKbps = ParseBitrate(options.VideoBitrate);

        return new Manifest
        {
            Id = id,
            Title = title,
            DurationSeconds = probe.Format.DurationSeconds,
            Status = TitleStatus.Transcoding,
            HeadSeconds = 0,
            Poster = hasPoster ? "poster.jpg" : null,
            Master = MasterPlaylist.FileName,
            Video = new VideoInfo
            {
                Width = video.Width ?? 0,
                Height = video.Height ?? 0,
                SourceCodec = video.CodecName ?? "unknown",
                SourceHdr = dynamicRange,
                Renditions =
                [
                    new Rendition { Name = $"{video.Height ?? 0}p", BitrateKbps = bitrateKbps, Uri = "v0/index.m3u8" }
                ]
            },
            Audio = audio,
            // Known from the moment the film was chosen, so unlike the fingerprint it does not
            // wait on the file being whole.
            Film = options.ImdbId is { Length: > 0 } imdbId
                ? new FilmIdentity { ImdbId = imdbId }
                : null,
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

    private static int ParseBitrate(string value)
    {
        var trimmed = value.Trim();
        var multiplier = 1;

        if (trimmed.EndsWith('M') || trimmed.EndsWith('m')) { multiplier = 1000; trimmed = trimmed[..^1]; }
        else if (trimmed.EndsWith('K') || trimmed.EndsWith('k')) { trimmed = trimmed[..^1]; }

        return int.TryParse(trimmed, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)
            ? parsed * multiplier
            : 0;
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
