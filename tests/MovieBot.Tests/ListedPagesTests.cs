using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using TheKrystalShip.MovieBot.Api.Library;
using TheKrystalShip.MovieBot.Api.People;
using TheKrystalShip.MovieBot.Core;
using Xunit;

namespace TheKrystalShip.MovieBot.Tests;

/// <summary>
/// The library's pages and the person's wants, searched, narrowed and ordered by the API: the titles
/// in the order asked, every order beside its opposite, the states counted as a section of their own,
/// and the whole page's total before the search and the picks.
/// </summary>
public sealed class ListedPagesTests(FollowFixture fixture) : IClassFixture<FollowFixture>
{
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    private sealed record Page(List<Item> Items, int Total, Facets Facets, List<string> Sorts);
    private sealed record Item(Named? Film, Named? Show);
    private sealed record Named(string Title);
    private sealed record Facets(List<Section> Sections);
    private sealed record Section(string Id, List<Option> Options);
    private sealed record Option(string Value, int? Count);
    private sealed record WantsPage(List<Named> Wants, int Total, Facets Facets, List<string> Sorts);

    private HttpClient As(string userId)
    {
        fixture.Services.GetRequiredService<PersonDirectory>().Remember(userId, "Cy");
        var client = fixture.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer",
            fixture.Issuer.Mint(userId, "Cy", lifetime: TimeSpan.FromDays(400)));
        return client;
    }

    /// <summary>A film on the shelf, named so a search for <c>zq</c> finds it among the fixture's.</summary>
    private void Film(string id, string name, int year, TitleStatus status, double seconds)
    {
        ManifestJson.WriteAtomic(Path.Combine(fixture.MediaRoot, id, "manifest.json"), new Manifest
        {
            Id = id, Title = id, DurationSeconds = seconds, Status = status,
            Film = new FilmIdentity { Name = name, Year = year },
            Video = new VideoInfo
            {
                Width = 1280, Height = 720, SourceCodec = "h264", SourceHdr = "sdr",
                Renditions = [new Rendition { Name = "r", BitrateKbps = 4000, Width = 1280, Height = 720, Uri = "v0/index.m3u8" }],
            },
            Audio = [], Subtitles = [],
        });
    }

    private async Task<Page> Library(HttpClient client, string query) =>
        (await client.GetFromJsonAsync<Page>("/api/library/films?q=zq" + query, Web))!;

    private static List<string> Titles(Page page) => [.. page.Items.Select(i => i.Film!.Title)];

    [Fact]
    public async Task A_shelf_s_films_are_searched_narrowed_by_state_and_ordered_each_way()
    {
        Film("zq-brazil", "Zq Brazil", 1985, TitleStatus.Ready, 8000);
        Film("zq-alien", "Zq Alien", 1979, TitleStatus.Transcoding, 7000);
        Film("zq-casablanca", "Zq Casablanca", 1942, TitleStatus.Failed, 6000);
        fixture.Services.GetRequiredService<LibraryListing>().Refresh();
        var me = As("9301");

        var byName = await Library(me, "&sort=name");
        Assert.Equal(["zq-alien", "zq-brazil", "zq-casablanca"], Titles(byName));
        Assert.Equal(["zq-casablanca", "zq-brazil", "zq-alien"], Titles(await Library(me, "&sort=name-desc")));
        Assert.Equal(["zq-brazil", "zq-alien", "zq-casablanca"], Titles(await Library(me, "&sort=newest")));
        Assert.Equal(["zq-casablanca", "zq-alien", "zq-brazil"], Titles(await Library(me, "&sort=shortest")));
        Assert.Equal(["zq-brazil"], Titles(await Library(me, "%20bra")));
        Assert.Equal(["zq-brazil"], Titles(await Library(me, "&state=ready")));

        Assert.Equal(["added", "first-added", "name", "name-desc", "rating", "lowest", "newest", "oldest", "shortest", "longest"], byName.Sorts);
        var states = byName.Facets.Sections.Single(s => s.Id == ListedPages.State).Options;
        Assert.Contains(states, o => o is { Value: "preparing", Count: >= 1 });
        Assert.True(byName.Total >= 3);
        Assert.DoesNotContain(byName.Facets.Sections, s => s.Id == "kind");
    }

    [Fact]
    public async Task A_page_of_shows_is_not_ordered_by_length_and_one_of_both_kinds_offers_a_kind()
    {
        var me = As("9302");
        var shows = (await me.GetFromJsonAsync<Page>("/api/library/shows", Web))!;
        Assert.DoesNotContain("shortest", shows.Sorts);
        var all = (await me.GetFromJsonAsync<Page>("/api/library/all?kind=film", Web))!;
        Assert.All(all.Items, i => Assert.NotNull(i.Film));
    }

    [Theory]
    [InlineData("/api/library/everything", HttpStatusCode.NotFound)]
    [InlineData("/api/library/films?sort=popular", HttpStatusCode.BadRequest)]
    [InlineData("/api/library/films?kind=show", HttpStatusCode.BadRequest)]
    [InlineData("/api/library/films?shelf=both", HttpStatusCode.BadRequest)]
    [InlineData("/api/library/shows?state=ready", HttpStatusCode.BadRequest)]
    [InlineData("/api/me/wants/page?sort=shortest", HttpStatusCode.BadRequest)]
    [InlineData("/api/me/wants/page?state=gone", HttpStatusCode.BadRequest)]
    public async Task A_page_refuses_what_it_does_not_have(string path, HttpStatusCode status)
    {
        using var response = await As("9303").GetAsync(path);
        Assert.Equal(status, response.StatusCode);
    }

    [Fact]
    public async Task A_page_keeps_the_titles_the_device_says_it_holds_and_counts_the_shelf_before_them()
    {
        Film("zq-held", "Zh Held", 2001, TitleStatus.Ready, 5000);
        Film("zq-not-held", "Zh Not Held", 2002, TitleStatus.Ready, 5000);
        fixture.Services.GetRequiredService<LibraryListing>().Refresh();
        var me = As("9305");

        var held = (await me.GetFromJsonAsync<Page>("/api/library/films?q=zh&held=zq-held", Web))!;
        Assert.Equal(["zq-held"], Titles(held));
        Assert.True(held.Total >= 2);
        Assert.Empty((await me.GetFromJsonAsync<Page>("/api/library/films?q=zh&held=-", Web))!.Items);
    }

    [Fact]
    public async Task A_search_holds_every_word_in_any_order_on_every_shelf_unless_one_is_named()
    {
        Film("zq-every-word", "Zw Night Of The Word", 1990, TitleStatus.Ready, 5000);
        fixture.Services.GetRequiredService<LibraryListing>().Refresh();
        var me = As("9306");

        var found = (await me.GetFromJsonAsync<Page>("/api/library/all?q=word%20zw%20night", Web))!;
        Assert.Contains(found.Items, i => i.Film?.Title == "zq-every-word");
        Assert.Empty((await me.GetFromJsonAsync<Page>("/api/library/all?shelf=anime&q=zw%20night", Web))!.Items);
        Assert.NotEmpty((await me.GetFromJsonAsync<Page>("/api/library/all?shelf=general&q=zw%20night", Web))!.Items);
    }

    [Fact]
    public async Task History_is_searched_by_name_and_each_entry_carries_the_library_s_title()
    {
        Film("zq-history", "Zy History Film", 1999, TitleStatus.Ready, 5000);
        fixture.Services.GetRequiredService<LibraryListing>().Refresh();
        fixture.Services.GetRequiredService<WatchRecord>().Record("9307", "zq-history", 2500, 5000, fixture.Clock.GetUtcNow());
        var me = As("9307");

        var page = (await me.GetFromJsonAsync<HistoryPage>("/api/me/history/page?q=history", Web))!;
        var entry = Assert.Single(page.Entries);
        Assert.Equal(("zq-history", "zq-history"), (entry.Entry.TitleId, entry.Title!.Id));
        Assert.Empty((await me.GetFromJsonAsync<HistoryPage>("/api/me/history/page?q=nothing-watched-by-that-name", Web))!.Entries);
        Assert.Equal(1, page.Total);
    }

    [Fact]
    public async Task Who_can_be_invited_is_searched_by_every_word_of_a_name()
    {
        fixture.Services.GetRequiredService<PersonDirectory>().Remember("9309", "Zq Ada Lovelace");
        var me = As("9308");
        var found = (await me.GetFromJsonAsync<List<Person>>("/api/people?q=lovelace%20zq", Web))!;
        Assert.Equal(["9309"], found.Select(p => p.UserId));
    }

    private sealed record HistoryPage(List<HistoryItem> Entries, int Total, Facets Facets);
    private sealed record HistoryItem(HistoryEntryRow Entry, TitleRow? Title);
    private sealed record HistoryEntryRow(string TitleId);
    private sealed record TitleRow(string Id);
    private sealed record Person(string UserId);

    [Fact]
    public async Task The_wants_page_lists_its_orders_and_counts_every_want_before_the_search()
    {
        var me = As("9304");
        var page = (await me.GetFromJsonAsync<WantsPage>("/api/me/wants/page?q=nothing-is-called-this", Web))!;
        Assert.Empty(page.Wants);
        Assert.Equal(["added", "first-added", "name", "name-desc", "rating", "lowest", "newest", "oldest", "most-wanted", "least-wanted"], page.Sorts);
    }
}
