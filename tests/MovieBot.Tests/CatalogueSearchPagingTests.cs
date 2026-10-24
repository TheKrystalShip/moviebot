using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using TheKrystalShip.MovieBot.Acquire.Tmdb;
using TheKrystalShip.MovieBot.Api.Films;
using Xunit;

namespace TheKrystalShip.MovieBot.Tests;

/// <summary>
/// Discoveries, questions and shelves that go on past their first page: each later page asked of the
/// film database when it is wanted and kept, the rating sort paged over one ranked list, a question's
/// count bounding the pages, and a shelf that is whole in one answer ending on its first.
/// </summary>
public sealed class CatalogueSearchPagingTests(SessionFixture fixture) : IClassFixture<SessionFixture>
{
    /// <summary>The paged subject, told apart from the other tests' by a runtime floor, so what each asks the database is its own.</summary>
    private static string Paged(int marker) => $"/api/catalogue/discover?about=paged&goodOnly=false&minRuntime={marker}";

    private HttpClient Signed()
    {
        var client = fixture.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", fixture.TokenFor("u9", "Gil"));
        return client;
    }

    private async Task<T> Get<T>(string path) => (await Signed().GetFromJsonAsync<T>(path))!;

    private List<string> Discoveries(int marker) =>
        [.. fixture.FilmDatabase.Requests.Where(r => r.StartsWith("/3/discover/movie") && r.Contains($"with_runtime.gte={marker}&"))];

    private static bool DateNightPage3(string request) =>
        request.StartsWith("/3/discover/movie") && request.Contains("with_genres=10749,35") && request.Contains("page=3");

    [Fact]
    public async Task A_later_page_asks_the_database_for_its_own_page_and_is_kept_like_the_first()
    {
        var first = await Get<CatalogueDiscoverView>(Paged(90));
        Assert.Equal(20, first.Titles.Count);
        Assert.Equal(2, first.Next);
        Assert.Single(Discoveries(90));
        Assert.Contains("page=1", Discoveries(90)[0]);

        var second = await Get<CatalogueDiscoverView>(Paged(90) + "&page=2");
        Assert.Equal(20, second.Titles.Count);
        Assert.Equal(3, second.Next);
        Assert.Equal(2, Discoveries(90).Count);
        Assert.Contains("page=2", Discoveries(90)[1]);
        Assert.Empty(first.Titles.Select(t => t.TmdbId).Intersect(second.Titles.Select(t => t.TmdbId)));

        var asked = fixture.FilmDatabase.Asked;
        var again = await Get<CatalogueDiscoverView>(Paged(90) + "&page=2");
        Assert.Equal(second.Titles.Select(t => t.TmdbId), again.Titles.Select(t => t.TmdbId));
        Assert.Equal(asked, fixture.FilmDatabase.Asked);
    }

    [Fact]
    public async Task The_last_page_has_no_next_and_a_page_out_of_range_is_refused()
    {
        var last = await Get<CatalogueDiscoverView>(Paged(92) + "&page=3");

        Assert.Equal(FilmDatabaseStub.PagedFilms - 40, last.Titles.Count);
        Assert.Null(last.Next);
        Assert.Equal(HttpStatusCode.BadRequest, (await Signed().GetAsync(Paged(92) + "&page=0")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Signed().GetAsync(Paged(92) + $"&page={CatalogueSearch.PagesListed + 1}")).StatusCode);
    }

    [Fact]
    public async Task A_sort_by_rating_pages_over_one_ranked_list_read_from_the_database_once()
    {
        var ranked = Paged(91) + "&sort=rating";
        var one = await Get<CatalogueDiscoverView>(ranked);
        var two = await Get<CatalogueDiscoverView>(ranked + "&page=2");
        var three = await Get<CatalogueDiscoverView>(ranked + "&page=3");

        Assert.Equal((2, 3, (int?)null), (one.Next, two.Next, three.Next));
        var all = one.Titles.Concat(two.Titles).Concat(three.Titles).ToList();
        Assert.Equal(FilmDatabaseStub.PagedFilms, all.Select(t => t.TmdbId).Distinct().Count());
        Assert.Equal(3, Discoveries(91).Count);

        var expected = WeightedRating.Order([.. all.Select(t => new ListedTitle
        {
            TmdbId = t.TmdbId, Kind = t.Kind, Title = t.Title, Rating = t.Rating, Votes = t.Votes,
        })]);
        Assert.Equal(expected.Select(t => t.TmdbId), all.Select(t => t.TmdbId));
    }

    [Fact]
    public async Task The_lowest_rated_first_is_the_ranked_list_last_first_and_pages_over_it()
    {
        var best = Paged(91) + "&sort=rating";
        var ranked = (await Get<CatalogueDiscoverView>(best)).Titles
            .Concat((await Get<CatalogueDiscoverView>(best + "&page=2")).Titles)
            .Concat((await Get<CatalogueDiscoverView>(best + "&page=3")).Titles).ToList();

        var worst = Paged(91) + "&sort=lowest";
        var one = await Get<CatalogueDiscoverView>(worst);
        var two = await Get<CatalogueDiscoverView>(worst + "&page=2");
        var three = await Get<CatalogueDiscoverView>(worst + "&page=3");

        Assert.Equal((2, 3, (int?)null), (one.Next, two.Next, three.Next));
        Assert.Equal("lowest", one.Resolved.Sort);
        Assert.Equal(ranked.Select(t => t.TmdbId).Reverse(), one.Titles.Concat(two.Titles).Concat(three.Titles).Select(t => t.TmdbId));
        Assert.Equal(3, Discoveries(91).Count);
    }

    [Fact]
    public async Task A_questions_count_bounds_the_titles_across_its_pages()
    {
        var ask = "/api/catalogue/ask?q=" + Uri.EscapeDataString("top 30 movies about paged");
        var one = await Get<CatalogueQuestionView>(ask);
        var two = await Get<CatalogueQuestionView>(ask + "&page=2");
        var three = await Get<CatalogueQuestionView>(ask + "&page=3");

        Assert.True(one.Question);
        Assert.Equal((20, 2), (one.Titles.Count, one.Next));
        Assert.Equal(10, two.Titles.Count);
        Assert.Null(two.Next);
        Assert.Empty(three.Titles);
        Assert.Null(three.Next);
    }

    [Fact]
    public async Task A_count_below_a_page_is_one_page()
    {
        var ask = "/api/catalogue/ask?q=" + Uri.EscapeDataString("top 5 movies about paged");
        var one = await Get<CatalogueQuestionView>(ask);

        Assert.Equal(5, one.Titles.Count);
        Assert.Null(one.Next);
    }

    [Fact]
    public async Task A_shelf_is_paged_in_pages_of_the_shelf_and_a_later_page_is_asked_when_wanted()
    {
        var first = await Get<ShelfView>("/api/discover/shelves/date-night");
        Assert.Equal((CatalogueSearch.ShelfListed, 2), (first.Titles.Count, first.Next));
        Assert.DoesNotContain(fixture.FilmDatabase.Requests, DateNightPage3);

        var second = await Get<ShelfView>("/api/discover/shelves/date-night?page=2");
        Assert.Equal(FilmDatabaseStub.PagedFilms - CatalogueSearch.ShelfListed, second.Titles.Count);
        Assert.Null(second.Next);
        Assert.Contains(fixture.FilmDatabase.Requests, DateNightPage3);
        Assert.Empty(first.Titles.Select(t => t.ImdbId).Intersect(second.Titles.Select(t => t.ImdbId)));
    }

    [Fact]
    public async Task A_shelf_ranked_by_rating_pages_over_the_ranked_list_asked_once()
    {
        var before = fixture.FilmDatabase.Requests.Count;
        var first = await Get<ShelfView>("/api/discover/shelves/top-rated");
        var second = await Get<ShelfView>("/api/discover/shelves/top-rated?page=2");

        Assert.Equal((CatalogueSearch.ShelfListed, 2), (first.Titles.Count, first.Next));
        Assert.Equal(FilmDatabaseStub.PagedFilms - CatalogueSearch.ShelfListed, second.Titles.Count);
        Assert.Null(second.Next);
        Assert.Equal(3, fixture.FilmDatabase.Requests.Skip(before).Count(r => r.StartsWith("/3/discover/movie")));
        Assert.Empty(first.Titles.Select(t => t.ImdbId).Intersect(second.Titles.Select(t => t.ImdbId)));
    }

    [Fact]
    public async Task A_shelf_lowest_rated_first_is_its_ranked_list_last_first()
    {
        var best = (await Get<ShelfView>("/api/discover/shelves/top-rated")).Titles
            .Concat((await Get<ShelfView>("/api/discover/shelves/top-rated?page=2")).Titles).ToList();
        var first = await Get<ShelfView>("/api/discover/shelves/top-rated?sort=lowest");
        var second = await Get<ShelfView>("/api/discover/shelves/top-rated?sort=lowest&page=2");

        Assert.Equal((CatalogueSearch.ShelfListed, 2), (first.Titles.Count, first.Next));
        Assert.Null(second.Next);
        Assert.Equal(best.Select(t => t.ImdbId).Reverse(), first.Titles.Concat(second.Titles).Select(t => t.ImdbId));
    }

    [Fact]
    public async Task A_shelf_that_is_whole_in_one_answer_ends_on_its_first_page()
    {
        var series = await Get<ShelfView>("/api/discover/shelves/star-wars");
        var later = await Get<ShelfView>("/api/discover/shelves/star-wars?page=2");

        Assert.Equal(2, series.Titles.Count);
        Assert.Null(series.Next);
        Assert.Empty(later.Titles);
        Assert.Null(later.Next);
    }

    [Fact]
    public async Task A_shelf_page_out_of_range_is_refused()
    {
        Assert.Equal(HttpStatusCode.BadRequest, (await Signed().GetAsync("/api/discover/shelves/date-night?page=0")).StatusCode);
    }
}
