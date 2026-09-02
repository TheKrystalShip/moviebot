using System.Text.Json.Serialization;

namespace TheKrystalShip.MovieBot.Core;

/// <summary>
/// Where a title is in its journey from source file to something a room can watch.
/// </summary>
public enum TitleStatus
{
    /// <summary>ffmpeg is still writing. Playlists are EVENT type and grow; seeks clamp to the head.</summary>
    [JsonStringEnumMemberName("transcoding")] Transcoding,

    /// <summary>Every rendition is complete. Playlists are VOD; the whole film is seekable.</summary>
    [JsonStringEnumMemberName("ready")] Ready,

    /// <summary>The transcode died. Nothing further will be written.</summary>
    [JsonStringEnumMemberName("failed")] Failed
}

/// <summary>
/// Whether a track belongs to the film or to a commentary over it. Driven by ffmpeg's
/// <c>comment</c> disposition, and the reason a 48-track subtitle list is navigable.
/// </summary>
public enum TrackKind
{
    [JsonStringEnumMemberName("feature")] Feature,
    [JsonStringEnumMemberName("commentary")] Commentary
}

/// <summary>
/// Where a subtitle track came from, which decides whether it can be offered at all.
/// </summary>
public enum SubtitleSource
{
    /// <summary>A text codec inside the container. Converts to WebVTT directly.</summary>
    [JsonStringEnumMemberName("embedded-text")] EmbeddedText,

    /// <summary>A .srt or .vtt file sitting beside the source.</summary>
    [JsonStringEnumMemberName("sidecar")] Sidecar,

    /// <summary>PGS or VobSub: pictures of text. Unusable without an OCR pass.</summary>
    [JsonStringEnumMemberName("bitmap")] Bitmap
}

/// <summary>
/// The authoritative description of one title: what it is, how far along its transcode is,
/// and every track a viewer may select. Written by the ingest worker, read by the API and
/// the player. The bot never reads it.
/// </summary>
public sealed class Manifest
{
    public required string Id { get; init; }
    /// <summary>
    /// What the film is called. Settable because it is not always known when the manifest is
    /// first written: a film identified later is renamed from whatever its release said to
    /// whatever it is actually called.
    /// </summary>
    public required string Title { get; set; }
    public required double DurationSeconds { get; init; }

    public TitleStatus Status { get; set; }

    /// <summary>
    /// Seconds of the film that are actually playable right now, taken from the shortest
    /// of the written playlists. Null once <see cref="Status"/> is Ready. The hub refuses
    /// seeks beyond this.
    /// </summary>
    public double? HeadSeconds { get; set; }

    /// <summary>Set when the transcode fails, so a stalled player can say why.</summary>
    public string? Error { get; set; }

    /// <summary>
    /// A file under the title's own directory, or null while the film has no artwork. Settable
    /// for the same reason as <see cref="Title"/>: a source carrying no cover art can be given
    /// one from the catalogue long after it was ingested.
    /// </summary>
    public string? Poster { get; set; }

    /// <summary>
    /// The HLS master playlist binding audio to video. A player should load this rather than a
    /// bare video rendition, which plays silently and offers no audio track to switch to.
    /// </summary>
    public string? Master { get; init; }

    /// <summary>
    /// Which film this is, as opposed to which file it came from. Null when nothing identified it.
    /// </summary>
    public FilmIdentity? Film { get; set; }

    /// <summary>
    /// Settable for the same reason as <see cref="Subtitles"/>, and null until it is known. A
    /// source that is still arriving cannot be fingerprinted: the hash covers the end of the file,
    /// and the space reserved for the end reads as zeroes until it lands.
    /// </summary>
    public SourceFingerprint? Source { get; set; }

    public required VideoInfo Video { get; init; }
    public required IReadOnlyList<AudioTrack> Audio { get; init; }
    /// <summary>
    /// Settable, like <see cref="Status"/> and <see cref="HeadSeconds"/>, because a track can
    /// become available after the manifest is first written: a source that is still arriving
    /// cannot have its subtitles demuxed until it has.
    /// </summary>
    public required IReadOnlyList<SubtitleTrack> Subtitles { get; set; }

    /// <summary>
    /// Languages the source carries that were not extracted, as a single line the menu can show.
    ///
    /// A room that reads one language does not want thirty rows, but a language simply missing
    /// from a film that plainly has it reads as a fault. Naming what was left behind answers that
    /// in one row instead of thirty, and says what is there to go back for.
    /// </summary>
    public IReadOnlyList<string> OtherLanguages { get; set; } = [];

    /// <summary>
    /// Where the film's parts begin, as the container already records them. Nothing is generated
    /// or guessed: a disc carries these, and a release that does not simply has none.
    /// </summary>
    public IReadOnlyList<Chapter> Chapters { get; init; } = [];

    /// <summary>
    /// The strip of frames the scrub bar previews from. Written after the main pass, because it is
    /// read from the whole source, so it is null until then and null for a source that has none.
    /// </summary>
    public ThumbnailStrip? Thumbnails { get; set; }
}

/// <summary>One point in the film with a name, taken from the container.</summary>
public sealed record Chapter
{
    public required double StartSeconds { get; init; }

    /// <summary>Null when the container numbered its chapters rather than naming them.</summary>
    public string? Title { get; init; }
}

/// <summary>
/// Every frame the scrub bar previews, in one image.
///
/// One image rather than hundreds of files because a preview is wanted the instant a pointer
/// lands on the bar: a request per frame would spend the whole hover fetching, and a browser
/// holds one sprite in memory and moves a window over it for nothing.
/// </summary>
public sealed record ThumbnailStrip
{
    public required string Uri { get; init; }

    /// <summary>Seconds of film between one frame and the next.</summary>
    public required double IntervalSeconds { get; init; }

    public required int Columns { get; init; }
    public required int Rows { get; init; }

    /// <summary>The size of one frame within the sheet, in pixels.</summary>
    public required int Width { get; init; }
    public required int Height { get; init; }

    /// <summary>How many frames were actually written. The last row is rarely full.</summary>
    public required int Count { get; init; }
}

/// <summary>
/// A subtitle fetched from outside and kept beside the film rather than inside it.
///
/// It lives outside the media root on purpose. Everything under that root is regenerable from the
/// source file; this is not — it cost one of a limited number of daily downloads — so a re-ingest
/// that replaces a title's directory must not be able to take it with it. Keeping it elsewhere
/// also means the transcode and this can never write to the same place.
///
/// One person fetching a subtitle adds it for the whole room. Which track each viewer then
/// selects stays their own choice, as it always was.
/// </summary>
public sealed record SidecarSubtitle
{
    /// <summary>Stable and derived from where it came from, so fetching it twice replaces it.</summary>
    public required string Id { get; init; }

    public required string Language { get; init; }

    /// <summary>
    /// What the menu shows. It names the release the subtitle was timed for, because a room that
    /// has fetched three of these otherwise sees three rows all reading "English".
    /// </summary>
    public required string Label { get; init; }

    public bool HearingImpaired { get; init; }

    /// <summary>The release it was timed against, as its uploader stated it.</summary>
    public string? Release { get; init; }

    /// <summary>Who fetched it, so a room can tell who to ask about a bad one.</summary>
    public string? AddedBy { get; init; }

    public DateTimeOffset AddedAt { get; init; }

    /// <summary>
    /// The offset measured against a track already known to fit the film, and already applied to
    /// the file on disk. Null means nothing could be measured, not that no shift was needed.
    /// </summary>
    public double? AppliedShiftSeconds { get; init; }

    /// <summary>How much of it lined up at that offset, as a fraction.</summary>
    public double? AlignedFraction { get; init; }
}

/// <summary>
/// Which film a title is, independently of the file it was made from.
///
/// Separate from <see cref="SourceFingerprint"/> because the two answer different questions and
/// age differently. A hash and a frame rate describe one encode and are meaningless for another;
/// an IMDb id describes the film itself, so it stays true across every copy of it and is what
/// anything describing the film — its genres, its cast, its artwork — hangs from.
/// </summary>
public sealed record FilmIdentity
{
    /// <summary>The IMDb id in its canonical form, <c>tt0458352</c>.</summary>
    public string? ImdbId { get; init; }

    /// <summary>
    /// The film's name as the database has it.
    ///
    /// A title parsed out of a release name is a good guess and no more: it keeps the edition
    /// words a release carries, loses the punctuation a name has, and is whatever the person who
    /// packed the file typed. This is the name, and it is what anything showing the film to a
    /// person uses.
    /// </summary>
    public string? Name { get; init; }

    /// <summary>The year the film came out, which is what separates a remake from what it remade.</summary>
    public int? Year { get; init; }

    /// <summary>Top-billed cast, as one line.</summary>
    public string? Starring { get; init; }

    /// <summary>
    /// Where the artwork was taken from, so a later pass can tell a poster that is still the
    /// current one from a poster that was the current one when it was fetched.
    /// </summary>
    public string? PosterUrl { get; init; }

    /// <summary>The film's page, for a message that wants somewhere to send a person.</summary>
    public string? Url => ImdbId is { Length: > 0 } id ? $"https://www.imdb.com/title/{id}/" : null;

    /// <summary>What to call the film: its name and year where they are known, and nothing invented.</summary>
    public string? Display =>
        Name is not { Length: > 0 } name ? null
        : Year is { } year ? $"{name} ({year})"
        : name;
}

/// <summary>
/// What identifies the file a title was made from, kept so a subtitle found elsewhere can be
/// matched against it long after the source itself is gone.
///
/// Each field answers something a viewer would otherwise have to discover by watching: whether a
/// subtitle was timed against this exact release, and whether it will drift.
/// </summary>
public sealed record SourceFingerprint
{
    /// <summary>The release the file arrived as, which is what an uploaded subtitle names.</summary>
    public required string Release { get; init; }

    public required long SizeBytes { get; init; }

    /// <summary>
    /// OpenSubtitles' hash of the source: its length folded together with its first and last 64 KiB.
    /// Subtitles are uploaded against it, so a match means timed against this file rather than
    /// against another cut of the same film. Null for a source too small to hash.
    /// </summary>
    public string? MovieHash { get; init; }

    /// <summary>
    /// Frames per second of the source. A subtitle timed at a different rate drifts further out of
    /// sync the longer the film runs, which is the fault people describe as the second half being
    /// worse than the first.
    /// </summary>
    public required double FrameRate { get; init; }
}

public sealed record VideoInfo
{
    public required int Width { get; init; }
    public required int Height { get; init; }

    /// <summary>The codec of the file on disk, kept for diagnostics.</summary>
    public required string SourceCodec { get; init; }

    /// <summary>
    /// The source's dynamic range, e.g. "hdr10", "hdr10+dovi-p8.1", "hlg" or "sdr".
    /// Decides whether the pipeline tone-maps; tone-mapping an SDR source washes it out
    /// exactly as failing to tone-map an HDR one does.
    /// </summary>
    public required string SourceHdr { get; init; }

    public required IReadOnlyList<Rendition> Renditions { get; init; }
}

public sealed record Rendition
{
    public required string Name { get; init; }
    public required int BitrateKbps { get; init; }
    public required string Uri { get; init; }
}

public sealed record AudioTrack
{
    public required string Id { get; init; }
    public required TrackKind Kind { get; init; }
    public required string Language { get; init; }
    public required string Label { get; init; }
    public required int Channels { get; init; }
    public bool Default { get; init; }
    public required string Uri { get; init; }
}

/// <summary>
/// Somebody watched a film with this track and said it was right.
///
/// It is the only ground truth there is. Frame rate, release name and hash are all proxies for
/// whether a subtitle looks right on screen; a person watching is that question answered directly,
/// so a pin outranks every measurement including our own.
/// </summary>
public sealed record SubtitlePin
{
    public required string PinnedBy { get; init; }

    public required DateTimeOffset PinnedAt { get; init; }

    /// <summary>
    /// How far into the film it had been watched when it was pinned, as a fraction.
    ///
    /// Drift only shows up late. A track pinned two minutes in has not been cleared of it; one
    /// pinned near the end has. Without this the two are indistinguishable, and the weaker claim
    /// would be read as the stronger one.
    /// </summary>
    public double? WatchedFraction { get; init; }
}

public sealed record SubtitleTrack
{
    public required string Id { get; init; }
    public required TrackKind Kind { get; init; }
    public required string Language { get; init; }
    public required string Label { get; init; }
    public bool HearingImpaired { get; init; }
    public bool Forced { get; init; }
    public required SubtitleSource Source { get; init; }

    /// <summary>False for bitmap tracks, which are listed so the absence is explained rather than silent.</summary>
    public required bool Available { get; init; }

    /// <summary>Why an unavailable track is unavailable, e.g. "needs-ocr".</summary>
    public string? Reason { get; init; }

    /// <summary>Null when <see cref="Available"/> is false.</summary>
    public string? Uri { get; init; }

    /// <summary>Set once somebody has watched with this track and confirmed it fits.</summary>
    public SubtitlePin? Pin { get; init; }

    /// <summary>
    /// The offset measured against a track from the film and already applied to this one. Only a
    /// fetched track carries it; one that came out of the file needed nothing done to it.
    /// </summary>
    public double? AppliedShiftSeconds { get; init; }

    /// <summary>How much of it lined up at that offset. Null when nothing could be measured.</summary>
    public double? AlignedFraction { get; init; }

    /// <summary>Who fetched it, for a track that came from outside the film.</summary>
    public string? AddedBy { get; init; }
}
