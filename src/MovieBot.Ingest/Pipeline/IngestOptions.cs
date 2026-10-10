using TheKrystalShip.MovieBot.Core;

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
    /// What the film is, as far as anything that catalogues films is concerned: its name, its
    /// year, its billing and where its artwork came from.
    ///
    /// Resolved before the ingest rather than after it, because the manifest is written before
    /// the transcode starts and rewritten as it runs — a film is watchable, and announced, long
    /// before the last segment lands, and metadata added afterwards would arrive after everything
    /// that shows it.
    /// </summary>
    public FilmIdentity? Film { get; init; }

    /// <summary>
    /// Where the source sits in its show, when it is an episode. <see cref="Film"/> is then the show,
    /// and the title is the show's name with the episode's place in it.
    /// </summary>
    public EpisodeIdentity? Episode { get; init; }

    /// <summary>
    /// The poster put beside the title: the title index's, on disk. A source's own cover art is never
    /// used.
    ///
    /// A path rather than an address: fetching it belongs to whoever knows where films are
    /// catalogued, and the ingest's whole job is turning one directory into another.
    /// </summary>
    public string? PosterSource { get; init; }

    /// <summary>
    /// Rebuilds only the scrub previews for a title already in the library, and leaves everything
    /// else where it is. What a preview is made from is the source file and nothing the transcode
    /// produced, so there is no reason to spend an hour re-encoding a film to correct a sheet.
    /// </summary>
    public bool ThumbnailsOnly { get; init; }

    /// <summary>
    /// Which subtitle languages to extract. Empty keeps every one of them, which is what a run by
    /// hand against an unfamiliar film should do.
    ///
    /// A track with no language tag survives whatever this says. Releases ship one untagged
    /// subtitle often enough, it is usually the one worth having, and dropping it would leave a
    /// film with none and nothing to explain it.
    /// </summary>
    public IReadOnlyList<string> SubtitleLanguages { get; init; } = [];

    /// <summary>
    /// Target bitrate of the top rung. 9 Mbps H.264 High is generous for a 1080p film and, on a
    /// symmetric gigabit link, there is no reason to go lower and little visible reason to go
    /// higher.
    /// </summary>
    public string VideoBitrate { get; init; } = $"{Ladder.TopKbps}k";

    /// <summary>
    /// The rungs under the top one: the width each is scaled to and the bitrate it is encoded at,
    /// widest first. Empty writes the top rung alone.
    ///
    /// A player can only drop to a rung that exists, and these are the rungs it drops to. `v1`, at
    /// 1280 wide, is sized to carry a path the top rung cannot — a little over a third of its
    /// bitrate — because a viewer who cannot sustain the top bitrate otherwise has nowhere to go,
    /// and a fragment that cannot arrive in time stops the film for good at whatever second the
    /// buffer empties. `v2`, at 854 wide, is the copy a phone keeps offline and what a weak mobile
    /// connection streams; its bitrate is measured below.
    ///
    /// The ladder stops at three, because every rung is a full copy of the film on a disk that
    /// holds the whole library. A source no wider than a step-down does without it: upscaling
    /// spends the space and carries no more picture than the source has.
    /// </summary>
    public IReadOnlyList<LadderStep> StepDowns { get; init; } = Ladder.StepDowns;

    /// <summary>
    /// The NVENC constant-quality target every rung is encoded at. Lower is better quality and a
    /// larger file.
    ///
    /// 25 is where the picture stops being worth the disk. Scored with VMAF against the source on
    /// two-minute clips of a grainy Blu-ray, a Hi10P one, a clean web release and an animated
    /// film, it keeps the mean between 93.6 and 96.8, within three points of 19 on each, at
    /// roughly half of 19's size; a difference a viewer notices is around six. Below it, a clean
    /// source comes out larger than the file it was made from.
    /// </summary>
    public const int DefaultCq = 25;

    /// <inheritdoc cref="DefaultCq"/>
    public int Cq { get; init; } = DefaultCq;

    public string PrimaryAudioBitrate { get; init; } = "256k";
    public string CommentaryAudioBitrate { get; init; } = "128k";

    /// <summary>
    /// Whether each feature track gains a second mix with the dialogue raised over the music and
    /// effects. It is made after the main pass, so it costs nothing in how soon a film plays and
    /// only delays the moment the title reports itself ready. Off unless asked for: the pass is
    /// CPU-bound and fills every core for as long as it runs.
    /// </summary>
    public bool DialogueBoost { get; init; }

    /// <summary>
    /// Segment length. The GOP is pinned to match so every segment opens on a keyframe and
    /// seeks land exactly where they were asked to.
    ///
    /// A segment is the unit a stall is measured in: a player abandons one that does not arrive in
    /// time, and retries fetch the whole thing again. Two seconds keeps a top-rung segment near
    /// two megabytes, small enough to complete over a path that has slowed. The cost is a keyframe
    /// every two seconds, which the encoder pays for in a few per cent of bitrate.
    /// </summary>
    public int SegmentSeconds { get; init; } = Ladder.SegmentSeconds;

    /// <summary>
    /// Put before the name of every segment and initialisation file a rung writes. A segment is
    /// served as immutable for a year, so a rung encoded again under the URLs of the encode it
    /// replaces would be mixed with the old one in any cache that kept it; a different name is a
    /// different URL.
    /// </summary>
    public string SegmentPrefix { get; init; } = "";

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
