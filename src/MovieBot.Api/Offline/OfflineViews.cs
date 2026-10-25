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

/// <summary>
/// What <c>GET /api/titles/{id}/offline</c> answers: everything the title can be saved as. A title still
/// being made is <c>Growing</c>, and each size is what is on disk of it so far.
/// </summary>
public sealed record OfflineOffer(
    string TitleId, List<OfflineRung> Rungs, List<OfflineAudio> Audio, List<OfflineSubtitle> Subtitles, bool Growing);

/// <summary>What a device asks to save.</summary>
public sealed record OfflineSaveRequest(
    string? TitleId, string? Rung, List<string>? Audio, List<string>? Subtitles, string? Device, string? SubtitleLanguage = null);

/// <summary>A save as the person reads it back.</summary>
public sealed record OfflineSave(
    string Id, string TitleId, string Rung, List<string> Audio, List<string> Subtitles, string Device,
    DateTimeOffset CreatedAt, DateTimeOffset? CompletedAt, DateTimeOffset? HoldsUntil, string? SubtitleLanguage = null);

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

/// <summary>
/// What <c>POST /api/me/offline</c> answers: everything the device fetches and keeps. A title still being
/// made is <c>Growing</c>: its tracks are what is on disk so far, and the device asks again for more.
/// <c>Card</c> is the title's catalogue card (an episode its show's), which the device's own library
/// keeps. <c>Grant</c> opens the save's tracks to the device's own downloader.
/// </summary>
public sealed record OfflinePackage(
    OfflineSave Save, Manifest Manifest, long Bytes, List<OfflineTrack> Tracks, List<OfflineSubtitle> Subtitles,
    TheKrystalShip.MovieBot.Catalogue.TitleCard? Card, OfflineEpisode? Episode, OfflineArtwork Artwork, bool Growing,
    OfflineGrant? Grant = null);

/// <summary>
/// The save's grant (<see cref="Auth.SaveGrant"/>): what a device's own downloader carries in
/// <c>Header</c> to fetch the save's tracks and say it is complete with no page running, until
/// <c>ExpiresAt</c>.
/// </summary>
public sealed record OfflineGrant(string Header, string Value, DateTimeOffset ExpiresAt);

/// <summary>
/// What a device asks to download before the library holds it: a film by its IMDb id, or a show's
/// episodes by the show's. <c>Height</c> is the picture height to download at, absent for the device's own;
/// <c>AudioLanguage</c> the audio language, absent for the title's own; <c>SubtitleLanguage</c> the
/// subtitle language, absent for none.
/// </summary>
public sealed record OfflineWantRequest(
    string? ImdbId, List<EpisodePick>? Episodes, int? Height, string? Device, string? AudioLanguage = null, string? SubtitleLanguage = null);

/// <summary>
/// A want as the person reads it back, with where it stands (<see cref="WantStages"/>): <c>Progress</c>
/// is how much of it the server has, from 0 to 1, while it is downloading there; <c>TitleId</c> is the
/// library's title once the device can download it. <c>Name</c> is the film's, or the episode's show's,
/// as the film database's description kept here has it, absent when none is kept. <c>AudioLanguage</c>
/// and <c>SubtitleLanguage</c> are as asked.
/// </summary>
public sealed record OfflineWant(
    string Id, string ImdbId, string? Name, EpisodePick? Episode, string Device, int? Height,
    string? AudioLanguage, string? SubtitleLanguage, DateTimeOffset CreatedAt,
    string Stage, double? Progress, string? Problem, string? Refusal, string? TitleId);

/// <summary>
/// What downloading a title the library does not hold would come to, before it is asked for: the
/// language it was made in, and roughly how many bytes it would take at each height, from the
/// runtime and what the library's titles take at that height. A size is absent when the runtime or
/// the measure is unknown.
/// </summary>
public sealed record OfflineEstimate(string? OriginalLanguage, List<OfflineEstimateSize> Sizes);

/// <summary>Roughly how many bytes a download at a picture height takes.</summary>
public sealed record OfflineEstimateSize(int Height, long? Bytes);

/// <summary>Where a want stands, and why one that failed did.</summary>
public static class WantStages
{
    /// <summary>A release is being picked for it.</summary>
    public const string Finding = "finding";

    /// <summary>Its download is arriving on the server.</summary>
    public const string Downloading = "downloading";

    /// <summary>It has arrived and is being made into something a device can download.</summary>
    public const string Preparing = "preparing";

    /// <summary>The library holds it; the device downloads <c>TitleId</c>.</summary>
    public const string Ready = "ready";

    public const string Failed = "failed";

    /// <summary>The tracker offers nothing it could be fetched from.</summary>
    public const string NotOffered = "notOffered";

    /// <summary>The disk budget or the tracker turned the download down; <c>Refusal</c> says why.</summary>
    public const string Refused = "refused";

    /// <summary>The tracker or the torrent client could not be reached, or the download has gone.</summary>
    public const string Unavailable = "unavailable";

    /// <summary>It arrived and could not be made watchable.</summary>
    public const string NotWatchable = "notWatchable";
}

/// <summary>One stretch watched on a device with no connection.</summary>
public sealed record OfflineProgress(string? TitleId, double PositionSeconds, DateTimeOffset WatchedAt);
