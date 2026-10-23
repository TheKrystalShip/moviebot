using System.Globalization;
using TheKrystalShip.MovieBot.Ingest.Probe;

namespace TheKrystalShip.MovieBot.Ingest.Pipeline;

/// <summary>How a video rung's source frames reach the filter chain.</summary>
public enum VideoDecode
{
    /// <summary>
    /// The <c>*_cuvid</c> decoder, which also scales: the step-down is produced at its size by
    /// NVDEC, so no frame at the source's size reaches the CPU.
    /// </summary>
    Cuvid,

    /// <summary>
    /// The generic CUDA hardware decode with a CPU scale behind it. For a source whose codec has
    /// no cuvid decoder here.
    /// </summary>
    Hwaccel,

    /// <summary>A source NVDEC cannot decode, which no hardware path is asked to try.</summary>
    Software
}

/// <summary>
/// The ffmpeg command lines of the main pass: one per video rung and one for every audio
/// rendition. They are built from the probe and the options alone, so what each process is told
/// to do is testable without a card.
/// </summary>
internal sealed class MainPassArguments(IngestOptions options)
{
    private const string ProbeWindow = "200M";

    /// <summary>Decode surfaces the cuvid decoder holds; see <see cref="SourceInput"/>.</summary>
    private const string CuvidSurfaces = "32";

    /// <summary>
    /// The cuvid decoder for a source, or null where NVDEC cannot decode it. Measured on the
    /// card's generation (Pascal): H.264 decodes only as 8-bit 4:2:0, HEVC as 4:2:0 at 8 or 10
    /// bits, and High 10 H.264 is decoded in software.
    /// </summary>
    public static string? CuvidDecoder(ProbeStream video)
    {
        var pixelFormat = video.PixFmt;

        return video.CodecName switch
        {
            "h264" when pixelFormat is "yuv420p" or "yuvj420p" => "h264_cuvid",
            "hevc" when pixelFormat is "yuv420p" or "yuv420p10le" => "hevc_cuvid",
            "vp9" when pixelFormat is "yuv420p" => "vp9_cuvid",
            _ => null
        };
    }

    /// <summary>
    /// The decode a rung starts with. A codec that has a cuvid decoder uses it; one that is known
    /// to fall to software skips the hardware attempt rather than opening a CUDA context for
    /// nothing.
    /// </summary>
    public static VideoDecode DecodeFor(ProbeStream video) =>
        CuvidDecoder(video) is not null ? VideoDecode.Cuvid
        : video.CodecName == "h264" && video.PixFmt is { Length: > 0 } ? VideoDecode.Software
        : VideoDecode.Hwaccel;

    /// <summary>
    /// One rung's process: the source video decoded once, filtered for the rung and encoded to
    /// its own playlist, from where <paramref name="resume"/> says when it carries on a stopped one.
    /// </summary>
    public List<string> Video(
        ProbeStream video, VideoRung rung, string outputDirectory, bool toneMap, VideoDecode decode,
        TrackResume? resume = null)
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

        // NVDEC, so that a 16 Mbps HEVC Main 10 stream does not cost every core for the length of
        // a feature. Frames land in system memory, which is where the OpenCL tone-map filter
        // wants them. A rung below the source's size is scaled in the decoder where there is one,
        // and by the CPU where there is not.
        var decoderScales = decode == VideoDecode.Cuvid && rung.ScaleFilter is not null;
        SourceInput(args, decode, video, decoderScales ? rung : null, resume);

        var gop = GopSize(video.FrameRate(), options.SegmentSeconds);

        args.Add("-map"); args.Add($"0:{video.Index}");
        args.Add("-vf"); args.Add(FilterChain(toneMap, decoderScales ? null : rung.ScaleFilter));

        args.AddRange([
            "-c:v", "h264_nvenc", "-preset", "p5", "-rc", "vbr",
            "-cq", options.Cq.ToString(CultureInfo.InvariantCulture),
            "-b:v", rung.Bitrate,
            "-maxrate", rung.Maxrate,
            "-bufsize", rung.Bufsize,
            "-profile:v", "high", "-level", "4.2",
            "-g", gop.ToString(CultureInfo.InvariantCulture),
            "-keyint_min", gop.ToString(CultureInfo.InvariantCulture),
            "-sc_threshold", "0",
            // Guarantees a keyframe exactly on each segment boundary whatever the frame rate, so a
            // seek lands on the frame it was asked for rather than the nearest GOP. Every rung is
            // its own process and is cut on this same expression, which is what lets a player
            // switch between them mid-film: segment n of one rung opens on the same frame as
            // segment n of the next.
            "-force_key_frames", $"expr:gte(t,n_forced*{options.SegmentSeconds})",
            "-an", "-sn"
        ]);
        args.AddRange(HlsOutput(Path.Combine(outputDirectory, rung.Directory), resume));

        return args;
    }

    /// <summary>
    /// Deals the renditions out to the audio processes, a track at a time, so that the heavy
    /// tracks of a disc (the 7.1 ones come first) do not all land in one process. Never more
    /// processes than tracks.
    /// </summary>
    public static List<int[]> AudioGroups(int tracks, int processes)
    {
        var count = Math.Clamp(processes, 1, Math.Max(tracks, 1));
        return Enumerable.Range(0, count)
            .Select(g => Enumerable.Range(0, tracks).Where(i => i % count == g).ToArray())
            .ToList();
    }

    /// <summary>
    /// One audio process: the given renditions of the source's tracks, each through a demuxer of
    /// its own. One demuxer feeding several outputs runs at the pace of the fastest of them and
    /// queues the rest in memory without limit — measured on a 1080p Blu-ray rip with four audio
    /// tracks, 9 MB a second of wall clock, so a feature exhausts an 8 GB host a third of the way
    /// in and is killed. Separate demuxers each wait on their own consumer and hold flat.
    /// </summary>
    /// <param name="renditions">Indexes into <paramref name="audioStreams"/>; rendition <c>i</c> is written to <c>a{i}</c>.</param>
    /// <param name="resumes">Where each rendition carries on a stopped one, by its index; absent for one made from the start.</param>
    public List<string> Audio(List<ProbeStream> audioStreams, IReadOnlyList<int> renditions, string outputDirectory,
        IReadOnlyDictionary<int, TrackResume>? resumes = null)
    {
        var args = new List<string> { "-y" };

        foreach (var i in renditions)
            SourceInput(args, VideoDecode.Software, video: null, scaledTo: null, resumes?.GetValueOrDefault(i));

        for (var k = 0; k < renditions.Count; k++)
        {
            var i = renditions[k];
            var stream = audioStreams[i];
            args.Add("-map"); args.Add($"{k}:{stream.Index}");
            args.Add("-c:a"); args.Add("aac");
            args.Add("-b:a"); args.Add(stream.IsCommentary
                ? options.CommentaryAudioBitrate
                : options.PrimaryAudioBitrate);
            // Stereo everywhere: multichannel AAC over MSE is inconsistent across browsers, and
            // most of a movie night is on headphones or laptop speakers.
            args.Add("-ac"); args.Add("2");
            args.AddRange(HlsOutput(Path.Combine(outputDirectory, $"a{i}"), resumes?.GetValueOrDefault(i)));
        }

        return args;
    }

    /// <summary>
    /// What a rung's frames pass through on the way to the encoder.
    ///
    /// Tone-mapping runs ahead of any CPU scale, so that step-down is built from frames already
    /// in bt709. A rung the decoder has scaled arrives at its size and is mapped there.
    /// </summary>
    internal static string FilterChain(bool toneMap, string? scale)
    {
        var chain = toneMap
            ? "format=p010le,hwupload,tonemap_opencl=tonemap=hable:desat=0"
              + ":transfer=bt709:matrix=bt709:primaries=bt709:format=nv12,hwdownload,format=nv12"
            : "format=yuv420p";

        return scale is null ? chain : $"{chain},{scale}";
    }

    /// <summary>
    /// Opens the source as an input, through the decoder a rung asks for, and from where a resumed
    /// track carries on.
    ///
    /// A whole source is seeked as an input, which reads from the nearest keyframe before the point;
    /// one still arriving is read from its start and decoded up to the point (<see cref="HlsOutput"/>),
    /// because a seek reads wherever the container's index says, and what has not arrived reads as
    /// zeros. Either way the first frame kept is the first one at or after the point, and the point is
    /// a millisecond early so the frame the stopped track's next segment opened on is never the one
    /// dropped.
    /// </summary>
    public void SourceInput(
        List<string> args, VideoDecode decode, ProbeStream? video, VideoRung? scaledTo, TrackResume? resume = null)
    {
        args.Add("-probesize"); args.Add(ProbeWindow);
        args.Add("-analyzeduration"); args.Add(ProbeWindow);

        switch (decode)
        {
            case VideoDecode.Cuvid:
                args.Add("-c:v"); args.Add(CuvidDecoder(video!)!);
                // The decoder's default surface pool is too small for a source with B-frames:
                // a surface is reused before the frame on it has been handed on, and that frame
                // goes out as one from 7 to 9 frames later. Measured on an x264 Blu-ray rip, the
                // first 11.5 minutes carried 74 to 82 such frames, in bursts; with this pool, none.
                args.Add("-surfaces"); args.Add(CuvidSurfaces);
                if (scaledTo is not null)
                {
                    args.Add("-resize");
                    args.Add($"{scaledTo.Width.ToString(CultureInfo.InvariantCulture)}"
                             + $"x{scaledTo.Height.ToString(CultureInfo.InvariantCulture)}");
                }
                break;
            case VideoDecode.Hwaccel:
                args.Add("-hwaccel"); args.Add("cuda");
                break;
        }

        if (resume is not null && options.Availability is null) { args.Add("-ss"); args.Add(SeekPoint(resume)); }
        args.Add("-i"); args.Add(options.SourcePath);
    }

    private static string SeekPoint(TrackResume resume) =>
        Math.Max(0, resume.SourceSeconds - 0.001).ToString("0.######", CultureInfo.InvariantCulture);

    /// <summary>
    /// The playlist a track is written to. A resumed track numbers its segments on from the ones kept
    /// and writes a playlist and initialisation file of its own, which the pipeline publishes from
    /// (<see cref="TrackResume.Publish"/>); one still arriving is decoded up to where it carries on.
    /// </summary>
    public IEnumerable<string> HlsOutput(string directory, TrackResume? resume = null) =>
    [
        .. resume is not null && options.Availability is not null ? ["-ss", SeekPoint(resume)] : Array.Empty<string>(),
        .. resume is not null ? ["-start_number", resume.Segments.ToString(CultureInfo.InvariantCulture)] : Array.Empty<string>(),
        "-f", "hls",
        "-hls_time", options.SegmentSeconds.ToString(CultureInfo.InvariantCulture),
        // EVENT rather than VOD: the playlist only ever appends, so a player can join and seek
        // within what exists while ffmpeg is still writing. ffmpeg appends #EXT-X-ENDLIST on
        // completion, which turns it into an ordinary complete VOD with nothing to clean up.
        "-hls_playlist_type", "event",
        "-hls_segment_type", "fmp4",
        "-hls_fmp4_init_filename", resume?.ResumeInit ?? $"{options.SegmentPrefix}init.mp4",
        "-hls_segment_filename", Path.Combine(directory, $"{options.SegmentPrefix}seg%05d.m4s"),
        Path.Combine(directory, resume is null ? "index.m3u8" : TrackResume.ResumePlaylist)
    ];

    private static int GopSize(double frameRate, int segmentSeconds)
    {
        if (frameRate <= 0) return 96;
        return Math.Max(1, (int)Math.Round(frameRate * segmentSeconds));
    }
}
