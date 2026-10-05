using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using TheKrystalShip.MovieBot.Api.Films;
using TheKrystalShip.MovieBot.Api.Library;
using Xunit;

namespace TheKrystalShip.MovieBot.Tests;

/// <summary>
/// A film described by the film database, asked once, kept, and served from here with its artwork,
/// so a page never reaches the database itself.
/// </summary>
public sealed class FilmsTests(SessionFixture fixture) : IClassFixture<SessionFixture>
{
    private HttpClient Signed()
    {
        var client = fixture.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", fixture.TokenFor("u7", "Gil"));
        return client;
    }

    [Fact]
    public async Task A_film_is_described_with_artwork_at_this_server_s_addresses()
    {
        var film = await Signed().GetFromJsonAsync<FilmView>("/api/films/tt0113277");

        Assert.Equal("Heat", film!.Title);
        Assert.Equal("R", film.Certification);
        Assert.Equal(["Crime", "Drama"], film.Genres);
        Assert.Equal(["Michael Mann"], film.Directors);
        Assert.Equal("/films/tt0113277/poster.jpg", film.PosterUrl);
        Assert.Null(film.BackdropUrl);
        Assert.Equal("/films/tt0113277/cast-0.jpg", film.Cast[0].PortraitUrl);
        Assert.Null(film.Cast[1].PortraitUrl);
    }

    [Fact]
    public async Task What_is_kept_is_served_without_asking_again()
    {
        await Signed().GetFromJsonAsync<FilmView>("/api/films/tt0113277");
        var asked = fixture.FilmDatabase.Asked;

        await Signed().GetFromJsonAsync<FilmView>("/api/films/tt0113277");

        Assert.Equal(asked, fixture.FilmDatabase.Asked);
    }

    [Fact]
    public async Task Artwork_needs_no_token_because_an_img_carries_none()
    {
        await Signed().GetFromJsonAsync<FilmView>("/api/films/tt0113277");

        using var response = await fixture.CreateClient().GetAsync("/films/tt0113277/poster.jpg");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("image/jpeg", response.Content.Headers.ContentType?.MediaType);
    }

    [Theory]
    [InlineData("/films/tt0113277/film.json")]
    [InlineData("/films/tt0113277/poster.jpg.src")]
    [InlineData("/films/../poster.jpg")]
    [InlineData("/films/heat/poster.jpg")]
    public async Task Nothing_but_the_artwork_is_served_from_there(string path)
    {
        await Signed().GetFromJsonAsync<FilmView>("/api/films/tt0113277");

        using var response = await fixture.CreateClient().GetAsync(path);

        Assert.NotEqual(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task An_id_the_database_does_not_hold_is_a_404()
    {
        using var response = await Signed().GetAsync("/api/films/tt0000001");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task A_listing_carries_what_is_kept_about_each_film()
    {
        await Signed().GetFromJsonAsync<FilmView>("/api/films/tt0113277");

        var titles = await Signed().GetFromJsonAsync<List<TitleSummary>>("/api/titles",
            new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web)
            {
                Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() },
            });
        var heat = titles!.Single(t => t.Id == SessionFixture.Heat);

        Assert.Equal(7.9, heat.Details?.Rating);
        Assert.Equal("R", heat.Details?.Certification);
        Assert.True(heat.SizeBytes > 0);
        Assert.NotNull(heat.AddedAt);
        // A film nobody has described is listed without it.
        Assert.Null(titles!.Single(t => t.Id == SessionFixture.ReadyTitle).Details);
    }
}
