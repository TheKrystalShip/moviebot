using Discord;
using Discord.WebSocket;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TheKrystalShip.MovieBot.Bot.Api;
using TheKrystalShip.MovieBot.Bot.Discord;

namespace TheKrystalShip.MovieBot.Bot.Launch;

/// <summary>
/// Says on the card itself when a launch has stopped being a way in, and takes its invite away.
///
/// The rooms are read from the API on every pass, exactly as the presence sweep reads them, and a
/// card whose room is gone — or whose room has moved on to another film — is a card describing
/// something that is not there. It is edited where it stands and the invite behind it is revoked,
/// so a message that offers a way in is one, and a message that does not says so.
///
/// A pass that cannot reach the API does nothing at all. Rooms it could not read are not rooms
/// that closed, and marking every card expired on a moment's trouble would be worse than the
/// silence it replaces.
///
/// Editing notifies nobody, which is right: this is for whoever scrolls back and finds the card,
/// not for the room that has already moved on.
/// </summary>
public sealed class LaunchExpiry(
    DiscordSocketClient client,
    MovieBotApiClient api,
    LaunchCards cards,
    ILogger<LaunchExpiry> logger) : BackgroundService
{
    /// <summary>
    /// How often the cards are reconsidered. The API forgets an idle room on a sweep of its own,
    /// so this is the delay on top of that before the card catches up — and there is nothing
    /// urgent about it, since whoever it is for is not looking yet.
    /// </summary>
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(30);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (!await timer.WaitForNextTickAsync(stoppingToken)) return;
                if (client.ConnectionState != ConnectionState.Connected) continue;
                await SweepAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "A launch card sweep failed.");
            }
        }
    }

    private async Task SweepAsync(CancellationToken ct)
    {
        var standing = cards.All();
        if (standing.Count == 0) return;

        var rooms = await api.ListRoomsAsync(ct);

        foreach (var card in standing)
        {
            if (ExpiredCard.Reason(card, rooms) is not { } reason) continue;

            var outcome = await TryExpireAsync(card, reason, ct);
            if (outcome is EditOutcome.Retry) continue;

            // Edited, or beyond reach for good. Either way there is nothing left for a later
            // pass to do with it, and the invite goes with the card that handed it out.
            if (outcome is EditOutcome.Edited) await TryRevokeAsync(card, ct);
            cards.Forget(card);
        }
    }

    /// <summary>What one pass at one card settled.</summary>
    private enum EditOutcome
    {
        /// <summary>The card now says it is over.</summary>
        Edited,

        /// <summary>Said nothing this time. The next pass tries again.</summary>
        Retry,

        /// <summary>Beyond reach for good, and not worth another pass.</summary>
        Gone,
    }

    private async Task<EditOutcome> TryExpireAsync(LaunchCard card, string reason, CancellationToken ct)
    {
        try
        {
            // A gateway part-way through connecting has no channels yet, which is a moment's
            // trouble rather than a missing channel, so it is left for the next pass.
            if (client.GetChannel(card.ChannelId) is not IMessageChannel channel) return EditOutcome.Retry;

            // Fetched through the channel rather than kept from the interaction that posted it.
            // An interaction's token lasts fifteen minutes and a film does not, so a card is
            // always edited long after the only thing that could have addressed it has expired.
            if (await channel.GetMessageAsync(card.MessageId) is not IUserMessage message)
                return EditOutcome.Retry;

            if (message.Embeds.FirstOrDefault() is not { } posted)
            {
                // A card with no embed is not one this posted, and nothing here can describe it.
                logger.LogWarning("The launch card {MessageId} carries no embed; leaving it alone.", card.MessageId);
                return EditOutcome.Gone;
            }

            var expired = ExpiredCard.Rebuild(posted, reason);

            // The button goes rather than being greyed out. A disabled door is still a door, and
            // the point of the edit is that there is nothing here to press.
            await message.ModifyAsync(m =>
            {
                m.Embed = expired;
                m.Components = new ComponentBuilder().Build();
            });

            logger.LogInformation(
                "Launch card {MessageId} in {ChannelId} is over: {Reason}",
                card.MessageId, card.ChannelId, reason);

            return EditOutcome.Edited;
        }
        catch (global::Discord.Net.HttpException ex) when (DiscordReach.IsOutOfReach(ex))
        {
            // Deleted, or in a channel the bot is not allowed to read. Both are settled somewhere
            // other than here, and asking again every half minute only spends the channel's
            // allowance to be refused in the same words.
            logger.LogDebug(
                "Giving up on the launch card {MessageId}: {Reason}",
                card.MessageId, DiscordReach.Explain(ex));
            return EditOutcome.Gone;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not expire the launch card {MessageId}.", card.MessageId);
            return EditOutcome.Retry;
        }
    }

    /// <summary>
    /// Takes the invite away, so what the card said is also true of the link somebody copied out
    /// of it. Best effort: an invite Discord has already dropped, or one the bot may no longer
    /// manage, leaves a card that is honest either way.
    /// </summary>
    private async Task TryRevokeAsync(LaunchCard card, CancellationToken ct)
    {
        try
        {
            var options = new RequestOptions { CancelToken = ct };
            if (await client.GetInviteAsync(card.InviteCode, options) is { } invite)
                await invite.DeleteAsync(options);
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "The invite {Code} could not be revoked.", card.InviteCode);
        }
    }
}
