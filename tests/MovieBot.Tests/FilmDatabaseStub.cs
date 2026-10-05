using System.Net;
using System.Text;

namespace TheKrystalShip.MovieBot.Tests;

/// <summary>
/// The film database for the tests: it knows Heat, has a poster and one portrait for it, and holds
/// nothing else. Every request is counted, so a test can show what was asked once and served after.
/// </summary>
public sealed class FilmDatabaseStub : HttpMessageHandler
{
    private int _asked;

    public int Asked => _asked;

    private const string Heat = """
        {
          "id": 949, "imdb_id": "tt0113277", "title": "Heat", "original_language": "en",
          "overview": "A thief and a detective.", "release_date": "1995-12-15", "runtime": 170,
          "vote_average": 7.9, "vote_count": 8740, "poster_path": "/heat-poster.jpg", "backdrop_path": null,
          "genres": [ { "name": "Crime" }, { "name": "Drama" } ],
          "spoken_languages": [ { "english_name": "English" } ],
          "production_countries": [ { "name": "United States of America" } ],
          "credits": {
            "cast": [ { "name": "Al Pacino", "character": "Vincent Hanna", "order": 0, "profile_path": "/pacino.jpg" },
                      { "name": "Robert De Niro", "character": "Neil McCauley", "order": 1, "profile_path": null } ],
            "crew": [ { "name": "Michael Mann", "job": "Director", "department": "Directing" } ]
          },
          "release_dates": { "results": [ { "iso_3166_1": "US", "release_dates": [ { "certification": "R", "type": 3 } ] } ] }
        }
        """;

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        Interlocked.Increment(ref _asked);
        var path = request.RequestUri!.AbsolutePath;

        if (path.EndsWith("/find/tt0113277")) return Json("""{ "movie_results": [ { "id": 949 } ] }""");
        if (path.EndsWith("/find/tt0325980"))
            return Json("""{ "movie_results": [ { "id": 22, "overview": "A blacksmith and a pirate.", "vote_average": 7.81, "vote_count": 21000, "release_date": "2003-07-09", "poster_path": "/pearl.jpg" } ] }""");
        if (path.Contains("/find/")) return Json("""{ "movie_results": [] }""");
        if (path.EndsWith("/movie/949")) return Json(Heat);
        if (path.EndsWith("/trending/movie/week"))
            return Json("""{ "results": [ { "id": 949, "title": "Heat", "release_date": "1995-12-15", "vote_average": 7.9, "vote_count": 8740, "poster_path": "/heat-poster.jpg" } ] }""");
        if (path.EndsWith("/trending/tv/week")) return Json("""{ "results": [] }""");
        if (path.EndsWith("/movie/949/external_ids")) return Json("""{ "imdb_id": "tt0113277" }""");
        if (path.EndsWith("/heat-poster.jpg") || path.EndsWith("/pacino.jpg") || path.EndsWith("/pearl.jpg"))
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent([0xFF, 0xD8, 0xFF]) });
        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
    }

    private static Task<HttpResponseMessage> Json(string body) =>
        Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") });
}
