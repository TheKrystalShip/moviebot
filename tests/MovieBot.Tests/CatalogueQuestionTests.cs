using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using TheKrystalShip.MovieBot.Api.Films;
using Xunit;

namespace TheKrystalShip.MovieBot.Tests;

/// <summary>
/// Questions typed into Discover, answered by <c>/api/catalogue/ask</c> over the test film database:
/// what each was read as, what the database was asked, and the words that are a title's name instead.
/// </summary>
public sealed class CatalogueQuestionTests(SessionFixture fixture) : IClassFixture<SessionFixture>
{
    private HttpClient Signed()
    {
        var client = fixture.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", fixture.TokenFor("u7", "Gil"));
        return client;
    }

    private async Task<(CatalogueQuestionView View, List<string> Asked)> Ask(string text)
    {
        var before = fixture.FilmDatabase.Requests.Count;
        var view = await Signed().GetFromJsonAsync<CatalogueQuestionView>("/api/catalogue/ask?q=" + Uri.EscapeDataString(text));
        return (view!, [.. fixture.FilmDatabase.Requests.Skip(before)]);
    }

    private static string Parts(CatalogueQuestionView view) => string.Join(" | ", view.Parts.Select(p => $"{p.Part}:{p.Text}"));

    [Fact]
    public async Task The_best_horror_of_one_year_is_a_discovery_of_the_genre_in_that_year()
    {
        var (view, asked) = await Ask("Top 10 horror movies from 2000");

        Assert.True(view.Question);
        Assert.Equal("count:Top 10 | genre:Horror | kind:Movies | years:2000", Parts(view));
        Assert.Equal("rating", view.Resolved!.Sort);
        Assert.Equal(["The Cellar Door", "Hollow Hill"], view.Titles.Select(t => t.Title));
        var discover = asked.First(a => a.StartsWith("/3/discover/movie"));
        Assert.Contains("with_genres=27", discover);
        Assert.Contains("primary_release_date.gte=2000-01-01", discover);
        Assert.Contains("primary_release_date.lte=2000-12-31", discover);
    }

    [Fact]
    public async Task Words_before_a_kind_are_a_person_by_their_exact_name()
    {
        var (view, asked) = await Ask("jo test movies");

        Assert.True(view.Question);
        Assert.Equal("person:Jo Test | kind:Movies", Parts(view));
        Assert.Contains(asked, a => a.StartsWith("/3/discover/movie") && a.Contains("with_people=7777"));
    }

    [Fact]
    public async Task A_person_and_a_subject_after_markers_narrow_one_discovery()
    {
        var (view, _) = await Ask("films with ray dane about silent film");

        Assert.True(view.Question);
        Assert.Equal("kind:Movies | person:Ray Dane | about:silent film", Parts(view));
        Assert.Equal(["Silver Harbour"], view.Titles.Select(t => t.Title));
    }

    [Fact]
    public async Task Words_before_a_kind_that_name_nobody_and_no_keyword_are_a_title_s_name()
    {
        var (view, asked) = await Ask("heat films");

        Assert.False(view.Question);
        Assert.Empty(view.Titles);
        Assert.DoesNotContain(asked, a => a.StartsWith("/3/discover"));
    }

    [Fact]
    public async Task Words_a_rule_leaves_unread_ask_only_whether_a_studio_is_called_so()
    {
        var (view, asked) = await Ask("Horror Express");

        Assert.False(view.Question);
        Assert.All(asked.Where(a => a.StartsWith("/3/")), a => Assert.StartsWith("/3/search/company", a));
    }

    [Fact]
    public async Task A_studio_alone_is_every_company_filed_under_its_name_with_no_rating_floor()
    {
        var (view, asked) = await Ask("blumhouse");

        Assert.True(view.Question);
        Assert.Equal("studio:Blumhouse", Parts(view));
        Assert.Equal(["Blumhouse"], view.Resolved!.Studios);
        Assert.Equal(12, view.Titles.Count);
        var discover = asked.Single(a => a.StartsWith("/3/discover/movie"));
        Assert.Contains("with_companies=68884|3172", discover);
        Assert.Contains("vote_count.gte=50", discover);
        Assert.DoesNotContain("vote_average.gte", discover);
    }

    [Fact]
    public async Task Words_before_a_kind_that_name_a_studio_are_its_work()
    {
        var (view, _) = await Ask("blumhouse movies");

        Assert.True(view.Question);
        Assert.Equal("studio:Blumhouse | kind:Movies", Parts(view));
        Assert.Equal(12, view.Titles.Count);
    }

    [Fact]
    public async Task A_company_called_exactly_the_words_outranks_a_keyword_of_them()
    {
        var (movies, asked) = await Ask("paged movies");
        var (bare, _) = await Ask("paged");

        Assert.Equal("studio:Paged", Parts(bare));
        Assert.Equal("studio:Paged | kind:Movies", Parts(movies));
        Assert.Contains(asked, a => a.StartsWith("/3/discover/movie") && a.Contains("with_companies=557"));
    }

    [Fact]
    public async Task A_studio_s_name_alone_that_is_also_a_title_s_or_made_a_few_films_is_no_question()
    {
        var (heat, heatAsked) = await Ask("heat");
        var (tiny, _) = await Ask("tiny");

        Assert.False(heat.Question);
        Assert.DoesNotContain(heatAsked, a => a.StartsWith("/3/discover"));
        Assert.False(tiny.Question);
    }

    [Fact]
    public void A_studio_is_matched_by_whole_words_and_shown_as_the_database_spells_it()
    {
        var studio = CatalogueSearch.MatchStudio(
            [new() { Id = 1, Name = "Studio Ghibli" }, new() { Id = 2, Name = "Ghiblin" }, new() { Id = 3, Name = "Ghibli Museum Library" }], "ghibli");

        Assert.Equal("Ghibli", studio!.Name);
        Assert.Equal([1, 3], studio.CompanyIds);
        Assert.False(studio.Exact);
        Assert.True(CatalogueSearch.MatchStudio([new() { Id = 4, Name = "A24" }], "a24")!.Exact);
        Assert.Null(CatalogueSearch.MatchStudio([new() { Id = 2, Name = "Ghiblin" }], "ghibli"));
    }

    [Fact]
    public async Task A_question_the_database_refuses_is_still_a_question_with_its_reason()
    {
        var (view, _) = await Ask("horror shows");

        Assert.True(view.Question);
        Assert.Empty(view.Titles);
        Assert.Contains("Horror", view.Message);
    }

    [Fact]
    public async Task A_genre_and_a_language_are_recognised_from_the_database_s_own_lists()
    {
        var (view, _) = await Ask("scary korean movies");

        Assert.True(view.Question);
        Assert.Equal("genre:Horror | language:Korean | kind:Movies", Parts(view));
    }

    [Fact]
    public async Task Nothing_typed_is_refused()
    {
        using var response = await Signed().GetAsync("/api/catalogue/ask?q=%20");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
