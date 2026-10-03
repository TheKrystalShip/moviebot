using System.Globalization;
using Discord;
using Discord.Net;
using Discord.WebSocket;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TheKrystalShip.MovieBot.Bot.Api;
using TheKrystalShip.MovieBot.Bot.Discord;
using TheKrystalShip.MovieBot.Core;

namespace TheKrystalShip.MovieBot.Bot.Notify;

/// <summary>
/// Sends a person, on Discord, the notifications no push reached.
///
/// The API decides who is owed a message and words it; this reads the queue, sends each as a direct
/// message with the page it opens as a link button, and reports how it went, which takes it off the
/// queue. A message Discord refuses outright, because the person shares no server with the bot or
/// takes no messages from it, is reported as refused rather than tried forever; one that failed for
/// any other reason is not reported and is tried on the next pass.
/// </summary>
public sealed class DirectMessages(
    DiscordSocketClient client,
    MovieBotApiClient api,
    ILogger<DirectMessages> logger) : BackgroundService
{
    /// <summary>An invite is somebody waiting in a room, so this is how late it may arrive.</summary>
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(15);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(Interval, stoppingToken);
                if (client.ConnectionState != ConnectionState.Connected) continue;
                await SendAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                // The messages are still queued by the API and the next pass reads them again.
                logger.LogWarning(ex, "Sending the messages owed failed.");
            }
        }
    }

    private async Task SendAsync(CancellationToken ct)
    {
        foreach (var message in await api.DirectMessagesAsync(ct))
        {
            if (!ulong.TryParse(message.UserId, NumberStyles.None, CultureInfo.InvariantCulture, out var userId))
            {
                await api.DirectMessageSentAsync(message.Id, sent: false, ct);
                continue;
            }

            if (await TrySendAsync(userId, message) is { } sent)
                await api.DirectMessageSentAsync(message.Id, sent, ct);
        }
    }

    /// <summary>True when sent, false when Discord will never take it, null when it is worth another try.</summary>
    private async Task<bool?> TrySendAsync(ulong userId, DirectMessage message)
    {
        try
        {
            var user = await client.Rest.GetUserAsync(userId);
            if (user is null)
            {
                logger.LogWarning("No Discord account {UserId} to message.", userId);
                return false;
            }

            var channel = await user.CreateDMChannelAsync();
            var components = message.Url is { } url && Uri.TryCreate(url, UriKind.Absolute, out _)
                ? new ComponentBuilder().WithButton("Open", style: ButtonStyle.Link, url: url).Build()
                : null;
            await channel.SendMessageAsync(text: message.Text, components: components,
                allowedMentions: AllowedMentions.None);

            logger.LogInformation("Messaged {UserId} on Discord.", userId);
            return true;
        }
        catch (HttpException ex) when (DiscordReach.IsOutOfReach(ex))
        {
            logger.LogWarning("Discord will not take a message to {UserId}: {Reason}", userId, DiscordReach.Explain(ex));
            return false;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not message {UserId}; the next pass tries again.", userId);
            return null;
        }
    }
}
