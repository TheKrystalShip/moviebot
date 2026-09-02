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
    public required string Title { get; init; }
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

    public string? Poster { get; init; }

    /// <summary>
    /// The HLS master playlist binding audio to video. A player should load this rather than a
    /// bare video rendition, which plays silently and offers no audio track to switch to.
    /// </summary>
    public string? Master { get; init; }

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
}
