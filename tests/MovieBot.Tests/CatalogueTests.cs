using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using TheKrystalShip.MovieBot.Api.Films;
using Xunit;

namespace TheKrystalShip.MovieBot.Tests;

/// <summary>
/// The catalogue browsed by a person and by a genre: asked of the film database once, kept, and each
/// title's poster and the person's portrait served from this server.
/// </summary>
public sealed class CatalogueTests(SessionFixture fixture) : IClassFixture<SessionFixture>
{
    private HttpClient Signed()
    {
        var client = fixture.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", fixture.TokenFor("u7", "Gil"));
        return client;
    }

    [Fact]
    public async Task A_person_s_work_is_listed_with_their_part_and_posters_served_from_here()
    {
        var person = await Signed().GetFromJsonAsync<CataloguePersonView>("/api/catalogue/people/1158");

        Assert.Equal((1158, "Al Pacino", "Acting"), (person!.PersonId, person.Name, person.KnownFor));
        Assert.Equal("/catalogue/people/1158.jpg", person.PortraitUrl);
        var heat = Assert.Single(person.Titles);
        Assert.Equal(("tt0113277", "film", "Heat", (int?)1995, "Vincent Hanna"), (heat.ImdbId, heat.Kind, heat.Title, heat.Year, heat.Role));
        Assert.NotNull(heat.PosterUrl);

        using var portrait = await fixture.CreateClient().GetAsync(person.PortraitUrl);
        Assert.Equal(HttpStatusCode.OK, portrait.StatusCode);
        using var poster = await fixture.CreateClient().GetAsync(heat.PosterUrl);
        Assert.Equal(HttpStatusCode.OK, poster.StatusCode);
    }

    [Fact]
    public async Task A_person_kept_is_served_without_asking_again()
    {
        await Signed().GetFromJsonAsync<CataloguePersonView>("/api/catalogue/people/1158");
        var asked = fixture.FilmDatabase.Asked;

        await Signed().GetFromJsonAsync<CataloguePersonView>("/api/catalogue/people/1158");

        Assert.Equal(asked, fixture.FilmDatabase.Asked);
    }

    [Fact]
    public async Task Nobody_under_the_id_is_not_found()
    {
        using var response = await Signed().GetAsync("/api/catalogue/people/999999");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task A_genre_is_named_in_any_case_and_answered_as_the_database_spells_it()
    {
        var genre = await Signed().GetFromJsonAsync<CatalogueGenreView>("/api/catalogue/genre?kind=film&name=adventure");

        Assert.Equal(("film", "Adventure"), (genre!.Kind, genre.Genre));
        var pirates = Assert.Single(genre.Titles);
        Assert.Equal(("tt0325980", "Pirates of the Caribbean", (double?)7.8), (pirates.ImdbId, pirates.Title, pirates.Rating));
        Assert.Equal("/glances/tt0325980/poster.jpg", pirates.PosterUrl);

        using var poster = await fixture.CreateClient().GetAsync(pirates.PosterUrl);
        Assert.Equal(HttpStatusCode.OK, poster.StatusCode);
    }

    [Theory]
    [InlineData("/api/catalogue/genre?kind=film&name=Westerns", HttpStatusCode.NotFound)]
    [InlineData("/api/catalogue/genre?kind=episode&name=Crime", HttpStatusCode.BadRequest)]
    [InlineData("/api/catalogue/genre?kind=film", HttpStatusCode.BadRequest)]
    public async Task A_genre_that_is_not_one_is_refused(string path, HttpStatusCode status)
    {
        using var response = await Signed().GetAsync(path);

        Assert.Equal(status, response.StatusCode);
        Assert.Contains("error", await response.Content.ReadAsStringAsync());
    }

    [Theory]
    [InlineData("/api/catalogue/people/1158")]
    [InlineData("/api/catalogue/genre?kind=film&name=Crime")]
    public async Task Browsing_needs_a_token(string path)
    {
        using var response = await fixture.CreateClient().GetAsync(path);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [InlineData("/catalogue/people/1158.json")]
    [InlineData("/catalogue/people/01158.jpg")]
    [InlineData("/catalogue/people/1158.jpg.src")]
    public async Task Nothing_but_portraits_is_served_from_there(string path)
    {
        await Signed().GetFromJsonAsync<CataloguePersonView>("/api/catalogue/people/1158");

        using var response = await fixture.CreateClient().GetAsync(path);

        Assert.NotEqual(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task A_film_s_cast_and_director_carry_their_person_id()
    {
        var film = await Signed().GetFromJsonAsync<FilmView>("/api/films/tt0113277");

        Assert.Equal(1158, film!.Cast[0].PersonId);
        Assert.Equal(638, film.PersonIds["Michael Mann"]);
    }
}
