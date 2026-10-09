using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace TheKrystalShip.MovieBot.Assistant.Api;

/// <summary>
/// The API said no, or could not be asked. <see cref="Message"/> is the sentence a tool hands the model:
/// it says what happened and, where the API named it, why.
/// </summary>
public sealed class ApiRefusal(HttpStatusCode status, string message) : Exception(message)
{
    public HttpStatusCode Status { get; } = status;
}

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

        public Task<DiscoverResult> DiscoverAsync(DiscoverQuery query, CancellationToken ct)
        {
            var parameters = new List<(string, object?)> { ("kind", query.Kind) };
            parameters.AddRange(query.Genre.Select(g => ("genre", (object?)g)));
            parameters.Add(("from", query.From));
            parameters.Add(("to", query.To));
            parameters.AddRange(query.With.Select(w => ("with", (object?)w)));
            parameters.AddRange(query.About.Select(a => ("about", (object?)a)));
            parameters.Add(("language", query.Language));
            parameters.Add(("like", query.Like));
            parameters.Add(("goodOnly", query.GoodOnly ? "true" : "false"));
            parameters.Add(("sort", query.Sort));
            return GetAsync<DiscoverResult>("api/catalogue/discover", parameters, ct);
        }

        public Task<TitleList> ReleasedAsync(string kind, CancellationToken ct) =>
            GetAsync<TitleList>("api/catalogue/released", [("kind", kind)], ct);

        public Task<TitleList> UpcomingAsync(string kind, CancellationToken ct) =>
            GetAsync<TitleList>("api/catalogue/upcoming", [("kind", kind)], ct);

        public Task<LibraryAdditions> AddedAsync(string? kind, int limit, CancellationToken ct) =>
            GetAsync<LibraryAdditions>("api/catalogue/added", [("kind", kind), ("limit", limit)], ct);

        public Task<Popular> PopularAsync(CancellationToken ct) => GetAsync<Popular>("api/popular", [], ct);

        public Task<FilmDetails> FilmAsync(string imdbId, CancellationToken ct) =>
            GetAsync<FilmDetails>($"api/films/{Uri.EscapeDataString(imdbId)}", [], ct);

        public Task<ShowDetails> ShowAsync(string imdbId, CancellationToken ct) =>
            GetAsync<ShowDetails>($"api/shows/{Uri.EscapeDataString(imdbId)}", [], ct);

        public Task<List<HistoryEntry>> HistoryAsync(CancellationToken ct) =>
            GetAsync<List<HistoryEntry>>("api/me/history", [], ct);

        public Task<List<ListSummary>> ListsAsync(CancellationToken ct) =>
            GetAsync<List<ListSummary>>("api/me/lists", [], ct);

        public Task<ListContents> ListAsync(string id, CancellationToken ct) =>
            GetAsync<ListContents>($"api/lists/{Uri.EscapeDataString(id)}", [], ct);

        public Task<List<Follow>> FollowsAsync(CancellationToken ct) =>
            GetAsync<List<Follow>>("api/me/follows", [], ct);

        private async Task<T> GetAsync<T>(string path, IEnumerable<(string Name, object? Value)> query, CancellationToken ct)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, path + Query(query));
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            try
            {
                using var response = await http.SendAsync(request, ct);
                if (!response.IsSuccessStatusCode)
                    throw await Refusal(response, ct);

                return await response.Content.ReadFromJsonAsync<T>(Json, ct)
                    ?? throw new ApiRefusal(HttpStatusCode.BadGateway, "Krystal Cinema answered with nothing.");
            }
            catch (Exception ex) when (ex is HttpRequestException or JsonException or TaskCanceledException
                                       && !ct.IsCancellationRequested)
            {
                throw new ApiRefusal(HttpStatusCode.ServiceUnavailable,
                    "Krystal Cinema could not be reached, so nothing is known about this.");
            }
        }
    }

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

    private static async Task<ApiRefusal> Refusal(HttpResponseMessage response, CancellationToken ct)
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

        return new ApiRefusal(response.StatusCode, response.StatusCode switch
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
