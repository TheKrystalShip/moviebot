using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using TheKrystalShip.Agent.Plugin;

namespace TheKrystalShip.MovieBot.Assistant.Api;

/// <summary>
/// Krystal Cinema's API, asked as the person whose token a turn carries. This service holds no state of
/// the API's and no credential of its own: what a person may see here is exactly what the API lets them,
/// and every call is answered at the moment it is asked.
/// </summary>
public sealed class CinemaApi(HttpClient http)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <summary>The API as the person <paramref name="accessToken"/> names.</summary>
    public PersonApi As(string accessToken) => new(http, accessToken);

    /// <summary>The API's answer to a question as a person asks it, over one connection.</summary>
    public sealed class PersonApi(HttpClient http, string accessToken)
    {
        public Task<SearchResult> SearchAsync(string query, string? kind, int? year, CancellationToken ct) =>
            GetAsync<SearchResult>("api/catalogue/search", [("q", query), ("kind", kind), ("year", year)], ct);

        public Task<PeopleResult> PeopleAsync(string name, CancellationToken ct) =>
            GetAsync<PeopleResult>("api/catalogue/people", [("q", name)], ct);

        public Task<PersonCredits> CreditsAsync(int personId, int? from, int? to, string? role, string? kind, CancellationToken ct) =>
            GetAsync<PersonCredits>($"api/catalogue/people/{personId}/credits",
                [("from", from), ("to", to), ("role", role), ("kind", kind)], ct);

        public Task<DiscoverResult> DiscoverAsync(DiscoverQuery query, CancellationToken ct) =>
            GetAsync<DiscoverResult>("api/catalogue/discover", DiscoverParameters(query), ct);

        /// <summary>A discovery the API may decline as unanswerable (not found, bad request): the answer, or why there is none.</summary>
        public Task<Answer<DiscoverResult>> TryDiscoverAsync(DiscoverQuery query, CancellationToken ct) =>
            TryAsync<DiscoverResult>("api/catalogue/discover", DiscoverParameters(query), ct);

        private static List<(string, object?)> DiscoverParameters(DiscoverQuery query)
        {
            var parameters = new List<(string, object?)> { ("kind", query.Kind) };
            parameters.AddRange(query.Genre.Select(g => ("genre", (object?)g)));
            parameters.AddRange(query.Exclude.Select(g => ("exclude", (object?)g)));
            if (query.IncludeFamily) parameters.Add(("includeFamily", "true"));
            parameters.Add(("from", query.From));
            parameters.Add(("to", query.To));
            parameters.AddRange(query.With.Select(w => ("with", (object?)w)));
            parameters.AddRange(query.About.Select(a => ("about", (object?)a)));
            parameters.Add(("language", query.Language));
            parameters.Add(("like", query.Like));
            parameters.Add(("goodOnly", query.GoodOnly ? "true" : "false"));
            parameters.Add(("sort", query.Sort));
            if (query.Anime) parameters.Add(("category", "anime"));
            return parameters;
        }

        public Task<TitleList> ReleasedAsync(string kind, bool anime, CancellationToken ct) =>
            GetAsync<TitleList>("api/catalogue/released", [("kind", kind), ("category", anime ? "anime" : null)], ct);

        public Task<TitleList> UpcomingAsync(string kind, bool anime, CancellationToken ct) =>
            GetAsync<TitleList>("api/catalogue/upcoming", [("kind", kind), ("category", anime ? "anime" : null)], ct);

        /// <summary>The upcoming list, or why the API declined to give it (not found, bad request).</summary>
        public Task<Answer<TitleList>> TryUpcomingAsync(string kind, CancellationToken ct) =>
            TryAsync<TitleList>("api/catalogue/upcoming", [("kind", kind)], ct);

        public Task<LibraryAdditions> AddedAsync(string? kind, bool anime, int limit, CancellationToken ct) =>
            GetAsync<LibraryAdditions>("api/catalogue/added", [("kind", kind), ("category", anime ? "anime" : null), ("limit", limit)], ct);

        public Task<Popular> PopularAsync(CancellationToken ct) => GetAsync<Popular>("api/popular", [], ct);

        /// <summary>The films people are waiting on, most waiters first; a person without the right to wait on films is refused in words the model can read.</summary>
        public async Task<List<AnticipatedTitle>> AnticipatedAsync(CancellationToken ct)
        {
            Reply<List<AnticipatedTitle>> reply = await SendAsync<List<AnticipatedTitle>>("api/anticipated", [], ct);
            if (reply.Value is not null) return reply.Value;
            throw new ToolRefusal(reply.Status == HttpStatusCode.Forbidden
                ? "This account may not wait on films in Krystal Cinema, so the list of films people are waiting for is not open to the person."
                : reply.Message);
        }

        public Task<FilmDetails> FilmAsync(string imdbId, CancellationToken ct) =>
            GetAsync<FilmDetails>($"api/films/{Uri.EscapeDataString(imdbId)}", [], ct);

        /// <summary>The film's page, or null when the database holds no film under the id.</summary>
        public async Task<FilmDetails?> FilmOrNullAsync(string imdbId, CancellationToken ct)
        {
            Reply<FilmDetails> reply = await SendAsync<FilmDetails>($"api/films/{Uri.EscapeDataString(imdbId)}", [], ct);
            if (reply.Value is not null) return reply.Value;
            return reply.Status == HttpStatusCode.NotFound ? null : throw new ToolRefusal(reply.Message);
        }

        public Task<ShowDetails> ShowAsync(string imdbId, CancellationToken ct) =>
            GetAsync<ShowDetails>($"api/shows/{Uri.EscapeDataString(imdbId)}", [], ct);

        public Task<List<HistoryEntry>> HistoryAsync(CancellationToken ct) =>
            GetAsync<List<HistoryEntry>>("api/me/history", [], ct);

        public Task<List<Want>> WantsAsync(CancellationToken ct) =>
            GetAsync<List<Want>>("api/me/wants", [], ct);

        public Task<List<Follow>> FollowsAsync(CancellationToken ct) =>
            GetAsync<List<Follow>>("api/me/follows", [], ct);

        private async Task<T> GetAsync<T>(string path, IEnumerable<(string Name, object? Value)> query, CancellationToken ct)
            where T : class
        {
            Reply<T> reply = await SendAsync<T>(path, query, ct);
            return reply.Value ?? throw new ToolRefusal(reply.Message);
        }

        private async Task<Answer<T>> TryAsync<T>(string path, IEnumerable<(string Name, object? Value)> query, CancellationToken ct)
            where T : class
        {
            Reply<T> reply = await SendAsync<T>(path, query, ct);
            if (reply.Value is not null) return new Answer<T>(reply.Value, null);
            return reply.Status is HttpStatusCode.NotFound or HttpStatusCode.BadRequest
                ? new Answer<T>(null, reply.Message)
                : throw new ToolRefusal(reply.Message);
        }

        private async Task<Reply<T>> SendAsync<T>(string path, IEnumerable<(string Name, object? Value)> query, CancellationToken ct)
            where T : class
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, path + Query(query));
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            try
            {
                using var response = await http.SendAsync(request, ct);
                if (!response.IsSuccessStatusCode)
                    return await Refusal<T>(response, ct);

                return await response.Content.ReadFromJsonAsync<T>(Json, ct) is { } value
                    ? new Reply<T>(value, response.StatusCode, "")
                    : new Reply<T>(null, HttpStatusCode.BadGateway, "Krystal Cinema answered with nothing.");
            }
            catch (Exception ex) when (ex is HttpRequestException or JsonException or TaskCanceledException
                                       && !ct.IsCancellationRequested)
            {
                return new Reply<T>(null, HttpStatusCode.ServiceUnavailable,
                    "Krystal Cinema could not be reached, so nothing is known about this.");
            }
        }
    }

    /// <summary>A read that the API may have declined: the answer, or the sentence that says why there is none.</summary>
    public readonly record struct Answer<T>(T? Value, string? Why) where T : class;

    private readonly record struct Reply<T>(T? Value, HttpStatusCode Status, string Message) where T : class;

    /// <summary>The query string for the parameters that have a value.</summary>
    internal static string Query(IEnumerable<(string Name, object? Value)> parameters)
    {
        var text = new StringBuilder();
        foreach (var (name, value) in parameters)
        {
            if (value is null) continue;
            text.Append(text.Length == 0 ? '?' : '&').Append(Uri.EscapeDataString(name)).Append('=')
                .Append(Uri.EscapeDataString(Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture)!));
        }

        return text.ToString();
    }

    private static async Task<Reply<T>> Refusal<T>(HttpResponseMessage response, CancellationToken ct)
        where T : class
    {
        string? said = null;
        try
        {
            using var body = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
            if (body.RootElement.ValueKind == JsonValueKind.Object
                && body.RootElement.TryGetProperty("error", out var error) && error.ValueKind == JsonValueKind.String)
                said = error.GetString();
        }
        catch (JsonException)
        {
        }

        return new Reply<T>(null, response.StatusCode, response.StatusCode switch
        {
            HttpStatusCode.Unauthorized => "Krystal Cinema did not accept this sign-in. The person has to sign in again.",
            HttpStatusCode.Forbidden => "This account does not let the person do that in Krystal Cinema.",
            HttpStatusCode.ServiceUnavailable => said is { Length: > 0 }
                ? $"The film database could not be asked just now ({said}), and nothing is kept for this."
                : "The film database could not be asked just now, and nothing is kept for this.",
            _ => said is { Length: > 0 } ? said : $"Krystal Cinema answered {(int)response.StatusCode}.",
        });
    }
}
