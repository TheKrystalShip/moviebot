using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using TheKrystalShip.MovieBot.Core;

namespace TheKrystalShip.MovieBot.Client;

/// <summary>
/// The route to anything that persists, for the bot and the assistant.
///
/// Neither holds state of the API's: no session cache, no library copy, no record of who asked
/// for what. Every question is asked of the API at the moment it is asked, so a restart loses
/// nothing and two processes would agree.
/// </summary>
/// <remarks>
/// <b>A person's act is made with the person's token.</b> <see cref="As"/> is the client for one
/// person: every request it makes carries their access token, so the API decides what they may do and
/// records them as who did it. The client built by the container carries whatever its
/// <see cref="HttpClient"/> was given, which for the bot is the service key: machine work that names
/// nobody, such as reading the rooms and the downloads it shows in Discord.
/// </remarks>
public sealed class MovieBotApiClient
{
    private readonly HttpClient _http;
    private readonly string? _bearer;

    public MovieBotApiClient(HttpClient http) : this(http, null)
    {
    }

    private MovieBotApiClient(HttpClient http, string? bearer)
    {
        _http = http;
        _bearer = bearer;
    }

    /// <summary>This client, acting as the person <paramref name="accessToken"/> names.</summary>
    public MovieBotApiClient As(string accessToken) => new(_http, accessToken);

    /// <summary>The library, as the API currently sees it.</summary>
    public async Task<IReadOnlyList<LibraryTitle>> ListTitlesAsync(CancellationToken ct) =>
        await GetAsync("api/titles", ClientJsonContext.Default.IReadOnlyListLibraryTitle, "The library could not be read.", ct) ?? [];

    /// <summary>One film's whole manifest — its tracks, its subtitles, its chapters — or null when the library has no such title.</summary>
    public Task<Manifest?> GetTitleAsync(string titleId, CancellationToken ct) =>
        GetAsync($"api/titles/{Uri.EscapeDataString(titleId)}", ManifestJsonContext.Default.Manifest,
            $"{titleId} could not be read.", ct, missing: true);

    /// <summary>
    /// The state of one room that already exists, or null when there is none. Read from the listing
    /// first, because reading a session by id creates it, and asking about a channel nobody is
    /// watching in would conjure a room.
    /// </summary>
    public async Task<SessionState?> ReadRoomAsync(string sessionId, CancellationToken ct)
    {
        var rooms = await ListRoomsAsync(ct);
        if (rooms.All(r => r.SessionId != sessionId)) return null;
        return await OpenSessionAsync(sessionId, ct);
    }

    /// <summary>
    /// Opens the room's session and returns its current state. The API creates the session on
    /// first read, so this is what makes the room exist before anyone clicks the link.
    /// </summary>
    public async Task<SessionState> OpenSessionAsync(string sessionId, CancellationToken ct)
    {
        var push = await GetAsync($"api/sessions/{Uri.EscapeDataString(sessionId)}",
            ManifestJsonContext.Default.SessionStatePush, "The session could not be opened.", ct);
        return push?.State ?? throw new MovieBotApiException("The session endpoint returned nothing.");
    }

    /// <summary>Every room, with what it is watching and how many are in it.</summary>
    public async Task<IReadOnlyList<RoomSummary>> ListRoomsAsync(CancellationToken ct) =>
        await GetAsync("api/sessions", ManifestJsonContext.Default.IReadOnlyListRoomSummary, "The rooms could not be read.", ct) ?? [];

    /// <summary>
    /// Puts the film into the room before anyone opens it.
    ///
    /// An Activity is launched by Discord from a URL the bot never writes, so there is no query
    /// string to carry the choice in. The session carries it instead, and whoever joins finds the
    /// film already loaded.
    /// </summary>
    public async Task<SessionState> SetSessionTitleAsync(string sessionId, string titleId, CancellationToken ct)
    {
        var push = await SendAsync(HttpMethod.Post, $"api/sessions/{Uri.EscapeDataString(sessionId)}/title",
            new SetTitleRequest(titleId), ManifestJsonContext.Default.SetTitleRequest,
            ManifestJsonContext.Default.SessionStatePush, $"The API refused to load {titleId}.", ct);
        return push.State;
    }

    /// <summary>Starts the room playing from wherever it is.</summary>
    public Task<RoomChanged> PlayAsync(string sessionId, CancellationToken ct) =>
        ControlAsync(sessionId, "play", new RoomPlaybackRequest(), ManifestJsonContext.Default.RoomPlaybackRequest, ct);

    /// <summary>Stops the room where it is.</summary>
    public Task<RoomChanged> PauseAsync(string sessionId, CancellationToken ct) =>
        ControlAsync(sessionId, "pause", new RoomPlaybackRequest(), ManifestJsonContext.Default.RoomPlaybackRequest, ct);

    /// <summary>
    /// Moves the room by <paramref name="deltaSeconds"/> from where it is. Relative rather than a
    /// position computed here, because the film moves during the round trip.
    /// </summary>
    public Task<RoomChanged> NudgeAsync(string sessionId, double deltaSeconds, CancellationToken ct) =>
        ControlAsync(sessionId, "seek-relative", new RoomNudgeRequest(deltaSeconds), ManifestJsonContext.Default.RoomNudgeRequest, ct);

    /// <summary>Moves the room to a position in the film. The API clamps it to what can be played.</summary>
    public Task<RoomChanged> SeekAsync(string sessionId, double toSeconds, CancellationToken ct) =>
        ControlAsync(sessionId, "seek", new RoomSeekRequest(toSeconds), ManifestJsonContext.Default.RoomSeekRequest, ct);

    /// <summary>The English subtitles the index holds for a film, best fit first, without spending an allowance.</summary>
    public Task<SubtitleSearchView?> SearchSubtitlesAsync(string titleId, CancellationToken ct) =>
        GetAsync($"api/titles/{Uri.EscapeDataString(titleId)}/subtitles/search", ManifestJsonContext.Default.SubtitleSearchView,
            $"The subtitles for {titleId} could not be searched.", ct, missing: true);

    /// <summary>
    /// Fetches one subtitle into the film for the whole room, which spends one of the day's
    /// downloads. The refusal is a sentence for the room: the allowance being spent says when it
    /// comes back.
    /// </summary>
    public async Task<(SubtitleAdded? Added, string? Refusal)> AddSubtitleAsync(string titleId, long fileId, CancellationToken ct)
    {
        using var request = Request(HttpMethod.Post, $"api/titles/{Uri.EscapeDataString(titleId)}/subtitles");
        request.Content = JsonContent.Create(new AddSubtitleRequest(fileId), ManifestJsonContext.Default.AddSubtitleRequest);
        using var response = await _http.SendAsync(request, ct);

        if (response.IsSuccessStatusCode)
            return (await response.Content.ReadFromJsonAsync(ManifestJsonContext.Default.SubtitleAdded, ct), null);

        if (response.StatusCode == HttpStatusCode.TooManyRequests
            && await response.Content.ReadFromJsonAsync(ManifestJsonContext.Default.QuotaReply, ct) is { } quota)
            return (null, quota.ResetsAt is { } at
                ? $"Today's subtitle downloads are spent. They come back at {at:HH:mm} UTC."
                : "Today's subtitle downloads are spent.");

        return (null, response.StatusCode switch
        {
            HttpStatusCode.NotFound => "The library no longer has that film.",
            HttpStatusCode.Forbidden => "Your account does not let you do that in Cinema.",
            _ => $"The subtitle could not be fetched ({(int)response.StatusCode}).",
        });
    }

    // ── Fetching films ─────────────────────────────────────────────────────────────────────────
    // The API starts every download, holds every want and records every keep; the bot asks it and
    // words what comes back for Discord.

    /// <summary>What to offer for something typed into a picker, debounced per <paramref name="session"/>.</summary>
    public Task<ReleaseSuggestions> SuggestReleasesAsync(string typed, string session, CancellationToken ct) =>
        GetAsync($"api/releases/suggest?q={Uri.EscapeDataString(typed)}&session={Uri.EscapeDataString(session)}",
            ManifestJsonContext.Default.ReleaseSuggestions, "The tracker could not be searched.", ct)!;

    /// <summary>The releases the tracker holds for a film, named in words, best first.</summary>
    public Task<RankedOffers> SearchReleasesAsync(string typed, CancellationToken ct) =>
        GetAsync($"api/releases?q={Uri.EscapeDataString(typed)}",
            ManifestJsonContext.Default.RankedOffers, "The tracker could not be searched.", ct)!;

    /// <summary>The releases the tracker holds for a film, by its IMDb id, best first.</summary>
    public Task<RankedOffers> ReleasesForAsync(string imdbId, CancellationToken ct) =>
        GetAsync($"api/releases?imdb={Uri.EscapeDataString(imdbId)}",
            ManifestJsonContext.Default.RankedOffers, "The tracker could not be searched.", ct)!;

    /// <summary>The shows the catalogue lists for a name, the closest match first.</summary>
    public async Task<IReadOnlyList<TheKrystalShip.MovieBot.Catalogue.TitleCard>> FindShowsAsync(string typed, CancellationToken ct) =>
        (await GetAsync($"api/catalogue/titles?kind=show&size=25&q={Uri.EscapeDataString(typed)}",
            ManifestJsonContext.Default.CardPage, "The catalogue could not be asked.", ct))?.Titles ?? [];

    /// <summary>What the tracker offers for a show, season by season, by the show's IMDb id.</summary>
    public Task<ShowOffers> ShowReleasesAsync(string imdbId, CancellationToken ct) =>
        GetAsync($"api/shows/{Uri.EscapeDataString(imdbId)}/releases",
            ManifestJsonContext.Default.ShowOffers, "The tracker could not be searched.", ct)!;

    /// <summary>A release the API offered, or null when it never offered that id.</summary>
    public Task<ReleaseOffer?> OfferedReleaseAsync(long torrentId, CancellationToken ct) =>
        GetAsync($"api/releases/{torrentId}",
            ManifestJsonContext.Default.ReleaseOffer, "The release could not be read.", ct, missing: true);

    /// <summary>Every download, running or seeding, with what its notes say about it.</summary>
    public async Task<IReadOnlyList<DownloadView>> ListDownloadsAsync(CancellationToken ct) =>
        await GetAsync("api/downloads",
            ManifestJsonContext.Default.IReadOnlyListDownloadView, "The downloads could not be read.", ct) ?? [];

    public Task<StartDownloadReply> StartDownloadAsync(StartDownloadRequest request, CancellationToken ct) =>
        SendAsync(HttpMethod.Post, "api/downloads", request, ManifestJsonContext.Default.StartDownloadRequest,
            ManifestJsonContext.Default.StartDownloadReply, "The download could not be started.", ct);

    /// <summary>
    /// Watches a film as the person this client acts as: the API opens a room for it (or loads it into
    /// <see cref="WatchNowRequest.Room"/>) and prepares it there. As many films being watched while
    /// they are prepared as the server allows is an answer, <c>full</c>, rather than a failure.
    /// </summary>
    public async Task<WatchNowReply> WatchNowAsync(WatchNowRequest request, CancellationToken ct)
    {
        const string failure = "The film could not be started.";
        try
        {
            using var message = Request(HttpMethod.Post, "api/watch");
            message.Content = JsonContent.Create(request, ManifestJsonContext.Default.WatchNowRequest);
            using var response = await _http.SendAsync(message, ct);
            if (response.StatusCode != HttpStatusCode.Conflict) Ensure(response, failure);
            return await response.Content.ReadFromJsonAsync(ManifestJsonContext.Default.WatchNowReply, ct)
                ?? throw new MovieBotApiException(failure);
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or TaskCanceledException
                                   && !ct.IsCancellationRequested)
        {
            throw new MovieBotApiException(failure, ex);
        }
    }

    /// <summary>The titles the person this client acts as wants, newest first.</summary>
    public async Task<IReadOnlyList<WantView>> ListWantsAsync(CancellationToken ct) =>
        await GetAsync("api/me/wants",
            ManifestJsonContext.Default.IReadOnlyListWantView, "The wants could not be read.", ct) ?? [];

    /// <summary>Makes the person's want of a title. Null when the film database has no such title.</summary>
    public async Task<SetWantReply?> SetWantAsync(string imdbId, SetWantRequest request, CancellationToken ct)
    {
        const string failure = "The want could not be made.";
        try
        {
            using var message = Request(HttpMethod.Put, $"api/me/wants/{Uri.EscapeDataString(imdbId)}");
            message.Content = JsonContent.Create(request, ManifestJsonContext.Default.SetWantRequest);
            using var response = await _http.SendAsync(message, ct);
            if (response.StatusCode == HttpStatusCode.NotFound) return null;
            Ensure(response, failure);
            return await response.Content.ReadFromJsonAsync(ManifestJsonContext.Default.SetWantReply, ct)
                ?? throw new MovieBotApiException(failure);
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or TaskCanceledException
                                   && !ct.IsCancellationRequested)
        {
            throw new MovieBotApiException(failure, ex);
        }
    }

    /// <summary>Ends the person's want of a title. False when they had none.</summary>
    public async Task<bool> RemoveWantAsync(string imdbId, CancellationToken ct)
    {
        const string failure = "The want could not be removed.";
        try
        {
            using var message = Request(HttpMethod.Delete, $"api/me/wants/{Uri.EscapeDataString(imdbId)}");
            using var response = await _http.SendAsync(message, ct);
            if (response.StatusCode == HttpStatusCode.NotFound) return false;
            Ensure(response, failure);
            return true;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException
                                   && !ct.IsCancellationRequested)
        {
            throw new MovieBotApiException(failure, ex);
        }
    }

    /// <summary>
    /// Whether the API is answering. Used at startup so an unreachable backend is reported once,
    /// on the log, rather than for the first time in front of a room.
    /// </summary>
    public async Task<bool> IsHealthyAsync(CancellationToken ct)
    {
        try
        {
            using var response = await _http.GetAsync("health", ct);
            return response.StatusCode == HttpStatusCode.OK;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException
                                   && !ct.IsCancellationRequested)
        {
            return false;
        }
    }

    private HttpRequestMessage Request(HttpMethod method, string path)
    {
        var request = new HttpRequestMessage(method, path);
        if (_bearer is { Length: > 0 }) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _bearer);
        return request;
    }

    private async Task<T?> GetAsync<T>(
        string path, JsonTypeInfo<T> type, string failure,
        CancellationToken ct, bool missing = false, HttpMethod? method = null)
    {
        try
        {
            using var request = Request(method ?? HttpMethod.Get, path);
            using var response = await _http.SendAsync(request, ct);
            if (missing && response.StatusCode == HttpStatusCode.NotFound) return default;
            Ensure(response, failure);
            return await response.Content.ReadFromJsonAsync(type, ct);
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or TaskCanceledException
                                   && !ct.IsCancellationRequested)
        {
            throw new MovieBotApiException(failure, ex);
        }
    }

    private async Task<TResponse> SendAsync<TRequest, TResponse>(
        HttpMethod method, string path, TRequest body, JsonTypeInfo<TRequest> requestType,
        JsonTypeInfo<TResponse> responseType, string failure, CancellationToken ct)
    {
        try
        {
            using var request = Request(method, path);
            request.Content = JsonContent.Create(body, requestType);
            using var response = await _http.SendAsync(request, ct);
            Ensure(response, failure);
            return await response.Content.ReadFromJsonAsync(responseType, ct)
                ?? throw new MovieBotApiException(failure);
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or TaskCanceledException
                                   && !ct.IsCancellationRequested)
        {
            throw new MovieBotApiException(failure, ex);
        }
    }

    /// <summary>
    /// Sends one room control. No position is ever sent for a play or a pause: the bot is not watching
    /// and does not know where the film is, and the API resolves "wherever the room is" under the same
    /// lock as the change.
    /// </summary>
    private Task<RoomChanged> ControlAsync<TRequest>(
        string sessionId, string control, TRequest request, JsonTypeInfo<TRequest> requestType, CancellationToken ct) =>
        SendAsync(HttpMethod.Post, $"api/sessions/{Uri.EscapeDataString(sessionId)}/{control}", request, requestType,
            ManifestJsonContext.Default.RoomChanged, $"The API refused {control} in {sessionId}.", ct);

    /// <summary>A refusal of the person this client acts as is its own exception, which a surface words as one.</summary>
    private static void Ensure(HttpResponseMessage response, string failure)
    {
        if (response.IsSuccessStatusCode) return;
        if (response.StatusCode == HttpStatusCode.Forbidden) throw new MovieBotAccessDeniedException(failure);
        throw new MovieBotApiException($"{failure} ({(int)response.StatusCode})");
    }
}
