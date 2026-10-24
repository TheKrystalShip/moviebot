using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using TheKrystalShip.MovieBot.Api.Films;
using TheKrystalShip.MovieBot.Api.People;
using Xunit;

namespace TheKrystalShip.MovieBot.Tests;

/// <summary>
/// The Discover page's shelves: the tabs and what is on each for a person, a shelf asked of the film
/// database by its own ids, the series, the shelves that follow from what a person and the people
/// here watched, and the filter's runtime. The database is answered by the test.
/// </summary>
public sealed class DiscoverShelvesTests(SessionFixture fixture) : IClassFixture<SessionFixture>
{
    private HttpClient Signed(string userId)
    {
        var client = fixture.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", fixture.TokenFor(userId, "Gil"));
        return client;
    }

    private async Task<T> Get<T>(string path, string userId = "d1") => (await Signed(userId).GetFromJsonAsync<T>(path))!;

    private void Finished(string userId, string titleId) =>
        fixture.Services.GetRequiredService<WatchRecord>().Record(userId, titleId, 5900, 6000, DateTimeOffset.UtcNow);

    /// <summary>The shelves the page's rows name, in order.</summary>
    private static List<string> RowShelves(DiscoverIndexView index) => [.. index.Rows.Where(r => r.Shelf is not null).Select(r => r.Shelf!.Id)];

    [Fact]
    public async Task The_categories_come_in_order_each_with_its_shelves_a_caption_and_the_filter_its_choices()
    {
        var index = await Get<DiscoverIndexView>("/api/discover");

        // A section filled from Wikidata is absent while nothing is kept for it.
        Assert.Equal(DiscoverShelves.Sections.Skip(1).Select(s => s.Id).Where(id => id is not ("awards" or "adaptations" or "places")),
            index.Sections.Select(s => s.Id));
        Assert.All(index.Sections, s => Assert.NotEmpty(s.Shelves));
        var moods = index.Sections.Single(s => s.Id == "moods");
        Assert.Contains(moods.Shelves, s => s.Id == "sunday-chill" && s.Title == "Sunday chill");
        Assert.Equal("12 shelves", moods.Caption);
        Assert.True(index.Sections.Single(s => s.Id == "franchises").Series);
        Assert.Contains("Horror", index.FilmGenres);
        Assert.Contains("Drama", index.ShowGenres);
        Assert.Equal(("en", "English"), (index.Languages[0].Code, index.Languages[0].Name));
        Assert.Equal(index.Languages.Skip(1).Select(l => l.Name).Order(StringComparer.Ordinal), index.Languages.Skip(1).Select(l => l.Name));
    }

    [Fact]
    public async Task The_page_has_the_moods_the_series_and_the_categories_one_director_a_day_and_a_season_s_shelf_in_its_season()
    {
        var index = await Get<DiscoverIndexView>("/api/discover");
        var shelves = RowShelves(index);

        Assert.Equal(["moods", "franchises", "browse"], index.Rows.Where(r => r.Kind != "shelf").Select(r => r.Kind));
        Assert.Single(shelves, id => DiscoverShelves.Fixed.Any(f => f.Id == id && f.Source == ShelfSource.Director));
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var season = DiscoverShelves.Fixed.Where(s => s.Season is not null && s.Season.Holds(today)).Select(s => s.Id).ToList();
        foreach (var seasonal in DiscoverShelves.Fixed.Where(s => s.Season is not null))
            Assert.Equal(seasonal.Season!.Holds(today), shelves.Contains(seasonal.Id));
        Assert.Equal(season.FirstOrDefault() ?? "trending-films", index.Spotlight);
    }

    [Fact]
    public async Task The_index_carries_the_spotlight_s_shelf_and_films_and_the_first_rows_as_their_own_routes_answer_them()
    {
        var index = await Get<DiscoverIndexView>("/api/discover");

        var first = new[] { index.Spotlight }.Concat(RowShelves(index).Take(3)).Distinct().Order().ToList();
        Assert.Equal(first, index.Shelves.Select(s => s.Id).Order());
        foreach (var carried in index.Shelves)
        {
            var own = await Get<ShelfView>($"/api/discover/shelves/{carried.Id}");
            Assert.Equal(own.Titles.Select(t => (t.ImdbId, t.Watched)), carried.Titles.Select(t => (t.ImdbId, t.Watched)));
        }

        var lead = index.Shelves.Single(s => s.Id == index.Spotlight);
        var picks = lead.Titles.Where(t => t.Kind == "film" && !t.Watched).Take(4).Select(t => t.ImdbId).Order().ToList();
        Assert.Equal(picks, index.Films.Select(f => f.ImdbId).Order());
        foreach (var film in index.Films)
            Assert.Equal((await Get<FilmView>($"/api/films/{film.ImdbId}")).Title, film.Title);
    }

    [Fact]
    public async Task A_series_tile_has_its_posters_and_follows_the_person_through_it()
    {
        Finished("d9", SessionFixture.Heat);

        var watcher = (await Get<DiscoverIndexView>("/api/discover", "d9")).Sections.Single(s => s.Id == "franchises").Shelves.Single(s => s.Id == "star-wars");
        var other = (await Get<DiscoverIndexView>("/api/discover", "d10")).Sections.Single(s => s.Id == "franchises").Shelves.Single(s => s.Id == "star-wars");

        Assert.Equal((2, 1, "Pirates of the Caribbean"), (watcher.Total, watcher.Watched, watcher.Next));
        Assert.Equal(2, watcher.Posters!.Count);
        Assert.Equal((0, (string?)null), (other.Watched, other.Next));
    }

    [Theory]
    [InlineData("Back to the Future Part II", "Back to the Future", "Part II")]
    [InlineData("Harry Potter and the Chamber of Secrets", "Harry Potter", "Chamber of Secrets")]
    [InlineData("The Dark Knight Rises", "The Dark Knight", "Rises")]
    [InlineData("Mission: Impossible - Fallout", "Mission: Impossible", "Fallout")]
    [InlineData("Aliens", "Alien", "Aliens")]
    [InlineData("Return of the Jedi", "Star Wars", "Return of the Jedi")]
    public void A_film_of_a_series_is_named_by_what_tells_it_from_the_series(string title, string series, string expected)
    {
        Assert.Equal(expected, DiscoverShelves.Shortened(title, series));
    }

    [Theory]
    [InlineData(10, 1, true)]
    [InlineData(10, 31, true)]
    [InlineData(11, 1, false)]
    [InlineData(9, 30, false)]
    public void A_season_holds_both_of_its_ends(int month, int day, bool holds)
    {
        Assert.Equal(holds, new ShelfSeason(10, 1, 10, 31).Holds(new DateOnly(2026, month, day)));
    }

    [Fact]
    public async Task A_tile_says_whether_its_shelf_goes_on_past_the_titles_it_counts()
    {
        var index = await Get<DiscoverIndexView>("/api/discover");
        var heads = index.Sections.SelectMany(s => s.Shelves).ToList();

        var dateNight = heads.Single(h => h.Id == "date-night");
        Assert.Equal((CatalogueSearch.ShelfListed, true), (dateNight.Total, dateNight.More));
        var series = heads.Single(h => h.Id == "star-wars");
        Assert.Equal((2, (bool?)null), (series.Total, series.More));
    }

    [Fact]
    public void Every_shelf_has_its_own_id_and_names_a_tab_that_exists()
    {
        Assert.Equal(DiscoverShelves.Fixed.Count, DiscoverShelves.Fixed.Select(s => s.Id).Distinct().Count());
        Assert.All(DiscoverShelves.Fixed, s => Assert.Contains(DiscoverShelves.Sections, t => t.Id == s.Section));
    }

    [Fact]
    public async Task A_genre_s_shelf_asks_by_the_database_s_ids_lists_what_is_out_and_is_asked_once()
    {
        var before = fixture.FilmDatabase.Requests.Count;

        var horror = await Get<ShelfView>("/api/discover/shelves/horror");
        var asked = fixture.FilmDatabase.Asked;
        await Get<ShelfView>("/api/discover/shelves/horror");

        Assert.Equal(("Horror", "Genres", "film"), (horror.Title, horror.Section, horror.Kind));
        Assert.Equal(["The Cellar Door", "Hollow Hill"], horror.Titles.Select(t => t.Title));
        Assert.Equal(("tt9000101", "Ada Moor, Ben Pike"), (horror.Titles[0].ImdbId, horror.Titles[0].Starring));
        var discovery = Assert.Single(fixture.FilmDatabase.Requests.Skip(before), r => r.StartsWith("/3/discover/movie"));
        Assert.Contains("with_genres=27&", discovery);
        Assert.Contains("without_genres=16|10751", discovery);
        Assert.Contains("primary_release_date.lte=" + DateTime.UtcNow.ToString("yyyy-MM-dd"), discovery);
        Assert.Equal(asked, fixture.FilmDatabase.Asked);
    }

    [Fact]
    public async Task A_series_lists_its_films_in_the_order_they_came_out_joined_with_the_library()
    {
        var series = await Get<ShelfView>("/api/discover/shelves/star-wars");

        Assert.Equal(["Heat", "Pirates of the Caribbean"], series.Titles.Select(t => t.Title));
        Assert.Equal((true, SessionFixture.Heat), (series.Titles[0].InLibrary, series.Titles[0].LibraryId));
        Assert.False(series.Titles[1].InLibrary);
    }

    [Fact]
    public async Task A_shelf_in_another_order_is_asked_of_the_database_in_that_order()
    {
        var before = fixture.FilmDatabase.Requests.Count;

        var horror = await Get<ShelfView>("/api/discover/shelves/horror?sort=newest");

        Assert.NotEmpty(horror.Titles);
        var discovery = Assert.Single(fixture.FilmDatabase.Requests.Skip(before), r => r.StartsWith("/3/discover/movie"));
        Assert.Contains("sort_by=primary_release_date.desc", discovery);
    }

    [Fact]
    public async Task A_shelf_asked_without_what_the_person_watched_leaves_it_off()
    {
        Finished("d-unwatched", SessionFixture.Heat);

        var all = await Get<ShelfView>("/api/discover/shelves/star-wars", "d-unwatched");
        var unwatched = await Get<ShelfView>("/api/discover/shelves/star-wars?unwatched=true", "d-unwatched");

        Assert.Contains(all.Titles, t => t.Title == "Heat" && t.Watched);
        Assert.Equal(["Pirates of the Caribbean"], unwatched.Titles.Select(t => t.Title));
    }

    [Fact]
    public async Task A_series_in_another_order_is_ordered_whole_before_it_is_paged()
    {
        var series = await Get<ShelfView>("/api/discover/shelves/star-wars?sort=newest");

        Assert.Equal(["Pirates of the Caribbean", "Heat"], series.Titles.Select(t => t.Title));
    }

    [Fact]
    public async Task A_shelf_s_own_order_is_named_order_and_any_other_name_is_refused()
    {
        var own = await Get<ShelfView>("/api/discover/shelves/star-wars?sort=order");
        using var refused = await Signed("d1").GetAsync("/api/discover/shelves/star-wars?sort=popular");

        Assert.Equal(["Heat", "Pirates of the Caribbean"], own.Titles.Select(t => t.Title));
        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
    }

    [Fact]
    public void A_shelf_whole_in_one_answer_is_ordered_best_rated_or_by_year()
    {
        List<ShelfTitleView> views =
        [
            new() { ImdbId = "tt1", Kind = "film", Title = "Seen by few", Year = 2001, Rating = 9.4, Votes = 120, RatedBy = TitleScores.ByImdb },
            new() { ImdbId = "tt2", Kind = "film", Title = "Well liked", Year = 1999, Rating = 8.1, Votes = 900_000, RatedBy = TitleScores.ByImdb },
            new() { ImdbId = "tt3", Kind = "film", Title = "Undated", Rating = 6.2, Votes = 40_000, RatedBy = TitleScores.ByImdb },
        ];

        Assert.Equal(["Well liked", "Undated", "Seen by few"], DiscoverShelves.Sorted(views, "rating").Select(v => v.Title));
        Assert.Equal(["Seen by few", "Well liked", "Undated"], DiscoverShelves.Sorted(views, "newest").Select(v => v.Title));
        Assert.Equal(["Well liked", "Seen by few", "Undated"], DiscoverShelves.Sorted(views, "oldest").Select(v => v.Title));
        Assert.Same(views, DiscoverShelves.Sorted(views, null));
    }

    [Fact]
    public async Task A_series_the_database_has_nothing_under_is_a_shelf_with_nothing_on_it()
    {
        var series = await Get<ShelfView>("/api/discover/shelves/james-bond");

        Assert.Empty(series.Titles);
    }

    [Fact]
    public async Task A_shelf_that_does_not_exist_is_not_found()
    {
        using var response = await Signed("d1").GetAsync("/api/discover/shelves/no-such-shelf");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Theory]
    [InlineData("/api/discover")]
    [InlineData("/api/discover/shelves/horror")]
    public async Task The_shelves_need_a_token(string path)
    {
        using var response = await fixture.CreateClient().GetAsync(path);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task What_a_person_finished_leads_to_a_shelf_of_what_is_like_it()
    {
        Finished("d2", SessionFixture.Heat);

        var index = await Get<DiscoverIndexView>("/api/discover", "d2");
        var like = Assert.Single(index.Rows, r => r.Shelf?.Id == "like-film-tt0113277").Shelf!;
        var shelf = await Get<ShelfView>("/api/discover/shelves/" + like.Id, "d2");

        Assert.Equal("Because you watched Heat", like.Title);
        Assert.Equal(["Echo Valley", "Again and Again"], shelf.Titles.Select(t => t.Title));
        Assert.DoesNotContain(RowShelves(await Get<DiscoverIndexView>("/api/discover", "d3")), id => id.StartsWith("like-"));
    }

    [Fact]
    public async Task What_the_people_here_watched_is_a_shelf_that_names_nobody_and_the_most_watched_first()
    {
        Finished("d4", SessionFixture.Collateral);
        Finished("d5", SessionFixture.Heat);
        Finished("d6", SessionFixture.Heat);

        var index = await Get<DiscoverIndexView>("/api/discover", "d7");
        var here = await Get<ShelfView>("/api/discover/shelves/popular-here", "d7");

        Assert.Contains("popular-here", RowShelves(index));
        Assert.Equal(["tt0113277", "tt0369339"], here.Titles.Take(2).Select(t => t.ImdbId));
        Assert.All(here.Titles, t => Assert.True(t.InLibrary));
    }

    [Fact]
    public async Task Ready_to_watch_is_the_library_s_films_that_play_now_less_what_the_person_finished()
    {
        Finished("d8", SessionFixture.Collateral);

        var ready = await Get<ShelfView>("/api/discover/shelves/ready-here", "d8");

        Assert.Contains(ready.Titles, t => t.ImdbId == "tt0113277" && t.LibraryId == SessionFixture.Heat);
        Assert.DoesNotContain(ready.Titles, t => t.ImdbId == "tt0369339");
    }

    [Fact]
    public async Task The_filter_narrows_a_discovery_by_runtime_and_refuses_runtime_on_one_like_another_title()
    {
        var before = fixture.FilmDatabase.Requests.Count;

        using var narrowed = await Signed("d1").GetAsync("/api/catalogue/discover?genre=horror&minRuntime=60&maxRuntime=90");
        using var refused = await Signed("d1").GetAsync("/api/catalogue/discover?like=tt0113277&maxRuntime=90");

        Assert.Equal(HttpStatusCode.OK, narrowed.StatusCode);
        Assert.Contains(fixture.FilmDatabase.Requests.Skip(before), r => r.Contains("with_runtime.gte=60&with_runtime.lte=90"));
        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
    }

    [Fact]
    public async Task A_director_s_shelf_lists_only_the_films_they_directed_each_as_its_director()
    {
        var shelf = await Get<ShelfView>($"/api/discover/shelves/directed-{FilmDatabaseStub.Jo}");

        Assert.Equal(($"directed-{FilmDatabaseStub.Jo}", "More from Jo Test", "megaphone", "film"), (shelf.Id, shelf.Title, shelf.Icon, shelf.Kind));
        var only = Assert.Single(shelf.Titles);
        Assert.Equal(("Silver Harbour", "Director"), (only.Title, only.Role));
        Assert.Null(shelf.Next);
    }

    [Fact]
    public async Task A_person_s_acting_shelf_leaves_out_appearances_uncredited_parts_unreleased_titles_and_short_guest_spots()
    {
        var shelf = await Get<ShelfView>($"/api/discover/shelves/starring-{FilmDatabaseStub.Kim}");

        Assert.Equal(("More from Kim Test", "user", null), (shelf.Title, shelf.Icon, shelf.Kind));
        Assert.Equal([("Old Letters", "film", "Vera"), ("Long Winter", "show", "Nora")], shelf.Titles.Select(t => (t.Title, t.Kind, t.Role)));
    }

    [Fact]
    public async Task A_person_s_shelf_is_ordered_whole_by_a_sort_and_ends_on_its_page()
    {
        var newest = await Get<ShelfView>($"/api/discover/shelves/starring-{FilmDatabaseStub.Kim}?sort=newest");
        var later = await Get<ShelfView>($"/api/discover/shelves/starring-{FilmDatabaseStub.Kim}?page=2");

        Assert.Equal(["Long Winter", "Old Letters"], newest.Titles.Select(t => t.Title));
        Assert.Empty(later.Titles);
    }

    [Fact]
    public async Task A_shelf_of_a_person_the_database_does_not_know_is_empty_and_one_with_no_id_is_not_found()
    {
        var unknown = await Get<ShelfView>("/api/discover/shelves/directed-999999");
        using var word = await Signed("d1").GetAsync("/api/discover/shelves/starring-kim");
        using var zero = await Signed("d1").GetAsync("/api/discover/shelves/directed-0");

        Assert.Empty(unknown.Titles);
        Assert.Equal(HttpStatusCode.NotFound, word.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, zero.StatusCode);
    }

    [Fact]
    public async Task A_title_on_any_other_shelf_has_no_role()
    {
        var series = await Get<ShelfView>("/api/discover/shelves/star-wars");

        Assert.All(series.Titles, t => Assert.Null(t.Role));
    }
}
