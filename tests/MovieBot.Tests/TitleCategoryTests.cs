using System.Net;
using System.Net.Http.Json;
using TheKrystalShip.MovieBot.Api.Auth;
using TheKrystalShip.MovieBot.Api.Films;
using TheKrystalShip.MovieBot.Api.Library;
using Xunit;

namespace TheKrystalShip.MovieBot.Tests;

/// <summary>
/// Which shelf a title is on beyond its kind: anime when the film database files it under Animation
/// and it was made in Japanese, else general.
/// </summary>
public sealed class TitleCategoryTests(SessionFixture fixture) : IClassFixture<SessionFixture>
{
    [Theory]
    [InlineData("Animation,Science Fiction", "ja", TitleCategory.Anime)]
    [InlineData("animation", "JA", TitleCategory.Anime)]
    [InlineData("Animation,Comedy", "en", TitleCategory.General)]
    [InlineData("Science Fiction,Action", "ja", TitleCategory.General)]
    [InlineData("Drama", null, TitleCategory.General)]
    public void Anime_is_animated_and_made_in_japanese(string genres, string? language, TitleCategory expected) =>
        Assert.Equal(expected, TitleCategories.ByRule(genres.Split(','), language));

    [Fact]
    public void A_title_with_no_genres_known_has_no_category() =>
        Assert.Null(TitleCategories.ByRule([], "ja"));

    [Fact]
    public async Task A_film_page_carries_its_category()
    {
        var client = fixture.CreatePersonClient("cat-1", "Cat", [CinemaActions.Watch]);

        var anime = await client.GetFromJsonAsync<FilmView>("/api/films/" + FilmDatabaseStub.DreamDetective);
        var heat = await client.GetFromJsonAsync<FilmView>("/api/films/tt0113277");

        Assert.Equal(TitleCategory.Anime, anime!.Category);
        Assert.Equal(TitleCategory.General, heat!.Category);
    }

    [Fact]
    public async Task A_list_of_the_film_database_s_is_narrowed_to_anime_alone()
    {
        var client = fixture.CreatePersonClient("cat-5", "Cat", [CinemaActions.Watch]);

        using var general = await client.GetAsync("/api/catalogue/released?category=general");
        using var unknown = await client.GetAsync("/api/catalogue/genre?kind=film&name=Drama&category=cartoon");

        Assert.Equal(HttpStatusCode.BadRequest, general.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, unknown.StatusCode);
    }
}
