using System.Net;
using System.Reflection;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TheKrystalShip.MovieBot.Catalogue.Data;
using TheKrystalShip.MovieBot.Catalogue.Entries;

namespace TheKrystalShip.MovieBot.Catalogue.Wikidata;

/// <summary>One claim Wikidata holds about a title's item.</summary>
public sealed record WikidataClaim(string Property, string Item, string? Label, string? Ordinal);

/// <summary>
/// Wikidata's SPARQL endpoint, asked about titles by their IMDb ids (P345), many at a time. Every
/// request names the catalogue and how to reach whoever runs it in the User-Agent, which Wikidata
/// requires. After a 429 nothing is asked until the time it named has passed.
/// </summary>
public sealed class WikidataSource(HttpClient http, IOptions<CatalogueOptions> options, TimeProvider clock, ILogger<WikidataSource> logger)
{
    /// <summary>The properties a title's facts are read from.</summary>
    public static readonly string[] Properties = ["P840", "P915", "P144", "P166", "P1411", "P921", "P136", "P179", "P155", "P156", "P1434", "P8345"];

    private const int Attempts = 4;
    private readonly SemaphoreSlim _slots = new(Math.Max(1, options.Value.WikidataAtOnce));
    private readonly Lock _gate = new();
    private DateTimeOffset _until;

    private static readonly string Agent =
        $"Cinema/{typeof(WikidataSource).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0] ?? "0"}";

    /// <summary>The item each IMDb id names (the lowest-numbered, where several do) and the claims it holds.</summary>
    public async Task<Dictionary<string, (string Item, List<WikidataClaim> Claims)>> ClaimsAsync(IReadOnlyCollection<string> imdbIds, CancellationToken ct)
    {
        var values = string.Join(' ', imdbIds.Where(Valid).Select(id => $"\"{id}\""));
        var found = new Dictionary<string, (string Item, List<WikidataClaim> Claims)>(StringComparer.Ordinal);
        if (values.Length == 0) return found;

        var direct = string.Join(' ', Properties.Select(p => "wdt:" + p));
        var rows = await QueryAsync($$"""
            SELECT ?imdb ?item ?p ?v ?vLabel WHERE {
              VALUES ?imdb { {{values}} }
              ?item wdt:P345 ?imdb .
              OPTIONAL { VALUES ?p { {{direct}} } ?item ?p ?v . FILTER(isIRI(?v)) }
              SERVICE wikibase:label { bd:serviceParam wikibase:language "en". }
            }
            """, ct);
        foreach (var row in rows)
        {
            if (Value(row, "imdb") is not { } imdb || Entity(Value(row, "item")) is not { } item) continue;
            if (found.TryGetValue(imdb, out var held) && Number(held.Item) <= Number(item))
            {
                if (held.Item != item) continue;
            }
            else
            {
                found[imdb] = held = (item, []);
            }
            if (Entity(Value(row, "p")) is { } property && Entity(Value(row, "v")) is { } value)
                held.Claims.Add(new WikidataClaim(property, value, Label(Value(row, "vLabel"), value), null));
        }

        var extra = await QueryAsync($$"""
            SELECT ?imdb ?item ?series ?ord ?author ?authorLabel WHERE {
              VALUES ?imdb { {{values}} }
              ?item wdt:P345 ?imdb .
              { ?item p:P179 ?st . ?st ps:P179 ?series ; pq:P1545 ?ord . }
              UNION
              { ?item wdt:P144 ?work . ?work wdt:P50 ?author . }
              SERVICE wikibase:label { bd:serviceParam wikibase:language "en". }
            }
            """, ct);
        foreach (var row in extra)
        {
            if (Value(row, "imdb") is not { } imdb || !found.TryGetValue(imdb, out var held) || Entity(Value(row, "item")) != held.Item) continue;
            if (Entity(Value(row, "series")) is { } series && Value(row, "ord") is { } ordinal)
            {
                var index = held.Claims.FindIndex(c => c.Property == "P179" && c.Item == series);
                if (index >= 0) held.Claims[index] = held.Claims[index] with { Ordinal = ordinal };
            }
            if (Entity(Value(row, "author")) is { } author)
                held.Claims.Add(new WikidataClaim("P144/P50", author, Label(Value(row, "authorLabel"), author), null));
        }
        return found;
    }

    private async Task<List<JsonNode>> QueryAsync(string sparql, CancellationToken ct)
    {
        var settings = options.Value;
        for (var attempt = 1; ; attempt++)
        {
            await _slots.WaitAsync(ct);
            try
            {
                TimeSpan wait;
                lock (_gate) wait = _until - clock.GetUtcNow();
                if (wait > TimeSpan.Zero) await Task.Delay(wait, clock, ct);

                using var request = new HttpRequestMessage(HttpMethod.Post, settings.WikidataEndpoint)
                {
                    Content = new FormUrlEncodedContent([new KeyValuePair<string, string>("query", sparql)]),
                };
                request.Headers.UserAgent.ParseAdd($"{Agent} ({settings.Contact})");
                request.Headers.Accept.ParseAdd("application/sparql-results+json");
                using var response = await http.SendAsync(request, ct);
                if (response.StatusCode == HttpStatusCode.TooManyRequests && attempt < Attempts)
                {
                    var retry = response.Headers.RetryAfter?.Delta ?? TimeSpan.FromSeconds(30);
                    logger.LogInformation("Wikidata asked for {Seconds} s before the next question.", retry.TotalSeconds);
                    lock (_gate) _until = clock.GetUtcNow() + retry;
                    continue;
                }
                if ((int)response.StatusCode >= 500 && attempt < Attempts)
                {
                    await Task.Delay(TimeSpan.FromSeconds(attempt * 5), clock, ct);
                    continue;
                }
                response.EnsureSuccessStatusCode();
                await using var body = await response.Content.ReadAsStreamAsync(ct);
                var answer = await JsonNode.ParseAsync(body, cancellationToken: ct);
                return answer?["results"]?["bindings"]?.AsArray().OfType<JsonNode>().ToList() ?? [];
            }
            finally
            {
                _slots.Release();
            }
        }
    }

    private static bool Valid(string imdbId) =>
        imdbId.Length is > 2 and < 16 && imdbId.StartsWith("tt", StringComparison.Ordinal) && imdbId.AsSpan(2).IndexOfAnyExceptInRange('0', '9') < 0;

    private static string? Value(JsonNode row, string name) => row[name]?["value"]?.GetValue<string>();

    /// <summary>The id at the end of an entity or property IRI (<c>Q155476</c>, <c>P179</c>).</summary>
    private static string? Entity(string? iri) => iri is null ? null : iri[(iri.LastIndexOf('/') + 1)..] is { Length: > 1 } id && id[0] is 'Q' or 'P' ? id : null;

    private static long Number(string item) => long.TryParse(item.AsSpan(1), out var n) ? n : long.MaxValue;

    /// <summary>The label service answers an item with no English label with the item's id, which is no label.</summary>
    private static string? Label(string? label, string item) => string.IsNullOrWhiteSpace(label) || label == item ? null : label;
}

/// <summary>
/// Keeps every title's Wikidata layer: asks about the titles never asked and those a
/// <see cref="CatalogueOptions.WikidataDays"/> old, a batch of IMDb ids to a question, and keeps each
/// title's item and claims with its entry built again, a batch to a transaction. When a pass has asked
/// about every title due, the universes are built again.
/// </summary>
public sealed class WikidataWorker(
    WikidataSource source,
    CatalogueDatabase database,
    EntryBuilder builder,
    IOptions<CatalogueOptions> options,
    TimeProvider clock,
    ILogger<WikidataWorker> logger) : BackgroundService
{
    private const int Batch = 800;
    private static readonly TimeSpan Idle = TimeSpan.FromHours(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    var asked = await RunPassAsync(stoppingToken);
                    if (asked > 0)
                    {
                        var universes = database.Write(Universes.Build);
                        logger.LogInformation("Wikidata: {Asked} titles asked about; {Universes} universes.", asked, universes);
                    }
                }
                catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException && !stoppingToken.IsCancellationRequested)
                {
                    logger.LogWarning("Wikidata could not be asked: {Reason}", ex.Message);
                }
                await Task.Delay(Idle, clock, stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
    }

    /// <summary>Asks about every title due, batch by batch; returns how many titles it asked about.</summary>
    public async Task<int> RunPassAsync(CancellationToken ct)
    {
        var asked = 0;
        while (Due() is { Count: > 0 } due)
        {
            var work = System.Diagnostics.Stopwatch.StartNew();
            var answers = await source.ClaimsAsync(due.Select(d => d.Imdb).ToList(), ct);
            var answered = work.Elapsed;
            Keep(due, answers);
            asked += due.Count;
            logger.LogInformation("Wikidata: {Count} titles, {Items} with an item, asked in {Asked:0.0}s and kept in {Kept:0.0}s.",
                due.Count, answers.Count, answered.TotalSeconds, (work.Elapsed - answered).TotalSeconds);
        }
        return asked;
    }

    private List<(long Id, string Imdb)> Due()
    {
        var stale = clock.GetUtcNow() - TimeSpan.FromDays(Math.Max(1, options.Value.WikidataDays));
        return database.Read(c => c.Query("""
            SELECT t.id, t.imdb_id FROM titles t LEFT JOIN wikidata_layer w ON w.title_id = t.id
            WHERE w.title_id IS NULL OR w.fetched_at < $stale
            ORDER BY w.fetched_at NULLS FIRST, t.id
            LIMIT $n
            """, r => (r.GetInt64(0), r.GetString(1)), ("$stale", stale), ("$n", Batch)));
    }

    public void Keep(List<(long Id, string Imdb)> titles, Dictionary<string, (string Item, List<WikidataClaim> Claims)> answers)
    {
        var now = clock.GetUtcNow();
        database.Write(c =>
        {
            foreach (var (id, imdb) in titles)
            {
                answers.TryGetValue(imdb, out var answer);
                var hash = HashOf(answer.Item, answer.Claims);

                // The same answer as held only says when it was asked.
                if (c.First("SELECT hash FROM wikidata_layer WHERE title_id = $id", r => r.GetString(0), ("$id", id)) == hash)
                {
                    c.Execute("UPDATE wikidata_layer SET fetched_at = $at WHERE title_id = $id", ("$at", now), ("$id", id));
                    continue;
                }

                c.Execute("""
                    INSERT INTO wikidata_layer (title_id, item, fetched_at, hash) VALUES ($id, $item, $at, $hash)
                    ON CONFLICT (title_id) DO UPDATE SET item = excluded.item, fetched_at = excluded.fetched_at, hash = excluded.hash
                    """, ("$id", id), ("$item", answer.Item), ("$at", now), ("$hash", hash));
                c.Execute("DELETE FROM wikidata_claims WHERE title_id = $id", ("$id", id));
                foreach (var claim in answer.Claims ?? [])
                    c.Execute("""
                        INSERT OR IGNORE INTO wikidata_claims (title_id, property, item, label, ordinal)
                        VALUES ($id, $p, $item, $label, $ord)
                        """, ("$id", id), ("$p", claim.Property), ("$item", claim.Item), ("$label", claim.Label), ("$ord", claim.Ordinal));
                if (answer.Item is not null)
                    c.Execute("UPDATE titles SET wikidata_id = $item WHERE id = $id", ("$item", answer.Item), ("$id", id));
                builder.Build(c, id);
            }
        });
    }

    /// <summary>The hash of an answer: the item and its claims, in an order of their own.</summary>
    public static string HashOf(string? item, List<WikidataClaim>? claims) => Hash.Of(
        (item ?? "~") + "\n" + string.Join('\n', (claims ?? [])
            .Select(c => $"{c.Property}|{c.Item}|{c.Label}|{c.Ordinal}")
            .Order(StringComparer.Ordinal)));
}
