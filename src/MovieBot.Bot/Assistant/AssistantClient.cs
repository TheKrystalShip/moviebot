using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using TheKrystalShip.MovieBot.Client.Assistant;

namespace TheKrystalShip.MovieBot.Bot.Assistant;

/// <summary>The assistant did not answer, or answered with something that is not an answer.</summary>
public sealed class AssistantUnavailableException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>Who the bot speaks for on a call: the person who spoke, or who pressed.</summary>
public sealed record Speaker(ulong UserId, string DisplayName);

/// <summary>
/// The bot's way to moviebot-assistant. It proves itself with the service key and names, on every
/// call, the person it speaks for, which is all it ever names: what that person may do follows from
/// the turn, so the bot cannot ask for more than they could.
/// </summary>
public sealed class AssistantClient(HttpClient http)
{
    public const string ActingHeader = "X-MovieBot-Acting";

    /// <summary>
    /// A turn in a room, answered whole. The bot posts the answer as one message, so it has no use for
    /// the steps as they happen, and a buffered turn is the one a dropped connection cannot leave half
    /// read.
    /// </summary>
    public async Task<TurnReply> TurnAsync(TurnRoom room, Speaker speaker, string said, CancellationToken ct)
    {
        using var request = Request(HttpMethod.Post, "turn", speaker);
        request.Content = JsonContent.Create(new TurnRequest { Prompt = said, Room = room }, AssistantJsonContext.Default.TurnRequest);
        return await SendAsync(request, AssistantJsonContext.Default.TurnReply, "The turn", ct)
               ?? throw new AssistantUnavailableException("The assistant answered the turn with nothing.");
    }

    /// <summary>Carries a proposal out as whoever agreed. Null when it was already answered, expired, or never was.</summary>
    public async Task<ConfirmReply?> ConfirmAsync(string token, Speaker agreed, CancellationToken ct)
    {
        using var request = Request(HttpMethod.Post, "confirm", agreed);
        request.Content = JsonContent.Create(new ConfirmRequest(token), AssistantJsonContext.Default.ConfirmRequest);
        return await SendAsync(request, AssistantJsonContext.Default.ConfirmReply, "The confirmation", ct, missing: true);
    }

    /// <summary>Writes a room verb the bot carried out into the room's conversation.</summary>
    public async Task RecordAsync(RecordRequest record, Speaker speaker, CancellationToken ct)
    {
        using var request = Request(HttpMethod.Post, "rooms/record", speaker);
        request.Content = JsonContent.Create(record, AssistantJsonContext.Default.RecordRequest);
        await SendAsync<RecordRequest>(request, null, "The record", ct);
    }

    private static HttpRequestMessage Request(HttpMethod method, string path, Speaker speaker)
    {
        var request = new HttpRequestMessage(method, path);
        // A display name may hold any character and a header only ASCII, so the name is
        // percent-encoded. The separator is the first bar.
        request.Headers.Add(ActingHeader, $"{speaker.UserId}|{Uri.EscapeDataString(speaker.DisplayName)}");
        return request;
    }

    private async Task<T?> SendAsync<T>(
        HttpRequestMessage request, System.Text.Json.Serialization.Metadata.JsonTypeInfo<T>? type, string what,
        CancellationToken ct, bool missing = false) where T : class
    {
        try
        {
            using var response = await http.SendAsync(request, ct);
            if (missing && response.StatusCode == HttpStatusCode.NotFound) return null;
            if (!response.IsSuccessStatusCode)
                throw new AssistantUnavailableException($"{what} was refused: {(int)response.StatusCode}.");
            return type is null || response.StatusCode == HttpStatusCode.NoContent
                ? null
                : await response.Content.ReadFromJsonAsync(type, ct);
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or TaskCanceledException
                                   && !ct.IsCancellationRequested)
        {
            throw new AssistantUnavailableException($"{what} could not reach the assistant.", ex);
        }
    }
}
