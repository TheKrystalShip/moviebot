using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Web;

namespace TheKrystalShip.MovieBot.Tests;

/// <summary>
/// The film database for the tests: it knows Heat, with a poster and one portrait, Al Pacino's credits,
/// and one film in its Adventure genre, and holds nothing else. Every request is counted, so a test
/// can show what was asked once and served after.
///
/// It also answers searches and discoveries over a small invented world: <see cref="Jo"/>, whose
/// credits run from a film long out to one with no date; <see cref="Ray"/>, who is in the black and
/// white <see cref="SilverHarbour"/>; two horror films of the early 2000s; and the lists of what is
/// out and what is coming. Dates are read against the year the test runs in.
/// </summary>
public sealed partial class FilmDatabaseStub : HttpMessageHandler
{
    private int _asked;
    private int _limited;
    private readonly ConcurrentQueue<string> _requests = new();

    public int Asked => _asked;

    /// <summary>Every request made, as its path and query, in the order the database saw them.</summary>
    public IReadOnlyCollection<string> Requests => _requests;

    /// <summary>The next <paramref name="requests"/> requests are answered that too many have been made, each with <c>Retry-After: 0</c>.</summary>
    public void Limit(int requests) => Interlocked.Exchange(ref _limited, requests);

    /// <summary>The year the dates in the stub are read against.</summary>
    public static int ThisYear => DateTime.UtcNow.Year;

    /// <summary>A person of the invented world: credits from 1999 to a film with no date.</summary>
    public const int Jo = 7777;

    /// <summary>A person who is in <see cref="SilverHarbour"/>.</summary>
    public const int Ray = 8001;

    public const string SilverHarbour = "tt9000004";

    private static string Quote(string? text) => text is null ? "null" : "\"" + text + "\"";

    private static string Num(double value) => value.ToString(CultureInfo.InvariantCulture);

    /// <summary>A film's description with its credits and keywords, which is also what names it to a list.</summary>
    private static string Movie(int id, string? imdb, string title, string? date, string status, string genres, string director, string cast, string keywords = "") =>
        "{ \"id\": " + id + ", \"imdb_id\": " + Quote(imdb) + ", \"title\": \"" + title + "\", \"original_language\": \"en\","
        + " \"overview\": \"About " + title + ". It goes on.\", \"release_date\": " + Quote(date) + ", \"status\": \"" + status + "\","
        + " \"vote_average\": 7.04, \"vote_count\": 100, \"poster_path\": \"/p" + id + ".jpg\","
        + " \"genres\": [ " + genres + " ], \"keywords\": { \"keywords\": [ " + keywords + " ] },"
        + " \"credits\": { \"cast\": [ " + cast + " ], \"crew\": [ " + director + " ] } }";

    private static string Cast(int id, string name, int order) =>
        "{ \"id\": " + id + ", \"name\": \"" + name + "\", \"character\": \"Part\", \"order\": " + order + " }";

    private static string Director(int id, string name) =>
        "{ \"id\": " + id + ", \"name\": \"" + name + "\", \"job\": \"Director\", \"department\": \"Directing\" }";

    private static string Genre(int id, string name) => "{ \"id\": " + id + ", \"name\": \"" + name + "\" }";

    /// <summary>A film as one of the database's lists names it; <paramref name="type"/> and <paramref name="character"/> are a credit's.</summary>
    private static string Listed(int id, string title, string? date, double rating, int votes, string genreIds,
        string? type = null, string? character = null) =>
        "{ \"id\": " + id + ", \"title\": \"" + title + "\", \"release_date\": " + Quote(date) + ", \"vote_average\": " + Num(rating) + ","
        + " \"vote_count\": " + votes + ", \"popularity\": " + votes + ", \"overview\": \"About " + title + ". It goes on.\","
        + " \"poster_path\": \"/p" + id + ".jpg\", \"genre_ids\": [ " + genreIds + " ], \"original_language\": \"en\""
        + (type is null ? "" : ", \"media_type\": \"" + type + "\"")
        + (character is null ? "" : ", \"character\": \"" + character + "\"")
        + " }";

    private static string Crew(int id, string title, string? date, int votes, string job, string department) =>
        "{ \"id\": " + id + ", \"media_type\": \"movie\", \"title\": \"" + title + "\", \"release_date\": " + Quote(date) + ", \"vote_count\": " + votes
        + ", \"vote_average\": 7.0, \"job\": \"" + job + "\", \"department\": \"" + department + "\" }";

    private static string Results(params string[] items) => "{ \"results\": [ " + string.Join(", ", items) + " ] }";

    private const string Heat = """
        {
          "id": 949, "imdb_id": "tt0113277", "title": "Heat", "original_language": "en",
          "overview": "A thief and a detective.", "release_date": "1995-12-15", "runtime": 170, "status": "Released",
          "vote_average": 7.9, "vote_count": 8740, "poster_path": "/heat-poster.jpg", "backdrop_path": null,
          "genres": [ { "name": "Crime" }, { "name": "Drama" } ],
          "spoken_languages": [ { "english_name": "English" } ],
          "production_countries": [ { "name": "United States of America" } ],
          "keywords": { "keywords": [ { "id": 818, "name": "heist" }, { "id": 9, "name": "Los Angeles" } ] },
          "credits": {
            "cast": [ { "id": 1158, "name": "Al Pacino", "character": "Vincent Hanna", "order": 0, "profile_path": "/pacino.jpg" },
                      { "id": 380, "name": "Robert De Niro", "character": "Neil McCauley", "order": 1, "profile_path": null },
                      { "id": 381, "name": "Val Kilmer", "character": "Chris Shiherlis", "order": 2, "profile_path": null } ],
            "crew": [ { "id": 638, "name": "Michael Mann", "job": "Director", "department": "Directing" } ]
          },
          "release_dates": { "results": [ { "iso_3166_1": "US", "release_dates": [ { "certification": "R", "type": 3 } ] } ] }
        }
        """;

    private string Answer(HttpRequestMessage request, string path)
    {
        var query = HttpUtility.ParseQueryString(request.RequestUri!.Query);
        var thisYear = ThisYear;
        var released = thisYear + "-01-01";
        var coming = (thisYear + 1) + "-06-01";
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        if (path.EndsWith("/find/tt0113277")) return """{ "movie_results": [ { "id": 949 } ] }""";
        if (path.EndsWith("/find/tt0325980"))
            return """{ "movie_results": [ { "id": 22, "overview": "A blacksmith and a pirate.", "vote_average": 7.81, "vote_count": 21000, "release_date": "2003-07-09", "poster_path": "/pearl.jpg" } ] }""";
        if (path.EndsWith("/find/" + SilverHarbour)) return """{ "movie_results": [ { "id": 7004 } ] }""";
        if (path.Contains("/find/")) return """{ "movie_results": [] }""";
        if (path.EndsWith("/movie/949")) return Heat;
        if (path.EndsWith("/trending/movie/week"))
            return """{ "results": [ { "id": 949, "title": "Heat", "release_date": "1995-12-15", "vote_average": 7.9, "vote_count": 8740, "poster_path": "/heat-poster.jpg" } ] }""";
        if (path.EndsWith("/trending/tv/week")) return """{ "results": [] }""";
        if (path.EndsWith("/movie/949/external_ids")) return """{ "imdb_id": "tt0113277" }""";
        if (path.EndsWith("/movie/22/external_ids")) return """{ "imdb_id": "tt0325980" }""";
        if (path.EndsWith("/person/1158"))
            return """
                { "id": 1158, "name": "Al Pacino", "known_for_department": "Acting", "profile_path": "/pacino.jpg",
                  "combined_credits": { "cast": [
                    { "id": 949, "media_type": "movie", "title": "Heat", "release_date": "1995-12-15", "vote_average": 7.9, "vote_count": 8740, "poster_path": "/heat-poster.jpg", "character": "Vincent Hanna" },
                    { "id": 7, "media_type": "tv", "name": "A Talk Show", "genre_ids": [10767], "vote_count": 900, "character": "Self" } ] } }
                """;
        if (path.EndsWith("/genre/movie/list"))
            return "{ \"genres\": [ " + string.Join(", ", Genre(80, "Crime"), Genre(12, "Adventure"), Genre(27, "Horror")) + " ] }";
        if (path.EndsWith("/genre/tv/list"))
            return "{ \"genres\": [ " + string.Join(", ", Genre(10759, "Action & Adventure"), Genre(18, "Drama")) + " ] }";

        // Searching by name: Heat, the film whose name it is inside, an announcement and a show.
        if (path.EndsWith("/search/movie") || path.EndsWith("/search/tv") || path.EndsWith("/search/multi"))
        {
            if (query["page"] is not (null or "1") || !string.Equals(query["query"], "heat", StringComparison.OrdinalIgnoreCase)) return Results();
            string[] films =
            [
                Listed(5000, "Dead Heat", "1988-05-06", 6.1, 900, "27", "movie"),
                Listed(949, "Heat", "1995-12-15", 7.9, 8740, "80, 18", "movie"),
                Listed(5001, "Heat Again", (thisYear + 1) + "-03-01", 0, 0, "", "movie"),
            ];
            const string show = """{ "id": 6000, "name": "Heat Wave", "first_air_date": "2019-04-01", "vote_average": 6.4, "vote_count": 300, "popularity": 40, "overview": "Summer in a city.", "genre_ids": [18], "original_language": "en", "media_type": "tv" }""";
            const string person = """{ "id": 1158, "name": "Al Pacino", "media_type": "person" }""";
            return Results(path.EndsWith("/search/movie") ? films : path.EndsWith("/search/tv") ? [show] : [.. films, show, person]);
        }

        if (path.EndsWith("/search/person"))
        {
            return query["query"]?.ToLowerInvariant() switch
            {
                "al pacino" => """
                    { "results": [
                      { "id": 1158, "name": "Al Pacino", "known_for_department": "Acting", "popularity": 50, "profile_path": "/pacino.jpg",
                        "known_for": [ { "media_type": "movie", "title": "Heat", "release_date": "1995-12-15" }, { "media_type": "tv", "name": "Hunters", "first_air_date": "2020-02-21" }, { "media_type": "movie", "title": "Untitled" } ] },
                      { "id": 1159, "name": "Al Pacino Jr", "known_for_department": "Acting", "popularity": 1, "known_for": [] } ] }
                    """,
                "jo test" => """
                    { "results": [
                      { "id": 7778, "name": "Jo Test Sr", "known_for_department": "Directing", "popularity": 2, "known_for": [] },
                      { "id": 7777, "name": "Jo Test", "known_for_department": "Acting", "popularity": 20, "known_for": [ { "media_type": "movie", "title": "Old Letters", "release_date": "1999-03-04" } ] } ] }
                    """,
                "ray dane" => """{ "results": [ { "id": 8001, "name": "Ray Dane", "known_for_department": "Acting", "popularity": 30, "known_for": [] } ] }""",
                _ => Results(),
            };
        }

        if (path.EndsWith("/search/keyword"))
        {
            return query["query"]?.ToLowerInvariant() switch
            {
                "black and white" => """{ "results": [ { "id": 12999, "name": "black and white" } ] }""",
                "time loop" => """{ "results": [ { "id": 366519, "name": "time loops" }, { "id": 10854, "name": "time loop" } ] }""",
                _ => Results(),
            };
        }

        if (path.EndsWith("/configuration/languages"))
            return """[ { "iso_639_1": "en", "english_name": "English", "name": "English" }, { "iso_639_1": "ko", "english_name": "Korean", "name": "한국어/조선말" } ]""";

        // A person whose credits run from a film long out to one with no date, and what they did on each.
        if (path.EndsWith("/person/" + Jo))
        {
            string[] cast =
            [
                Listed(7000, "Old Letters", "1999-03-04", 7.5, 2000, "18", "movie", "Mara"),
                Listed(7001, "Spring Letters", released, 7.2, 400, "18", "movie", "Anna"),
                Listed(7002, "Winter Orchard", coming, 0, 0, "18", "movie", "Ines"),
                Listed(7003, "Untitled Jo Test Project", null, 0, 0, "", "movie", "Lead"),
                "{ \"id\": 7, \"media_type\": \"tv\", \"name\": \"A Talk Show\", \"genre_ids\": [10767], \"first_air_date\": \"" + released + "\", \"vote_count\": 900, \"character\": \"Self\" }",
                "{ \"id\": 8, \"media_type\": \"movie\", \"title\": \"A Documentary\", \"release_date\": \"" + released + "\", \"vote_count\": 30, \"character\": \"Self - Interviewee\" }",
                "{ \"id\": 6001, \"media_type\": \"tv\", \"name\": \"Long Winter\", \"first_air_date\": \"2021-01-10\", \"vote_average\": 8.1, \"vote_count\": 700, \"character\": \"Nora\", \"genre_ids\": [18], \"overview\": \"Snow.\" }",
            ];
            string[] crew =
            [
                Crew(7004, "Silver Harbour", released, 50, "Director", "Directing"),
                Crew(7001, "Spring Letters", released, 400, "Screenplay", "Writing"),
                Crew(7005, "Quiet Money", "2010-06-01", 90, "Producer", "Production"),
            ];
            return "{ \"id\": 7777, \"name\": \"Jo Test\", \"known_for_department\": \"Acting\", \"profile_path\": \"/jo.jpg\", \"combined_credits\": { \"cast\": [ "
                + string.Join(", ", cast) + " ], \"crew\": [ " + string.Join(", ", crew) + " ] } }";
        }

        // What names a title: its IMDb id, who made it and who stars in it.
        var drama = Genre(18, "Drama");
        var horror = Genre(27, "Horror");
        if (path.EndsWith("/movie/22"))
            return Movie(22, "tt0325980", "Pirates of the Caribbean", "2003-07-09", "Released", Genre(12, "Adventure"), Director(5, "Gore Verbinski"),
                string.Join(", ", Cast(11, "Johnny Depp", 0), Cast(12, "Orlando Bloom", 1), Cast(13, "Keira Knightley", 2)));
        if (path.EndsWith("/movie/5000")) return Movie(5000, "tt0094987", "Dead Heat", "1988-05-06", "Released", horror, Director(21, "Mark Goldblatt"), Cast(22, "Treat Williams", 0));
        if (path.EndsWith("/movie/5001")) return Movie(5001, null, "Heat Again", (thisYear + 1) + "-03-01", "Planned", "", "", "");
        if (path.EndsWith("/movie/7000")) return Movie(7000, "tt9000000", "Old Letters", "1999-03-04", "Released", drama, Director(31, "Pat Reed"), Cast(Jo, "Jo Test", 0));
        if (path.EndsWith("/movie/7001")) return Movie(7001, "tt9000001", "Spring Letters", released, "Released", drama, Director(32, "Sam Lane"), Cast(Jo, "Jo Test", 0));
        if (path.EndsWith("/movie/7002")) return Movie(7002, "tt9000002", "Winter Orchard", coming, "Post Production", drama, Director(33, "Kit Marsh"), Cast(Jo, "Jo Test", 0));
        if (path.EndsWith("/movie/7003")) return Movie(7003, null, "Untitled Jo Test Project", null, "Planned", "", "", Cast(Jo, "Jo Test", 0));
        if (path.EndsWith("/movie/7004"))
            return Movie(7004, SilverHarbour, "Silver Harbour", released, "Released", drama, Director(Jo, "Jo Test"), Cast(Ray, "Ray Dane", 0),
                "{ \"id\": 12999, \"name\": \"black and white\" }, { \"id\": 5, \"name\": \"harbour\" }");
        if (path.EndsWith("/movie/7005")) return Movie(7005, "tt9000005", "Quiet Money", "2010-06-01", "Released", drama, "", "");
        if (path.EndsWith("/movie/7101"))
            return Movie(7101, "tt9000101", "The Cellar Door", "2002-10-31", "Released", horror, Director(41, "Lou Frame"), Cast(42, "Ada Moor", 0) + ", " + Cast(43, "Ben Pike", 1));
        if (path.EndsWith("/movie/7102")) return Movie(7102, "tt9000102", "Hollow Hill", "2004-09-10", "Released", horror, Director(44, "Eve Stone"), Cast(45, "Cal Rowe", 0));
        if (path.EndsWith("/movie/7201"))
            return Movie(7201, "tt9000201", "Echo Valley", "2019-05-05", "Released", horror, Director(51, "Una Fox"), Cast(52, "Dee Hale", 0), "{ \"id\": 10854, \"name\": \"time loop\" }");
        if (path.EndsWith("/movie/7202"))
            return Movie(7202, "tt9000202", "Again and Again", "2021-02-02", "Released", drama, Director(53, "Vic Dunn"), Cast(54, "Eli Ward", 0), "{ \"id\": 10854, \"name\": \"time loop\" }");
        if (path.EndsWith("/movie/7203")) return Movie(7203, "tt9000203", "Rerun", "2018-08-08", "Released", drama, Director(55, "Wes Hart"), Cast(56, "Fay Cole", 0));
        if (path.EndsWith("/movie/7301")) return Movie(7301, "tt9000301", "Fresh Cut", released, "Released", drama, Director(61, "Max Orr"), Cast(62, "Gia Fox", 0));
        if (path.EndsWith("/movie/7302")) return Movie(7302, "tt9000302", "Next Summer", coming, "Post Production", drama, Director(63, "Ned Ives"), Cast(64, "Hal Vance", 0));
        if (path.EndsWith("/tv/6000"))
            return """
                { "id": 6000, "name": "Heat Wave", "original_language": "en", "first_air_date": "2019-04-01", "status": "Ended",
                  "genres": [ { "name": "Drama" } ], "created_by": [ { "id": 71, "name": "Rae Cho" } ],
                  "external_ids": { "imdb_id": "tt9100001" },
                  "aggregate_credits": { "cast": [ { "id": 72, "name": "Ola Beck", "order": 0 }, { "id": 73, "name": "Pim Hart", "order": 1 }, { "id": 74, "name": "Quin Day", "order": 2 } ] } }
                """;
        if (path.EndsWith("/tv/6001"))
            return """{ "id": 6001, "name": "Long Winter", "original_language": "en", "first_air_date": "2021-01-10", "status": "Ended", "genres": [ { "name": "Drama" } ], "created_by": [], "external_ids": { "imdb_id": "tt9100002" }, "aggregate_credits": { "cast": [] } }""";

        // Discovering by what a title is like.
        if (path.EndsWith("/discover/movie"))
        {
            var genres = query["with_genres"];
            var keywords = query["with_keywords"];
            var people = query["with_people"];
            var from = query["primary_release_date.gte"];
            if (genres == "12") return Results(Listed(22, "Pirates of the Caribbean", "2003-07-09", 7.8, 21000, "12"));
            if (genres == "27")
                return Results(Listed(7101, "The Cellar Door", "2002-10-31", 7.1, 1500, "27"), Listed(7102, "Hollow Hill", "2004-09-10", 6.8, 800, "27"));
            if (keywords == "12999" && people == "8001") return Results(Listed(7004, "Silver Harbour", released, 7.4, 50, "18"));
            if (keywords == "12999")
                return Results(Listed(7004, "Silver Harbour", released, 7.4, 50, "18"), Listed(7000, "Old Letters", "1999-03-04", 7.5, 2000, "18"));
            if (from is not null && DateOnly.Parse(from) > today)
                return Results(Listed(7302, "Next Summer", coming, 0, 0, "18"), Listed(7002, "Winter Orchard", coming, 0, 0, "18"));
            if (from is not null)
                return Results(Listed(7301, "Fresh Cut", released, 6.5, 40, "18"), Listed(7001, "Spring Letters", released, 7.2, 400, "18"));
            return Results();
        }
        if (path.EndsWith("/discover/tv")) return Results();

        // What the database counts as similar to Heat.
        if (path.EndsWith("/movie/949/recommendations"))
            return Results(
                Listed(7201, "Echo Valley", "2019-05-05", 7.0, 600, "27"),
                Listed(7202, "Again and Again", "2021-02-02", 8.0, 1200, "18"),
                Listed(7203, "Rerun", "2018-08-08", 5.0, 3000, "18"));
        if (path.EndsWith("/movie/949/similar"))
            return Results(Listed(7203, "Rerun", "2018-08-08", 5.0, 3000, "18"), Listed(5000, "Dead Heat", "1988-05-06", 6.1, 900, "27"));
        return "";
    }

    [GeneratedRegex(@"/(p\d+|heat-poster|pacino|pearl|jo)\.jpg$")]
    private static partial Regex KnownImage();

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        Interlocked.Increment(ref _asked);
        var path = request.RequestUri!.AbsolutePath;
        _requests.Enqueue(Uri.UnescapeDataString(request.RequestUri.PathAndQuery));

        if (Interlocked.Decrement(ref _limited) >= 0)
        {
            var limited = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
            limited.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.Zero);
            return Task.FromResult(limited);
        }
        Interlocked.Exchange(ref _limited, 0);

        if (path.EndsWith(".jpg"))
            return Task.FromResult(KnownImage().IsMatch(path)
                ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent([0xFF, 0xD8, 0xFF]) }
                : new HttpResponseMessage(HttpStatusCode.NotFound));

        var body = Answer(request, path);
        return Task.FromResult(body == ""
            ? new HttpResponseMessage(HttpStatusCode.NotFound)
            : new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") });
    }
}
