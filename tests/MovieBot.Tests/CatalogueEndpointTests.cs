using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using TheKrystalShip.MovieBot.Api.Auth;
using TheKrystalShip.MovieBot.Api.Library;
using TheKrystalShip.MovieBot.Api.People;
using TheKrystalShip.MovieBot.Catalogue;
using TheKrystalShip.MovieBot.Core;
using Xunit;

namespace TheKrystalShip.MovieBot.Tests;

/// <summary>
/// The catalogue's routes over the test host's catalogue (<see cref="SessionFixture.Catalogue"/>): the one
/// list query and its refusals, the facets, a title's page and a show's season, the words resolved to
/// filters, people, franchises, Discover, the asking person's joins, and the images served from the
/// catalogue's own files.
/// </summary>
public sealed class CatalogueEndpointTests(SessionFixture fixture) : IClassFixture<SessionFixture>
{
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    private HttpClient Signed(string userId = "cat-e1") => fixture.CreatePersonClient(userId, "Cat", [CinemaActions.Watch]);

    private async Task<T> Get<T>(string path, string userId = "cat-e1") => (await Signed(userId).GetFromJsonAsync<T>(path, Web))!;

    private static List<string> Ids(CardPage page) => [.. page.Titles.Select(t => t.Id)];

    [Fact]
    public async Task A_list_is_narrowed_by_its_filters_and_ordered_as_asked()
    {
        var shows = await Get<CardPage>("/api/catalogue/titles?kind=show");
        var films = await Get<CardPage>("/api/catalogue/titles?kind=film&sort=oldest");
        var set = await Get<CardPage>("/api/catalogue/titles?fact=P840:Q65");

        Assert.Equal([TestCatalogue.Chernobyl], Ids(shows));
        Assert.Equal(6, films.Total);
        Assert.Equal("tt0113277", films.Titles[0].Id); // Heat, 1995.
        Assert.Equal(SessionFixture.Quiet, films.Titles[^1].Id); // Out this year.
        Assert.Equal([TestCatalogue.Fast], Ids(set));
    }

    [Fact]
    public async Task A_list_is_served_a_page_at_a_time_and_says_which_is_next()
    {
        var first = await Get<CardPage>("/api/catalogue/titles?kind=film&sort=title&size=4");
        var second = await Get<CardPage>("/api/catalogue/titles?kind=film&sort=title&size=4&page=2");

        Assert.Equal((6, 2, 4), (first.Total, first.Next!.Value, first.Titles.Count));
        Assert.Equal(2, second.Titles.Count);
        Assert.Null(second.Next);
        Assert.Empty(Ids(first).Intersect(Ids(second)));
    }

    [Theory]
    [InlineData("/api/catalogue/titles?colour=red")]
    [InlineData("/api/catalogue/titles?kind=anime")]
    [InlineData("/api/catalogue/titles?unwatched=perhaps")]
    [InlineData("/api/catalogue/titles?here=everywhere")]
    [InlineData("/api/catalogue/facets?colour=red")]
    [InlineData("/api/catalogue/titles/fast")]
    [InlineData("/api/catalogue/terms")]
    [InlineData("/api/catalogue/ask")]
    public async Task A_parameter_or_a_value_a_route_does_not_take_is_refused(string path)
    {
        using var response = await Signed().GetAsync(path);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task The_catalogue_is_for_somebody_signed_in()
    {
        using var response = await fixture.CreateClient().GetAsync("/api/catalogue/titles");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Facets_count_each_section_without_its_own_pick()
    {
        var facets = await Get<Facets>("/api/catalogue/facets?kind=film");

        Assert.Equal(6, facets.Total);
        var kind = facets.Sections.Single(s => s.Id == "kind").Options;
        Assert.Equal([("film", 6), ("show", 1)], kind.Select(o => (o.Value, o.Count)));
        Assert.Equal(CatalogueQueries.FacetSections, facets.Sections.Select(s => s.Id));
    }

    [Theory]
    [InlineData("minVotes=400000")]
    [InlineData("maxVotes=400000")]
    [InlineData("minVotes=1&maxVotes=400000")]
    public async Task Facets_held_in_memory_count_a_range_of_votes_as_the_list_does(string votes)
    {
        var facets = await Get<Facets>($"/api/catalogue/facets?kind=film&{votes}");
        var list = await Get<CardPage>($"/api/catalogue/titles?kind=film&{votes}&size=50");

        Assert.Equal(list.Total, facets.Total);
        Assert.InRange(facets.Total, 1, 5); // Some films, not every film.
    }

    [Theory]
    [InlineData("Action")]
    [InlineData("Crime")]
    public async Task A_genre_lists_as_many_titles_as_its_facet_counts(string genre)
    {
        var facets = await Get<Facets>("/api/catalogue/facets?kind=film");
        var list = await Get<CardPage>($"/api/catalogue/titles?kind=film&anyGenre={genre}&size=50");

        Assert.Equal(facets.Sections.Single(s => s.Id == "genre").Options.Single(o => o.Value == genre).Count, list.Total);
        Assert.True(list.Total > 0);
    }

    [Fact]
    public async Task A_title_s_page_holds_what_the_catalogue_does_and_one_it_does_not_list_is_not_found()
    {
        var page = await Get<TitlePage>($"/api/catalogue/titles/{TestCatalogue.Fast}");
        using var missing = await Signed().GetAsync("/api/catalogue/titles/tt9999999");

        Assert.Equal(("The Fast and the Furious", "film"), (page.Title, page.Kind));
        Assert.Equal([TestCatalogue.TwoFast], page.FollowedBy.Select(t => t.Id));
        Assert.Equal("PG-13", page.Certificates["US"]);
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
    }

    [Fact]
    public async Task A_show_s_season_is_served_with_its_episodes()
    {
        var season = await Get<Season>($"/api/catalogue/titles/{TestCatalogue.Chernobyl}/seasons/1");
        using var missing = await Signed().GetAsync($"/api/catalogue/titles/{TestCatalogue.Chernobyl}/seasons/2");

        Assert.Equal(["1:23:45", "Please Remain Calm"], season.Episodes.Select(e => e.Name));
        Assert.Equal("/images/w300/e1.jpg", season.Episodes[0].Still);
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
    }

    [Fact]
    public async Task Words_resolve_to_the_filters_that_take_them_and_a_person_is_named_with_their_titles()
    {
        var hbo = await Get<List<Term>>("/api/catalogue/terms?q=hbo");
        var jared = await Get<List<Term>>("/api/catalogue/terms?q=jared");
        var person = jared.First(t => t.Filter == "person");

        var harris = await Get<Person>($"/api/catalogue/people/{person.Value}");
        using var nobody = await Signed().GetAsync("/api/catalogue/people/987654321");

        Assert.Contains(hbo, t => t is { Filter: "company", Value: "s49", Name: "HBO" });
        Assert.Equal(("Jared Harris", 1), (harris.Name, harris.Titles));
        Assert.Equal(HttpStatusCode.NotFound, nobody.StatusCode);
    }

    [Fact]
    public async Task A_question_is_answered_as_the_list_query_s_filters()
    {
        var answer = await Get<JsonElement>("/api/catalogue/ask?q=best%20thrillers%20from%20the%2090s");

        Assert.True(answer.GetProperty("question").GetBoolean());
        var query = answer.GetProperty("query");
        Assert.Equal(["Thriller"], query.GetProperty("genre").EnumerateArray().Select(v => v.GetString()));
        Assert.Equal("1990", query.GetProperty("from")[0].GetString());
        Assert.Equal("rating", query.GetProperty("sort")[0].GetString());

        var name = await Get<JsonElement>("/api/catalogue/ask?q=heat");
        Assert.False(name.GetProperty("question").GetBoolean());
    }

    [Fact]
    public async Task Franchises_are_listed_and_one_is_served_with_its_parts()
    {
        var heads = await Get<JsonElement>("/api/catalogue/franchises");
        var franchise = await Get<Franchise>("/api/catalogue/franchises/c9485");
        using var missing = await Signed().GetAsync("/api/catalogue/franchises/c1");

        Assert.Equal(JsonValueKind.Array, heads.GetProperty("franchises").ValueKind);
        Assert.True(heads.GetProperty("total").GetInt32() >= 0);
        Assert.Equal(("The Fast and the Furious Collection", 2), (franchise.Card.Name, franchise.Card.Titles));
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
    }

    [Fact]
    public async Task Discover_is_served_with_its_sections_and_rows()
    {
        var page = await Get<JsonElement>("/api/catalogue/discover");

        Assert.False(string.IsNullOrEmpty(page.GetProperty("spotlight").GetString()));
        Assert.Equal(JsonValueKind.Array, page.GetProperty("rows").ValueKind);
        Assert.Contains(page.GetProperty("sections").EnumerateArray(), s => s.GetProperty("id").GetString() == "moods");
        Assert.Equal(JsonValueKind.Array, page.GetProperty("genres").ValueKind);
    }

    [Fact]
    public async Task Every_card_is_joined_with_what_the_asking_person_has_of_it()
    {
        const string titleId = "fast-in-library-2001";
        fixture.AddTitle(titleId, new FilmIdentity { ImdbId = TestCatalogue.Fast, Name = "The Fast and the Furious", Year = 2001 });
        fixture.Services.GetRequiredService<LibraryListing>().Refresh();
        fixture.Services.GetRequiredService<WatchRecord>().Record("cat-you", titleId, SessionFixture.TitleDuration, SessionFixture.TitleDuration, DateTimeOffset.UtcNow);

        var held = await Get<CardPage>("/api/catalogue/titles?inLibrary=true", "cat-you");
        Assert.Contains("tt0113277", Ids(held)); // Heat, which the fixture's library holds.
        Assert.DoesNotContain(TestCatalogue.TwoFast, Ids(held));
        Assert.Equal(new Viewer(true, titleId, true, false), held.Titles.Single(t => t.Id == TestCatalogue.Fast).You);

        var page = await Get<TitlePage>($"/api/catalogue/titles/{TestCatalogue.Fast}", "cat-you");
        Assert.Equal(new Viewer(true, titleId, true, false), page.You);
        Assert.False(page.FollowedBy.Single().You!.InLibrary);

        Assert.DoesNotContain(TestCatalogue.Fast, Ids(await Get<CardPage>("/api/catalogue/titles?kind=film&unwatched=true", "cat-you")));
        Assert.DoesNotContain(TestCatalogue.Fast, Ids(await Get<CardPage>("/api/catalogue/titles?kind=film&inLibrary=false", "cat-you")));

        // Somebody else holds the same library and has watched nothing.
        var theirs = (await Get<CardPage>("/api/catalogue/titles?inLibrary=true", "cat-other")).Titles.Single(t => t.Id == TestCatalogue.Fast);
        Assert.Equal(new Viewer(true, titleId, false, false), theirs.You);
    }

    [Fact]
    public async Task An_image_is_served_from_the_catalogue_s_files_for_good()
    {
        using var served = await fixture.CreateClient().GetAsync($"/images/w500/{TestCatalogue.FastPoster}");
        using var head = await fixture.CreateClient().SendAsync(new HttpRequestMessage(HttpMethod.Head, $"/images/w500/{TestCatalogue.FastPoster}"));
        using var size = await fixture.CreateClient().GetAsync($"/images/original/{TestCatalogue.FastPoster}");
        using var name = await fixture.CreateClient().GetAsync("/images/w500/a..b.jpg");
        using var absent = await fixture.CreateClient().GetAsync("/images/w500/absent.jpg");

        Assert.Equal(HttpStatusCode.OK, served.StatusCode);
        Assert.Equal("image/jpeg", served.Content.Headers.ContentType!.MediaType);
        Assert.Equal(TestCatalogue.FastPoster, await served.Content.ReadAsStringAsync());
        Assert.Equal(TimeSpan.FromDays(365), served.Headers.CacheControl!.MaxAge);
        Assert.Contains(served.Headers.CacheControl.Extensions, e => e.Name == "immutable");
        Assert.Equal(HttpStatusCode.OK, head.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, size.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, name.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, absent.StatusCode);
    }

    [Fact]
    public async Task A_title_s_artwork_by_its_imdb_id_is_a_relative_redirect_to_its_image()
    {
        var client = fixture.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        using var poster = await client.GetAsync($"/images/titles/{TestCatalogue.Fast}/poster");
        using var backdrop = await client.GetAsync($"/images/titles/{TestCatalogue.Fast}/backdrop");
        using var still = await client.GetAsync($"/images/titles/{TestCatalogue.Chernobyl}/s01e02");
        using var unknown = await client.GetAsync("/images/titles/tt9999999/poster");
        using var noEpisode = await client.GetAsync($"/images/titles/{TestCatalogue.Chernobyl}/s01e09");

        Assert.Equal(HttpStatusCode.Redirect, poster.StatusCode);
        Assert.Equal($"../../w500/{TestCatalogue.FastPoster}", poster.Headers.Location!.OriginalString);
        Assert.Equal(TimeSpan.FromHours(1), poster.Headers.CacheControl!.MaxAge);
        Assert.Equal("../../w1280/backdrop.jpg", backdrop.Headers.Location!.OriginalString);
        Assert.Equal("../../w300/e2.jpg", still.Headers.Location!.OriginalString);
        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, noEpisode.StatusCode);

        // Followed from where it was asked, the redirect lands on the image itself.
        using var followed = await fixture.CreateClient().GetAsync($"/images/titles/{TestCatalogue.Fast}/poster");
        Assert.Equal(TestCatalogue.FastPoster, await followed.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task What_a_person_has_of_a_show_joins_the_catalogue_s_seasons_with_the_library()
    {
        fixture.AddEpisode("chernobyl-s01e02", TestCatalogue.Chernobyl, "Chernobyl", 1, 2);
        fixture.Services.GetRequiredService<LibraryListing>().Refresh();

        var show = await Get<JsonElement>($"/api/me/shows/{TestCatalogue.Chernobyl}");
        using var unknown = await Signed().GetAsync("/api/me/shows/tt9999999");
        using var film = await Signed().GetAsync($"/api/me/shows/{TestCatalogue.Fast}");
        using var malformed = await Signed().GetAsync("/api/me/shows/chernobyl");

        var episodes = show.GetProperty("seasons").EnumerateArray().Single().GetProperty("episodes").EnumerateArray().ToList();
        Assert.Equal(["1:23:45", "Please Remain Calm"], episodes.Select(e => e.GetProperty("name").GetString()));
        Assert.False(episodes[0].TryGetProperty("titleId", out _));
        Assert.Equal("chernobyl-s01e02", episodes[1].GetProperty("titleId").GetString());
        Assert.False(show.GetProperty("followable").GetBoolean()); // Chernobyl has ended.
        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, film.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, malformed.StatusCode);
    }
}
