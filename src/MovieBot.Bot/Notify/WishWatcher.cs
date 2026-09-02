using Discord;
using Discord.WebSocket;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TheKrystalShip.MovieBot.Acquire.Tracker;
using TheKrystalShip.MovieBot.Acquire.Search;

namespace TheKrystalShip.MovieBot.Bot.Notify;

/// <summary>
/// Asks the tracker, on a slow clock, about every film somebody is waiting on, and tells them
/// when it can be downloaded.
///
/// The announcement is a new message rather than an edit, because an edit notifies nobody, and
/// it names exactly the people who asked in that channel. A wish is forgotten once its people
/// have been told; a message that fails to send leaves them on the list, so the next pass tries
/// again rather than losing them.
/// </summary>
public sealed class WishWatcher(
    DiscordSocketClient client,
    IReleaseSearch search,
    WishList wishes,
    IOptions<NotifyOptions> options,
    ILogger<WishWatcher> logger) : BackgroundService
{
    /// <summary>
    /// How long after starting the first pass runs. Long enough for the gateway to connect, short
    /// enough that a bot restarted after a film appeared says so without waiting out the interval.
    /// </summary>
    private static readonly TimeSpan FirstPass = TimeSpan.FromMinutes(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("Wishes are kept at {Path}.", wishes.Path);

        var delay = FirstPass;

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(delay, stoppingToken);
                delay = TimeSpan.FromMinutes(Math.Max(1, options.Value.SweepMinutes));

                if (client.ConnectionState != ConnectionState.Connected) continue;
                await SweepAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                // A failing sweep must not end the watcher: the films are still on the list and
                // the next pass will ask about them.
                logger.LogWarning(ex, "A wish sweep failed.");
            }
        }
    }

    private async Task SweepAsync(CancellationToken ct)
    {
        var pending = wishes.All();
        if (pending.Count == 0) return;

        foreach (var wish in pending)
        {
            RankedReleases ranked;
            try
            {
                ranked = await search.ByImdbAsync(wish.ImdbId, ct);
            }
            catch (TrackerException ex)
            {
                // One tracker failure is every tracker failure; the rest of the pass would only
                // repeat it against the same cap.
                logger.LogWarning(ex, "The tracker could not be asked about {Film}; the pass stops here.",
                    wish.Display);
                return;
            }

            if (ReleaseAvailability.Pick(ranked, options.Value.MinimumSource) is not { } release)
                continue;

            logger.LogInformation("{Film} is on the tracker as {Release}.", wish.Display, release.ReleaseName);

            var told = new HashSet<ulong>();
            foreach (var channel in wish.Subscribers.GroupBy(s => s.ChannelId))
            {
                if (await TryAnnounceAsync(channel.Key, channel.Select(s => s.UserId).ToList(), wish, release))
                    told.Add(channel.Key);
            }

            wishes.Forget(wish.ImdbId, told);
        }
    }

    private async Task<bool> TryAnnounceAsync(
        ulong channelId, IReadOnlyList<ulong> userIds, Wish wish, Release release)
    {
        try
        {
            if (client.GetChannel(channelId) is not IMessageChannel channel)
            {
                // The channel is gone or the bot can no longer see it. Forgetting is the only way
                // to stop retrying something that will never work.
                logger.LogWarning("Cannot announce {Film}: channel {ChannelId} is not reachable.",
                    wish.Display, channelId);
                return true;
            }

            // The mentions have to be in the message itself: text inside an embed renders as a
            // mention and notifies nobody. Exactly these accounts, named by id, rather than
            // allowing mentions generally.
            await channel.SendMessageAsync(
                text: string.Join(" ", userIds.Select(MentionUtils.MentionUser)),
                embed: WishEmbed.Available(wish, release),
                allowedMentions: new AllowedMentions { UserIds = [.. userIds] });

            logger.LogInformation("Announced {Film} in {ChannelId} to {Count} person(s).",
                wish.Display, channelId, userIds.Count);
            return true;
        }
        catch (Exception ex)
        {
            // Left on the list means untried: the next pass will attempt it again.
            logger.LogWarning(ex, "Could not announce {Film} in {ChannelId}.", wish.Display, channelId);
            return false;
        }
    }
}
