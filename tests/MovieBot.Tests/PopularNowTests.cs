using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using TheKrystalShip.MovieBot.Api.Films;
using Xunit;

namespace TheKrystalShip.MovieBot.Tests;

/// <summary>
/// The week's trending films and shows, asked of the film database and kept with their posters, so the
/// page that shows them never waits on it.
/// </summary>
public sealed class PopularNowTests(SessionFixture fixture) : IClassFixture<SessionFixture>
{
    private HttpClient Signed()
    {
        var client = fixture.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", fixture.TokenFor("u7", "Gil"));
        return client;
    }

    private Task Pass() => fixture.Services.GetRequiredService<PopularNow>().PassAsync(CancellationToken.None);

    [Fact]
    public async Task What_is_trending_is_served_with_its_poster_at_this_server_s_address()
    {
        await Pass();

        var popular = await Signed().GetFromJsonAsync<PopularNowView>("/api/popular");

        var heat = Assert.Single(popular!.Films);
        Assert.Equal(("tt0113277", "Heat", (int?)1995, (double?)7.9), (heat.ImdbId, heat.Title, heat.Year, heat.Rating));
        Assert.Equal("/popular/tt0113277.jpg", heat.PosterUrl);
        Assert.Empty(popular.Shows);
        Assert.NotNull(popular.FetchedAt);
    }

    [Fact]
    public async Task What_is_kept_is_served_without_asking_again()
    {
        await Pass();
        var asked = fixture.FilmDatabase.Asked;

        await Signed().GetFromJsonAsync<PopularNowView>("/api/popular");

        Assert.Equal(asked, fixture.FilmDatabase.Asked);
    }

    [Fact]
    public async Task The_list_needs_a_token_and_the_poster_does_not()
    {
        await Pass();

        using var list = await fixture.CreateClient().GetAsync("/api/popular");
        using var poster = await fixture.CreateClient().GetAsync("/popular/tt0113277.jpg");

        Assert.Equal(HttpStatusCode.Unauthorized, list.StatusCode);
        Assert.Equal(HttpStatusCode.OK, poster.StatusCode);
        Assert.Equal("image/jpeg", poster.Content.Headers.ContentType?.MediaType);
    }

    [Theory]
    [InlineData("/popular/popular.json")]
    [InlineData("/popular/tt0113277.jpg.src")]
    [InlineData("/popular/heat.jpg")]
    public async Task Nothing_but_the_posters_is_served_from_there(string path)
    {
        await Pass();

        using var response = await fixture.CreateClient().GetAsync(path);

        Assert.NotEqual(HttpStatusCode.OK, response.StatusCode);
    }
}
