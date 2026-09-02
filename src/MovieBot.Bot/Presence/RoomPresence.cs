using Discord;
using Discord.WebSocket;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TheKrystalShip.MovieBot.Bot.Api;
using TheKrystalShip.MovieBot.Bot.Configuration;
using TheKrystalShip.MovieBot.Core;

namespace TheKrystalShip.MovieBot.Bot.Presence;

/// <summary>
/// Says what the rooms are watching, in the two places Discord lets a bot say it: beside the
/// bot's own name, and under the voice channel each room lives in.
///
/// It holds nothing worth keeping. The rooms are read from the API on every pass and the words
/// are derived from them, so a restarted bot says the right thing on its first sweep. What it
/// does remember is what it last sent, because an update that changes nothing still spends a
/// request, and the channels it has written under, so it can clear them when the film ends.
/// </summary>
public sealed class RoomPresence(
    DiscordSocketClient client,
    MovieBotApiClient api,
    VoiceChannelStatus voiceStatus,
    IOptions<DiscordOptions> options,
    ILogger<RoomPresence> logger) : BackgroundService
{
    /// <summary>
    /// How often the rooms are read. A pause reaches the sidebar within this; the loopback read
    /// costs nothing, and the lines are written to the minute so the pace of the writes is set
    /// by the clock rather than by this.
    /// </summary>
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(10);

    private readonly HashSet<ulong> _guilds = [.. options.Value.GuildIds];

    private string? _sentActivity;
    private readonly Dictionary<ulong, string> _sentLines = [];
    private readonly HashSet<ulong> _refused = [];
    private bool _swept;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval);

        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                try
                {
                    if (client.ConnectionState != ConnectionState.Connected) continue;
                    await SweepAsync(stoppingToken);
                }
                catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
                {
                    logger.LogWarning(ex, "A presence sweep failed.");
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Stopping.
        }

        // A planned stop takes its lines with it. The gateway going away clears the bot's own
        // status by itself; nothing clears a channel's line but a write.
        await ClearAllAsync();
    }

    private async Task SweepAsync(CancellationToken ct)
    {
        var rooms = await api.ListRoomsAsync(ct);

        await SetActivityAsync(RoomStatusText.BotActivity(rooms));

        if (!_swept)
        {
            await ClearLeftoversAsync(rooms, ct);
            _swept = true;
        }

        var wanted = new Dictionary<ulong, string>();
        foreach (var room in rooms)
        {
            if (RoomStatusText.VoiceChannelLine(room) is not { } line) continue;
            if (VoiceChannelFor(room.SessionId) is not { } channel) continue;
            wanted[channel.Id] = line;
        }

        foreach (var (channelId, line) in wanted)
        {
            if (_sentLines.TryGetValue(channelId, out var sent) && sent == line) continue;
            if (await WriteAsync(channelId, line, ct)) _sentLines[channelId] = line;
        }

        foreach (var channelId in _sentLines.Keys.Where(id => !wanted.ContainsKey(id)).ToList())
        {
            if (await WriteAsync(channelId, null, ct)) _sentLines.Remove(channelId);
        }
    }

    private async Task SetActivityAsync(string? name)
    {
        if (name == _sentActivity) return;

        if (name is null) await client.SetActivityAsync(null);
        else await client.SetGameAsync(name, type: ActivityType.Watching);

        _sentActivity = name;
        logger.LogInformation("Status: {Status}", name is null ? "nothing" : $"Watching {name}");
    }

    /// <summary>
    /// Clears a line of ours that outlived its room. A bot that died mid-film wrote nothing on
    /// the way out, and the channel would otherwise keep saying the film is playing until the
    /// next one in that channel.
    /// </summary>
    private async Task ClearLeftoversAsync(IReadOnlyList<RoomSummary> rooms, CancellationToken ct)
    {
        var watched = rooms
            .Where(r => RoomStatusText.VoiceChannelLine(r) is not null)
            .Select(r => r.SessionId)
            .ToHashSet(StringComparer.Ordinal);

        foreach (var guildId in _guilds)
        {
            if (client.GetGuild(guildId) is not { } guild) continue;

            foreach (var channel in guild.VoiceChannels)
            {
                if (!RoomStatusText.IsOwnLine(channel.Status)) continue;
                if (watched.Contains(channel.Id.ToString())) continue;

                logger.LogInformation("Clearing a leftover line under {Channel}: {Line}", channel.Name, channel.Status);
                await WriteAsync(channel.Id, null, ct);
            }
        }
    }

    private async Task<bool> WriteAsync(ulong channelId, string? line, CancellationToken ct)
    {
        if (_refused.Contains(channelId)) return false;

        if (client.GetChannel(channelId) is SocketVoiceChannel channel)
        {
            var permissions = channel.Guild.CurrentUser.GetPermissions(channel);
            if (!permissions.SetVoiceChannelStatus || !permissions.ManageChannel)
            {
                // Said once per channel. Discord requires both while the bot is not connected to
                // the channel, and neither is in the invite of a bot installed before this existed.
                logger.LogWarning(
                    "Cannot write under {Channel}: the bot needs Set Voice Channel Status and Manage Channels there.",
                    channel.Name);
                _refused.Add(channelId);
                return false;
            }
        }

        var accepted = await voiceStatus.SetAsync(channelId, line, ct);
        if (accepted)
            logger.LogInformation("Channel {ChannelId}: {Line}", channelId, line ?? "cleared");
        return accepted;
    }

    private SocketVoiceChannel? VoiceChannelFor(string sessionId)
    {
        // The room is the voice channel, so its id is the channel's; a room opened from a browser
        // link is named otherwise and has no channel to write under.
        if (!ulong.TryParse(sessionId, out var channelId)) return null;
        if (client.GetChannel(channelId) is not SocketVoiceChannel channel) return null;
        return _guilds.Contains(channel.Guild.Id) ? channel : null;
    }

    private async Task ClearAllAsync()
    {
        foreach (var channelId in _sentLines.Keys.ToList())
        {
            try
            {
                if (await voiceStatus.SetAsync(channelId, null, CancellationToken.None))
                    _sentLines.Remove(channelId);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Could not clear the line under channel {ChannelId} on the way out.", channelId);
            }
        }
    }
}
