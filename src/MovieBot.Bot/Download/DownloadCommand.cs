using Microsoft.Extensions.Logging;
using TheKrystalShip.MovieBot.Acquire.Download;
using TheKrystalShip.MovieBot.Acquire.Tracker;
using TheKrystalShip.MovieBot.Acquire.Search;

namespace TheKrystalShip.MovieBot.Bot.Download;

/// <summary>What the command was asked, reduced to the facts it acts on.</summary>
public sealed record DownloadRequest
{
    /// <summary>The torrent id behind the row that was picked.</summary>
    public required long TorrentId { get; init; }

    /// <summary>Where to announce the film when it can be watched.</summary>
    public required ulong ChannelId { get; init; }

    public required string RequestedBy { get; init; }

    /// <summary>
    /// Who asked, so they can be told when the film arrives rather than having to watch a
    /// channel for it.
    /// </summary>
    public required ulong RequesterId { get; init; }

    /// <summary>
    /// The voice channel the requester is standing in, or null when they are in none. A person
    /// in one has asked to watch the film there, so it is loaded into that room the moment it
    /// can be watched; a person in none has asked for the film to be here.
    /// </summary>
    public ulong? RoomId { get; init; }
}

public enum DownloadOutcome
{
    Started,
    NoLongerOffered,
    Refused,
    TrackerUnavailable,
}

/// <summary>
/// The command's answer. <see cref="Release"/> is set only when a download started.
/// </summary>
public sealed record DownloadResult(
    DownloadOutcome Outcome, string Message, Release? Release = null, string? Hash = null);

/// <summary>
/// Turns a picked tracker row into a running download.
///
/// Deliberately free of Discord's types: what makes this command wrong is starting the wrong
/// film, filling the disk, or telling somebody a download began when it did not, and none of
/// those need a gateway to reproduce.
/// </summary>
public sealed class DownloadCommand(
    AutocompleteSearch suggestions,
    AcquisitionService acquisition,
    ILogger<DownloadCommand> logger)
{
    /// <summary>
    /// Records which message shows this download's progress, so whatever keeps it current can
    /// find it again. Kept on the torrent rather than here: a download outlives a restart and the
    /// message should not be left saying whatever it last said.
    /// </summary>
    public async Task RecordProgressMessageAsync(
        string hash, ulong channelId, ulong messageId, CancellationToken ct)
    {
        try
        {
            await acquisition.TagAsync(hash, TorrentTags.Progress(channelId, messageId), ct);
        }
        catch (QBittorrentException ex)
        {
            // The download is running either way; it just will not report itself.
            logger.LogWarning(ex, "Could not record the progress message for {Hash}.", hash);
        }
    }

    public async Task<DownloadResult> ExecuteAsync(DownloadRequest request, CancellationToken ct)
    {
        var release = suggestions.Resolve(request.TorrentId);
        if (release is null)
            return new DownloadResult(DownloadOutcome.NoLongerOffered,
                "That result has gone stale. Run the command again and pick from the fresh list.");

        try
        {
            // Notes left on the torrent rather than held in this process: where to announce it,
            // that it is not watchable until something has transcoded it, and which film it is.
            // A download wearing none of them is one nobody is waiting on.
            var tags = new List<string>
            {
                TorrentTags.Notify(request.ChannelId),
                TorrentTags.Requester(request.RequesterId),
                TorrentTags.NeedsIngest,
            };

            // The room is the reason for the download. Whichever pass sees the film become
            // watchable, in whichever process is running by then, loads it there.
            if (request.RoomId is { } roomId) tags.Add(TorrentTags.Room(roomId));

            // The tracker states the film outright. Recording it here is the only chance to keep
            // a fact rather than something re-guessed from a release name later on.
            if (release.ImdbId is { Length: > 0 } imdbId) tags.Add(TorrentTags.Imdb(imdbId));

            var result = await acquisition.StartAsync(release, tags, ct);

            if (!result.Started)
                return new DownloadResult(DownloadOutcome.Refused, result.Refusal!, release);

            logger.LogInformation(
                "{User} started {Release} as {Hash}{Room}.",
                request.RequestedBy, release.ReleaseName, result.Hash,
                request.RoomId is { } room ? $" for room {room}" : "");

            return new DownloadResult(DownloadOutcome.Started,
                request.RoomId is null
                    ? "Downloading. It will be announced here when it can be watched."
                    : "Downloading. It starts in your voice channel as soon as enough of it has "
                      + "arrived, and you will be pinged here.",
                release, result.Hash);
        }
        catch (Exception ex) when (ex is TrackerException or QBittorrentException)
        {
            logger.LogWarning(ex, "Could not start {Release}.", release.ReleaseName);
            return new DownloadResult(DownloadOutcome.TrackerUnavailable,
                "Could not reach the tracker or the torrent client. Try again in a minute.",
                release);
        }
    }
}
