using Microsoft.Extensions.Logging;
using TheKrystalShip.MovieBot.Acquire.Download;
using TheKrystalShip.MovieBot.Bot.Api;
using TheKrystalShip.MovieBot.Core;

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
    DownloadOutcome Outcome, string Message, ReleaseOffer? Release = null, string? Hash = null);

/// <summary>
/// Asks the API to turn a picked release into a running download, and says how that went in the
/// words a Discord channel is told.
///
/// The API starts every download, so a release picked from /watch, from /download or by the
/// assistant is the same act with the same notes on the torrent. What stays here is the sentence,
/// and the one note that is the bot's own: which message shows the download's progress.
/// </summary>
public sealed class DownloadCommand(
    MovieBotApiClient api,
    AcquisitionService acquisition,
    ILogger<DownloadCommand> logger)
{
    /// <summary>
    /// What a picked row whose search has been forgotten is answered with.
    ///
    /// One spelling, because both commands that start a download are picked from a menu that can
    /// go stale, and one of them resolves the row itself to see whether the film is already here.
    /// </summary>
    public const string Stale =
        "That result has gone stale. Run the command again and pick from the fresh list.";

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
        StartDownloadReply reply;
        try
        {
            reply = await api.StartDownloadAsync(new StartDownloadRequest
            {
                TorrentId = request.TorrentId,
                RequestedBy = new Actor(request.RequesterId.ToString(), request.RequestedBy),
                NotifyChannelId = request.ChannelId == 0 ? null : request.ChannelId.ToString(),
                RoomId = request.RoomId?.ToString(),
            }, ct);
        }
        catch (MovieBotApiException ex)
        {
            logger.LogWarning(ex, "The API could not be asked to start {TorrentId}.", request.TorrentId);
            return new DownloadResult(DownloadOutcome.TrackerUnavailable,
                "The MovieBot API is not answering, so nothing started. Try again in a minute.");
        }

        return reply.Outcome switch
        {
            "started" => new DownloadResult(DownloadOutcome.Started,
                request.RoomId is null
                    ? "Downloading. It will be announced here when it can be watched."
                    : "Downloading. It starts in your voice channel as soon as enough of it has "
                      + "arrived, and you will be pinged here.",
                reply.Release, reply.Hash),
            "noLongerOffered" => new DownloadResult(DownloadOutcome.NoLongerOffered, Stale),
            "refused" => new DownloadResult(DownloadOutcome.Refused, reply.Refusal ?? "The download was refused.", reply.Release),
            _ => new DownloadResult(DownloadOutcome.TrackerUnavailable,
                "Could not reach the tracker or the torrent client. Try again in a minute.", reply.Release),
        };
    }
}
