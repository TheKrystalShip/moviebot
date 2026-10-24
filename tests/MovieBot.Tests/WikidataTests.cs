using System.Diagnostics;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using TheKrystalShip.MovieBot.Api.Films;
using Xunit;

namespace TheKrystalShip.MovieBot.Tests;

/// <summary>The client alone, over Wikidata as the test answers it: who it says it is, how many it asks at once, and what it does when told to wait.</summary>
public sealed class WikidataClientTests
{
    private static WikidataClient Client(WikidataStub stub, int atOnce = 2)
    {
        var options = Options.Create(new WikidataOptions { Contact = "https://contact.example", MaxAtOnce = atOnce });
        return new WikidataClient(new HttpClient(stub), options, new WikidataGate(options, TimeProvider.System), TimeProvider.System);
    }

    [Fact]
    public async Task Every_request_says_who_asks_and_a_set_is_read_from_the_answer()
    {
        var stub = new WikidataStub();

        var films = await Client(stub).FilmsAsync(WikidataRelation.Genre, "Q853630", CancellationToken.None);

        Assert.Equal(["Heat", "Forgotten Reel", "Dream Detective"], films.Select(f => f.Title));
        Assert.Equal([949, 7701, 7401], films.Select(f => f.TmdbId!.Value));
        Assert.Equal(new DateOnly(1995, 12, 15), films[0].Released);
        var agent = Assert.Single(stub.Agents);
        Assert.StartsWith("Cinema/", agent);
        Assert.Contains("(https://contact.example)", agent);
    }

    [Fact]
    public async Task A_name_is_sent_to_the_entity_search_and_read_in_the_order_it_ranks_them()
    {
        var found = await Client(new WikidataStub()).SearchAsync("Paris", CancellationToken.None);

        Assert.Equal(["Q483020", "Q90"], found.Select(e => e.Id));
        Assert.Equal("Paris", found[1].Label);
    }

    [Fact]
    public async Task Too_many_requests_is_waited_out_as_long_as_wikidata_says_and_asked_again()
    {
        var stub = new WikidataStub { RetryAfterSeconds = 1 };
        stub.Limit(1);
        var clock = Stopwatch.StartNew();

        var films = await Client(stub).FilmsAsync(WikidataRelation.Genre, "Q496523", CancellationToken.None);

        Assert.Single(films);
        Assert.Equal(2, stub.Asked);
        Assert.True(clock.Elapsed >= TimeSpan.FromMilliseconds(900), $"waited only {clock.Elapsed}");
    }

    [Fact]
    public async Task A_wikidata_that_keeps_refusing_is_given_up_on_after_three_asks()
    {
        var stub = new WikidataStub();
        stub.Limit(10);

        await Assert.ThrowsAsync<HttpRequestException>(() => Client(stub).FilmsAsync(WikidataRelation.Genre, "Q496523", CancellationToken.None));

        Assert.Equal(3, stub.Asked);
    }

    [Fact]
    public async Task At_most_the_configured_number_of_questions_are_in_flight()
    {
        var stub = new WikidataStub { Delay = TimeSpan.FromMilliseconds(60) };
        var client = Client(stub, atOnce: 2);

        await Task.WhenAll(Enumerable.Range(0, 6).Select(_ => client.FilmsAsync(WikidataRelation.Genre, "Q496523", CancellationToken.None)));

        Assert.Equal(6, stub.Asked);
        Assert.Equal(2, stub.MostInFlight);
    }

    [Fact]
    public async Task Facts_are_the_awards_won_the_nominations_and_the_work_a_film_is_based_on()
    {
        var facts = await Client(new WikidataStub()).FactsAsync(WikidataStub.Heat, CancellationToken.None);

        Assert.Equal([new WikidataAward("Test Prize", 1996)], facts.Won);
        Assert.Equal(2, facts.Nominations);
        Assert.Equal([new WikidataWork("The Heat Novel", "Ann Writer")], facts.BasedOn);
    }
}

public sealed class WikidataFixture : SessionFixture
{
    protected override Dictionary<string, string?> MoreSettings() => new()
    {
        ["Wikidata:Enabled"] = "true",
        ["Wikidata:WarmMinutes"] = "0",
    };
}

/// <summary>
/// The shelves Wikidata fills: absent and empty until the weekly cycle has kept their sets, never
/// asked of Wikidata by a page, then listed in their order a page at a time and joined with the
/// library like any other shelf.
/// </summary>
public sealed class WikidataShelfTests(WikidataFixture fixture) : IClassFixture<WikidataFixture>
{
    private HttpClient Signed()
    {
        var client = fixture.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", fixture.TokenFor("w1", "Gil"));
        return client;
    }

    [Fact]
    public async Task A_page_reads_what_the_cycle_kept_and_never_asks_wikidata_itself()
    {
        var client = Signed();
        var asked = fixture.Wikidata.SetQueries.Count;

        var before = (await client.GetFromJsonAsync<DiscoverIndexView>("/api/discover"))!;
        var empty = (await client.GetFromJsonAsync<ShelfView>("/api/discover/shelves/set-in-new-york"))!;

        Assert.DoesNotContain(before.Sections, s => s.Id is "awards" or "adaptations" or "places");
        Assert.Empty(empty.Titles);
        Assert.Equal(asked, fixture.Wikidata.SetQueries.Count);

        var sets = fixture.Services.GetRequiredService<WikidataSets>();
        Assert.Equal(0, await sets.WarmAsync(CancellationToken.None));
        var kept = fixture.Wikidata.SetQueries.Count;
        Assert.Equal(WikidataSets.Fixed.Count, kept - asked);

        var first = (await client.GetFromJsonAsync<ShelfView>("/api/discover/shelves/set-in-new-york"))!;
        var second = (await client.GetFromJsonAsync<ShelfView>("/api/discover/shelves/set-in-new-york?page=2"))!;
        var after = (await client.GetFromJsonAsync<DiscoverIndexView>("/api/discover"))!;

        Assert.Equal(CatalogueSearch.ShelfListed, first.Titles.Count);
        Assert.Equal(2, first.Next);
        Assert.Equal("Paged 0", first.Titles[0].Title);
        Assert.Equal("Places", first.Section);
        Assert.Equal(FilmDatabaseStub.PagedFilms - CatalogueSearch.ShelfListed, second.Titles.Count);
        Assert.Null(second.Next);
        Assert.Equal("Paged 40", second.Titles[0].Title);
        Assert.Equal(kept, fixture.Wikidata.SetQueries.Count);

        // A set the cycle kept with no films stays off the page; the others are there in their sections.
        var places = after.Sections.Single(s => s.Id == "places");
        Assert.Equal(["set-in-paris", "set-in-new-york"], places.Shelves.Select(s => s.Id));
        Assert.DoesNotContain(after.Sections, s => s.Id == "adaptations" && s.Shelves.Any(h => h.Id == "jane-austen"));

        // The weekly cycle asks nothing of a set that is kept.
        await sets.WarmAsync(CancellationToken.None);
        Assert.Equal(kept, fixture.Wikidata.SetQueries.Count);
    }

}

/// <summary>An awards shelf once the cycle has kept its set. It has a host of its own because the shelves test above needs the sets cold.</summary>
public sealed class WikidataAwardsShelfTests(WikidataFixture fixture) : IClassFixture<WikidataFixture>
{
    private HttpClient Signed()
    {
        var client = fixture.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", fixture.TokenFor("w1", "Gil"));
        return client;
    }

    [Fact]
    public async Task An_awards_shelf_lists_the_newest_first_joined_with_the_library()
    {
        await fixture.Services.GetRequiredService<WikidataSets>().WarmAsync(CancellationToken.None);

        var shelf = (await Signed().GetFromJsonAsync<ShelfView>("/api/discover/shelves/best-picture-winners"))!;

        Assert.Equal(["Dream Detective", "Pirates of the Caribbean", "Forgotten Reel", "Heat"], shelf.Titles.Select(t => t.Title));
        var heat = shelf.Titles.Single(t => t.ImdbId == WikidataStub.Heat);
        Assert.True(heat.InLibrary);
        Assert.NotNull(heat.LibraryId);
        Assert.False(shelf.Titles.Single(t => t.ImdbId == WikidataStub.Pirates).InLibrary);
        Assert.Equal("Awards", shelf.Section);
    }
}

/// <summary>Words that name a set Wikidata lists, answered over the film database's titles: resolved by name, kept, and narrowed by the other words.</summary>
public sealed class WikidataQuestionTests(WikidataFixture fixture) : IClassFixture<WikidataFixture>
{
    private async Task<CatalogueQuestionView> Ask(string text)
    {
        var client = fixture.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", fixture.TokenFor("w2", "Gil"));
        return (await client.GetFromJsonAsync<CatalogueQuestionView>("/api/catalogue/ask?q=" + Uri.EscapeDataString(text)))!;
    }

    [Fact]
    public async Task An_award_is_a_closed_phrase_and_lists_its_films_newest_first()
    {
        var view = await Ask("oscar winners");

        Assert.True(view.Question);
        Assert.Equal(["award:Best Picture winners"], view.Parts.Select(p => $"{p.Part}:{p.Text}"));
        Assert.Equal(["Dream Detective", "Pirates of the Caribbean", "Forgotten Reel", "Heat"], view.Titles.Select(t => t.Title));
        Assert.Null(view.Message);
    }

    [Fact]
    public async Task An_authors_name_is_resolved_by_wikidatas_entity_search_and_their_adaptations_listed()
    {
        var view = await Ask("best stephen king adaptations");

        Assert.Equal(["sort:Best rated", "adaptation:Adaptations of Stephen King"], view.Parts.Select(p => $"{p.Part}:{p.Text}"));
        Assert.Equal(["Pirates of the Caribbean", "Heat"], view.Titles.Select(t => t.Title));
        Assert.Contains(fixture.Wikidata.Queries, q => q.Contains("wdt:P144 ?work. ?work wdt:P50 wd:Q39829."));
    }

    [Fact]
    public async Task A_name_is_the_item_with_that_exact_label_and_not_the_first_the_search_ranks()
    {
        var view = await Ask("set in paris");

        Assert.Equal(["place:Set in Paris"], view.Parts.Select(p => $"{p.Part}:{p.Text}"));
        Assert.Equal(2, view.Titles.Count);
        Assert.Contains(fixture.Wikidata.Queries, q => q.Contains("wdt:P840 wd:Q90."));
        Assert.DoesNotContain(fixture.Wikidata.Queries, q => q.Contains("Q483020"));
    }

    [Fact]
    public async Task A_name_wikidata_does_not_know_is_a_question_with_no_titles_and_a_reason()
    {
        var view = await Ask("set in atlantis");

        Assert.True(view.Question);
        Assert.Empty(view.Titles);
        Assert.Contains("atlantis", view.Message);
    }

    [Fact]
    public async Task A_subgenre_narrowed_by_a_genre_keeps_the_titles_whose_cards_have_it()
    {
        var view = await Ask("horror slasher movies");

        Assert.Equal(["Forgotten Reel"], view.Titles.Select(t => t.Title));
        Assert.Equal(["Horror"], view.Resolved!.Genres);
    }

    [Fact]
    public async Task A_subgenre_narrowed_by_years_and_ordered_by_a_sort_word()
    {
        var years = await Ask("slasher movies from the 2000s");
        var newest = await Ask("newest slasher movies");

        Assert.Equal(["Forgotten Reel", "Dream Detective"], years.Titles.Select(t => t.Title));
        Assert.Equal(["Dream Detective", "Forgotten Reel", "Heat"], newest.Titles.Select(t => t.Title));
    }

    [Fact]
    public async Task A_count_bounds_the_titles_and_a_set_asked_for_once_is_kept()
    {
        var first = await Ask("top 1 heist movies");
        var asked = fixture.Wikidata.AskedAbout("Q496523");
        var second = await Ask("heist movies");

        Assert.Equal(["Heat"], first.Titles.Select(t => t.Title));
        Assert.Equal(["Heat"], second.Titles.Select(t => t.Title));
        Assert.Equal(1, asked);
        Assert.Equal(asked, fixture.Wikidata.AskedAbout("Q496523"));
    }

    [Fact]
    public async Task A_sets_films_are_narrowed_by_picks_and_faceted_over_the_whole_set()
    {
        var client = fixture.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", fixture.TokenFor("w2", "Gil"));

        var picked = (await client.GetFromJsonAsync<CatalogueQuestionView>("/api/catalogue/ask?q=" + Uri.EscapeDataString("oscar winners") + "&decade=1990"))!;
        Assert.Equal(["Heat"], picked.Titles.Select(t => t.Title));

        var facets = (await client.GetFromJsonAsync<CatalogueFacetsView>("/api/catalogue/ask/facets?q=" + Uri.EscapeDataString("oscar winners") + "&language=en"))!;
        Assert.DoesNotContain(facets.Sections, s => s.Id == FacetSections.Kind);
        Assert.Equal(["2000", "1990"], facets.Sections.Single(s => s.Id == FacetSections.Decade).Options.Select(o => o.Value));
        Assert.Equal(["en", "ja"], facets.Sections.Single(s => s.Id == FacetSections.Language).Options.Select(o => o.Value));
        Assert.Contains("Crime", facets.Sections.Single(s => s.Id == FacetSections.Genre).Options.Select(o => o.Value));
        Assert.DoesNotContain("Animation", facets.Sections.Single(s => s.Id == FacetSections.Genre).Options.Select(o => o.Value));

        var category = (await client.GetFromJsonAsync<CatalogueFacetsView>("/api/discover/sections/awards/facets"))!;
        Assert.Contains("1990", category.Sections.Single(s => s.Id == FacetSections.Decade).Options.Select(o => o.Value));
        Assert.DoesNotContain(category.Sections, s => s.Id == FacetSections.Kind);
    }

    [Fact]
    public async Task What_wikidata_cannot_narrow_is_answered_with_the_reason()
    {
        var shows = await Ask("slasher shows");

        Assert.True(shows.Question);
        Assert.Empty(shows.Titles);
        Assert.Contains("films", shows.Message);
    }
}

/// <summary>A film's awards and what it is based on, from Wikidata by its IMDb id, kept and asked for in the background.</summary>
public sealed class WikidataFactsTests(WikidataFixture fixture) : IClassFixture<WikidataFixture>
{
    [Fact]
    public async Task A_film_carries_its_awards_and_its_source_once_they_are_kept_and_is_never_kept_waiting_for_them()
    {
        var client = fixture.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", fixture.TokenFor("w3", "Gil"));

        var first = (await client.GetFromJsonAsync<FilmView>("/api/films/" + WikidataStub.Heat))!;
        Assert.Null(first.Awards);
        Assert.Empty(first.BasedOn);

        FilmView? kept = null;
        for (var i = 0; i < 100 && kept?.Awards is null; i++)
        {
            await Task.Delay(50);
            kept = await client.GetFromJsonAsync<FilmView>("/api/films/" + WikidataStub.Heat);
        }

        Assert.NotNull(kept!.Awards);
        Assert.Equal(1, kept.Awards.Wins);
        Assert.Equal([new FilmAwardView("Test Prize", 1996)], kept.Awards.Won);
        Assert.Equal(2, kept.Awards.Nominations);
        Assert.Equal([new FilmBasedOnView("The Heat Novel", "Ann Writer")], kept.BasedOn);
        var asked = fixture.Wikidata.Asked;
        await client.GetFromJsonAsync<FilmView>("/api/films/" + WikidataStub.Heat);
        Assert.Equal(asked, fixture.Wikidata.Asked);
    }
}
