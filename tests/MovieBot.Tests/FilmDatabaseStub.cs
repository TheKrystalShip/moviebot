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
/// credits run from a film long out to one with no date; <see cref="Ray"/>, who is in the silent
/// <see cref="SilverHarbour"/>; two horror films of the early 2000s; and the lists of what is
/// out and what is coming; and one collection, <see cref="Series"/>. Dates are read against the year the test runs in.
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

    /// <summary>A person whose acting work has a lead, an uncredited part, an appearance, a coming film and shows of 12 and 2 episodes.</summary>
    public const int Kim = 7778;

    /// <summary>A person who is in <see cref="SilverHarbour"/>.</summary>
    public const int Ray = 8001;

    public const string SilverHarbour = "tt9000004";

    /// <summary>The collection the Discover page's Star Wars shelf names, which here holds Heat and Pirates of the Caribbean.</summary>
    public const int Series = 10;

    /// <summary>The keyword whose films run to <see cref="PagedFilms"/> across three pages of the database; the Date night and Top rated shelves are answered the same.</summary>
    public const int PagedKeyword = 31337;

    /// <summary>How many films the paged subject holds: two full pages of the database's and a short one.</summary>
    public const int PagedFilms = 45;

    /// <summary>The first film of the paged subject; the others follow by one.</summary>
    public const int PagedFirst = 9000;

    /// <summary>A horror film the database files nowhere its discovery lists, known to it only by its IMDb id.</summary>
    public const string ForgottenReel = "tt9000777";

    /// <summary>An animated film made in Japanese, which is what anime is here.</summary>
    public const string DreamDetective = "tt9000401";

    private static string Quote(string? text) => text is null ? "null" : "\"" + text + "\"";

    private static string Num(double value) => value.ToString(CultureInfo.InvariantCulture);

    /// <summary>A film's description with its credits and keywords, which is also what names it to a list.</summary>
    private static string Movie(int id, string? imdb, string title, string? date, string status, string genres, string director, string cast, string keywords = "", string language = "en") =>
        "{ \"id\": " + id + ", \"imdb_id\": " + Quote(imdb) + ", \"title\": \"" + title + "\", \"original_language\": \"" + language + "\","
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
          "release_dates": { "results": [ { "iso_3166_1": "US", "release_dates": [ { "certification": "R", "type": 3 } ] } ] },
          "tagline": "A Los Angeles crime saga", "budget": 60000000, "revenue": 187436818,
          "videos": { "results": [
            { "key": "heat-clip", "name": "A clip", "site": "YouTube", "type": "Clip", "official": true, "iso_639_1": "en" },
            { "key": "heat-teaser", "name": "Teaser", "site": "YouTube", "type": "Teaser", "official": true, "iso_639_1": "en", "published_at": "1995-09-01T00:00:00.000Z" },
            { "key": "heat-fan", "name": "Fan trailer", "site": "YouTube", "type": "Trailer", "official": false, "iso_639_1": "en", "published_at": "1996-01-01T00:00:00.000Z" },
            { "key": "heat-trailer", "name": "Official Trailer", "site": "YouTube", "type": "Trailer", "official": true, "iso_639_1": "en", "published_at": "1995-10-01T00:00:00.000Z" } ] },
          "recommendations": { "results": [
            { "id": 7201, "title": "Echo Valley", "release_date": "2019-05-05", "vote_average": 7.0, "vote_count": 600, "poster_path": "/p7201.jpg", "genre_ids": [27], "original_language": "en" },
            { "id": 7202, "title": "Again and Again", "release_date": "2021-02-02", "vote_average": 8.0, "vote_count": 1200, "poster_path": "/p7202.jpg", "genre_ids": [18], "original_language": "en" } ] },
          "reviews": { "total_results": 2, "results": [
            { "author": "Critic", "content": "A masterpiece of the genre.", "url": "https://reviews.example/1", "author_details": { "rating": 9.0 } } ] },
          "images": { "logos": [ { "file_path": "/heat-logo.png", "iso_639_1": "en", "vote_average": 5, "width": 500 } ] }
        }
        """;

    /// <summary>One page of the paged subject, whose films are voted and rated in an order of their own.</summary>
    private static string PagedPage(int page)
    {
        var first = (page - 1) * 20;
        var films = Enumerable.Range(first, Math.Max(0, Math.Min(20, PagedFilms - first)))
            .Select(i => PagedFirst + i)
            .Select(id => Listed(id, "Paged " + (id - PagedFirst), "2010-01-01", 7 + id * 7 % 20 / 10.0, 50 + id * 37 % 900, "35"));
        return "{ \"page\": " + page + ", \"total_pages\": 3, \"results\": [ " + string.Join(", ", films) + " ] }";
    }

    /// <summary>The company and the keyword whose titles are <see cref="Prism"/>: a discovery of them is answered by its filters, as the database answers one.</summary>
    public const int PrismId = 4242;

    /// <summary>A title of the Prism world, with what a discovery filters it by.</summary>
    public sealed record PrismTitle(int Id, string Kind, string Title, string Date, int[] Genres, string Language, int Runtime, double Rating, int Popularity);

    /// <summary>
    /// Films and one show of the 1990s, 2010s and 2020s, of Horror, Crime, Adventure, Animation and
    /// Family and nothing of Western, made in English, French, Korean and Japanese; the reels are
    /// thirty Crime films of the 1990s and 2020s, enough for two decades apart to run to a second page.
    /// </summary>
    public static readonly IReadOnlyList<PrismTitle> Prism =
    [
        new(4300, "film", "Prism Night", "1994-03-01", [27], "en", 95, 6.9, 9000),
        new(4301, "film", "Prism Run", "1997-03-01", [80], "en", 120, 7.4, 8500),
        new(4302, "film", "Prism Deep", "2012-03-01", [27], "en", 85, 6.1, 8000),
        new(4303, "film", "Prism Gold", "2015-03-01", [12], "fr", 160, 7.9, 7500),
        new(4304, "film", "Prism Hollow", "2018-03-01", [27, 80], "ko", 100, 8.3, 7000),
        new(4305, "film", "Prism Light", "2021-03-01", [80], "en", 140, 6.6, 6500),
        new(4306, "film", "Prism Song", "2023-03-01", [12], "en", 80, 7.1, 6000),
        new(4307, "film", "Prism Toon", "2011-03-01", [16], "ja", 90, 7.7, 5500),
        new(4308, "film", "Prism Edge", "1995-03-01", [80], "en", 155, 7.0, 5000),
        new(4309, "film", "Prism Dusk", "2016-03-01", [27], "en", 110, 5.8, 4500),
        new(4310, "film", "Prism Fall", "2019-03-01", [80], "fr", 100, 6.4, 4000),
        new(4311, "film", "Prism Kin", "2013-03-01", [10751], "en", 90, 6.2, 3500),
        new(4312, "film", "Prism Orbit", "2022-03-01", [878], "en", 120, 7.3, 3200),
        .. Enumerable.Range(0, 30).Select(i => new PrismTitle(4400 + i, "film", "Prism Reel " + i, (i % 2 == 0 ? 1990 + i / 4 : 2020 + i / 6) + "-06-01",
            [80], "en", 100, 6.5, 3000 - i)),
        new(4500, "show", "Prism Tales", "2020-05-01", [18], "en", 45, 7.5, 2000),
        new(4501, "show", "Prism Stars", "2021-05-01", [10765], "en", 50, 7.2, 1900),
    ];

    /// <summary>A person credited on the Prism world's titles: every film but the reels, and the show.</summary>
    public const int PrismPerson = 4600;

    [GeneratedRegex(@"/(?:movie|tv)/(4\d{3})/external_ids$")]
    private static partial Regex PrismIds();

    /// <summary>A Prism title as a list or a person's credits names it.</summary>
    private static string PrismListed(PrismTitle t, string? type = null, string? character = null) =>
        "{ \"id\": " + t.Id + ", \"" + (t.Kind == "film" ? "title" : "name") + "\": \"" + t.Title + "\", \"" + (t.Kind == "film" ? "release_date" : "first_air_date") + "\": \"" + t.Date + "\","
        + " \"vote_average\": " + Num(t.Rating) + ", \"vote_count\": 500, \"popularity\": " + t.Popularity + ", \"overview\": \"About " + t.Title + ".\","
        + " \"poster_path\": \"/p" + t.Id + ".jpg\", \"genre_ids\": [ " + string.Join(", ", t.Genres) + " ], \"original_language\": \"" + t.Language + "\""
        + (type is null ? "" : ", \"media_type\": \"" + type + "\"") + (character is null ? "" : ", \"character\": \"" + character + "\"") + " }";

    /// <summary>A page of the Prism world for a discovery, filtered and ordered as the database does it.</summary>
    private static string PrismPage(string kind, System.Collections.Specialized.NameValueCollection query)
    {
        var dates = kind == "film" ? "primary_release_date" : "first_air_date";
        bool Genres(PrismTitle t, string? list, bool without)
        {
            if (string.IsNullOrEmpty(list)) return true;
            var any = list.Contains('|');
            var ids = list.Split(any ? '|' : ',').Select(x => int.Parse(x, CultureInfo.InvariantCulture)).ToList();
            if (without) return !ids.Any(t.Genres.Contains);
            return any ? ids.Any(t.Genres.Contains) : ids.All(t.Genres.Contains);
        }
        var found = Prism
            .Where(t => t.Kind == kind)
            .Where(t => Genres(t, query["with_genres"], false) && Genres(t, query["without_genres"], true))
            .Where(t => query[dates + ".gte"] is not { } from || string.CompareOrdinal(t.Date, from) >= 0)
            .Where(t => query[dates + ".lte"] is not { } to || string.CompareOrdinal(t.Date, to) <= 0)
            .Where(t => query["with_runtime.gte"] is not { } min || t.Runtime >= int.Parse(min, CultureInfo.InvariantCulture))
            .Where(t => query["with_runtime.lte"] is not { } max || t.Runtime <= int.Parse(max, CultureInfo.InvariantCulture))
            .Where(t => query["with_original_language"] is not { } languages || languages.Split('|').Contains(t.Language))
            .Where(t => query["vote_average.gte"] is not { } rating || t.Rating >= double.Parse(rating, CultureInfo.InvariantCulture))
            .OrderByDescending(t => t.Popularity)
            .ToList();
        var page = int.Parse(query["page"] ?? "1", CultureInfo.InvariantCulture);
        var items = found.Skip((page - 1) * 20).Take(20).Select(t => PrismListed(t));
        return "{ \"page\": " + page + ", \"total_pages\": " + Math.Max(1, (found.Count + 19) / 20) + ", \"total_results\": " + found.Count
            + ", \"results\": [ " + string.Join(", ", items) + " ] }";
    }

    [GeneratedRegex(@"/movie/(9\d{3})$")]
    private static partial Regex PagedFilm();

    [GeneratedRegex(@"/find/tt000(90\d{2})$")]
    private static partial Regex PagedFind();

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
        if (path.EndsWith("/find/" + DreamDetective)) return """{ "movie_results": [ { "id": 7401 } ] }""";
        if (path.EndsWith("/find/" + ForgottenReel))
            return """{ "movie_results": [ { "id": 7701, "overview": "Found only by IMDb.", "vote_average": 6.0, "vote_count": 12, "release_date": "2003-05-05", "poster_path": "/p7701.jpg" } ] }""";
        if (PagedFind().Match(path) is { Success: true } pagedFind)
        {
            var id = int.Parse(pagedFind.Groups[1].Value, CultureInfo.InvariantCulture);
            return "{ \"movie_results\": [ { \"id\": " + id + ", \"overview\": \"About Paged " + (id - PagedFirst) + ". It goes on.\", \"vote_average\": 7.0,"
                + " \"vote_count\": 100, \"release_date\": \"2010-01-01\", \"poster_path\": \"/p" + id + ".jpg\" } ] }";
        }
        if (path.EndsWith("/find/tt9900001")) return """{ "movie_results": [], "tv_results": [ { "id": 9901, "vote_count": 0 } ] }""";
        if (path.EndsWith("/find/tt1790809") || path.EndsWith("/find/tt1298650") || path.EndsWith("/find/tt0383574") || path.EndsWith("/find/tt0449088"))
            return "{ \"movie_results\": [ { \"id\": " + (9000 + int.Parse(path[^3..], CultureInfo.InvariantCulture)) + ", \"vote_count\": 0 } ] }";
        if (path.Contains("/find/")) return """{ "movie_results": [] }""";
        if (PrismIds().Match(path) is { Success: true } prismIds) return "{ \"imdb_id\": \"tt99" + prismIds.Groups[1].Value + "0\" }";
        if (path.EndsWith("/person/" + PrismPerson))
            return "{ \"id\": " + PrismPerson + ", \"name\": \"Pia Prism\", \"known_for_department\": \"Acting\", \"combined_credits\": { \"cast\": [ "
                + string.Join(", ", Prism.Where(t => t.Id < 4400 || t.Kind == "show").Select(t => PrismListed(t, t.Kind == "film" ? "movie" : "tv", "Lead"))) + " ] } }";
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
            return "{ \"genres\": [ " + string.Join(", ", Genre(80, "Crime"), Genre(12, "Adventure"), Genre(27, "Horror"), Genre(16, "Animation"), Genre(10751, "Family"), Genre(37, "Western"), Genre(878, "Science Fiction")) + " ] }";
        if (path.EndsWith("/genre/tv/list"))
            return "{ \"genres\": [ " + string.Join(", ", Genre(10759, "Action & Adventure"), Genre(18, "Drama"), Genre(10765, "Sci-Fi & Fantasy")) + " ] }";

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
                "silent film" => """{ "results": [ { "id": 12999, "name": "silent film" } ] }""",
                "paged" => """{ "results": [ { "id": 31337, "name": "paged" } ] }""",
                "prism world" => """{ "results": [ { "id": 4242, "name": "prism world" } ] }""",
                "time loop" => """{ "results": [ { "id": 366519, "name": "time loops" }, { "id": 10854, "name": "time loop" } ] }""",
                _ => Results(),
            };
        }

        // Studios: one filed under several companies, a word that is also a title's name, and one that made a few films.
        if (path.EndsWith("/search/company"))
        {
            return query["query"]?.ToLowerInvariant() switch
            {
                "blumhouse" => """{ "results": [ { "id": 68884, "name": "Blumhouse Television" }, { "id": 3172, "name": "Blumhouse Productions" }, { "id": 73625, "name": "BH Tilt" } ] }""",
                "heat" => """{ "results": [ { "id": 555, "name": "Heat Productions" } ] }""",
                "tiny" => """{ "results": [ { "id": 556, "name": "Tiny Films" } ] }""",
                "paged" => """{ "results": [ { "id": 557, "name": "Paged" } ] }""",
                "prism" => """{ "results": [ { "id": 4242, "name": "Prism" } ] }""",
                _ => Results(),
            };
        }

        if (path.EndsWith("/configuration/languages"))
            return """[ { "iso_639_1": "en", "english_name": "English", "name": "English" }, { "iso_639_1": "ko", "english_name": "Korean", "name": "한국어/조선말" }, { "iso_639_1": "fr", "english_name": "French", "name": "Français" }, { "iso_639_1": "ja", "english_name": "Japanese", "name": "日本語" } ]""";

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

        if (path.EndsWith("/person/" + Kim))
        {
            string[] cast =
            [
                Listed(7000, "Old Letters", "1999-03-04", 7.5, 2000, "18", "movie", "Vera"),
                Listed(7001, "Spring Letters", released, 7.2, 400, "18", "movie", "Passer-by (uncredited)"),
                Listed(7002, "Winter Orchard", coming, 0, 0, "18", "movie", "Ines"),
                "{ \"id\": 8, \"media_type\": \"movie\", \"title\": \"A Documentary\", \"release_date\": \"" + released + "\", \"vote_count\": 30, \"character\": \"Self - Interviewee\" }",
                "{ \"id\": 6001, \"media_type\": \"tv\", \"name\": \"Long Winter\", \"first_air_date\": \"2021-01-10\", \"vote_average\": 8.1, \"vote_count\": 700, \"character\": \"Nora\", \"episode_count\": 12, \"genre_ids\": [18] }",
                "{ \"id\": 6000, \"media_type\": \"tv\", \"name\": \"Heat Wave\", \"first_air_date\": \"2019-04-01\", \"vote_average\": 7.0, \"vote_count\": 300, \"character\": \"Guest\", \"episode_count\": 2, \"genre_ids\": [18] }",
            ];
            return "{ \"id\": 7778, \"name\": \"Kim Test\", \"known_for_department\": \"Acting\", \"combined_credits\": { \"cast\": [ " + string.Join(", ", cast) + " ], \"crew\": [] } }";
        }

        // What names a title: its IMDb id, who made it and who stars in it.
        var drama = Genre(18, "Drama");
        var horror = Genre(27, "Horror");
        if (PagedFilm().Match(path) is { Success: true } paged)
        {
            var id = int.Parse(paged.Groups[1].Value, CultureInfo.InvariantCulture);
            return Movie(id, "tt" + id.ToString("D7", CultureInfo.InvariantCulture), "Paged " + (id - PagedFirst), "2010-01-01", "Released", drama, Director(id + 1000, "Pat " + id), Cast(id + 2000, "Lee " + id, 0));
        }
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
                "{ \"id\": 12999, \"name\": \"silent film\" }, { \"id\": 5, \"name\": \"harbour\" }");
        if (path.EndsWith("/movie/7005")) return Movie(7005, "tt9000005", "Quiet Money", "2010-06-01", "Released", drama, "", "");
        if (path.EndsWith("/movie/7101"))
            return Movie(7101, "tt9000101", "The Cellar Door", "2002-10-31", "Released", horror, Director(41, "Lou Frame"), Cast(42, "Ada Moor", 0) + ", " + Cast(43, "Ben Pike", 1));
        if (path.EndsWith("/movie/7701")) return Movie(7701, ForgottenReel, "Forgotten Reel", "2003-05-05", "Released", horror, Director(46, "Fen Lowe"), Cast(47, "Gus Park", 0));
        if (path.EndsWith("/movie/7102")) return Movie(7102, "tt9000102", "Hollow Hill", "2004-09-10", "Released", horror, Director(44, "Eve Stone"), Cast(45, "Cal Rowe", 0));
        if (path.EndsWith("/movie/7201"))
            return Movie(7201, "tt9000201", "Echo Valley", "2019-05-05", "Released", horror, Director(51, "Una Fox"), Cast(52, "Dee Hale", 0), "{ \"id\": 10854, \"name\": \"time loop\" }");
        if (path.EndsWith("/movie/7202"))
            return Movie(7202, "tt9000202", "Again and Again", "2021-02-02", "Released", drama, Director(53, "Vic Dunn"), Cast(54, "Eli Ward", 0), "{ \"id\": 10854, \"name\": \"time loop\" }");
        if (path.EndsWith("/movie/7203")) return Movie(7203, "tt9000203", "Rerun", "2018-08-08", "Released", drama, Director(55, "Wes Hart"), Cast(56, "Fay Cole", 0));
        if (path.EndsWith("/movie/7301")) return Movie(7301, "tt9000301", "Fresh Cut", released, "Released", drama, Director(61, "Max Orr"), Cast(62, "Gia Fox", 0));
        if (path.EndsWith("/movie/7401"))
            return Movie(7401, DreamDetective, "Dream Detective", "2006-11-25", "Released", Genre(16, "Animation") + ", " + Genre(878, "Science Fiction"),
                Director(81, "Kai Mori"), Cast(82, "Rin Sato", 0), language: "ja");
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
        if ((path.EndsWith("/discover/movie") || path.EndsWith("/discover/tv"))
            && (query["with_companies"] == PrismId.ToString(CultureInfo.InvariantCulture) || query["with_keywords"] == PrismId.ToString(CultureInfo.InvariantCulture)))
            return PrismPage(path.EndsWith("/discover/movie") ? "film" : "show", query);
        if (path.EndsWith("/discover/movie") && query["with_genres"] == "80" && query["vote_count.gte"] == "100" && query["without_genres"] is null)
            return PrismPage("film", query);
        if (path.EndsWith("/discover/movie"))
        {
            var genres = query["with_genres"];
            var keywords = query["with_keywords"];
            var people = query["with_people"];
            var from = query["primary_release_date.gte"];

            if (keywords == PagedKeyword.ToString(CultureInfo.InvariantCulture) || genres == "10749,35" || query["vote_count.gte"] == "5000")
                return PagedPage(int.Parse(query["page"] ?? "1", CultureInfo.InvariantCulture));
            if (genres == "12") return Results(Listed(22, "Pirates of the Caribbean", "2003-07-09", 7.8, 21000, "12"));
            if (query["with_companies"] is "68884|3172" or "555" or "557")
                return Results([.. Enumerable.Range(0, 12).Select(i => Listed(7500 + i, "Studio Night " + (i + 1), "2015-10-0" + (i % 9 + 1), 5.2, 400, "27"))]);
            if (query["with_companies"] == "556") return Results(Listed(7600, "Tiny One", "2015-01-01", 6.0, 90, "18"));
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

        // A series: Pirates of the Caribbean before Heat, as they are not in release order.
        if (path.EndsWith("/collection/" + Series))
            return "{ \"id\": " + Series + ", \"name\": \"Test Collection\", \"parts\": [ "
                + Listed(949, "Heat", "1995-12-15", 7.9, 8740, "80, 18") + ", " + Listed(22, "Pirates of the Caribbean", "2003-07-09", 7.8, 21000, "12") + " ] }";

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

    [GeneratedRegex(@"/(p\d+|heat-poster|pacino|pearl|jo|heat-logo|nflx|atv|prime|disney)\.(jpg|png)$")]
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

        if (path.EndsWith(".jpg") || path.EndsWith(".png"))
            return Task.FromResult(KnownImage().IsMatch(path)
                ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent([0xFF, 0xD8, 0xFF]) }
                : new HttpResponseMessage(HttpStatusCode.NotFound));

        var body = Answer(request, path);
        return Task.FromResult(body == ""
            ? new HttpResponseMessage(HttpStatusCode.NotFound)
            : new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") });
    }
}
