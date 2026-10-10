using System.Net;
using System.Net.Http.Json;
using TheKrystalShip.MovieBot.Api.Auth;
using TheKrystalShip.MovieBot.Api.Films;
using TheKrystalShip.MovieBot.Api.Library;
using Xunit;

namespace TheKrystalShip.MovieBot.Tests;

/// <summary>
/// Which shelf a title is on beyond its kind: anime when the film database files it under Animation
/// and it was made in Japanese, else general, unless somebody who fetches titles shelved it otherwise.
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
    public async Task Somebody_who_fetches_titles_shelves_one_against_the_rule_and_back()
    {
        var client = fixture.CreatePersonClient("cat-2", "Curator", [CinemaActions.Watch, CinemaActions.Download]);
        await client.GetFromJsonAsync<FilmView>("/api/films/" + FilmDatabaseStub.DreamDetective);

        using var moved = await client.PutAsJsonAsync("/api/categories/" + FilmDatabaseStub.DreamDetective, new { kind = "film", category = "general" });
        var set = await moved.Content.ReadFromJsonAsync<CategoryView>();
        var after = await client.GetFromJsonAsync<FilmView>("/api/films/" + FilmDatabaseStub.DreamDetective);

        Assert.Equal(HttpStatusCode.OK, moved.StatusCode);
        Assert.Equal(new CategoryView(FilmDatabaseStub.DreamDetective, TitleCategory.General, true), set);
        Assert.Equal(TitleCategory.General, after!.Category);

        using var back = await client.PutAsJsonAsync("/api/categories/" + FilmDatabaseStub.DreamDetective, new { kind = "film", category = "anime" });
        var agreed = await back.Content.ReadFromJsonAsync<CategoryView>();

        // Agreeing with the rule keeps nothing, so the title follows its description again.
        Assert.False(agreed!.Set);
        Assert.Equal(TitleCategory.Anime, (await client.GetFromJsonAsync<FilmView>("/api/films/" + FilmDatabaseStub.DreamDetective))!.Category);
    }

    [Fact]
    public async Task Somebody_who_only_watches_cannot_move_a_title()
    {
        var client = fixture.CreatePersonClient("cat-3", "Viewer", [CinemaActions.Watch]);

        using var response = await client.PutAsJsonAsync("/api/categories/" + FilmDatabaseStub.DreamDetective, new { kind = "film", category = "general" });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Theory]
    [InlineData("heat", "film", "general")]
    [InlineData("tt0113277", "episode", "general")]
    [InlineData("tt0113277", "film", "cartoon")]
    public async Task A_setting_names_an_imdb_id_a_kind_and_a_category(string imdbId, string kind, string category)
    {
        var client = fixture.CreatePersonClient("cat-4", "Curator", [CinemaActions.Download]);

        using var response = await client.PutAsJsonAsync("/api/categories/" + imdbId, new { kind, category });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
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
