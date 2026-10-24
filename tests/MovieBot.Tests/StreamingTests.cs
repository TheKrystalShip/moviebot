using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using TheKrystalShip.MovieBot.Api.Films;
using TheKrystalShip.MovieBot.Api.Shows;
using Xunit;

namespace TheKrystalShip.MovieBot.Tests;

/// <summary>
/// What a description carries beyond its facts (trailers, where it streams, recommendations, reviews,
/// the logo, box office, keywords), the streaming shelves of the region, and the questions that say
/// <c>on netflix</c>. The film database is answered by the test, Heat's description with everything.
/// </summary>
public sealed class StreamingTests(SessionFixture fixture) : IClassFixture<SessionFixture>
{
    private HttpClient Signed(string userId = "s1")
    {
        var client = fixture.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", fixture.TokenFor(userId, "Gil"));
        return client;
    }

    private Task<T?> Get<T>(string path) => Signed().GetFromJsonAsync<T>(path);

    private async Task<HttpResponseMessage> Open(string path) => await fixture.CreateClient().GetAsync(path);

    [Fact]
    public async Task A_film_lists_its_trailers_official_first_and_leaves_out_what_is_not_a_trailer_or_teaser()
    {
        var film = await Get<FilmView>("/api/films/tt0113277");

        Assert.Equal(["heat-trailer", "heat-teaser", "heat-fan"], film!.Trailers.Select(t => t.Key));
        Assert.Equal(("Official Trailer", "Trailer", true, "en"),
            (film.Trailers[0].Name, film.Trailers[0].Kind, film.Trailers[0].Official, film.Trailers[0].Language));
        Assert.False(film.Trailers[2].Official);
    }

    [Fact]
    public async Task A_film_carries_its_tagline_box_office_keywords_with_ids_and_reviews()
    {
        var film = await Get<FilmView>("/api/films/tt0113277");

        Assert.Equal("A Los Angeles crime saga", film!.Tagline);
        Assert.Equal((60000000L, 187436818L), (film.Budget, film.Revenue));
        Assert.Equal([(818, "heist"), (9, "Los Angeles")], film.Keywords.Select(k => (k.Id, k.Name)));
        Assert.Equal(2, film.ReviewCount);
        var review = Assert.Single(film.Reviews);
        Assert.Equal(("Critic", 9.0, "A masterpiece of the genre.", "https://reviews.example/1"), (review.Author, review.Rating, review.Excerpt, review.Url));
    }

    [Fact]
    public async Task A_film_s_providers_are_the_regions_with_their_logos_served_from_here()
    {
        var film = await Get<FilmView>("/api/films/tt0113277");

        var watch = film!.Watch!;
        Assert.Equal(("US", "https://watch.example/heat"), (watch.Region, watch.Link));
        var netflix = Assert.Single(watch.Flatrate);
        Assert.Equal((8, "Netflix", "/catalogue/providers/8.png"), (netflix.ProviderId, netflix.Name, netflix.LogoUrl));
        Assert.Equal(["Apple TV Store"], watch.Rent.Select(p => p.Name));
        Assert.Empty(watch.Free);

        using var logo = await Open(netflix.LogoUrl!);
        Assert.Equal(HttpStatusCode.OK, logo.StatusCode);
        Assert.Equal("image/png", logo.Content.Headers.ContentType?.MediaType);
        using var missing = await Open("/catalogue/providers/999.png");
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        using var notAnId = await Open("/catalogue/providers/x.png");
        Assert.Equal(HttpStatusCode.NotFound, notAnId.StatusCode);
    }

    [Fact]
    public async Task A_film_s_logo_is_kept_and_served_from_here_as_a_png()
    {
        var film = await Get<FilmView>("/api/films/tt0113277");

        Assert.Equal("/films/tt0113277/logo.png", film!.LogoUrl);
        using var logo = await Open(film.LogoUrl!);
        Assert.Equal(HttpStatusCode.OK, logo.StatusCode);
        Assert.Equal("image/png", logo.Content.Headers.ContentType?.MediaType);
        using var poster = await Open(film.PosterUrl!);
        Assert.Equal("image/jpeg", poster.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task Recommendations_are_title_cards_joined_with_the_library_like_any_list_of_titles()
    {
        var film = await Get<FilmView>("/api/films/tt0113277");

        Assert.Equal(["Echo Valley", "Again and Again"], film!.Recommendations.Select(t => t.Title));
        var echo = film.Recommendations[0];
        Assert.Equal(("tt9000201", "film", (int?)2019), (echo.ImdbId, echo.Kind, echo.Year));
        Assert.NotNull(echo.PosterUrl);
        Assert.False(echo.InLibrary);
        Assert.Equal(["Una Fox"], echo.MadeBy);
    }

    [Fact]
    public async Task One_description_call_carries_it_all()
    {
        var before = fixture.FilmDatabase.Requests.Count;
        await Get<FilmView>("/api/films/tt0113277");
        await Get<FilmView>("/api/films/tt0113277");

        // A title card (credits alone) is another, smaller question.
        static bool Description(string r) => r.StartsWith("/3/movie/949?") && !r.Contains("append_to_response=credits&");
        Assert.True(fixture.FilmDatabase.Requests.Skip(before).Count(Description) <= 1);
        var all = fixture.FilmDatabase.Requests.Where(Description).ToList();
        Assert.NotEmpty(all);
        Assert.All(all, r => Assert.Contains("append_to_response=credits,release_dates,keywords,videos,recommendations,reviews,images,watch/providers", r));
        Assert.DoesNotContain(fixture.FilmDatabase.Requests, r => r.Contains("/movie/949/videos") || r.Contains("/movie/949/reviews") || r.Contains("/movie/949/watch/providers"));
    }

    [Fact]
    public async Task The_streaming_section_has_a_shelf_of_films_and_of_series_for_each_service_the_region_lists()
    {
        var index = await Get<DiscoverIndexView>("/api/discover");

        var section = index!.Sections.Single(s => s.Id == "streaming");
        Assert.Equal("Streaming", section.Label);
        Assert.Equal(
            [("streaming-8-film", "Netflix", "film"), ("streaming-8-show", "Netflix series", "show"),
             ("streaming-119-film", "Amazon Prime Video", "film"),
             ("streaming-337-film", "Disney Plus", "film"), ("streaming-337-show", "Disney Plus series", "show")],
            section.Shelves.Select(s => (s.Id, s.Title, s.Kind!)));
    }

    [Fact]
    public async Task A_streaming_shelf_asks_for_the_regions_titles_on_that_service_and_pages_like_the_rest()
    {
        await Get<DiscoverIndexView>("/api/discover");
        var shelf = await Get<ShelfView>("/api/discover/shelves/streaming-8-film");

        Assert.Equal(("Netflix", "Streaming", "film"), (shelf!.Title, shelf.Section, shelf.Kind));
        Assert.Equal(["The Cellar Door", "Echo Valley"], shelf.Titles.Select(t => t.Title));
        // The answer is kept, so whichever call first needed it asked the database once.
        var discovery = Assert.Single(fixture.FilmDatabase.Requests,
            r => r.StartsWith("/3/discover/movie") && r.Contains("with_watch_providers=8") && !r.Contains("with_genres"));
        Assert.Contains("with_watch_providers=8", discovery);
        Assert.Contains("watch_region=US", discovery);
        Assert.Contains("with_watch_monetization_types=flatrate|free|ads", discovery);
        Assert.Contains("vote_count.gte=100", discovery);

        using var none = await Signed().GetAsync("/api/discover/shelves/streaming-5-film");
        Assert.Equal(HttpStatusCode.NotFound, none.StatusCode);
    }

    [Fact]
    public async Task A_question_that_names_a_service_asks_the_database_for_it_in_any_order()
    {
        var view = await Get<CatalogueQuestionView>("/api/catalogue/ask?q=" + Uri.EscapeDataString("horror on netflix"));

        Assert.True(view!.Question);
        Assert.Equal("genre:Horror | provider:Netflix", string.Join(" | ", view.Parts.Select(p => $"{p.Part}:{p.Text}")));
        Assert.Equal(["Netflix"], view.Resolved!.Providers);
        Assert.Equal(["The Cellar Door"], view.Titles.Select(t => t.Title));
        // The answer is kept, so whichever call first needed it asked the database once.
        var discovery = Assert.Single(fixture.FilmDatabase.Requests,
            r => r.StartsWith("/3/discover/movie") && r.Contains("with_genres=27") && r.Contains("with_watch_providers=8"));
        Assert.Contains("with_genres=27", discovery);
        Assert.Contains("with_watch_providers=8", discovery);
        Assert.Contains("watch_region=US", discovery);

        var reversed = await Get<CatalogueQuestionView>("/api/catalogue/ask?q=" + Uri.EscapeDataString("on netflix horror"));
        Assert.Equal(view.Titles.Select(t => t.ImdbId), reversed!.Titles.Select(t => t.ImdbId));
    }

    [Fact]
    public async Task A_question_that_says_prime_means_the_service_the_database_lists_as_amazon_prime_video()
    {
        var view = await Get<CatalogueQuestionView>("/api/catalogue/ask?q=" + Uri.EscapeDataString("horror movies on prime"));

        Assert.Equal(["Amazon Prime Video"], view!.Resolved!.Providers);
        Assert.Equal("genre:Horror | kind:Movies | provider:Amazon Prime Video", string.Join(" | ", view.Parts.Select(p => $"{p.Part}:{p.Text}")));
        Assert.Equal(["Hollow Hill"], view.Titles.Select(t => t.Title));
    }

    [Fact]
    public async Task A_discovery_names_a_service_and_a_service_the_region_lacks_is_refused()
    {
        var found = await Get<CatalogueDiscoverView>("/api/catalogue/discover?genre=Horror&provider=netflix");
        Assert.Equal(["Netflix"], found!.Resolved.Providers);
        Assert.Equal(["The Cellar Door"], found.Titles.Select(t => t.Title));

        using var refused = await Signed().GetAsync("/api/catalogue/discover?provider=hulu");
        Assert.Equal(HttpStatusCode.NotFound, refused.StatusCode);
        using var like = await Signed().GetAsync("/api/catalogue/discover?like=heat&provider=netflix");
        Assert.Equal(HttpStatusCode.BadRequest, like.StatusCode);
    }
}
