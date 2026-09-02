namespace TheKrystalShip.MovieBot.Ingest.Pipeline;

public sealed record IngestOptions
{
    public required string SourcePath { get; init; }
    public required string OutputRoot { get; init; }

    /// <summary>Slug used for the output directory and the manifest id. Derived from the title when omitted.</summary>
    public string? Id { get; init; }

    /// <summary>
    /// What the film is called, for anyone choosing one from a list.
    ///
    /// Takes precedence over the container's own title tag. A muxer writes whatever it was given,
    /// which for a release is routinely the release name — resolution, source, codec and group —
    /// and a caller that knows the film's actual name knows better than that.
    /// </summary>
    public string? Title { get; init; }

    /// <summary>
    /// Which film this is, as <c>tt0458352</c>, when something already knew. Recorded rather than
    /// derived: everything downstream that describes the film hangs from this id, and a release
    /// name is a guess where the tracker's answer is a fact.
    /// </summary>
    public string? ImdbId { get; init; }

    /// <summary>
    /// Target video bitrate. 9 Mbps H.264 High is generous for a 1080p film and, on a symmetric
    /// gigabit link, there is no reason to go lower and little visible reason to go higher.
    /// </summary>
    public string VideoBitrate { get; init; } = "9M";
    public string VideoMaxrate { get; init; } = "12M";
    public string VideoBufsize { get; init; } = "24M";

    /// <summary>NVENC constant-quality target. Lower is better quality and a larger file.</summary>
    public int Cq { get; init; } = 19;

    public string PrimaryAudioBitrate { get; init; } = "256k";
    public string CommentaryAudioBitrate { get; init; } = "128k";

    /// <summary>
    /// Segment length. The GOP is pinned to match so every segment opens on a keyframe and
    /// seeks land exactly where they were asked to.
    /// </summary>
    public int SegmentSeconds { get; init; } = 4;

    /// <summary>Overrides HDR auto-detection. Tone-mapping an SDR source washes it out.</summary>
    public bool? ForceToneMap { get; init; }

    /// <summary>Replaces an existing output directory instead of refusing to touch it.</summary>
    public bool Force { get; init; }

    /// <summary>
    /// How much of the source has arrived, when it is still arriving. Null means the file is
    /// whole, which is the ordinary case and behaves exactly as it always has.
    ///
    /// Supplying it changes the order of the work. The main pass runs first and is held back to
    /// stay behind what has arrived; the subtitles, which have to be demuxed from the whole file
    /// to be complete, wait until it is.
    /// </summary>
    public ISourceAvailability? Availability { get; init; }

    /// <summary>
    /// How far behind the arrived bytes the reader is kept, in bytes.
    ///
    /// ffmpeg reads ahead of what it has decoded, and the arrived length is sampled rather than
    /// watched, so the two are both approximate in the same direction. The margin is what stops
    /// the pair of approximations meeting.
    /// </summary>
    public long ReadAheadMarginBytes { get; init; } = 64L << 20;

    /// <summary>
    /// Probe and build the manifest, then stop. Inspecting what a film offers is worth doing
    /// before committing a GPU to it for twenty minutes.
    /// </summary>
    public bool DryRun { get; init; }

    public string OutputDirectory(string id) => Path.Combine(OutputRoot, id);
}
