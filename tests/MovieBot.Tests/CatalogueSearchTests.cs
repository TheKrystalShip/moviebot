using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using TheKrystalShip.MovieBot.Acquire.Tmdb;
using TheKrystalShip.MovieBot.Api.Films;
using TheKrystalShip.MovieBot.Api.People;
using Xunit;

namespace TheKrystalShip.MovieBot.Tests;

/// <summary>
/// Finding films and shows: by name, by a person and everything they are credited on, by what a title
/// is like, and by what is new. The film database is answered by the test over a small invented world;
/// each result is joined with the library and with what the asking person watched.
/// </summary>
public sealed class CatalogueSearchTests(SessionFixture fixture) : IClassFixture<SessionFixture>
{
    private static int ThisYear => FilmDatabaseStub.ThisYear;
    private static DateOnly Today => DateOnly.FromDateTime(DateTime.UtcNow);

    private HttpClient Signed(string userId = "u7")
    {
        var client = fixture.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", fixture.TokenFor(userId, "Gil"));
        return client;
    }

    private Task<T?> Get<T>(string path, string userId = "u7") => Signed(userId).GetFromJsonAsync<T>(path);

    private static string Escaped(string text) => Uri.EscapeDataString(text);

    /// <summary>What the database was asked from the point <paramref name="from"/> on.</summary>
    private List<string> AskedSince(int from) => [.. fixture.FilmDatabase.Requests.Skip(from)];

    private async Task<HttpStatusCode> StatusOf(string path)
    {
        using var response = await Signed().GetAsync(path);
        return response.StatusCode;
    }

    // Searching by name.

    [Fact]
    public async Task A_search_lists_who_made_each_title_and_who_stars_in_it_with_the_exact_name_first()
    {
        var search = await Get<CatalogueSearchView>("/api/catalogue/search?q=heat");

        Assert.Equal("heat", search!.Query);
        Assert.Equal(["Heat", "Dead Heat", "Heat Again", "Heat Wave"], search.Titles.Select(t => t.Title));

        var heat = search.Titles[0];
        Assert.Equal(("tt0113277", "film", (int?)1995, "1995-12-15"), (heat.ImdbId, heat.Kind, heat.Year, heat.ReleaseDate));
        Assert.Equal(["Michael Mann"], heat.MadeBy);
        Assert.Equal(["Al Pacino", "Robert De Niro"], heat.Starring);
        Assert.Equal(["Crime", "Drama"], heat.Genres);
        Assert.Equal((7.9, 8740, "About Heat."), (heat.Rating, heat.Votes, heat.Plot));
        Assert.NotNull(heat.PosterUrl);
        using var poster = await fixture.CreateClient().GetAsync(heat.PosterUrl);
        Assert.Equal(HttpStatusCode.OK, poster.StatusCode);

        var show = search.Titles[3];
        Assert.Equal(("tt9100001", "show", (int?)2019), (show.ImdbId, show.Kind, show.Year));
        Assert.Equal(["Rae Cho"], show.MadeBy);
        Assert.Equal(["Ola Beck", "Pim Hart"], show.Starring);
    }

    [Fact]
    public async Task A_title_the_database_has_no_imdb_id_for_is_listed_by_its_own_id_and_has_no_page()
    {
        var search = await Get<CatalogueSearchView>("/api/catalogue/search?q=heat");

        var announced = search!.Titles.Single(t => t.Title == "Heat Again");
        Assert.Null(announced.ImdbId);
        Assert.Equal((5001, ThisYear + 1), (announced.TmdbId, announced.Year));
        Assert.Null(announced.PosterUrl);
        Assert.False(announced.InLibrary);
    }

    [Fact]
    public async Task A_title_in_the_library_says_so_and_names_the_film_that_plays_it()
    {
        var search = await Get<CatalogueSearchView>("/api/catalogue/search?q=heat");

        var heat = search!.Titles.Single(t => t.ImdbId == "tt0113277");
        Assert.True(heat.InLibrary);
        Assert.Equal(SessionFixture.Heat, heat.LibraryId);
        var deadHeat = search.Titles.Single(t => t.Title == "Dead Heat");
        Assert.False(deadHeat.InLibrary);
        Assert.Null(deadHeat.LibraryId);
    }

    [Fact]
    public async Task A_film_watched_through_is_marked_for_the_person_who_watched_it_alone()
    {
        fixture.Services.GetRequiredService<WatchRecord>().Record("9101", SessionFixture.Heat, 5900, 6000, DateTimeOffset.UtcNow);

        var watcher = await Get<CatalogueSearchView>("/api/catalogue/search?q=heat", "9101");
        var other = await Get<CatalogueSearchView>("/api/catalogue/search?q=heat", "9102");

        Assert.True(watcher!.Titles.Single(t => t.ImdbId == "tt0113277").Watched);
        Assert.False(other!.Titles.Single(t => t.ImdbId == "tt0113277").Watched);
    }

    [Fact]
    public async Task A_search_can_be_limited_to_a_kind()
    {
        var films = await Get<CatalogueSearchView>("/api/catalogue/search?q=heat&kind=film");
        var shows = await Get<CatalogueSearchView>("/api/catalogue/search?q=heat&kind=show");

        Assert.Equal(["Heat", "Dead Heat", "Heat Again"], films!.Titles.Select(t => t.Title));
        Assert.Equal("Heat Wave", Assert.Single(shows!.Titles).Title);
    }

    [Theory]
    [InlineData(1996, new[] { "Heat" })]
    [InlineData(1994, new[] { "Heat" })]
    [InlineData(1989, new[] { "Dead Heat" })]
    [InlineData(1990, new string[0])]
    public async Task A_year_keeps_the_titles_released_in_it_or_the_year_either_side(int year, string[] expected)
    {
        var search = await Get<CatalogueSearchView>($"/api/catalogue/search?q=heat&year={year}");

        Assert.Equal(expected, search!.Titles.Select(t => t.Title));
    }

    [Fact]
    public async Task A_search_asks_the_database_once_however_it_is_spelled()
    {
        await Get<CatalogueSearchView>("/api/catalogue/search?q=heat");
        var asked = fixture.FilmDatabase.Asked;

        await Get<CatalogueSearchView>("/api/catalogue/search?q=%20HEAT%20");

        Assert.Equal(asked, fixture.FilmDatabase.Asked);
    }

    [Fact]
    public async Task A_request_the_database_refuses_for_being_one_too_many_is_made_again()
    {
        var before = fixture.FilmDatabase.Requests.Count;
        fixture.FilmDatabase.Limit(2);

        var search = await Get<CatalogueSearchView>("/api/catalogue/search?q=zzzz");

        Assert.Empty(search!.Titles);
        Assert.Equal(3, AskedSince(before).Count(a => a.Contains("query=zzzz")));
    }

    [Theory]
    [InlineData("/api/catalogue/search")]
    [InlineData("/api/catalogue/search?q=%20")]
    [InlineData("/api/catalogue/search?q=heat&kind=episode")]
    [InlineData("/api/catalogue/people?q=")]
    public async Task A_search_needs_something_to_search_for(string path)
    {
        Assert.Equal(HttpStatusCode.BadRequest, await StatusOf(path));
    }

    [Theory]
    [InlineData("/api/catalogue/search?q=heat")]
    [InlineData("/api/catalogue/people?q=al")]
    [InlineData("/api/catalogue/people/1158/credits")]
    [InlineData("/api/catalogue/discover")]
    [InlineData("/api/catalogue/released")]
    [InlineData("/api/catalogue/upcoming")]
    [InlineData("/api/catalogue/added")]
    public async Task Searching_needs_a_token(string path)
    {
        using var response = await fixture.CreateClient().GetAsync(path);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // People.

    [Fact]
    public async Task A_name_lists_the_people_it_could_mean_with_what_each_is_known_for()
    {
        var people = await Get<CataloguePeopleView>("/api/catalogue/people?q=al%20pacino");

        Assert.Equal(["Al Pacino", "Al Pacino Jr"], people!.People.Select(p => p.Name));
        var pacino = people.People[0];
        Assert.Equal((1158, "Acting"), (pacino.PersonId, pacino.KnownFor));
        Assert.Equal(["Heat (1995)", "Hunters (2020)", "Untitled"], pacino.KnownForTitles);
    }

    // A person's credits.

    private async Task<PersonCreditsView> Credits(string query = "", int person = FilmDatabaseStub.Jo, string userId = "u7") =>
        (await Get<PersonCreditsView>($"/api/catalogue/people/{person}/credits{query}", userId))!;

    [Fact]
    public async Task A_person_s_credits_are_every_credit_oldest_first_each_with_where_it_stands()
    {
        var credits = await Credits();

        Assert.Equal(("Jo Test", "Acting"), (credits.Name, credits.KnownFor));
        Assert.Equal(Today.ToString("yyyy-MM-dd"), credits.AsOf);
        Assert.Equal(7, credits.Total);
        Assert.Equal(
            [
                ("Old Letters", "1999-03-04", "released", "tt9000000", "Mara"),
                ("Quiet Money", "2010-06-01", "released", "tt9000005", "Producer"),
                ("Long Winter", "2021-01-10", "released", "tt9100002", "Nora"),
                ("Silver Harbour", $"{ThisYear}-01-01", "released", FilmDatabaseStub.SilverHarbour, "Director"),
                ("Spring Letters", $"{ThisYear}-01-01", "released", "tt9000001", "Anna · Screenplay"),
                ("Winter Orchard", $"{ThisYear + 1}-06-01", "upcoming", "tt9000002", "Ines"),
                ("Untitled Jo Test Project", (string?)null, "announced", (string?)null, "Lead"),
            ],
            credits.Credits.Select(c => (c.Title, c.ReleaseDate, c.Status, c.ImdbId, c.Role)));
        Assert.Equal(["film", "film", "show", "film", "film", "film", "film"], credits.Credits.Select(c => c.Kind));
    }

    [Fact]
    public async Task Appearances_as_oneself_and_on_talk_shows_are_left_out_unless_asked_for()
    {
        var work = await Credits();
        var all = await Credits("?appearances=true");

        Assert.DoesNotContain(work.Credits, c => c.Title is "A Talk Show" or "A Documentary");
        Assert.Equal(9, all.Total);
        Assert.Equal(["A Documentary", "A Talk Show"], all.Credits.Where(c => c.Role is "Self" or "Self - Interviewee").Select(c => c.Title).Order());
        Assert.All(all.Credits.Where(c => c.Title is "A Talk Show" or "A Documentary"), c => Assert.Equal("released", c.Status));
    }

    [Theory]
    [InlineData("acting", new[] { "Old Letters", "Long Winter", "Spring Letters", "Winter Orchard", "Untitled Jo Test Project" })]
    [InlineData("directing", new[] { "Silver Harbour" })]
    [InlineData("writing", new[] { "Spring Letters" })]
    public async Task Credits_can_be_limited_to_what_the_person_did(string role, string[] expected)
    {
        var credits = await Credits($"?role={role}");

        Assert.Equal(expected, credits.Credits.Select(c => c.Title));
    }

    [Fact]
    public async Task Credits_can_be_limited_to_a_kind()
    {
        Assert.Equal("Long Winter", Assert.Single((await Credits("?kind=show")).Credits).Title);
        Assert.DoesNotContain((await Credits("?kind=film")).Credits, c => c.Kind == "show");
    }

    [Fact]
    public async Task One_year_asked_for_twice_is_that_year_and_the_announcements_that_may_fall_in_it()
    {
        var credits = await Credits($"?from={ThisYear}&to={ThisYear}&role=acting");

        // The date of an announcement is not known, so it may fall in a year still to come.
        Assert.Equal(["Spring Letters", "Untitled Jo Test Project"], credits.Credits.Select(c => c.Title));
        Assert.Equal(["released", "announced"], credits.Credits.Select(c => c.Status));
    }

    [Fact]
    public async Task Years_in_the_past_hold_no_announcements()
    {
        var credits = await Credits("?from=2000&to=2012");

        Assert.Equal("Quiet Money", Assert.Single(credits.Credits).Title);
        Assert.Equal(1, credits.Total);
    }

    [Fact]
    public async Task A_year_still_to_come_holds_what_is_dated_in_it_and_the_announcements()
    {
        var credits = await Credits($"?from={ThisYear + 1}");

        Assert.Equal(["Winter Orchard", "Untitled Jo Test Project"], credits.Credits.Select(c => c.Title));
    }

    [Fact]
    public async Task A_credit_in_the_library_says_so_and_a_credit_the_person_watched_is_marked_for_them()
    {
        fixture.Services.GetRequiredService<WatchRecord>().Record("9103", SessionFixture.Heat, 5900, 6000, DateTimeOffset.UtcNow);

        var watcher = await Credits(person: 1158, userId: "9103");
        var other = await Credits(person: 1158, userId: "9104");

        var heat = Assert.Single(watcher.Credits);
        Assert.Equal(("Heat", "released", "Vincent Hanna", true, SessionFixture.Heat), (heat.Title, heat.Status, heat.Role, heat.InLibrary, heat.LibraryId));
        Assert.True(heat.Watched);
        Assert.False(Assert.Single(other.Credits).Watched);
    }

    [Fact]
    public async Task A_person_s_portrait_is_kept_with_their_credits_and_served_from_here()
    {
        var credits = await Credits();

        Assert.Equal("/catalogue/people/7777.jpg", credits.PortraitUrl);
        using var portrait = await fixture.CreateClient().GetAsync(credits.PortraitUrl);
        Assert.Equal(HttpStatusCode.OK, portrait.StatusCode);
    }

    [Fact]
    public async Task A_person_s_credits_are_asked_of_the_database_once()
    {
        await Credits();
        var asked = fixture.FilmDatabase.Asked;

        await Credits("?role=writing");

        Assert.Equal(asked, fixture.FilmDatabase.Asked);
    }

    [Theory]
    [InlineData("?role=producing", HttpStatusCode.BadRequest)]
    [InlineData("?kind=episode", HttpStatusCode.BadRequest)]
    [InlineData("?from=2020&to=2010", HttpStatusCode.BadRequest)]
    public async Task Credits_are_refused_for_a_question_that_has_no_answer(string query, HttpStatusCode status)
    {
        Assert.Equal(status, await StatusOf($"/api/catalogue/people/{FilmDatabaseStub.Jo}/credits{query}"));
    }

    [Fact]
    public async Task Nobody_under_the_id_has_no_credits()
    {
        Assert.Equal(HttpStatusCode.NotFound, await StatusOf("/api/catalogue/people/424242/credits"));
    }

    [Fact]
    public async Task The_people_page_the_site_uses_still_lists_a_person_s_best_known_work()
    {
        var person = await Get<CataloguePersonView>("/api/catalogue/people/1158");

        Assert.Equal("Heat", Assert.Single(person!.Titles).Title);
    }

    // Discovering.

    private async Task<(CatalogueDiscoverView? View, List<string> Asked)> Discover(string query)
    {
        var before = fixture.FilmDatabase.Requests.Count;
        var view = await Get<CatalogueDiscoverView>($"/api/catalogue/discover{query}");
        return (view, AskedSince(before));
    }

    [Fact]
    public async Task Good_horror_of_the_early_2000s_is_asked_for_by_genre_years_and_a_floor_of_rating_and_votes()
    {
        var (view, asked) = await Discover("?genre=horror&from=2000&to=2005");

        Assert.Equal(["The Cellar Door", "Hollow Hill"], view!.Titles.Select(t => t.Title));
        var discover = asked.Single(a => a.StartsWith("/3/discover/movie"));
        Assert.Contains("with_genres=27", discover);
        Assert.Contains("primary_release_date.gte=2000-01-01", discover);
        Assert.Contains("primary_release_date.lte=2005-12-31", discover);
        Assert.Contains("vote_average.gte=6.5", discover);
        Assert.Contains("vote_count.gte=200", discover);
        Assert.Contains("sort_by=popularity.desc", discover);

        Assert.Equal("film", view.Kind);
        Assert.Equal(["Horror"], view.Resolved.Genres);
        Assert.Equal((6.5, 200, "popular"), (view.Resolved.MinRating, view.Resolved.MinVotes, view.Resolved.Sort));

        var first = view.Titles[0];
        Assert.Equal(("tt9000101", (int?)2002, "2002-10-31", 7.1, "About The Cellar Door."), (first.ImdbId, first.Year, first.ReleaseDate, first.Rating, first.Plot));
        Assert.Equal(["Horror"], first.Genres);
        Assert.Equal(["Lou Frame"], first.MadeBy);
        Assert.Equal(["Ada Moor", "Ben Pike"], first.Starring);
        Assert.False(first.InLibrary);
    }

    [Fact]
    public async Task A_genre_search_leaves_out_animation_and_family_unless_they_are_asked_for_and_says_so()
    {
        var (view, asked) = await Discover("?genre=horror&from=2000&to=2004");
        Assert.Contains("without_genres=16|10751", asked.Single(a => a.StartsWith("/3/discover/movie")));
        Assert.Equal(["Animation", "Family"], view!.Resolved.Excluded);

        var (kept, askedKept) = await Discover("?genre=horror&includeFamily=true&from=2000&to=2006");
        Assert.DoesNotContain("without_genres", askedKept.Single(a => a.StartsWith("/3/discover/movie")));
        Assert.Empty(kept!.Resolved.Excluded);

        var (cartoons, askedCartoons) = await Discover("?genre=horror&genre=animation&from=2000&to=2007");
        Assert.DoesNotContain("without_genres", askedCartoons.Single(a => a.StartsWith("/3/discover/movie")));
        Assert.Empty(cartoons!.Resolved.Excluded);

        var (open, askedOpen) = await Discover("?from=2000&to=2008");
        Assert.DoesNotContain("without_genres", askedOpen.Single(a => a.StartsWith("/3/discover/movie")));
        Assert.Empty(open!.Resolved.Excluded);
    }

    [Fact]
    public async Task Genres_named_to_leave_out_are_added_to_the_default_and_cannot_be_the_genre_asked_for()
    {
        var (view, asked) = await Discover("?genre=horror&exclude=crime&from=2000&to=2009");
        Assert.Contains("without_genres=80|16|10751", asked.Single(a => a.StartsWith("/3/discover/movie")));
        Assert.Equal(["Crime", "Animation", "Family"], view!.Resolved.Excluded);

        Assert.Equal(HttpStatusCode.BadRequest, await StatusOf("/api/catalogue/discover?genre=horror&exclude=horror"));
    }

    [Fact]
    public async Task Good_only_off_asks_for_no_floor()
    {
        var (view, asked) = await Discover("?genre=horror&goodOnly=false&from=1990");

        var discover = asked.Single(a => a.StartsWith("/3/discover/movie"));
        Assert.DoesNotContain("vote_average.gte", discover);
        Assert.DoesNotContain("vote_count.gte", discover);
        Assert.Null(view!.Resolved.MinRating);
        Assert.Null(view.Resolved.MinVotes);
    }

    [Fact]
    public async Task A_floor_can_be_named_and_a_sort_by_rating_wants_votes_behind_the_rating()
    {
        var (_, named) = await Discover("?genre=horror&goodOnly=false&minRating=7&minVotes=50&from=1991");
        var (_, rated) = await Discover("?genre=horror&goodOnly=false&sort=rating&from=1992");

        var floor = named.Single(a => a.StartsWith("/3/discover/movie"));
        Assert.Contains("vote_average.gte=7.0", floor);
        Assert.Contains("vote_count.gte=50", floor);
        var rating = rated.Single(a => a.StartsWith("/3/discover/movie"));
        Assert.Contains("sort_by=vote_count.desc", rating);
        Assert.Contains($"vote_count.gte={CatalogueSearch.RatedVotes}", rating);
    }

    [Fact]
    public async Task A_keyword_with_a_named_person_is_resolved_to_the_databases_keyword_and_person()
    {
        var (view, asked) = await Discover($"?about={Escaped("silent film")}&with={Escaped("Ray Dane")}&goodOnly=false");

        var discover = asked.Single(a => a.StartsWith("/3/discover/movie"));
        Assert.Contains("with_keywords=12999", discover);
        Assert.Contains("with_people=8001", discover);
        var found = Assert.Single(view!.Titles);
        Assert.Equal((FilmDatabaseStub.SilverHarbour, "Silver Harbour"), (found.ImdbId, found.Title));
        Assert.Equal(["silent film"], view.Resolved.Keywords);
        var ray = Assert.Single(view.Resolved.People);
        Assert.Equal((8001, "Ray Dane", "Acting"), (ray.PersonId, ray.Name, ray.KnownFor));
    }

    [Fact]
    public async Task A_keyword_is_the_one_named_exactly_before_the_databases_first_match()
    {
        var (view, asked) = await Discover($"?about={Escaped("time loop")}&goodOnly=false&from=1980");

        Assert.Contains("with_keywords=10854", asked.Single(a => a.StartsWith("/3/discover/movie")));
        Assert.Equal(["time loop"], view!.Resolved.Keywords);
    }

    [Fact]
    public async Task A_language_is_named_by_its_english_name_or_its_code()
    {
        var (named, askedNamed) = await Discover("?language=Korean&goodOnly=false&from=1981");
        var (coded, askedCoded) = await Discover("?language=KO&goodOnly=false&from=1982");

        Assert.Contains("with_original_language=ko", askedNamed.Single(a => a.StartsWith("/3/discover/movie")));
        Assert.Contains("with_original_language=ko", askedCoded.Single(a => a.StartsWith("/3/discover/movie")));
        Assert.Equal(("Korean", "Korean"), (named!.Resolved.Language, coded!.Resolved.Language));
    }

    [Fact]
    public async Task The_newest_are_those_already_out()
    {
        var (_, asked) = await Discover("?genre=horror&goodOnly=false&sort=newest&from=1983");

        var discover = asked.Single(a => a.StartsWith("/3/discover/movie"));
        Assert.Contains("sort_by=primary_release_date.desc", discover);
        Assert.Contains($"primary_release_date.lte={Today:yyyy-MM-dd}", discover);
    }

    [Fact]
    public async Task The_least_popular_are_asked_for_with_votes_behind_them()
    {
        var (view, asked) = await Discover("?genre=horror&goodOnly=false&sort=least-popular");

        var discover = asked.Single(a => a.StartsWith("/3/discover/movie"));
        Assert.Contains("sort_by=popularity.asc", discover);
        Assert.Contains($"vote_count.gte={CatalogueSearch.RatedVotes}", discover);
        Assert.Equal("least-popular", view!.Resolved.Sort);
    }

    [Fact]
    public async Task A_genre_of_shows_is_found_by_a_word_of_its_name()
    {
        var (view, asked) = await Discover("?kind=show&genre=action");

        Assert.Equal("show", view!.Kind);
        Assert.Equal(["Action & Adventure"], view.Resolved.Genres);
        Assert.Contains("with_genres=10759", asked.Single(a => a.StartsWith("/3/discover/tv")));
        Assert.Empty(view.Titles);
    }

    [Fact]
    public async Task A_discovery_is_kept_and_asks_the_database_nothing_the_second_time()
    {
        await Discover("?genre=horror&from=2001&to=2004");
        var asked = fixture.FilmDatabase.Asked;

        var (view, _) = await Discover("?genre=Horror&to=2004&from=2001");

        Assert.Equal(2, view!.Titles.Count);
        Assert.Equal(asked, fixture.FilmDatabase.Asked);
    }

    [Theory]
    [InlineData("?genre=westerns", HttpStatusCode.NotFound)]
    [InlineData("?with=Nobody%20Known", HttpStatusCode.NotFound)]
    [InlineData("?about=zzzz", HttpStatusCode.NotFound)]
    [InlineData("?language=Klingon", HttpStatusCode.NotFound)]
    [InlineData("?like=Nothing%20Known", HttpStatusCode.NotFound)]
    [InlineData("?kind=episode", HttpStatusCode.BadRequest)]
    [InlineData("?sort=best", HttpStatusCode.BadRequest)]
    [InlineData("?from=2010&to=2000", HttpStatusCode.BadRequest)]
    [InlineData("?kind=show&with=Ray%20Dane", HttpStatusCode.BadRequest)]
    [InlineData("?like=tt0113277&kind=show", HttpStatusCode.BadRequest)]
    public async Task A_word_the_database_does_not_know_is_refused_rather_than_ignored(string query, HttpStatusCode status)
    {
        using var response = await Signed().GetAsync($"/api/catalogue/discover{query}");

        Assert.Equal(status, response.StatusCode);
        Assert.Contains("error", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task A_genre_that_is_not_known_is_refused_with_the_genres_that_are()
    {
        using var response = await Signed().GetAsync("/api/catalogue/discover?genre=westerns");

        Assert.Contains("Crime, Adventure, Horror", await response.Content.ReadAsStringAsync());
    }

    // Like another title.

    [Theory]
    [InlineData("Heat")]
    [InlineData("Heat%20(1995)")]
    [InlineData("tt0113277")]
    public async Task Titles_like_another_are_its_recommendations_and_the_rest_the_database_counts_similar_narrowed_to_the_well_rated(string like)
    {
        var (view, _) = await Discover($"?like={like}");

        // The one rated 5.0 and the one rated 6.1 fall under the floor; the database's order stands.
        Assert.Equal(["Echo Valley", "Again and Again"], view!.Titles.Select(t => t.Title));
        Assert.Equal(("Heat (1995)", "similar", "film"), (view.Resolved.Like, view.Resolved.Sort, view.Kind));
    }

    [Fact]
    public async Task Titles_like_another_can_be_sorted_and_narrowed_by_genre_years_and_language()
    {
        var (byRating, _) = await Discover("?like=Heat&sort=rating");
        var (horror, _) = await Discover("?like=Heat&genre=horror");
        var (late, _) = await Discover("?like=Heat&from=2020");
        var (korean, _) = await Discover("?like=Heat&language=Korean");
        var (everything, _) = await Discover("?like=Heat&goodOnly=false&sort=oldest");

        Assert.Equal(["Again and Again", "Echo Valley"], byRating!.Titles.Select(t => t.Title));
        Assert.Equal("Echo Valley", Assert.Single(horror!.Titles).Title);
        Assert.Equal("Again and Again", Assert.Single(late!.Titles).Title);
        Assert.Empty(korean!.Titles);
        Assert.Equal(["Dead Heat", "Rerun", "Echo Valley", "Again and Again"], everything!.Titles.Select(t => t.Title));
    }

    [Fact]
    public async Task Titles_like_another_can_be_narrowed_by_keyword_and_person_which_the_database_cannot_filter_them_by()
    {
        var (loop, _) = await Discover($"?like=Heat&about={Escaped("time loop")}");
        var (withRay, _) = await Discover($"?like=Heat&with={Escaped("Ray Dane")}");

        Assert.Equal(["Echo Valley", "Again and Again"], loop!.Titles.Select(t => t.Title));
        Assert.Empty(withRay!.Titles);
    }

    // What is new.

    [Fact]
    public async Task What_came_out_lately_is_the_last_weeks_most_looked_at_newest_first()
    {
        var before = fixture.FilmDatabase.Requests.Count;

        var released = await Get<CatalogueListView>("/api/catalogue/released");

        var discover = AskedSince(before).Single(a => a.StartsWith("/3/discover/movie"));
        Assert.Contains($"primary_release_date.gte={Today.AddDays(-CatalogueSearch.RecentDays):yyyy-MM-dd}", discover);
        Assert.Contains($"primary_release_date.lte={Today:yyyy-MM-dd}", discover);
        Assert.Contains("sort_by=popularity.desc", discover);
        Assert.Equal(("film", Today.AddDays(-CatalogueSearch.RecentDays).ToString("yyyy-MM-dd"), Today.ToString("yyyy-MM-dd")), (released!.Kind, released.From, released.To));
        Assert.Equal(["Fresh Cut", "Spring Letters"], released.Titles.Select(t => t.Title));
        Assert.Equal("tt9000301", released.Titles[0].ImdbId);
    }

    [Fact]
    public async Task What_has_a_date_coming_is_the_next_months_soonest_first_and_a_window_can_be_named()
    {
        var before = fixture.FilmDatabase.Requests.Count;

        var upcoming = await Get<CatalogueListView>("/api/catalogue/upcoming?days=30");

        var discover = AskedSince(before).Single(a => a.StartsWith("/3/discover/movie"));
        Assert.Contains($"primary_release_date.gte={Today.AddDays(1):yyyy-MM-dd}", discover);
        Assert.Contains($"primary_release_date.lte={Today.AddDays(30):yyyy-MM-dd}", discover);
        Assert.Equal(["Next Summer", "Winter Orchard"], upcoming!.Titles.Select(t => t.Title));
    }

    [Fact]
    public async Task The_lists_of_the_week_are_kept_for_hours()
    {
        await Get<CatalogueListView>("/api/catalogue/released");
        var asked = fixture.FilmDatabase.Asked;

        await Get<CatalogueListView>("/api/catalogue/released");

        Assert.Equal(asked, fixture.FilmDatabase.Asked);
    }

    [Fact]
    public async Task A_list_of_shows_is_a_list_of_shows_and_no_other_kind_is_one()
    {
        var shows = await Get<CatalogueListView>("/api/catalogue/released?kind=show");

        Assert.Equal("show", shows!.Kind);
        Assert.Equal(HttpStatusCode.BadRequest, await StatusOf("/api/catalogue/upcoming?kind=episode"));
    }

    [Fact]
    public async Task The_newest_additions_to_the_library_are_listed_newest_first_from_what_is_kept()
    {
        var before = fixture.FilmDatabase.Asked;

        var added = await Get<LibraryAdditionsView>("/api/catalogue/added");

        Assert.Equal(added!.Titles.OrderByDescending(t => t.AddedAt).Select(t => t.LibraryId), added.Titles.Select(t => t.LibraryId));
        var heat = added.Titles.Single(t => t.LibraryId == SessionFixture.Heat);
        Assert.Equal(("tt0113277", "film", "Heat", (int?)1995), (heat.ImdbId, heat.Kind, heat.Title, heat.Year));
        Assert.Equal(before, fixture.FilmDatabase.Asked);

        var one = await Get<LibraryAdditionsView>("/api/catalogue/added?limit=1");
        Assert.Single(one!.Titles);
        Assert.Empty((await Get<LibraryAdditionsView>("/api/catalogue/added?kind=show"))!.Titles);
        Assert.Equal(HttpStatusCode.BadRequest, await StatusOf("/api/catalogue/added?kind=episode"));
    }

    // A title's own page.

    [Fact]
    public async Task A_film_carries_its_keywords_and_the_library_and_watch_state_beside_them()
    {
        fixture.Services.GetRequiredService<WatchRecord>().Record("9105", SessionFixture.Heat, 5900, 6000, DateTimeOffset.UtcNow);

        var watcher = await Get<FilmView>("/api/films/tt0113277", "9105");
        var other = await Get<FilmView>("/api/films/tt0113277", "9106");

        Assert.Equal(["heist", "Los Angeles"], watcher!.Keywords.Select(k => k.Name));
        Assert.Equal((SessionFixture.Heat, true), (watcher.LibraryId, watcher.Watched));
        Assert.Equal((SessionFixture.Heat, false), (other!.LibraryId, other.Watched));
    }

    [Fact]
    public async Task A_film_the_library_does_not_hold_carries_its_keywords()
    {
        var film = await Get<FilmView>($"/api/films/{FilmDatabaseStub.SilverHarbour}");

        Assert.Equal(["silent film", "harbour"], film!.Keywords.Select(k => k.Name));
        Assert.Null(film.LibraryId);
        Assert.False(film.Watched);
    }

    [Fact]
    public void A_description_kept_before_the_database_was_asked_for_keywords_is_due_at_once()
    {
        var films = fixture.Services.GetRequiredService<FilmStore>();
        StoredFilm Kept(IReadOnlyList<string>? keywords) => new()
        {
            FetchedAt = DateTimeOffset.UtcNow,
            Details = new FilmDetails { TmdbId = 1, ImdbId = "tt0000001", Title = "Kept", Keywords = keywords, Trailers = [] },
        };

        Assert.True(films.IsDue(Kept(null)));
        Assert.False(films.IsDue(Kept([])));
        Assert.False(films.IsDue(Kept(["heist"])));
    }

    // Reading the words of a question.

    [Theory]
    [InlineData("A thief and a detective.", "A thief and a detective.")]
    [InlineData("First sentence. Second sentence.", "First sentence.")]
    [InlineData("Version 2.0 arrives. Then more.", "Version 2.0 arrives.")]
    [InlineData("  Spaced\nover lines.  ", "Spaced over lines.")]
    [InlineData(null, null)]
    [InlineData("  ", null)]
    public void A_plot_is_the_first_sentence(string? overview, string? expected)
    {
        Assert.Equal(expected, CatalogueSearch.OneLine(overview));
    }

    [Fact]
    public void A_plot_with_no_end_in_sight_is_cut_on_a_word()
    {
        var long_ = string.Join(' ', Enumerable.Repeat("wandering", 40));

        var line = CatalogueSearch.OneLine(long_)!;

        Assert.EndsWith("wandering…", line);
        Assert.True(line.Length <= 201);
    }

    [Theory]
    [InlineData("Action", "Action & Adventure")]
    [InlineData("adventure", "Action & Adventure")]
    [InlineData("Drama", "Drama")]
    [InlineData("Sci-Fi", null)]
    [InlineData("Thriller", null)]
    [InlineData("Science Fiction", "Sci-Fi & Fantasy")]
    [InlineData("war", null)]
    public void A_genre_is_found_by_its_name_or_by_the_one_genre_a_word_of_whose_name_it_is(string name, string? expected)
    {
        IReadOnlyList<CatalogueGenre> genres =
            [new() { Id = 1, Name = "Action & Adventure" }, new() { Id = 2, Name = "Drama" }, new() { Id = 3, Name = "Sci-Fi & Fantasy" }];

        Assert.Equal(expected, CatalogueSearch.MatchGenre(genres, name)?.Name);
    }

    [Theory]
    [InlineData("2020-06-01", 2019, 2021, true)]
    [InlineData("2022-06-01", 2019, 2021, false)]
    [InlineData("2018-06-01", 2019, null, false)]
    [InlineData("2018-06-01", null, 2018, true)]
    [InlineData(null, 2000, 2012, false)]
    [InlineData(null, 2000, null, true)]
    [InlineData(null, 2030, 2040, true)]
    public void A_title_with_no_date_is_in_whenever_the_years_reach_this_year_or_later(string? released, int? from, int? to, bool expected)
    {
        var today = new DateOnly(2026, 10, 9);

        Assert.Equal(expected, CatalogueSearch.InYears(released is null ? null : DateOnly.Parse(released), from, to, today));
    }

    [Theory]
    [InlineData("2026-10-09", null, "released")]
    [InlineData("2026-10-10", null, "upcoming")]
    [InlineData("2026-10-10", "Post Production", "upcoming")]
    [InlineData("2026-10-10", "Planned", "announced")]
    [InlineData("2026-10-10", "Rumored", "announced")]
    [InlineData(null, "Post Production", "announced")]
    [InlineData(null, null, "announced")]
    public void A_credit_stands_by_its_date_and_what_the_database_says_of_it(string? released, string? status, string expected)
    {
        var card = status is null ? null : new TitleCard { TmdbId = 1, Kind = "film", Title = "T", Status = status };

        Assert.Equal(expected, CatalogueSearch.StatusOf(released is null ? null : DateOnly.Parse(released), card, new DateOnly(2026, 10, 9)));
    }
}
