using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using TheKrystalShip.MovieBot.Core;

namespace TheKrystalShip.MovieBot.Client;

public sealed partial class MovieBotApiClient
{
    // ── What a person has, wants and plans ─────────────────────────────────────────────────────
    // Everything here answers for the person this client acts as (As), exactly as the site's
    // pages ask it: what they watched, follow and want, who else wants it, and the plans they are in.

    /// <summary>What the person watched, newest first.</summary>
    public async Task<List<HistoryEntry>> HistoryAsync(CancellationToken ct) =>
        await GetAsync("api/me/history", ManifestJsonContext.Default.ListHistoryEntry, "The history could not be read.", ct) ?? [];

    /// <summary>The shows the person follows.</summary>
    public async Task<List<FollowView>> FollowsAsync(CancellationToken ct) =>
        await GetAsync("api/me/follows", ManifestJsonContext.Default.ListFollowView, "The follows could not be read.", ct) ?? [];

    /// <summary>What the person has of a show: where they pick up and their follow.</summary>
    public async Task<PersonShowView> PersonShowAsync(string imdbId, CancellationToken ct) =>
        (await GetAsync($"api/me/shows/{Uri.EscapeDataString(imdbId)}",
            ManifestJsonContext.Default.PersonShowView, $"{imdbId} could not be read.", ct))!;

    /// <summary>The films people wait on, most waited on first.</summary>
    public async Task<List<AnticipatedView>> AnticipatedAsync(CancellationToken ct) =>
        await GetAsync("api/anticipated", ManifestJsonContext.Default.ListAnticipatedView,
            "The films people are waiting for could not be read.", ct) ?? [];

    /// <summary>Whether a film can be had: the library, the torrent client, then the tracker.</summary>
    public async Task<FilmAvailability> FilmAvailabilityAsync(string imdbId, CancellationToken ct) =>
        (await GetAsync($"api/films/{Uri.EscapeDataString(imdbId)}/availability",
            ManifestJsonContext.Default.FilmAvailability, $"Whether {imdbId} can be had could not be read.", ct))!;

    /// <summary>Everybody the server has a name for but the person, most recently seen first; with <paramref name="words"/>, those whose name holds each.</summary>
    public async Task<List<PersonView>> PeopleAsync(string? words, CancellationToken ct) =>
        await GetAsync("api/people" + (words is { Length: > 0 } ? $"?q={Uri.EscapeDataString(words)}" : ""),
            ManifestJsonContext.Default.ListPersonView, "The people could not be read.", ct) ?? [];

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
            await EnsureAsync(response, failure, ct);
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
            await EnsureAsync(response, failure, ct);
            return true;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException
                                   && !ct.IsCancellationRequested)
        {
            throw new MovieBotApiException(failure, ex);
        }
    }

    /// <summary>The titles the person wants that somebody else visibly wants too, with those people.</summary>
    public async Task<List<TogetherView>> TogetherAsync(CancellationToken ct) =>
        await GetAsync("api/me/together", ManifestJsonContext.Default.ListTogetherView,
            "What the person wants with others could not be read.", ct) ?? [];

    /// <summary>The people with a visible want on a title, the person left out, newest first.</summary>
    public async Task<List<WanterView>> WantersAsync(string imdbId, CancellationToken ct) =>
        await GetAsync($"api/titles/{Uri.EscapeDataString(imdbId)}/wanters", ManifestJsonContext.Default.ListWanterView,
            "Who wants it could not be read.", ct) ?? [];

    /// <summary>The plans the person is in, soonest first.</summary>
    public async Task<List<PlanView>> PlansAsync(CancellationToken ct) =>
        await GetAsync("api/me/plans", ManifestJsonContext.Default.ListPlanView, "The plans could not be read.", ct) ?? [];

    /// <summary>One plan the person is in; null when there is none they are in.</summary>
    public Task<PlanView?> PlanAsync(string id, CancellationToken ct) =>
        GetAsync($"api/plans/{Uri.EscapeDataString(id)}", ManifestJsonContext.Default.PlanView, "The plan could not be read.", ct, missing: true);

    /// <summary>Makes a plan, with the person as its maker.</summary>
    public Task<PlanView> MakePlanAsync(MakePlanRequest request, CancellationToken ct) =>
        SendAsync(HttpMethod.Post, "api/plans", request, ManifestJsonContext.Default.MakePlanRequest,
            ManifestJsonContext.Default.PlanView, "The plan could not be made.", ct);

    /// <summary>Puts another time forward for a plan; the person is in for it.</summary>
    public Task<PlanView> SuggestPlanTimeAsync(string id, DateTimeOffset at, CancellationToken ct) =>
        SendAsync(HttpMethod.Post, $"api/plans/{Uri.EscapeDataString(id)}/times", new FixPlanRequest { At = at },
            ManifestJsonContext.Default.FixPlanRequest, ManifestJsonContext.Default.PlanView, "The time could not be suggested.", ct);

    /// <summary>The person's answer to one time of a plan: <see cref="PlanAnswers"/>.</summary>
    public Task<PlanView> AnswerPlanAsync(string id, DateTimeOffset at, string answer, CancellationToken ct) =>
        SendAsync(HttpMethod.Put, $"api/plans/{Uri.EscapeDataString(id)}/answers/{Uri.EscapeDataString(Instant(at))}",
            new PlanAnswerRequest { Answer = answer }, ManifestJsonContext.Default.PlanAnswerRequest,
            ManifestJsonContext.Default.PlanView, "The answer could not be given.", ct);

    /// <summary>Brings one more person into a plan.</summary>
    public Task<InviteReply> InviteToPlanAsync(string id, string userId, CancellationToken ct) =>
        SendAsync(HttpMethod.Post, $"api/plans/{Uri.EscapeDataString(id)}/people", new InviteRequest { UserId = userId },
            ManifestJsonContext.Default.InviteRequest, ManifestJsonContext.Default.InviteReply, "They could not be invited.", ct);

    /// <summary>The maker picks one of the plan's times, or moves the pick.</summary>
    public Task<PlanView> FixPlanAsync(string id, DateTimeOffset at, CancellationToken ct) =>
        SendAsync(HttpMethod.Post, $"api/plans/{Uri.EscapeDataString(id)}/fix", new FixPlanRequest { At = at },
            ManifestJsonContext.Default.FixPlanRequest, ManifestJsonContext.Default.PlanView, "The time could not be picked.", ct);

    /// <summary>The maker cancels the plan, which deletes it.</summary>
    public Task CancelPlanAsync(string id, CancellationToken ct) =>
        SendAsync(HttpMethod.Delete, $"api/plans/{Uri.EscapeDataString(id)}", "The plan could not be cancelled.", ct);

    /// <summary>An instant as a route names it: UTC, to the second.</summary>
    private static string Instant(DateTimeOffset at) =>
        at.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", System.Globalization.CultureInfo.InvariantCulture);
}
