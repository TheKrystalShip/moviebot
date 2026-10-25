using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using TheKrystalShip.Agent.Plugin;

namespace TheKrystalShip.MovieBot.Assistant.Api;

/// <summary>
/// Cinema's API, asked as the person whose token a turn carries. This service holds no state of
/// the API's and no credential of its own: what a person may see here is exactly what the API lets them,
/// and every call is answered at the moment it is asked.
/// </summary>
public sealed class CinemaApi(HttpClient http)
{
    /// <summary>The API as the person <paramref name="accessToken"/> names.</summary>
    public PersonApi As(string accessToken) => new(http, accessToken);

    /// <summary>The API's answer to a question as a person asks it, over one connection.</summary>
    public sealed class PersonApi(HttpClient http, string accessToken)
    {
        /// <summary>A list of titles: every search, discovery, ranking, filmography and window of dates is one.</summary>
        public Task<CardPage> TitlesAsync(TitleFilter filter, CancellationToken ct) =>
            GetAsync<CardPage>("api/catalogue/titles", filter.Parameters(), ct);

        /// <summary>A list the API may decline as unanswerable (not found, bad request): the answer, or why there is none.</summary>
        public Task<Answer<CardPage>> TryTitlesAsync(TitleFilter filter, CancellationToken ct) =>
            TryAsync<CardPage>("api/catalogue/titles", filter.Parameters(), ct);

        /// <summary>A title's page, film or show.</summary>
        public Task<TitlePage> TitleAsync(string imdbId, CancellationToken ct) =>
            GetAsync<TitlePage>($"api/catalogue/titles/{Uri.EscapeDataString(imdbId)}", [], ct);

        /// <summary>Words resolved to what the list filters take: genres, people, keywords, companies, Wikidata items, franchises.</summary>
        public Task<List<Term>> TermsAsync(string words, CancellationToken ct) =>
            GetAsync<List<Term>>("api/catalogue/terms", [("q", words)], ct);

        /// <summary>The catalogue's facets over every title, which name the genres and the original languages it holds.</summary>
        public Task<Facets> FacetsAsync(CancellationToken ct) =>
            GetAsync<Facets>("api/catalogue/facets", [], ct);

        /// <summary>What the asking person has of a show: where they pick up and their follow.</summary>
        public Task<PersonShow> PersonShowAsync(string imdbId, CancellationToken ct) =>
            GetAsync<PersonShow>($"api/me/shows/{Uri.EscapeDataString(imdbId)}", [], ct);

        /// <summary>The films people are waiting on, most waiters first; a person without the right to wait on films is refused in words the model can read.</summary>
        public async Task<List<AnticipatedTitle>> AnticipatedAsync(CancellationToken ct)
        {
            Reply<List<AnticipatedTitle>> reply = await SendAsync<List<AnticipatedTitle>>("api/anticipated", [], ct);
            if (reply.Value is not null) return reply.Value;
            throw new ToolRefusal(reply.Status == HttpStatusCode.Forbidden
                ? "This account may not wait on films in Cinema, so the list of films people are waiting for is not open to the person."
                : reply.Message);
        }

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

                return await response.Content.ReadFromJsonAsync((JsonTypeInfo<T>)ApiJson.Default.GetTypeInfo(typeof(T))!, ct) is { } value
                    ? new Reply<T>(value, response.StatusCode, "")
                    : new Reply<T>(null, HttpStatusCode.BadGateway, "Cinema answered with nothing.");
            }
            catch (Exception ex) when (ex is HttpRequestException or JsonException or TaskCanceledException
                                       && !ct.IsCancellationRequested)
            {
                return new Reply<T>(null, HttpStatusCode.ServiceUnavailable,
                    "Cinema could not be reached, so nothing is known about this.");
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
            HttpStatusCode.Unauthorized => "Cinema did not accept this sign-in. The person has to sign in again.",
            HttpStatusCode.Forbidden => "This account does not let the person do that in Cinema.",
            HttpStatusCode.ServiceUnavailable => said is { Length: > 0 }
                ? $"The film database could not be read just now ({said}), so nothing is known about this."
                : "The film database could not be read just now, so nothing is known about this.",
            _ => said is { Length: > 0 } ? said : $"Cinema answered {(int)response.StatusCode}.",
        });
    }
}
