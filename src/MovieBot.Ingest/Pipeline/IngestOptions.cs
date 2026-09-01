namespace TheKrystalShip.MovieBot.Ingest.Pipeline;

public sealed record IngestOptions
{
    public required string SourcePath { get; init; }
    public required string OutputRoot { get; init; }

    /// <summary>Slug used for the output directory and the manifest id. Derived from the title when omitted.</summary>
    public string? Id { get; init; }

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
    /// Probe and build the manifest, then stop. Inspecting what a film offers is worth doing
    /// before committing a GPU to it for twenty minutes.
    /// </summary>
    public bool DryRun { get; init; }

    public string OutputDirectory(string id) => Path.Combine(OutputRoot, id);
}
