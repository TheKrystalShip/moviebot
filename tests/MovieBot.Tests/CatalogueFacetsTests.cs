using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using TheKrystalShip.MovieBot.Acquire.Tmdb;
using TheKrystalShip.MovieBot.Api.Films;
using Xunit;
using static TheKrystalShip.MovieBot.Tests.FilmDatabaseStub;

namespace TheKrystalShip.MovieBot.Tests;

/// <summary>
/// A filter's several values per section and the options each section's answers hold, over the Prism
/// world of the test film database, which answers a discovery by its filters: any of a section's
/// values, all of the sections, the questions the database cannot take at once asked as several and
/// paged as one list, and the facets measured without each section's own picks.
/// </summary>
public sealed class CatalogueFacetsTests(SessionFixture fixture) : IClassFixture<SessionFixture>
{
    private const string World = "/api/catalogue/discover?about=prism%20world&goodOnly=false";

    private HttpClient Signed()
    {
        var client = fixture.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", fixture.TokenFor("u12", "Ivy"));
        return client;
    }

    private async Task<T> Get<T>(string path) => (await Signed().GetFromJsonAsync<T>(path))!;

    private static List<PrismTitle> Films(Func<PrismTitle, bool> keep) =>
        [.. Prism.Where(t => t.Kind == "film").Where(keep).OrderByDescending(t => t.Popularity)];

    private static int Year(PrismTitle t) => int.Parse(t.Date[..4]);

    /// <summary>The options of a section that hold titles at the picks asked.</summary>
    private static List<string> Options(CatalogueFacetsView facets, string section) =>
        [.. facets.Sections.Single(s => s.Id == section).Options.Where(o => o.Empty != true).Select(o => o.Value)];

    /// <summary>Every option a section offers, empty or not.</summary>
    private static List<string> Offered(CatalogueFacetsView facets, string section) =>
        [.. facets.Sections.Single(s => s.Id == section).Options.Select(o => o.Value)];

    [Fact]
    public async Task Two_genres_answer_titles_of_either()
    {
        var view = await Get<CatalogueDiscoverView>(World + "&anyGenre=Horror&anyGenre=Adventure");

        var expected = Films(t => t.Genres.Contains(27) || t.Genres.Contains(12));
        Assert.Equal(expected.Select(t => t.Title), view.Titles.Select(t => t.Title));
        Assert.Equal(["Horror", "Adventure"], view.Resolved.AnyGenres);
        Assert.Contains(fixture.FilmDatabase.Requests, r => r.StartsWith("/3/discover/movie") && r.Contains("with_keywords=4242") && r.Contains("with_genres=27|12"));
    }

    [Fact]
    public async Task Decades_apart_are_two_questions_paged_as_one_list()
    {
        var first = await Get<CatalogueDiscoverView>(World + "&decade=1990&decade=2020");
        var second = await Get<CatalogueDiscoverView>(World + "&decade=1990&decade=2020&page=2");

        var expected = Films(t => Year(t) is >= 1990 and <= 1999 or >= 2020 and <= 2029);
        Assert.True(expected.Count > CatalogueSearch.ResultsListed);
        Assert.Equal((CatalogueSearch.ResultsListed, (int?)2), (first.Titles.Count, first.Next));
        Assert.Null(second.Next);
        Assert.Equal(expected.Select(t => t.Id), first.Titles.Concat(second.Titles).Select(t => t.TmdbId));
        Assert.Equal([2020, 1990], first.Resolved.Decades);
    }

    [Fact]
    public async Task Decades_next_to_each_other_are_one_window()
    {
        var before = fixture.FilmDatabase.Requests.Count;
        var view = await Get<CatalogueDiscoverView>(World + "&decade=2010&decade=2020&minVotes=1");

        Assert.Equal(Films(t => Year(t) >= 2010).Take(CatalogueSearch.ResultsListed).Select(t => t.Id), view.Titles.Select(t => t.TmdbId));
        var asked = fixture.FilmDatabase.Requests.Skip(before).Where(r => r.StartsWith("/3/discover/movie")).ToList();
        Assert.Single(asked);
        Assert.Contains("primary_release_date.gte=2010-01-01", asked[0]);
        Assert.Contains("primary_release_date.lte=2029-12-31", asked[0]);
    }

    [Fact]
    public async Task The_shortest_and_the_longest_films_together_leave_out_the_middle()
    {
        var view = await Get<CatalogueDiscoverView>(World + "&length=short&length=long");

        Assert.Equal(Films(t => t.Runtime is <= 89 or >= 151).Select(t => t.Title), view.Titles.Select(t => t.Title));
        Assert.Equal(["short", "long"], view.Resolved.Lengths);
    }

    [Fact]
    public async Task Several_languages_are_any_of_them()
    {
        var view = await Get<CatalogueDiscoverView>(World + "&language=fr&language=ko");

        Assert.Equal(Films(t => t.Language is "fr" or "ko").Select(t => t.Title), view.Titles.Select(t => t.Title));
        Assert.Contains(fixture.FilmDatabase.Requests, r => r.Contains("with_keywords=4242") && r.Contains("with_original_language=fr|ko"));
    }

    [Fact]
    public async Task Both_kinds_together_are_one_list_of_films_and_shows()
    {
        var view = await Get<CatalogueDiscoverView>(World + "&kind=all&decade=2020");

        Assert.Equal("all", view.Kind);
        Assert.Contains(view.Titles, t => t.Kind == "show" && t.Title == "Prism Tales");
        Assert.Contains(view.Titles, t => t.Kind == "film");
        Assert.Equal(Prism.Where(t => Year(t) >= 2020).OrderByDescending(t => t.Popularity).Take(CatalogueSearch.ResultsListed).Select(t => t.Id),
            view.Titles.Select(t => t.TmdbId));
    }

    [Fact]
    public async Task A_studios_facets_are_the_options_its_titles_hold()
    {
        var facets = await Get<CatalogueFacetsView>("/api/catalogue/ask/facets?q=prism");
        Assert.Empty(facets.Sorts);

        Assert.Equal(["film", "show", "anime"], Options(facets, FacetSections.Kind));
        Assert.Equal(["Adventure", "Animation", "Crime", "Family", "Horror", "Science Fiction"], Options(facets, FacetSections.Genre));
        Assert.Equal(["2020", "2010", "1990"], Options(facets, FacetSections.Decade));
        Assert.Equal(["short", "standard", "long"], Options(facets, FacetSections.Length));
        Assert.Equal(["8", "7", "6"], Options(facets, FacetSections.Rating));
        Assert.Equal(["en", "fr", "ja", "ko"], Options(facets, FacetSections.Language).Order());
        Assert.Equal("Korean", facets.Sections.Single(s => s.Id == FacetSections.Language).Options.Single(o => o.Value == "ko").Label);
        Assert.DoesNotContain(facets.Sections, s => s.Id == "studio");
    }

    [Fact]
    public async Task A_sections_options_leave_out_its_own_picks_and_follow_every_other()
    {
        var unpicked = await Get<CatalogueFacetsView>("/api/catalogue/ask/facets?q=prism");
        var facets = await Get<CatalogueFacetsView>("/api/catalogue/ask/facets?q=prism&anyGenre=Adventure");

        Assert.Equal(["Adventure", "Animation", "Crime", "Family", "Horror", "Science Fiction"], Options(facets, FacetSections.Genre));
        Assert.Equal(["2020", "2010"], Options(facets, FacetSections.Decade));
        Assert.Equal(["short", "long"], Options(facets, FacetSections.Length));
        Assert.Equal(["en", "fr"], Options(facets, FacetSections.Language).Order());
        foreach (var section in new[] { FacetSections.Genre, FacetSections.Decade, FacetSections.Length, FacetSections.Rating, FacetSections.Language })
            Assert.Equal(Offered(unpicked, section), Offered(facets, section));
    }

    [Fact]
    public async Task A_questions_picks_narrow_its_answer()
    {
        var view = await Get<CatalogueQuestionView>("/api/catalogue/ask?q=prism&anyGenre=Horror&decade=2010");

        Assert.True(view.Question);
        Assert.Equal(["studio:Prism"], view.Parts.Select(p => $"{p.Part}:{p.Text}"));
        Assert.Equal(Films(t => t.Genres.Contains(27) && Year(t) is >= 2010 and <= 2019).Select(t => t.Title), view.Titles.Select(t => t.Title));
    }

    [Fact]
    public async Task A_section_the_question_names_is_no_facet_and_takes_no_pick()
    {
        var facets = await Get<CatalogueFacetsView>("/api/catalogue/ask/facets?q=" + Uri.EscapeDataString("prism horror movies"));
        Assert.DoesNotContain(facets.Sections, s => s.Id is FacetSections.Genre or FacetSections.Kind);
        Assert.Equal(["2010", "1990"], Options(facets, FacetSections.Decade));

        var refused = await Signed().GetAsync("/api/catalogue/ask?q=" + Uri.EscapeDataString("prism horror movies") + "&anyGenre=Crime");
        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
    }

    [Fact]
    public async Task A_catalogue_filters_facets_hold_what_the_database_lists()
    {
        var facets = await Get<CatalogueFacetsView>(World.Replace("/discover?", "/discover/facets?") + "&decade=1990");
        Assert.Equal(["popular", "least-popular", "rating", "lowest", "newest", "oldest"], facets.Sorts);

        Assert.Equal(["Crime", "Horror"], Options(facets, FacetSections.Genre));
        Assert.Equal(["2020", "2010", "1990"], Options(facets, FacetSections.Decade));
        Assert.Equal(["en"], Options(facets, FacetSections.Language));
    }

    [Fact]
    public async Task A_pick_that_is_no_option_is_refused()
    {
        Assert.Equal(HttpStatusCode.BadRequest, (await Signed().GetAsync(World + "&decade=1995")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Signed().GetAsync(World + "&decade=nineties")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Signed().GetAsync(World + "&length=epic")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Signed().GetAsync(World + "&kind=show&length=short")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Signed().GetAsync(World + "&from=soon")).StatusCode);
    }

    [Fact]
    public async Task A_shelf_of_the_databases_is_narrowed_by_picks_and_any_other_shelf_is_not()
    {
        var before = fixture.FilmDatabase.Requests.Count;
        var view = await Get<ShelfView>("/api/discover/shelves/date-night?decade=1990&decade=2020");
        Assert.NotNull(view);
        var asked = fixture.FilmDatabase.Requests.Skip(before).Where(r => r.StartsWith("/3/discover/movie") && r.Contains("with_genres=10749,35")).ToList();
        Assert.Contains(asked, r => r.Contains("primary_release_date.gte=1990-01-01") && r.Contains("primary_release_date.lte=1999-12-31"));
        Assert.Contains(asked, r => r.Contains("primary_release_date.gte=2020-01-01"));

        var facets = await Get<CatalogueFacetsView>("/api/discover/shelves/date-night/facets");
        Assert.DoesNotContain(facets.Sections, s => s.Id == FacetSections.Kind);
        Assert.Contains(facets.Sections, s => s.Id == FacetSections.Decade);
        Assert.Equal(["order", "rating", "lowest", "newest", "oldest"], facets.Sorts);

    }

    [Fact]
    public async Task A_shelf_held_whole_is_narrowed_and_faceted_over_every_title_it_holds()
    {
        var picked = await Get<ShelfView>("/api/discover/shelves/star-wars?decade=1990");
        Assert.Equal(["Heat"], picked.Titles.Select(t => t.Title));
        Assert.Empty((await Get<ShelfView>("/api/discover/shelves/star-wars?decade=2010")).Titles);

        var facets = await Get<CatalogueFacetsView>("/api/discover/shelves/star-wars/facets");
        Assert.Equal(["2000", "1990"], Options(facets, FacetSections.Decade));
        Assert.All(facets.Sections.Single(s => s.Id == FacetSections.Decade).Options, o => Assert.Equal(1, o.Count));

        var narrowed = await Get<CatalogueFacetsView>("/api/discover/shelves/star-wars/facets?minRating=8");
        Assert.Equal(Offered(facets, FacetSections.Decade), Offered(narrowed, FacetSections.Decade));
        Assert.All(narrowed.Sections.Single(s => s.Id == FacetSections.Decade).Options, o => Assert.Equal(o.Count == 0, o.Empty == true));
        Assert.DoesNotContain(facets.Sections, s => s.Id is FacetSections.Kind or FacetSections.Length);
        Assert.Equal(HttpStatusCode.BadRequest, (await Signed().GetAsync("/api/discover/shelves/star-wars?kind=show")).StatusCode);
    }

    [Fact]
    public async Task A_persons_titles_are_narrowed_and_faceted_over_all_of_them()
    {
        var picked = await Get<CataloguePersonView>($"/api/catalogue/people/{PrismPerson}?anyGenre=Horror&anyGenre=Drama");
        Assert.Equal(Prism.Where(t => (t.Id < 4400 || t.Kind == "show") && (t.Genres.Contains(27) || t.Genres.Contains(18))).Select(t => t.Title).Order(),
            picked.Titles.Select(t => t.Title).Order());

        var facets = await Get<CatalogueFacetsView>($"/api/catalogue/people/{PrismPerson}/facets?kind=film");
        Assert.Equal(["film", "show", "anime"], Options(facets, FacetSections.Kind));
        Assert.Equal(["Adventure", "Animation", "Crime", "Family", "Horror", "Science Fiction"], Options(facets, FacetSections.Genre));
        Assert.Equal(["2020", "2010", "1990"], Options(facets, FacetSections.Decade));
        Assert.Equal(["en", "fr", "ja", "ko"], Options(facets, FacetSections.Language));
        Assert.Equal("Japanese", facets.Sections.Single(s => s.Id == FacetSections.Language).Options.Single(o => o.Value == "ja").Label);
        Assert.DoesNotContain(facets.Sections, s => s.Id == FacetSections.Length);
        var film = facets.Sections.Single(s => s.Id == FacetSections.Kind).Options.Single(o => o.Value == "film");
        Assert.Equal(Prism.Count(t => t.Id < 4400 && t.Kind == "film"), film.Count);
    }

    [Fact]
    public async Task A_persons_titles_are_served_in_the_order_asked_and_the_facets_list_the_orders()
    {
        var own = await Get<CataloguePersonView>($"/api/catalogue/people/{PrismPerson}");
        var newest = await Get<CataloguePersonView>($"/api/catalogue/people/{PrismPerson}?sort=newest");
        var reversed = await Get<CataloguePersonView>($"/api/catalogue/people/{PrismPerson}?sort=least-popular");
        var facets = await Get<CatalogueFacetsView>($"/api/catalogue/people/{PrismPerson}/facets");

        Assert.Equal(own.Titles.Select(t => t.Year).Where(y => y is not null).OrderDescending(), newest.Titles.Select(t => t.Year).Where(y => y is not null));
        Assert.Equal(own.Titles.Select(t => t.Title).Reverse(), reversed.Titles.Select(t => t.Title));
        Assert.Equal(["popular", "least-popular", "rating", "lowest", "newest", "oldest"], facets.Sorts);
        Assert.Equal(HttpStatusCode.BadRequest, (await Signed().GetAsync($"/api/catalogue/people/{PrismPerson}?sort=shortest")).StatusCode);
    }

    [Fact]
    public async Task A_genres_titles_are_narrowed_and_faceted_and_its_genre_is_its_own()
    {
        var picked = await Get<CatalogueGenreView>("/api/catalogue/genre?kind=film&name=crime&decade=1990&language=en");
        Assert.All(picked.Titles, t => Assert.InRange(t.Year!.Value, 1990, 1999));
        Assert.Equal(Prism.Count(t => t.Kind == "film" && t.Genres.Contains(80) && Year(t) < 2000), picked.Titles.Count);

        var facets = await Get<CatalogueFacetsView>("/api/catalogue/genre/facets?kind=film&name=crime&decade=1990");
        Assert.DoesNotContain(facets.Sections, s => s.Id is FacetSections.Genre or FacetSections.Kind);
        Assert.Equal(["2020", "2010", "1990"], Options(facets, FacetSections.Decade));
        Assert.Equal(["en"], Options(facets, FacetSections.Language));
        Assert.Equal(HttpStatusCode.BadRequest, (await Signed().GetAsync("/api/catalogue/genre?kind=film&name=crime&anyGenre=Horror")).StatusCode);
    }

    [Fact]
    public async Task One_genre_is_picked_once_for_films_and_shows()
    {
        var facets = await Get<CatalogueFacetsView>(World.Replace("/discover?", "/discover/facets?") + "&kind=all");
        var genres = Options(facets, FacetSections.Genre);
        Assert.Contains("Science Fiction", genres);
        Assert.Contains("Fantasy", genres);
        Assert.Contains("Drama", genres);
        Assert.DoesNotContain("Sci-Fi & Fantasy", genres);
        Assert.Equal(genres.Distinct().Count(), genres.Count);

        var picked = await Get<CatalogueDiscoverView>(World + "&kind=all&anyGenre=Science%20Fiction");
        Assert.Equal(["Prism Orbit", "Prism Stars"], picked.Titles.Select(t => t.Title));
        Assert.Contains(fixture.FilmDatabase.Requests, r => r.StartsWith("/3/discover/tv") && r.Contains("with_genres=10765"));

        var person = await Get<CatalogueFacetsView>($"/api/catalogue/people/{PrismPerson}/facets");
        var held = Options(person, FacetSections.Genre);
        Assert.Contains("Science Fiction", held);
        Assert.DoesNotContain("Sci-Fi & Fantasy", held);
        Assert.Equal(2, person.Sections.Single(s => s.Id == FacetSections.Genre).Options.Single(o => o.Value == "Science Fiction").Count);
        var both = await Get<CataloguePersonView>($"/api/catalogue/people/{PrismPerson}?anyGenre=Science%20Fiction");
        Assert.Equal(["Prism Orbit", "Prism Stars"], both.Titles.Select(t => t.Title).Order());
    }

    [Fact]
    public void The_sites_genre_vocabulary_is_the_apis()
    {
        var root = AppContext.BaseDirectory;
        while (!File.Exists(Path.Combine(root, "moviebot.slnx"))) root = Path.GetDirectoryName(root)!;
        var script = File.ReadAllText(Path.Combine(root, "web", "site", "src", "facets.js"));
        var table = script[script.IndexOf("JOINED_GENRES = {", StringComparison.Ordinal)..];
        table = table[..table.IndexOf("};", StringComparison.Ordinal)];
        var site = System.Text.RegularExpressions.Regex.Matches(table, "\"([^\"]+)\": \\[([^\\]]*)\\]")
            .ToDictionary(m => m.Groups[1].Value, m => System.Text.RegularExpressions.Regex.Matches(m.Groups[2].Value, "\"([^\"]+)\"").Select(n => n.Groups[1].Value).ToArray());

        Assert.Equal(CinemaGenres.Joined.Keys.Order(), site.Keys.Order());
        foreach (var (name, joined) in CinemaGenres.Joined) Assert.Equal(joined, site[name]);
    }

    [Fact]
    public void A_pick_of_genres_on_a_question_of_genres_is_one_question_per_pick()
    {
        var query = new DiscoverQuery { Kind = "film", GenreIds = [16] };
        var asked = DiscoverNarrowing.Narrow(query, new Narrowing { AnyGenreIds = [27, 80] });
        Assert.Equal([[16, 27], [16, 80]], asked.Select(q => q.GenreIds.ToArray()));

        var already = DiscoverNarrowing.Narrow(query, new Narrowing { AnyGenreIds = [16, 80] });
        Assert.Equal([16], Assert.Single(already).GenreIds);

        var any = DiscoverNarrowing.Narrow(new DiscoverQuery { Kind = "film", AnyGenreIds = [80, 53] }, new Narrowing { AnyGenreIds = [80, 27] });
        Assert.Equal([[80], [53, 27]], any.Select(q => q.GenreIds.ToArray()));
    }

    [Fact]
    public void A_window_outside_the_questions_years_asks_nothing()
    {
        var query = new DiscoverQuery { Kind = "film", From = new DateOnly(2000, 1, 1), To = new DateOnly(2009, 12, 31) };
        var asked = DiscoverNarrowing.Narrow(query, new Narrowing { Windows = DiscoverNarrowing.Windows([1990, 2000, 2020]) });

        var window = Assert.Single(asked);
        Assert.Equal((new DateOnly(2000, 1, 1), new DateOnly(2009, 12, 31)), (window.From, window.To));
        Assert.Empty(DiscoverNarrowing.Narrow(new DiscoverQuery { Kind = "film", Language = "en" }, new Narrowing { Languages = ["fr"] }));
    }
}
