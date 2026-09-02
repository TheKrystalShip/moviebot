using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using TheKrystalShip.MovieBot.Core;

namespace TheKrystalShip.MovieBot.Bot.Api;

/// <summary>
/// The bot's only route to anything that persists.
///
/// The bot holds no state: no session cache, no library copy, no record of who asked for what.
/// Every question it answers is asked of the API at the moment it is asked of the bot, so a
/// restart loses nothing and two bot processes would agree.
/// </summary>
public sealed class MovieBotApiClient(HttpClient http)
{
    /// <summary>The library, as the API currently sees it.</summary>
    public async Task<IReadOnlyList<LibraryTitle>> ListTitlesAsync(CancellationToken ct)
    {
        try
        {
            var titles = await http.GetFromJsonAsync(
                "api/titles", BotJsonContext.Default.IReadOnlyListLibraryTitle, ct);
            return titles ?? [];
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or TaskCanceledException
                                   && !ct.IsCancellationRequested)
        {
            throw new MovieBotApiException("The library could not be read.", ex);
        }
    }

    /// <summary>
    /// Opens the room's session and returns its current state. The API creates the session on
    /// first read, so this is what makes the room exist before anyone clicks the link.
    /// </summary>
    public async Task<SessionState> OpenSessionAsync(string sessionId, CancellationToken ct)
    {
        try
        {
            var push = await http.GetFromJsonAsync(
                $"api/sessions/{Uri.EscapeDataString(sessionId)}",
                ManifestJsonContext.Default.SessionStatePush, ct);

            return push?.State
                   ?? throw new MovieBotApiException("The session endpoint returned nothing.");
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or TaskCanceledException
                                   && !ct.IsCancellationRequested)
        {
            throw new MovieBotApiException("The session could not be opened.", ex);
        }
    }

    /// <summary>Every room, with what it is watching and how many are in it.</summary>
    public async Task<IReadOnlyList<RoomSummary>> ListRoomsAsync(CancellationToken ct)
    {
        try
        {
            var rooms = await http.GetFromJsonAsync(
                "api/sessions", ManifestJsonContext.Default.IReadOnlyListRoomSummary, ct);
            return rooms ?? [];
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or TaskCanceledException
                                   && !ct.IsCancellationRequested)
        {
            throw new MovieBotApiException("The rooms could not be read.", ex);
        }
    }

    /// <summary>
    /// Puts the film into the room before anyone opens it.
    ///
    /// An Activity is launched by Discord from a URL the bot never writes, so there is no query
    /// string to carry the choice in. The session carries it instead, and whoever joins finds the
    /// film already loaded.
    /// </summary>
    public async Task<SessionState> SetSessionTitleAsync(
        string sessionId, string titleId, string requestedBy, CancellationToken ct)
    {
        using var response = await http.PostAsJsonAsync(
            $"api/sessions/{Uri.EscapeDataString(sessionId)}/title",
            new SetTitleRequest(titleId, requestedBy, requestedBy),
            ManifestJsonContext.Default.SetTitleRequest, ct);

        if (!response.IsSuccessStatusCode)
            throw new MovieBotApiException($"The API refused to load {titleId}: {(int)response.StatusCode}.");

        var push = await response.Content.ReadFromJsonAsync(
                       ManifestJsonContext.Default.SessionStatePush, ct)
            ?? throw new MovieBotApiException("The API returned no session state.");

        return push.State;
    }

    /// <summary>
    /// Whether the API is answering. Used at startup so an unreachable backend is reported once,
    /// on the log, rather than for the first time in front of a room.
    /// </summary>
    public async Task<bool> IsHealthyAsync(CancellationToken ct)
    {
        try
        {
            using var response = await http.GetAsync("health", ct);
            return response.StatusCode == HttpStatusCode.OK;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException
                                   && !ct.IsCancellationRequested)
        {
            return false;
        }
    }
}
