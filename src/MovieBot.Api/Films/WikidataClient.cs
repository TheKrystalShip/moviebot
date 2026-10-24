using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;

namespace TheKrystalShip.MovieBot.Api.Films;

public sealed class WikidataOptions
{
    public const string Section = "Wikidata";

    /// <summary>Whether Wikidata is asked at all; without it the shelves it fills are absent and its questions are declined.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Where SPARQL questions are put.</summary>
    public string Endpoint { get; set; } = "https://query.wikidata.org/sparql";

    /// <summary>The MediaWiki API that resolves a name to an item.</summary>
    public string EntitySearchEndpoint { get; set; } = "https://www.wikidata.org/w/api.php";

    /// <summary>Where Wikidata's operators can reach whoever asks, sent in the User-Agent.</summary>
    public string Contact { get; set; } = "https://movies.thekrystalship.com";

    /// <summary>How long a kept set or a title's facts are kept before the weekly cycle asks again.</summary>
    public int RefreshDays { get; set; } = 7;

    /// <summary>How often the cycle looks for sets and films that have come due; zero runs no cycle.</summary>
    public int WarmMinutes { get; set; } = 60;

    public int TimeoutSeconds { get; set; } = 60;

    /// <summary>The most questions in flight at once; Wikidata allows five per address.</summary>
    public int MaxAtOnce { get; set; } = 2;
}

/// <summary>A film of a Wikidata set: its IMDb id, the film database's id where Wikidata holds one, its English label and its earliest release.</summary>
public sealed record WikidataFilm
{
    public required string ImdbId { get; init; }
    public int? TmdbId { get; init; }
    public string? Title { get; init; }
    public DateOnly? Released { get; init; }
}

/// <summary>An item a name was resolved to.</summary>
public sealed record WikidataEntity
{
    public string Id { get; init; } = "";
    public string Label { get; init; } = "";
    public string? Description { get; init; }
}

/// <summary>An award a film won, and the year it was given in where Wikidata says.</summary>
public sealed record WikidataAward(string Name, int? Year);

/// <summary>A work a film is based on and its author, where Wikidata names one.</summary>
public sealed record WikidataWork(string Name, string? Author);

/// <summary>What Wikidata says of one film: the awards it won, how many it was nominated for and what it is based on.</summary>
public sealed record WikidataTitleFacts
{
    public List<WikidataAward> Won { get; init; } = [];
    public int Nominations { get; init; }
    public List<WikidataWork> BasedOn { get; init; } = [];
}

public sealed record SparqlResponse
{
    public SparqlResults? Results { get; init; }
}

public sealed record SparqlResults
{
    public List<Dictionary<string, SparqlTerm>> Bindings { get; init; } = [];
}

public sealed record SparqlTerm
{
    public string Value { get; init; } = "";
}

public sealed record EntitySearchResponse
{
    public List<WikidataEntity> Search { get; init; } = [];
}

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(SparqlResponse))]
[JsonSerializable(typeof(EntitySearchResponse))]
[JsonSerializable(typeof(List<WikidataFilm>))]
[JsonSerializable(typeof(List<WikidataEntity>))]
[JsonSerializable(typeof(WikidataTitleFacts))]
internal partial class WikidataJson : JsonSerializerContext;

/// <summary>
/// Wikidata's rules for the whole process: at most <see cref="WikidataOptions.MaxAtOnce"/> questions
/// at once, and after a 429 nothing is asked until the time Wikidata named has passed, since
/// ignoring it is what gets an address banned.
/// </summary>
public sealed class WikidataGate(IOptions<WikidataOptions> options, TimeProvider clock)
{
    private readonly SemaphoreSlim _slots = new(Math.Max(1, options.Value.MaxAtOnce));
    private readonly Lock _gate = new();
    private DateTimeOffset _until;

    /// <summary>Waits out a back-off and takes one of the slots; disposing the result gives it back.</summary>
    public async Task<IDisposable> EnterAsync(CancellationToken ct)
    {
        await _slots.WaitAsync(ct);
        try
        {
            TimeSpan remaining;
            lock (_gate) remaining = _until - clock.GetUtcNow();
            if (remaining > TimeSpan.Zero) await Task.Delay(remaining, clock, ct);
        }
        catch
        {
            _slots.Release();
            throw;
        }
        return new Slot(_slots);
    }

    /// <summary>Nothing is asked for <paramref name="wait"/>.</summary>
    public void BackOff(TimeSpan wait)
    {
        lock (_gate)
        {
            var until = clock.GetUtcNow() + wait;
            if (until > _until) _until = until;
        }
    }

    private sealed class Slot(SemaphoreSlim slots) : IDisposable
    {
        private int _released;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _released, 1) == 0) slots.Release();
        }
    }
}

/// <summary>
/// Wikidata's query service and its entity search. Every call identifies Cinema in the User-Agent,
/// goes through <see cref="WikidataGate"/>, and when Wikidata answers that too many have been made,
/// waits as long as it says and asks again, a few times, before giving up with an
/// <see cref="HttpRequestException"/>.
/// </summary>
public sealed partial class WikidataClient
{
    /// <summary>The longest a back-off is honoured in one go, and how many times a question is put.</summary>
    private static readonly TimeSpan LongestWait = TimeSpan.FromSeconds(60);

    private const int Attempts = 3;

    private static readonly TimeSpan DefaultWait = TimeSpan.FromSeconds(30);

    /// <summary>The most films a question lists.</summary>
    public const int MostFilms = 5000;

    private readonly HttpClient _http;
    private readonly WikidataOptions _options;
    private readonly WikidataGate _gate;
    private readonly TimeProvider _clock;

    public WikidataClient(HttpClient http, IOptions<WikidataOptions> options, WikidataGate gate, TimeProvider clock)
    {
        _http = http;
        _options = options.Value;
        _gate = gate;
        _clock = clock;
        var version = typeof(WikidataClient).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0] ?? "0";
        _http.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", $"Cinema/{version} ({_options.Contact})");
    }

    public bool Enabled => _options.Enabled;

    [GeneratedRegex(@"^Q[1-9]\d*$")]
    private static partial Regex ItemId();

    public static bool IsItemId(string? id) => id is not null && ItemId().IsMatch(id);

    /// <summary>The films a relation to an item lists, each with an IMDb id.</summary>
    public async Task<List<WikidataFilm>> FilmsAsync(WikidataRelation relation, string item, CancellationToken ct)
    {
        var body = await SparqlAsync(WikidataQueries.Films(relation, item), ct);
        var films = new Dictionary<string, WikidataFilm>(StringComparer.Ordinal);
        foreach (var row in Rows(body))
        {
            if (Text(row, "imdb") is not { } imdb || !FilmStore.IsImdbId(imdb) || films.ContainsKey(imdb)) continue;
            films[imdb] = new WikidataFilm
            {
                ImdbId = imdb,
                TmdbId = int.TryParse(Text(row, "tmdb"), NumberStyles.None, CultureInfo.InvariantCulture, out var tmdb) && tmdb > 0 ? tmdb : null,
                Title = Text(row, "label"),
                Released = Day(Text(row, "date")),
            };
        }
        return [.. films.Values];
    }

    /// <summary>What Wikidata says of the film with an IMDb id.</summary>
    public async Task<WikidataTitleFacts> FactsAsync(string imdbId, CancellationToken ct)
    {
        var body = await SparqlAsync(WikidataQueries.Facts(imdbId), ct);
        var won = new Dictionary<string, WikidataAward>(StringComparer.Ordinal);
        var nominated = new HashSet<string>(StringComparer.Ordinal);
        var works = new Dictionary<string, WikidataWork>(StringComparer.Ordinal);
        foreach (var row in Rows(body))
        {
            var item = Text(row, "item");
            var name = Text(row, "itemLabel");
            if (item is null || name is null || IsItemLabel(name)) continue;
            switch (Text(row, "kind"))
            {
                case "won":
                {
                    var year = Day(Text(row, "date"))?.Year;
                    if (!won.TryGetValue(item, out var held) || (held.Year is null && year is not null) || year < held.Year)
                        won[item] = new WikidataAward(name, year);
                    break;
                }
                case "nominated":
                    nominated.Add(item);
                    break;
                case "basedon":
                {
                    var author = Text(row, "authorLabel");
                    if (author is not null && IsItemLabel(author)) author = null;
                    if (!works.TryGetValue(item, out var held) || (held.Author is null && author is not null))
                        works[item] = new WikidataWork(name, author);
                    break;
                }
            }
        }
        return new WikidataTitleFacts
        {
            Won = [.. won.Values.OrderByDescending(a => a.Year ?? 0).ThenBy(a => a.Name, StringComparer.OrdinalIgnoreCase)],
            Nominations = nominated.Count,
            BasedOn = [.. works.Values.OrderBy(w => w.Name, StringComparer.OrdinalIgnoreCase)],
        };
    }

    /// <summary>The items a name could mean, best first, as Wikidata's entity search ranks them.</summary>
    public async Task<List<WikidataEntity>> SearchAsync(string name, CancellationToken ct)
    {
        var uri = $"{_options.EntitySearchEndpoint}?action=wbsearchentities&type=item&language=en&uselang=en&format=json&limit=10&search={Uri.EscapeDataString(name)}";
        var body = await GetAsync(uri, "application/json", ct);
        try
        {
            return [.. JsonSerializer.Deserialize(body, WikidataJson.Default.EntitySearchResponse)?.Search ?? []];
        }
        catch (JsonException ex)
        {
            throw new HttpRequestException("Wikidata's entity search answered something unreadable: " + ex.Message);
        }
    }

    /// <summary>An item Wikidata has no label for is labelled by its own id.</summary>
    private static bool IsItemLabel(string label) => ItemId().IsMatch(label);

    private async Task<string> SparqlAsync(string query, CancellationToken ct) =>
        await GetAsync($"{_options.Endpoint}?format=json&query={Uri.EscapeDataString(query)}", "application/sparql-results+json", ct);

    private static List<Dictionary<string, SparqlTerm>> Rows(string body)
    {
        try
        {
            return JsonSerializer.Deserialize(body, WikidataJson.Default.SparqlResponse)?.Results?.Bindings ?? [];
        }
        catch (JsonException ex)
        {
            throw new HttpRequestException("Wikidata answered something unreadable: " + ex.Message);
        }
    }

    private static string? Text(Dictionary<string, SparqlTerm> row, string name) =>
        row.TryGetValue(name, out var term) && !string.IsNullOrWhiteSpace(term.Value)
            ? (term.Value.StartsWith("http://www.wikidata.org/entity/", StringComparison.Ordinal) ? term.Value[31..] : term.Value)
            : null;

    private static DateOnly? Day(string? text) =>
        DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var at) ? DateOnly.FromDateTime(at.UtcDateTime) : null;

    private async Task<string> GetAsync(string uri, string accept, CancellationToken ct)
    {
        for (var attempt = 1; ; attempt++)
        {
            using (await _gate.EnterAsync(ct))
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, uri);
                request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue(accept));
                using var response = await _http.SendAsync(request, ct);
                if (response.StatusCode == HttpStatusCode.TooManyRequests || (response.StatusCode == HttpStatusCode.ServiceUnavailable && response.Headers.RetryAfter is not null))
                {
                    var wait = WaitOf(response);
                    _gate.BackOff(wait);
                    if (attempt >= Attempts)
                        throw new HttpRequestException($"Wikidata asked for {wait.TotalSeconds:0} s between questions and is still asking.", null, response.StatusCode);
                    continue;
                }
                response.EnsureSuccessStatusCode();
                return await response.Content.ReadAsStringAsync(ct);
            }
        }
    }

    /// <summary>How long Wikidata asked to be left alone: its Retry-After, else a default, never past <see cref="LongestWait"/>.</summary>
    private TimeSpan WaitOf(HttpResponseMessage response)
    {
        var asked = response.Headers.RetryAfter is { } after
            ? after.Delta ?? (after.Date is { } date ? date - _clock.GetUtcNow() : DefaultWait)
            : DefaultWait;
        return asked < TimeSpan.Zero ? TimeSpan.Zero : asked > LongestWait ? LongestWait : asked;
    }
}

/// <summary>How a film is related to an item of Wikidata, which decides the question that lists the films.</summary>
public enum WikidataRelation
{
    /// <summary>Received the award.</summary>
    Won,

    /// <summary>Was nominated for the award, or received it.</summary>
    Nominated,

    /// <summary>Based on a work by the author.</summary>
    Adaptation,

    /// <summary>Its narrative location is the place.</summary>
    Set,

    /// <summary>Filmed in the place.</summary>
    Filmed,

    /// <summary>Of the genre.</summary>
    Genre,

    /// <summary>About the subject.</summary>
    Subject,
}

/// <summary>The SPARQL questions put to Wikidata. Items are validated as <c>Q</c> numbers, so no text reaches a query.</summary>
public static class WikidataQueries
{
    /// <summary>The films a relation to an item lists: IMDb id, the film database's id, English label and earliest release.</summary>
    public static string Films(WikidataRelation relation, string item)
    {
        if (!WikidataClient.IsItemId(item)) throw new ArgumentException("Not an item of Wikidata.", nameof(item));
        var pattern = relation switch
        {
            WikidataRelation.Won => $"?f wdt:P166 wd:{item}.",
            WikidataRelation.Nominated => $"{{ ?f wdt:P1411 wd:{item} }} UNION {{ ?f wdt:P166 wd:{item} }}.",
            WikidataRelation.Adaptation => $"?f wdt:P144 ?work. ?work wdt:P50 wd:{item}.",
            WikidataRelation.Set => $"?f wdt:P840 wd:{item}.",
            WikidataRelation.Filmed => $"?f wdt:P915 wd:{item}.",
            WikidataRelation.Genre => $"?f wdt:P136 wd:{item}.",
            _ => $"?f wdt:P921 wd:{item}.",
        };
        return $$"""
            SELECT ?f (SAMPLE(?i) AS ?imdb) (SAMPLE(?t) AS ?tmdb) (SAMPLE(?l) AS ?label) (MIN(?d) AS ?date) WHERE {
              ?f wdt:P31 wd:Q11424; wdt:P345 ?i.
              {{pattern}}
              OPTIONAL { ?f wdt:P4947 ?t }
              OPTIONAL { ?f wdt:P577 ?d }
              OPTIONAL { ?f rdfs:label ?l FILTER(LANG(?l) = "en") }
            } GROUP BY ?f LIMIT {{WikidataClient.MostFilms}}
            """;
    }

    /// <summary>The awards one film won and was nominated for, and the works it is based on with their authors.</summary>
    public static string Facts(string imdbId)
    {
        if (!FilmStore.IsImdbId(imdbId)) throw new ArgumentException("Not an IMDb id.", nameof(imdbId));
        return $$"""
            SELECT ?kind ?item ?itemLabel ?date ?authorLabel WHERE {
              ?f wdt:P345 "{{imdbId}}".
              {
                ?f p:P166 ?s. ?s ps:P166 ?item.
                OPTIONAL { ?s pq:P585 ?date }
                BIND("won" AS ?kind)
              } UNION {
                ?f wdt:P1411 ?item.
                BIND("nominated" AS ?kind)
              } UNION {
                ?f wdt:P144 ?item.
                OPTIONAL { ?item wdt:P50 ?author }
                BIND("basedon" AS ?kind)
              }
              SERVICE wikibase:label { bd:serviceParam wikibase:language "en". }
            } LIMIT 500
            """;
    }
}
