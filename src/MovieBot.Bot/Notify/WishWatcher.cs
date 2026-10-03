using Discord;
using Discord.WebSocket;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TheKrystalShip.MovieBot.Bot.Api;
using TheKrystalShip.MovieBot.Core;

namespace TheKrystalShip.MovieBot.Bot.Notify;

/// <summary>
/// Tells the people waiting on a film that it can be downloaded, once the API's sweep has found it.
///
/// The API asks the tracker about every wish on its own slow clock and holds the ones it can answer;
/// this reads them every minute and announces each in the channels its people asked in. The
/// announcement is a new message rather than an edit, because an edit notifies nobody, and it names
/// exactly the people who asked in that channel. The channels reached are reported back, which is
/// what forgets those people; a message that fails to send leaves them on the list, so the next read
/// tries again rather than losing them.
/// </summary>
public sealed class WishWatcher(
    DiscordSocketClient client,
    MovieBotApiClient api,
    ILogger<WishWatcher> logger) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(Interval, stoppingToken);
                if (client.ConnectionState != ConnectionState.Connected) continue;
                await AnnounceAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                // A failing pass must not end the watcher: the wishes are still held by the API and
                // the next pass reads them again.
                logger.LogWarning(ex, "Announcing the wishes that can be downloaded failed.");
            }
        }
    }

    private async Task AnnounceAsync(CancellationToken ct)
    {
        foreach (var available in await api.AvailableWishesAsync(ct))
        {
            var wish = Wish.From(available.Wish);
            var told = new List<string>();

            foreach (var channel in wish.Subscribers.Where(s => s.ChannelId is not null).GroupBy(s => s.ChannelId!.Value))
            {
                if (await TryAnnounceAsync(channel.Key, channel.Select(s => s.UserId).ToList(), wish, available.Release))
                    told.Add(channel.Key.ToString());
            }

            if (told.Count > 0)
                await api.WishesToldAsync(wish.ImdbId, told, ct);
        }
    }

    private async Task<bool> TryAnnounceAsync(
        ulong channelId, IReadOnlyList<ulong> userIds, Wish wish, ReleaseOffer release)
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
