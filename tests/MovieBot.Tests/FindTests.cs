using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using TheKrystalShip.MovieBot.Api.Auth;
using Xunit;

namespace TheKrystalShip.MovieBot.Tests;

/// <summary>What a search box is offered for a name: films and shows, each with its poster and cast.</summary>
public sealed class FindTests(SessionFixture fixture) : IClassFixture<SessionFixture>
{
    [Fact]
    public async Task Films_and_shows_come_together_in_the_index_s_order_with_poster_and_cast()
    {
        var client = fixture.CreatePersonClient("acc-f1", "Finder", [CinemaActions.Download]);

        var found = await client.GetFromJsonAsync<JsonElement>("/api/titles/find?q=pirates");

        var first = found[0];
        Assert.Equal("tt0325980", first.GetProperty("imdbId").GetString());
        Assert.Equal("film", first.GetProperty("kind").GetString());
        Assert.Equal("Johnny Depp, Geoffrey Rush", first.GetProperty("starring").GetString());

        var second = found[1];
        Assert.Equal("tt9900001", second.GetProperty("imdbId").GetString());
        Assert.Equal("show", second.GetProperty("kind").GetString());
        Assert.False(second.TryGetProperty("posterUrl", out _));

        // The film database's poster, which the title's page shows, is preferred to the index's.
        Assert.Equal("/glances/tt0325980/poster.jpg", first.GetProperty("posterUrl").GetString());
        using var poster = await fixture.CreateClient().GetAsync("/glances/tt0325980/poster.jpg");
        Assert.Equal(HttpStatusCode.OK, poster.StatusCode);
        Assert.Equal("https://m.media-amazon.com/images/M/tales._V1_QL75_UX500_.jpg", found[2].GetProperty("posterUrl").GetString());

        Assert.DoesNotContain(found.EnumerateArray(), t => t.GetProperty("imdbId").GetString()!.StartsWith("nm", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_result_carries_the_film_database_s_rating_and_overview_and_is_asked_about_once()
    {
        var client = fixture.CreatePersonClient("acc-f3", "Finder", [CinemaActions.Download]);

        var first = await client.GetFromJsonAsync<JsonElement>("/api/films/find?q=pirates");
        var asked = fixture.FilmDatabase.Asked;
        var again = await client.GetFromJsonAsync<JsonElement>("/api/films/find?q=Pirates");

        var pearl = first[0];
        Assert.Equal(7.8, pearl.GetProperty("rating").GetDouble());
        Assert.Equal("A blacksmith and a pirate.", pearl.GetProperty("overview").GetString());
        Assert.False(first[1].TryGetProperty("rating", out _));
        Assert.Equal(7.8, again[0].GetProperty("rating").GetDouble());
        Assert.Equal(asked, fixture.FilmDatabase.Asked);
    }

    [Fact]
    public async Task Films_alone_are_found_by_somebody_who_may_only_wish()
    {
        var client = fixture.CreatePersonClient("acc-f2", "Wisher", [CinemaActions.Wish]);

        using var both = await client.GetAsync("/api/titles/find?q=pirates");
        var films = await client.GetFromJsonAsync<JsonElement>("/api/films/find?q=pirates");

        Assert.Equal(HttpStatusCode.Forbidden, both.StatusCode);
        Assert.All(films.EnumerateArray(), f => Assert.Equal("film", f.GetProperty("kind").GetString()));
        Assert.Equal(5, films.GetArrayLength());
    }
}
