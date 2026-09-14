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

    /// <summary>Starts the room playing from wherever it is.</summary>
    public Task<RoomChanged> PlayAsync(string sessionId, string userId, string displayName, CancellationToken ct) =>
        ControlAsync(sessionId, "play",
            new RoomPlaybackRequest(AtSeconds: null, userId, displayName),
            ManifestJsonContext.Default.RoomPlaybackRequest, ct);

    /// <summary>Stops the room where it is.</summary>
    public Task<RoomChanged> PauseAsync(string sessionId, string userId, string displayName, CancellationToken ct) =>
        ControlAsync(sessionId, "pause",
            new RoomPlaybackRequest(AtSeconds: null, userId, displayName),
            ManifestJsonContext.Default.RoomPlaybackRequest, ct);

    /// <summary>
    /// Moves the room by <paramref name="deltaSeconds"/> from where it is. Relative rather than a
    /// position computed here, because the film moves during the round trip.
    /// </summary>
    public Task<RoomChanged> NudgeAsync(
        string sessionId, double deltaSeconds, string userId, string displayName, CancellationToken ct) =>
        ControlAsync(sessionId, "seek-relative",
            new RoomNudgeRequest(deltaSeconds, userId, displayName),
            ManifestJsonContext.Default.RoomNudgeRequest, ct);

    /// <summary>
    /// Sends one room control. No position is ever sent for a play or a pause: the bot is not watching
    /// and does not know where the film is, and the API resolves "wherever the room is" under the same
    /// lock as the change.
    /// </summary>
    private async Task<RoomChanged> ControlAsync<TRequest>(
        string sessionId, string control, TRequest request,
        System.Text.Json.Serialization.Metadata.JsonTypeInfo<TRequest> requestType, CancellationToken ct)
    {
        using var response = await http.PostAsJsonAsync(
            $"api/sessions/{Uri.EscapeDataString(sessionId)}/{control}", request, requestType, ct);

        if (!response.IsSuccessStatusCode)
            throw new MovieBotApiException($"The API refused {control} in {sessionId}: {(int)response.StatusCode}.");

        return await response.Content.ReadFromJsonAsync(ManifestJsonContext.Default.RoomChanged, ct)
            ?? throw new MovieBotApiException("The API returned no room change.");
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
