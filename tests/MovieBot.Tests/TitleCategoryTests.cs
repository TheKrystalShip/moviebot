using System.Net;
using System.Net.Http.Json;
using TheKrystalShip.MovieBot.Api.Auth;
using TheKrystalShip.MovieBot.Api.Library;
using TheKrystalShip.MovieBot.Catalogue;
using Xunit;

namespace TheKrystalShip.MovieBot.Tests;

/// <summary>
/// Which shelf a title is on beyond its kind: the catalogue's category, anime for a title it files under
/// Animation made in Japanese, else general; a title the catalogue does not list has none.
/// </summary>
public sealed class TitleCategoryTests(SessionFixture fixture) : IClassFixture<SessionFixture>
{
    private static TitleCard Card(string category) => new() { Id = "tt0000001", Kind = "film", Title = "A Title", Category = category };

    [Theory]
    [InlineData("anime", TitleCategory.Anime)]
    [InlineData("general", TitleCategory.General)]
    public void A_title_s_category_is_its_card_s(string category, TitleCategory expected) =>
        Assert.Equal(expected, TitleCategories.Of(Card(category)));

    [Fact]
    public void A_title_with_no_card_has_no_category() =>
        Assert.Null(TitleCategories.Of(null));

    [Theory]
    [InlineData("anime", TitleCategory.Anime)]
    [InlineData(" General ", TitleCategory.General)]
    [InlineData("cartoon", null)]
    [InlineData(null, null)]
    public void A_category_is_read_as_the_wire_spells_it(string? text, TitleCategory? expected) =>
        Assert.Equal(expected, TitleCategories.Parse(text));

    [Fact]
    public async Task A_title_page_carries_its_category()
    {
        var client = fixture.CreatePersonClient("cat-1", "Cat", [CinemaActions.Watch]);

        var page = await client.GetFromJsonAsync<TitlePage>("/api/catalogue/titles/" + TestCatalogue.Fast);

        Assert.Equal("general", page!.Category);
    }

    [Fact]
    public async Task A_list_is_narrowed_by_category_and_refuses_one_there_is_not()
    {
        var client = fixture.CreatePersonClient("cat-5", "Cat", [CinemaActions.Watch]);

        var anime = await client.GetFromJsonAsync<CardPage>("/api/catalogue/titles?category=anime");
        var general = await client.GetFromJsonAsync<CardPage>("/api/catalogue/titles?category=general");
        using var unknown = await client.GetAsync("/api/catalogue/titles?category=cartoon");

        Assert.Empty(anime!.Titles);
        Assert.Equal(7, general!.Total);
        Assert.Equal(HttpStatusCode.BadRequest, unknown.StatusCode);
    }
}
