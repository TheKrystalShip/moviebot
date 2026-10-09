using System.Net;
using System.Text;
using System.Text.Json;

namespace TheKrystalShip.MovieBot.Assistant.Api;

/// <summary>One answer of the API, as recorded: what was asked and what came back.</summary>
public sealed record RecordedAnswer(string Method, string Target, int Status, string Body);

/// <summary>
/// A recording of the API's answers, one JSON object per line. It is the seam a behavioural suite
/// replays through: the same tools run over the same HTTP client, and the world is whatever was
/// recorded or written down.
/// </summary>
public static class ApiRecording
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static string Line(RecordedAnswer answer) => JsonSerializer.Serialize(answer, Json);

    public static IReadOnlyList<RecordedAnswer> Read(string path) =>
    [
        .. File.ReadLines(path).Where(l => l.Length > 0)
            .Select(l => JsonSerializer.Deserialize<RecordedAnswer>(l, Json)!),
    ];

    /// <summary>
    /// The key a question is matched by: the method, the path, and the query with its parameters in
    /// ordinal order, unescaped and in lower case, so neither the order a tool wrote them in, how a space
    /// was spelled, nor the case a name was typed in matters: the API reads names the same way.
    /// </summary>
    public static string Key(string method, Uri uri)
    {
        string query = uri.Query.TrimStart('?');
        string sorted = query.Length == 0
            ? ""
            : "?" + string.Join('&', query.Split('&')
                .Select(p => Uri.UnescapeDataString(p.Replace('+', ' ')).ToLowerInvariant()).Order(StringComparer.Ordinal));
        return $"{method.ToUpperInvariant()} {uri.AbsolutePath}{sorted}";
    }
}

/// <summary>Appends every answer that passes through it to a recording.</summary>
public sealed class RecordingHandler(string path) : DelegatingHandler
{
    private readonly object _gate = new();

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        HttpResponseMessage response = await base.SendAsync(request, ct);
        string body = await response.Content.ReadAsStringAsync(ct);
        var answer = new RecordedAnswer(request.Method.Method, request.RequestUri!.PathAndQuery, (int)response.StatusCode, body);
        lock (_gate)
            File.AppendAllText(path, ApiRecording.Line(answer) + "\n");

        // The content was consumed to be recorded; the caller reads an identical copy.
        response.Content = new StringContent(body, Encoding.UTF8, response.Content.Headers.ContentType?.MediaType ?? "application/json");
        return response;
    }
}

/// <summary>
/// Answers from a recording or from answers written down, and refuses every question it was not given:
/// a tool that asks for something the world does not hold gets a 404, as the API would.
/// </summary>
public sealed class ReplayHandler : HttpMessageHandler
{
    private readonly Dictionary<string, RecordedAnswer> _answers = new(StringComparer.Ordinal);
    private readonly List<string> _asked = [];

    public ReplayHandler(IEnumerable<RecordedAnswer>? answers = null)
    {
        foreach (RecordedAnswer answer in answers ?? [])
            Add(answer);
    }

    /// <summary>Every question asked, in order, as <c>METHOD /path?sorted=query</c>.</summary>
    public IReadOnlyList<string> Asked => _asked;

    /// <summary>The bearer tokens the questions carried, in step with <see cref="Asked"/>.</summary>
    public List<string?> Tokens { get; } = [];

    public ReplayHandler Add(RecordedAnswer answer)
    {
        _answers[ApiRecording.Key(answer.Method, new Uri(new Uri("http://replay"), answer.Target))] = answer;
        return this;
    }

    /// <summary>Answers <paramref name="target"/> (path and query) with <paramref name="body"/> as JSON.</summary>
    public ReplayHandler Answer(string target, object body, int status = 200) =>
        Add(new RecordedAnswer("GET", target, status,
            body as string ?? JsonSerializer.Serialize(body, new JsonSerializerOptions(JsonSerializerDefaults.Web))));

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        string key = ApiRecording.Key(request.Method.Method, new Uri(new Uri("http://replay"), request.RequestUri!));
        lock (_asked)
        {
            _asked.Add(key);
            Tokens.Add(request.Headers.Authorization?.Parameter);
        }

        if (!_answers.TryGetValue(key, out RecordedAnswer? answer))
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound)
            {
                Content = new StringContent(JsonSerializer.Serialize(new { error = $"Nothing is recorded for {key}." }),
                    Encoding.UTF8, "application/json"),
            });

        return Task.FromResult(new HttpResponseMessage((HttpStatusCode)answer.Status)
        {
            Content = new StringContent(answer.Body, Encoding.UTF8, "application/json"),
        });
    }
}
