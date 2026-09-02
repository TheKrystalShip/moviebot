using Microsoft.Extensions.Logging;
using TheKrystalShip.MovieBot.Acquire.Download;
using TheKrystalShip.MovieBot.Acquire.Tracker;
using TheKrystalShip.MovieBot.Acquire.Search;

namespace TheKrystalShip.MovieBot.Bot.Find;

/// <summary>What the command was asked, reduced to the facts it acts on.</summary>
public sealed record FindRequest
{
    /// <summary>
    /// What came back from the picked suggestion, which is a torrent id. It arrives as text
    /// because that is what the surface sends, and a person who typed instead of picking sends
    /// something that is not one at all.
    /// </summary>
    public required string Chosen { get; init; }

    /// <summary>Where to announce the film when it arrives.</summary>
    public required ulong ChannelId { get; init; }

    public required string RequestedBy { get; init; }

    /// <summary>
    /// Who asked, so they can be told when the film arrives rather than having to watch a
    /// channel for it.
    /// </summary>
    public required ulong RequesterId { get; init; }
}

public enum FindStatus
{
    Started,
    NothingPicked,
    NoLongerOffered,
    Refused,
    TrackerUnavailable,
}

/// <summary>
/// The command's answer. <see cref="Release"/> is set only when a download started.
/// </summary>
public sealed record FindResult(
    FindStatus Status, string Message, Release? Release = null, string? Hash = null);

/// <summary>
/// Turns a picked suggestion into a running download.
///
/// Deliberately free of Discord's types: what makes this command wrong is starting the wrong
/// film, filling the disk, or telling somebody a download began when it did not, and none of
/// those need a gateway to reproduce.
/// </summary>
public sealed class FindCommand(
    AutocompleteSearch suggestions,
    AcquisitionService acquisition,
    ILogger<FindCommand> logger)
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

    public async Task<FindResult> ExecuteAsync(FindRequest request, CancellationToken ct)
    {
        // Somebody can always type over the suggestion instead of picking one, and what they
        // typed is a film's name rather than anything this can act on.
        if (!long.TryParse(request.Chosen, out var torrentId))
            return new FindResult(FindStatus.NothingPicked,
                "Pick one of the suggestions rather than typing over them. Start typing the "
                + "film's name and a list will appear.");

        var release = suggestions.Resolve(torrentId);
        if (release is null)
            return new FindResult(FindStatus.NoLongerOffered,
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

            // The tracker states the film outright. Recording it here is the only chance to keep
            // a fact rather than something re-guessed from a release name later on.
            if (release.ImdbId is { Length: > 0 } imdbId) tags.Add(TorrentTags.Imdb(imdbId));

            var result = await acquisition.StartAsync(release, tags, ct);

            if (!result.Started)
                return new FindResult(FindStatus.Refused, result.Refusal!, release);

            logger.LogInformation(
                "{User} started {Release} as {Hash}.",
                request.RequestedBy, release.ReleaseName, result.Hash);

            return new FindResult(FindStatus.Started,
                "Downloading. It will be announced here when it is ready to watch.",
                release, result.Hash);
        }
        catch (Exception ex) when (ex is TrackerException or QBittorrentException)
        {
            logger.LogWarning(ex, "Could not start {Release}.", release.ReleaseName);
            return new FindResult(FindStatus.TrackerUnavailable,
                "Could not reach the tracker or the torrent client. Try again in a minute.",
                release);
        }
    }
}
