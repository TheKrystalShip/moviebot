using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using TheKrystalShip.MovieBot.Api.Films;
using Xunit;
using static TheKrystalShip.MovieBot.Tests.FilmDatabaseStub;

namespace TheKrystalShip.MovieBot.Tests;

/// <summary>
/// A shelf that is a question of the film database, held whole as the warmer holds it: a filter's
/// picks, its orders and the options each section holds are answered over the titles held, from their
/// cards, and the database is asked no discovery and about no title. The shelf is <see cref="PrismShelf"/>, which the test
/// database answers with the Prism world's films.
/// </summary>
public sealed class WholeShelfTests(SessionFixture fixture) : IClassFixture<SessionFixture>
{
    private HttpClient Signed()
    {
        var client = fixture.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", fixture.TokenFor("u31", "Wren"));
        return client;
    }

    private async Task<T> Get<T>(string path) => (await Signed().GetFromJsonAsync<T>(path))!;

    private async Task Held() => Assert.True(await fixture.Services.GetRequiredService<DiscoverShelves>().HoldWholeAsync(PrismShelf, CancellationToken.None));

    private static List<string> Options(CatalogueFacetsView facets, string section) =>
        [.. facets.Sections.Single(s => s.Id == section).Options.Where(o => o.Empty != true).Select(o => o.Value)];

    private static List<string> Offered(CatalogueFacetsView facets, string section) =>
        [.. facets.Sections.Single(s => s.Id == section).Options.Select(o => o.Value)];

    [Fact]
    public async Task Picks_on_a_shelf_held_whole_ask_the_database_nothing()
    {
        await Held();
        var before = fixture.FilmDatabase.Requests.Count;

        var shelf = await Get<ShelfView>($"/api/discover/shelves/{PrismShelf}?length=short&language=en");
        Assert.Equal(Prism.Where(t => t.Kind == "film" && t.Runtime < 90 && t.Language == "en").OrderByDescending(t => t.Popularity).Select(t => t.Title),
            shelf.Titles.Select(t => t.Title));

        var facets = await Get<CatalogueFacetsView>($"/api/discover/shelves/{PrismShelf}/facets?length=short");
        Assert.DoesNotContain(facets.Sections, s => s.Id == FacetSections.Kind);
        Assert.Equal(["short", "standard", "long"], Options(facets, FacetSections.Length));
        Assert.Equal(["en"], Options(facets, FacetSections.Language));
        Assert.Equal(["en", "fr", "ja", "ko"], Offered(facets, FacetSections.Language));
        var horror = facets.Sections.Single(s => s.Id == FacetSections.Genre).Options.Single(o => o.Value == "Horror");
        Assert.Equal(Prism.Count(t => t.Kind == "film" && t.Runtime < 90 && t.Genres.Contains(27)), horror.Count);

        Assert.Equal([], fixture.FilmDatabase.Requests.Skip(before).Where(r => r.StartsWith("/3/discover") || r.StartsWith("/3/movie")));
    }

    [Fact]
    public async Task A_shelf_held_whole_is_ordered_after_it_is_narrowed()
    {
        await Held();
        var newest = await Get<ShelfView>($"/api/discover/shelves/{PrismShelf}?anyGenre=Crime&sort=newest");
        var years = newest.Titles.Select(t => t.Year ?? 0).ToList();
        Assert.Equal(Prism.Count(t => t.Kind == "film" && t.Genres.Contains(80)), years.Count);
        Assert.Equal(years.OrderDescending(), years);

        var lowest = await Get<ShelfView>($"/api/discover/shelves/{PrismShelf}?decade=2010&sort=lowest");
        Assert.All(lowest.Titles, t => Assert.InRange(t.Year ?? 0, 2010, 2019));
    }

    [Fact]
    public async Task A_shelf_held_whole_refuses_what_the_database_would()
    {
        await Held();
        Assert.Equal(HttpStatusCode.BadRequest, (await Signed().GetAsync($"/api/discover/shelves/{PrismShelf}?kind=film")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Signed().GetAsync($"/api/discover/shelves/{PrismShelf}?length=epic")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Signed().GetAsync($"/api/discover/shelves/{PrismShelf}/facets?anyGenre=Nonsense")).StatusCode);
    }
}
