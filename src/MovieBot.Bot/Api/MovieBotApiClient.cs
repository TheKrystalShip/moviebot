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

    /// <summary>One film's whole manifest — its tracks, its subtitles, its chapters — or null when the library has no such title.</summary>
    public async Task<Manifest?> GetTitleAsync(string titleId, CancellationToken ct)
    {
        try
        {
            using var response = await http.GetAsync($"api/titles/{Uri.EscapeDataString(titleId)}", ct);
            if (response.StatusCode == HttpStatusCode.NotFound) return null;
            if (!response.IsSuccessStatusCode)
                throw new MovieBotApiException($"The API refused to describe {titleId}: {(int)response.StatusCode}.");

            return await response.Content.ReadFromJsonAsync(ManifestJsonContext.Default.Manifest, ct);
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or TaskCanceledException
                                   && !ct.IsCancellationRequested)
        {
            throw new MovieBotApiException($"{titleId} could not be read.", ex);
        }
    }

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

    /// <summary>Moves the room to a position in the film. The API clamps it to what can be played.</summary>
    public Task<RoomChanged> SeekAsync(
        string sessionId, double toSeconds, string userId, string displayName, CancellationToken ct) =>
        ControlAsync(sessionId, "seek",
            new RoomSeekRequest(toSeconds, userId, displayName),
            ManifestJsonContext.Default.RoomSeekRequest, ct);

    /// <summary>The English subtitles the index holds for a film, best fit first, without spending an allowance.</summary>
    public async Task<SubtitleSearchView?> SearchSubtitlesAsync(string titleId, CancellationToken ct)
    {
        try
        {
            using var response = await http.GetAsync(
                $"api/titles/{Uri.EscapeDataString(titleId)}/subtitles/search", ct);
            if (response.StatusCode == HttpStatusCode.NotFound) return null;
            if (!response.IsSuccessStatusCode)
                throw new MovieBotApiException($"The subtitle search for {titleId} failed: {(int)response.StatusCode}.");

            return await response.Content.ReadFromJsonAsync(ManifestJsonContext.Default.SubtitleSearchView, ct);
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or TaskCanceledException
                                   && !ct.IsCancellationRequested)
        {
            throw new MovieBotApiException($"The subtitles for {titleId} could not be searched.", ex);
        }
    }

    /// <summary>
    /// Fetches one subtitle into the film for the whole room, which spends one of the day's
    /// downloads. The refusal is a sentence for the room: the allowance being spent says when it
    /// comes back.
    /// </summary>
    public async Task<(SubtitleAdded? Added, string? Refusal)> AddSubtitleAsync(
        string titleId, long fileId, string addedBy, CancellationToken ct)
    {
        using var response = await http.PostAsJsonAsync(
            $"api/titles/{Uri.EscapeDataString(titleId)}/subtitles",
            new AddSubtitleRequest(fileId, addedBy),
            ManifestJsonContext.Default.AddSubtitleRequest, ct);

        if (response.IsSuccessStatusCode)
            return (await response.Content.ReadFromJsonAsync(ManifestJsonContext.Default.SubtitleAdded, ct), null);

        if (response.StatusCode == HttpStatusCode.TooManyRequests
            && await response.Content.ReadFromJsonAsync(ManifestJsonContext.Default.QuotaReply, ct) is { } quota)
            return (null, quota.ResetsAt is { } at
                ? $"Today's subtitle downloads are spent. They come back at {at:HH:mm} UTC."
                : "Today's subtitle downloads are spent.");

        return (null, response.StatusCode == HttpStatusCode.NotFound
            ? "The library no longer has that film."
            : $"The subtitle could not be fetched ({(int)response.StatusCode}).");
    }

    // ── Fetching films ─────────────────────────────────────────────────────────────────────────
    // The API starts every download, holds every wish and records every keep; the bot asks it and
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

    /// <summary>Every wish, or one person's when <paramref name="userId"/> is given.</summary>
    public async Task<IReadOnlyList<WishView>> ListWishesAsync(string? userId, CancellationToken ct) =>
        await GetAsync(userId is null ? "api/wishes" : $"api/wishes?userId={Uri.EscapeDataString(userId)}",
            ManifestJsonContext.Default.IReadOnlyListWishView, "The wishes could not be read.", ct) ?? [];

    public Task<AddWishReply> AddWishAsync(AddWishRequest request, CancellationToken ct) =>
        SendAsync(HttpMethod.Post, "api/wishes", request, ManifestJsonContext.Default.AddWishRequest,
            ManifestJsonContext.Default.AddWishReply, "The wish could not be made.", ct);

    public async Task<CancelWishReply> CancelWishAsync(string userId, string chosen, CancellationToken ct) =>
        await GetAsync($"api/wishes/{Uri.EscapeDataString(chosen)}?userId={Uri.EscapeDataString(userId)}",
            ManifestJsonContext.Default.CancelWishReply, "The wish could not be cancelled.", ct,
            method: HttpMethod.Delete)
        ?? new CancelWishReply { Outcome = "notWaiting" };

    /// <summary>The wishes the tracker can now answer, with whoever is still waiting on each.</summary>
    public async Task<IReadOnlyList<AvailableWish>> AvailableWishesAsync(CancellationToken ct) =>
        await GetAsync("api/wishes/available",
            ManifestJsonContext.Default.IReadOnlyListAvailableWish, "The wishes could not be read.", ct) ?? [];

    /// <summary>Reports the channels a wish was announced in, whose people the API then forgets.</summary>
    public async Task WishesToldAsync(string imdbId, IReadOnlyList<string> channelIds, CancellationToken ct)
    {
        try
        {
            using var response = await http.PostAsJsonAsync(
                $"api/wishes/{Uri.EscapeDataString(imdbId)}/told", new WishesTold(channelIds),
                ManifestJsonContext.Default.WishesTold, ct);
            if (!response.IsSuccessStatusCode)
                throw new MovieBotApiException($"The API refused to forget {imdbId}: {(int)response.StatusCode}.");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
        {
            throw new MovieBotApiException($"{imdbId} could not be marked as told.", ex);
        }
    }

    /// <summary>Every film on disk, with where the retention rule has it.</summary>
    public async Task<IReadOnlyList<KeptFilmView>> ListKeepsAsync(CancellationToken ct) =>
        await GetAsync("api/keeps",
            ManifestJsonContext.Default.IReadOnlyListKeptFilmView, "The downloads could not be read.", ct) ?? [];

    public Task<KeepReply> KeepAsync(string hash, Actor requestedBy, CancellationToken ct) =>
        SendAsync(HttpMethod.Put, $"api/keeps/{Uri.EscapeDataString(hash)}", new KeepRequest { RequestedBy = requestedBy },
            ManifestJsonContext.Default.KeepRequest, ManifestJsonContext.Default.KeepReply, "The film could not be kept.", ct);

    public Task<KeepReply> ReleaseKeepAsync(string hash, Actor requestedBy, CancellationToken ct) =>
        SendAsync(HttpMethod.Post, $"api/keeps/{Uri.EscapeDataString(hash)}/release", new KeepRequest { RequestedBy = requestedBy },
            ManifestJsonContext.Default.KeepRequest, ManifestJsonContext.Default.KeepReply, "The film could not be let go.", ct);

    /// <summary>Where a film in the library stands with the retention rule, or null when no download is behind it.</summary>
    public async Task<KeptFilmView?> RetentionAsync(string titleId, CancellationToken ct)
    {
        try
        {
            using var response = await http.GetAsync($"api/titles/{Uri.EscapeDataString(titleId)}/retention", ct);
            if (response.StatusCode is HttpStatusCode.NoContent or HttpStatusCode.NotFound) return null;
            if (!response.IsSuccessStatusCode)
                throw new MovieBotApiException($"The retention of {titleId} could not be read: {(int)response.StatusCode}.");
            return await response.Content.ReadFromJsonAsync(ManifestJsonContext.Default.KeptFilmView, ct);
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or TaskCanceledException
                                   && !ct.IsCancellationRequested)
        {
            throw new MovieBotApiException($"The retention of {titleId} could not be read.", ex);
        }
    }

    private async Task<T?> GetAsync<T>(
        string path, System.Text.Json.Serialization.Metadata.JsonTypeInfo<T> type, string failure,
        CancellationToken ct, bool missing = false, HttpMethod? method = null)
    {
        try
        {
            using var request = new HttpRequestMessage(method ?? HttpMethod.Get, path);
            using var response = await http.SendAsync(request, ct);
            if (missing && response.StatusCode == HttpStatusCode.NotFound) return default;
            if (!response.IsSuccessStatusCode)
                throw new MovieBotApiException($"{failure} ({(int)response.StatusCode})");
            return await response.Content.ReadFromJsonAsync(type, ct);
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or TaskCanceledException
                                   && !ct.IsCancellationRequested)
        {
            throw new MovieBotApiException(failure, ex);
        }
    }

    private async Task<TResponse> SendAsync<TRequest, TResponse>(
        HttpMethod method, string path, TRequest body,
        System.Text.Json.Serialization.Metadata.JsonTypeInfo<TRequest> requestType,
        System.Text.Json.Serialization.Metadata.JsonTypeInfo<TResponse> responseType,
        string failure, CancellationToken ct)
    {
        try
        {
            using var request = new HttpRequestMessage(method, path)
            {
                Content = JsonContent.Create(body, requestType),
            };
            using var response = await http.SendAsync(request, ct);
            if (!response.IsSuccessStatusCode)
                throw new MovieBotApiException($"{failure} ({(int)response.StatusCode})");
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
