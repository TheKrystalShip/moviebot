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
    /// The tag a download carries so it can be announced in the right place later. It lives on
    /// the torrent rather than in this process, which is what lets a bot restarted mid-download
    /// still announce it.
    /// </summary>
    public const string NotifyTagPrefix = "notify:";

    public static string NotifyTag(ulong channelId) => $"{NotifyTagPrefix}{channelId}";

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
            var result = await acquisition.StartAsync(
                release, [NotifyTag(request.ChannelId)], ct);

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
