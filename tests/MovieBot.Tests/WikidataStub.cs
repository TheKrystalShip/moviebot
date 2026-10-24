using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Text;
using System.Web;

namespace TheKrystalShip.MovieBot.Tests;

/// <summary>
/// Wikidata for the tests: its query service answers the few sets the tests ask about, over the film
/// database stub's invented films, and its entity search knows <c>Stephen King</c> and two things called
/// <c>Paris</c>. Every request is counted and its User-Agent kept, and the next requests can be answered
/// that too many have been made.
/// </summary>
public sealed class WikidataStub : HttpMessageHandler
{
    public const string Heat = "tt0113277";
    public const string Pirates = "tt0325980";

    private int _asked;
    private int _limited;
    private int _inFlight;
    private int _mostInFlight;
    private readonly ConcurrentQueue<string> _queries = new();
    private readonly ConcurrentQueue<string> _agents = new();

    /// <summary>Every request, SPARQL or entity search.</summary>
    public int Asked => _asked;

    /// <summary>The SPARQL questions put, as their text.</summary>
    public IReadOnlyCollection<string> Queries => _queries;

    /// <summary>The SPARQL questions about a set of films, not about one film.</summary>
    public IReadOnlyList<string> SetQueries => [.. _queries.Where(q => !q.Contains("wdt:P345 \""))];

    /// <summary>The User-Agent of every request.</summary>
    public IReadOnlyCollection<string> Agents => _agents;

    /// <summary>The most requests that were being answered at once.</summary>
    public int MostInFlight => _mostInFlight;

    /// <summary>How long each request takes to answer.</summary>
    public TimeSpan Delay { get; set; }

    /// <summary>The seconds named by the Retry-After of a request answered as too many.</summary>
    public int RetryAfterSeconds { get; set; }

    /// <summary>The next <paramref name="requests"/> requests are answered that too many have been made.</summary>
    public void Limit(int requests) => Interlocked.Exchange(ref _limited, requests);

    /// <summary>How many SPARQL questions mention the item.</summary>
    public int AskedAbout(string item) => _queries.Count(q => q.Contains("wd:" + item + ".") || q.Contains("wd:" + item + " }"));

    private static string Row(string imdb, int? tmdb, string label, string date) =>
        "{ \"imdb\": { \"type\": \"literal\", \"value\": \"" + imdb + "\" }"
        + (tmdb is null ? "" : ", \"tmdb\": { \"type\": \"literal\", \"value\": \"" + tmdb + "\" }")
        + ", \"label\": { \"type\": \"literal\", \"value\": \"" + label + "\" }, \"date\": { \"type\": \"literal\", \"value\": \"" + date + "T00:00:00Z\" } }";

    private static string Rows(params string[] rows) => "{ \"results\": { \"bindings\": [ " + string.Join(", ", rows) + " ] } }";

    private static readonly string HeatRow = Row(Heat, 949, "Heat", "1995-12-15");
    private static readonly string PiratesRow = Row(Pirates, 22, "Pirates of the Caribbean", "2003-07-09");
    private static readonly string ReelRow = Row("tt9000777", 7701, "Forgotten Reel", "2003-05-05");
    private static readonly string DreamRow = Row("tt9000401", 7401, "Dream Detective", "2006-11-25");

    /// <summary>Forty-five films the film database stub names, as one place's set.</summary>
    private static string Paged() => Rows([.. Enumerable.Range(0, FilmDatabaseStub.PagedFilms)
        .Select(i => Row("tt" + (FilmDatabaseStub.PagedFirst + i).ToString("D7", CultureInfo.InvariantCulture), FilmDatabaseStub.PagedFirst + i, "Paged " + i, "2010-01-01"))]);

    private static string Binding(string kind, string item, string label, string? date = null, string? author = null) =>
        "{ \"kind\": { \"value\": \"" + kind + "\" }, \"item\": { \"value\": \"http://www.wikidata.org/entity/" + item + "\" }, \"itemLabel\": { \"value\": \"" + label + "\" }"
        + (date is null ? "" : ", \"date\": { \"value\": \"" + date + "T00:00:00Z\" }")
        + (author is null ? "" : ", \"authorLabel\": { \"value\": \"" + author + "\" }") + " }";

    private static string Sparql(string query)
    {
        if (query.Contains("wdt:P345 \"" + Heat + "\""))
            return Rows(
                Binding("won", "Q1", "Test Prize", "1996-03-01"),
                Binding("nominated", "Q2", "Other Prize"),
                Binding("nominated", "Q3", "Third Prize"),
                Binding("basedon", "Q4", "The Heat Novel", author: "Ann Writer"));
        var item = System.Text.RegularExpressions.Regex.Match(query, @"wd:(Q\d+)\.|wd:(Q\d+) \}").Groups.Cast<System.Text.RegularExpressions.Group>().Skip(1).FirstOrDefault(g => g.Success)?.Value;
        return (item, query) switch
        {
            ("Q102427", _) => Rows(DreamRow, PiratesRow, ReelRow, HeatRow),
            ("Q39829", _) => Rows(PiratesRow, HeatRow),
            ("Q90", _) => Rows(HeatRow, PiratesRow),
            ("Q60", _) => Paged(),
            ("Q853630", _) => Rows(HeatRow, ReelRow, DreamRow),
            ("Q496523", _) => Rows(HeatRow),
            _ => Rows(),
        };
    }

    private static string Entities(string search) => search.ToLowerInvariant() switch
    {
        "stephen king" => "{ \"search\": [ { \"id\": \"Q39829\", \"label\": \"Stephen King\", \"description\": \"American author\" } ] }",
        "paris" => "{ \"search\": [ { \"id\": \"Q483020\", \"label\": \"Paris Saint-Germain FC\" }, { \"id\": \"Q90\", \"label\": \"Paris\", \"description\": \"capital of France\" } ] }",
        _ => "{ \"search\": [] }",
    };

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        Interlocked.Increment(ref _asked);
        _agents.Enqueue(request.Headers.UserAgent.ToString());
        var now = Interlocked.Increment(ref _inFlight);
        int seen;
        while (now > (seen = _mostInFlight)) Interlocked.CompareExchange(ref _mostInFlight, now, seen);
        try
        {
            if (Delay > TimeSpan.Zero) await Task.Delay(Delay, ct);

            if (Interlocked.Decrement(ref _limited) >= 0)
            {
                var limited = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
                limited.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.FromSeconds(RetryAfterSeconds));
                return limited;
            }
            Interlocked.Exchange(ref _limited, 0);

            var query = HttpUtility.ParseQueryString(request.RequestUri!.Query);
            string body;
            if (query["action"] == "wbsearchentities") body = Entities(query["search"] ?? "");
            else
            {
                var text = query["query"] ?? "";
                _queries.Enqueue(text);
                body = Sparql(text);
            }
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        }
        finally
        {
            Interlocked.Decrement(ref _inFlight);
        }
    }
}
