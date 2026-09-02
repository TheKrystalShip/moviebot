using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.Logging;
using TheKrystalShip.MovieBot.Bot.Api;

namespace TheKrystalShip.MovieBot.Bot.Presence;

/// <summary>
/// Writes the line under a voice channel's name.
///
/// Discord's own endpoint, reached directly: the client library carries the permission bit for
/// it and nothing that calls it. A refusal is reported and not retried here; the next sweep
/// computes the line again and sends it if it still differs.
/// </summary>
public sealed class VoiceChannelStatus(HttpClient http, ILogger<VoiceChannelStatus> logger)
{
    /// <summary>Sets the line, or clears it with null. Returns whether Discord accepted it.</summary>
    public async Task<bool> SetAsync(ulong channelId, string? status, CancellationToken ct)
    {
        using var response = await http.PutAsJsonAsync(
            $"channels/{channelId}/voice-status",
            new VoiceStatusRequest(status),
            BotJsonContext.Default.VoiceStatusRequest, ct);

        if (response.IsSuccessStatusCode) return true;

        var body = await response.Content.ReadAsStringAsync(ct);

        // Discord asks that its limits be read from the response rather than assumed. The sweep
        // that follows the wait sends the line again if it is still the one wanted.
        if (response.StatusCode == HttpStatusCode.TooManyRequests)
            logger.LogWarning("Discord rate-limited the status of channel {ChannelId}; retry after {RetryAfter}s.",
                channelId, response.Headers.RetryAfter?.Delta?.TotalSeconds);
        else
            logger.LogWarning("Discord refused the status of channel {ChannelId}: {Status} {Body}",
                channelId, (int)response.StatusCode, body);

        return false;
    }
}

/// <summary>The body of a voice status write. Null clears the line, so null has to be written.</summary>
public sealed record VoiceStatusRequest(string? Status);
