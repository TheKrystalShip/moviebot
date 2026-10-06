using TheKrystalShip.MovieBot.Core;

namespace TheKrystalShip.MovieBot.Api.Offline;

/// <summary>A video rendition a title can be saved at, and what it takes.</summary>
public sealed record OfflineRung(string Rendition, string Uri, string Name, int Width, int Height, long Bytes);

/// <summary>An audio rendition a title can be saved with, and what it takes.</summary>
public sealed record OfflineAudio(
    string Rendition, string Uri, TrackKind Kind, string Language, string Label, int Channels, bool Default, long Bytes);

/// <summary>A text subtitle a title can be saved with. <c>Url</c> is set in a package and absent in an offer.</summary>
public sealed record OfflineSubtitle(
    string Id, TrackKind Kind, string Language, string Label, bool HearingImpaired, bool Forced, long Bytes,
    string? Url = null);

/// <summary>What <c>GET /api/titles/{id}/offline</c> answers: everything the title can be saved as.</summary>
public sealed record OfflineOffer(
    string TitleId, List<OfflineRung> Rungs, List<OfflineAudio> Audio, List<OfflineSubtitle> Subtitles);

/// <summary>What a device asks to save.</summary>
public sealed record OfflineSaveRequest(
    string? TitleId, string? Rung, List<string>? Audio, List<string>? Subtitles, string? Device);

/// <summary>A save as the person reads it back.</summary>
public sealed record OfflineSave(
    string Id, string TitleId, string Rung, List<string> Audio, List<string> Subtitles, string Device,
    DateTimeOffset CreatedAt, DateTimeOffset? CompletedAt, DateTimeOffset? HoldsUntil);

/// <summary>
/// One track of a package: the file a device fetches whole, and where each segment lies in it, which
/// is what the playlists it plays from are built of.
/// </summary>
public sealed record OfflineTrack(
    string Kind, string Rendition, string Uri, string Url, long Bytes, string Etag, long InitBytes,
    double[] SegmentSeconds, long[] SegmentBytes);

/// <summary>An episode's own description, beside its show's.</summary>
public sealed record OfflineEpisode(int Season, int Number, string? Name, string? Overview);

/// <summary>The artwork a saved title is shown with, each at an open address.</summary>
public sealed record OfflineArtwork(string? Poster, string? Backdrop, string? Still, string? Thumbnails);

/// <summary>What <c>POST /api/me/offline</c> answers: everything the device fetches and keeps.</summary>
public sealed record OfflinePackage(
    OfflineSave Save, Manifest Manifest, long Bytes, List<OfflineTrack> Tracks, List<OfflineSubtitle> Subtitles,
    Films.FilmFacts? Details, OfflineEpisode? Episode, OfflineArtwork Artwork);

/// <summary>One stretch watched on a device with no connection.</summary>
public sealed record OfflineProgress(string? TitleId, double PositionSeconds, DateTimeOffset WatchedAt);
