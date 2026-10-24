using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using TheKrystalShip.MovieBot.Api.Films;
using TheKrystalShip.MovieBot.Api.Shows;
using Xunit;

namespace TheKrystalShip.MovieBot.Tests;

/// <summary>
/// What a description carries beyond its facts: official trailers, recommendations, reviews, the logo,
/// box office and keywords. The film database is answered by the test, Heat's description with everything.
/// </summary>
public sealed class TitleExtrasTests(SessionFixture fixture) : IClassFixture<SessionFixture>
{
    private HttpClient Signed(string userId = "s1")
    {
        var client = fixture.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", fixture.TokenFor(userId, "Gil"));
        return client;
    }

    private Task<T?> Get<T>(string path) => Signed().GetFromJsonAsync<T>(path);

    private async Task<HttpResponseMessage> Open(string path) => await fixture.CreateClient().GetAsync(path);

    [Fact]
    public async Task A_film_lists_its_official_trailers_and_teasers_and_leaves_out_the_rest()
    {
        var film = await Get<FilmView>("/api/films/tt0113277");

        Assert.Equal(["heat-trailer", "heat-teaser"], film!.Trailers.Select(t => t.Key));
        Assert.Equal(("Official Trailer", "Trailer", "en"), (film.Trailers[0].Name, film.Trailers[0].Kind, film.Trailers[0].Language));
    }

    [Fact]
    public async Task A_film_carries_its_tagline_box_office_keywords_with_ids_and_reviews()
    {
        var film = await Get<FilmView>("/api/films/tt0113277");

        Assert.Equal("A Los Angeles crime saga", film!.Tagline);
        Assert.Equal((60000000L, 187436818L), (film.Budget, film.Revenue));
        Assert.Equal([(818, "heist"), (9, "Los Angeles")], film.Keywords.Select(k => (k.Id, k.Name)));
        Assert.Equal(2, film.ReviewCount);
        var review = Assert.Single(film.Reviews);
        Assert.Equal(("Critic", 9.0, "A masterpiece of the genre.", "https://reviews.example/1"), (review.Author, review.Rating, review.Excerpt, review.Url));
    }

    [Fact]
    public async Task A_film_s_logo_is_kept_and_served_from_here_as_a_png()
    {
        var film = await Get<FilmView>("/api/films/tt0113277");

        Assert.Equal("/films/tt0113277/logo.png", film!.LogoUrl);
        using var logo = await Open(film.LogoUrl!);
        Assert.Equal(HttpStatusCode.OK, logo.StatusCode);
        Assert.Equal("image/png", logo.Content.Headers.ContentType?.MediaType);
        using var poster = await Open(film.PosterUrl!);
        Assert.Equal("image/jpeg", poster.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task Recommendations_are_title_cards_joined_with_the_library_like_any_list_of_titles()
    {
        var film = await Get<FilmView>("/api/films/tt0113277");

        Assert.Equal(["Echo Valley", "Again and Again"], film!.Recommendations.Select(t => t.Title));
        var echo = film.Recommendations[0];
        Assert.Equal(("tt9000201", "film", (int?)2019), (echo.ImdbId, echo.Kind, echo.Year));
        Assert.NotNull(echo.PosterUrl);
        Assert.False(echo.InLibrary);
        Assert.Equal(["Una Fox"], echo.MadeBy);
    }

    [Fact]
    public async Task One_description_call_carries_it_all()
    {
        var before = fixture.FilmDatabase.Requests.Count;
        await Get<FilmView>("/api/films/tt0113277");
        await Get<FilmView>("/api/films/tt0113277");

        // A title card (credits alone) is another, smaller question.
        static bool Description(string r) => r.StartsWith("/3/movie/949?") && !r.Contains("append_to_response=credits&");
        Assert.True(fixture.FilmDatabase.Requests.Skip(before).Count(Description) <= 1);
        var all = fixture.FilmDatabase.Requests.Where(Description).ToList();
        Assert.NotEmpty(all);
        Assert.All(all, r => Assert.Contains("append_to_response=credits,release_dates,keywords,videos,recommendations,reviews,images", r));
        Assert.DoesNotContain(fixture.FilmDatabase.Requests, r => r.Contains("/movie/949/videos") || r.Contains("/movie/949/reviews"));
    }
}
