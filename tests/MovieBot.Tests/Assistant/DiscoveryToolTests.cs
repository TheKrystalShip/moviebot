using TheKrystalShip.Agent.Plugin.Testing;
using TheKrystalShip.MovieBot.Assistant.Api;
using Xunit;

namespace TheKrystalShip.MovieBot.Tests.Assistant;

/// <summary>discover, whats_new and my_taste over the invented world, and what the tools do when the API cannot answer.</summary>
public sealed class DiscoveryToolTests
{
    private readonly ToolRig _rig = new();

    // ── discover ───────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Horror_of_the_early_2000s_asks_for_well_rated_titles_and_reports_what_the_words_became()
    {
        ToolOutcome result = await _rig.RunAsync(_rig.Discover, new { genre = new[] { "horror" }, from = 2000, to = 2005 });

        Assert.False(result.IsError);
        Assert.Equal(["GET /api/catalogue/discover?from=2000&genre=horror&goodonly=true&kind=film&to=2005"], _rig.World.Asked);
        Assert.Contains("3 films, in the database's popular order.", result.Text);
        Assert.Contains("Matched: genres Horror; rated at least 6.5 on at least 200 votes.", result.Text);
        Assert.Contains("- Hollow House (2002), film. Directed by Dara Quill. With Mina Ford, Cass Hale. Horror. Rated 7.2 on 410,000 votes.", result.Text);
        Assert.Contains("  A family hears the walls breathe.", result.Text);
        Assert.Equal(["tt1000010", "tt1000011", "tt1000012"], ToolRig.CardIds(result));
    }

    [Fact]
    public async Task Anime_is_asked_for_as_a_category_and_said_as_the_kind()
    {
        ToolOutcome result = await _rig.RunAsync(_rig.Discover, new { kind = "show", anime = true });

        Assert.False(result.IsError);
        Assert.Equal(["GET /api/catalogue/discover?category=anime&goodonly=true&kind=show"], _rig.World.Asked);
        Assert.Contains("1 anime show, in the database's popular order.", result.Text);
        Assert.Contains("Matched: genres Animation; original language Japanese;", result.Text);
        Assert.Contains("- Paper Lanterns (2019), anime show.", result.Text);
    }

    [Fact]
    public async Task The_library_s_newest_anime_is_asked_for_by_category()
    {
        ToolOutcome result = await _rig.RunAsync(_rig.WhatsNew, new { which = "library", kind = "show", anime = true });

        Assert.Equal(["GET /api/catalogue/added?category=anime&kind=show&limit=20"], _rig.World.Asked);
        Assert.Equal("The library has no anime.", result.Text);
    }

    [Fact]
    public async Task The_week_s_trending_list_is_not_narrowed_to_anime()
    {
        ToolOutcome result = await _rig.RunAsync(_rig.WhatsNew, new { which = "trending", anime = true });

        Assert.True(result.IsError);
        Assert.Empty(_rig.World.Asked);
    }

    [Fact]
    public async Task Genres_to_leave_out_and_keeping_family_titles_are_passed_and_reported()
    {
        ToolOutcome result = await _rig.RunAsync(
            _rig.Discover, new { genre = new[] { "horror" }, exclude = new[] { "crime" }, include_family = true });

        Assert.False(result.IsError);
        Assert.Equal(["GET /api/catalogue/discover?exclude=crime&genre=horror&goodonly=true&includefamily=true&kind=film"], _rig.World.Asked);
        Assert.Contains("Matched: genres Horror; leaving out Crime; rated at least 6.5", result.Text);
    }

    [Fact]
    public async Task A_title_the_person_watched_is_marked_so_a_recommendation_can_leave_it_out()
    {
        ToolOutcome result = await _rig.RunAsync(_rig.Discover, new { genre = new[] { "horror" }, from = 2000, to = 2005 });

        string[] lines = result.Text.Split('\n');
        Assert.Contains("The person has watched it.", lines.Single(l => l.Contains("Hollow House")));
        Assert.DoesNotContain("has watched", lines.Single(l => l.Contains("The Quiet Room")));
    }

    [Fact]
    public async Task Filters_that_are_lists_repeat_and_good_only_can_be_turned_off()
    {
        await _rig.RunAsync(_rig.Discover, new { genre = new[] { "Horror", "Mystery" }, good_only = false, sort = "rating" });

        Assert.Equal(
            ["GET /api/catalogue/discover?genre=horror&genre=mystery&goodonly=false&kind=film&sort=rating"], _rig.World.Asked);
    }

    [Fact]
    public async Task A_black_and_white_film_with_a_person_is_a_keyword_and_a_name()
    {
        ToolOutcome result = await _rig.RunAsync(
            _rig.Discover, new { about = new[] { "black and white" }, with = new[] { "Julian Rook" }, good_only = false });

        Assert.Contains("Matched: people Julian Rook; keywords black and white.", result.Text);
        Assert.Contains("The Lantern Keeper (2019)", result.Text);
    }

    [Fact]
    public async Task Like_names_the_title_it_is_similar_to()
    {
        ToolOutcome result = await _rig.RunAsync(_rig.Discover, new { like = "Night Harbor (1995)" });

        Assert.Contains("similar to Night Harbor (1995)", result.Text);
        Assert.Contains("The Quiet Room (2003)", result.Text);
    }

    [Fact]
    public async Task A_list_written_as_one_string_is_read_as_a_list()
    {
        await _rig.RunAsync(_rig.Discover, new { genre = "Horror, Mystery", good_only = false, sort = "rating" });

        Assert.Contains("genre=horror&genre=mystery", _rig.World.Asked.Single());
    }

    [Fact]
    public async Task Nothing_that_fits_says_the_database_has_none_and_what_was_searched()
    {
        ToolOutcome result = await _rig.RunAsync(_rig.Discover, new { genre = new[] { "Horror" }, language = "Korean" });

        Assert.False(result.IsError);
        Assert.StartsWith("The film database has no film that fits all of that. Matched: genres Horror; original language ko;", result.Text);
        Assert.Contains("call discover again without the people, the language or the similar title.", result.Text);
        Assert.EndsWith("A title no tool returned is not to be named.", result.Text);
        Assert.Null(result.Card);
    }

    [Fact]
    public async Task An_empty_discovery_names_the_filter_to_take_away_keywords_first()
    {
        var world = CinemaWorld.Build().Answer(
            "/api/catalogue/discover?kind=film&genre=Horror&about=ghost story&goodOnly=true",
            new DiscoverResult { Resolved = new DiscoverResolved { Genres = ["Horror"], Keywords = ["ghost story"], Sort = "popular" } });
        var rig = new ToolRig(world);

        ToolOutcome result = await rig.RunAsync(rig.Discover, new { genre = new[] { "Horror" }, about = new[] { "ghost story" } });

        Assert.Contains("a keyword the most: call discover again without about (ghost story).", result.Text);
    }

    [Fact]
    public async Task A_genre_the_database_does_not_have_is_refused_with_the_genres_it_does()
    {
        ToolOutcome result = await _rig.RunAsync(_rig.Discover, new { genre = new[] { "Spooky" } });

        Assert.True(result.IsError);
        Assert.Contains("Its genres: Action, Comedy, Drama, Horror.", result.Text);
    }

    [Fact]
    public async Task Shows_cannot_be_filtered_by_people_and_the_request_is_not_sent()
    {
        ToolOutcome result = await _rig.RunAsync(_rig.Discover, new { kind = "show", with = new[] { "Nell Aldous" } });

        Assert.True(result.IsError);
        Assert.Contains("person_credits", result.Text);
        Assert.Empty(_rig.World.Asked);
    }

    [Fact]
    public async Task Shows_are_discovered_with_the_shows_kind_and_a_show_card()
    {
        ToolOutcome result = await _rig.RunAsync(_rig.Discover, new { kind = "show", genre = new[] { "Drama" } });

        Assert.Contains("1 show,", result.Text);
        Assert.Equal("show", result.Card!.Value.GetProperty("titles")[0].GetProperty("kind").GetString());
    }

    // ── whats_new ──────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Trending_lists_films_and_shows_with_their_ids()
    {
        ToolOutcome result = await _rig.RunAsync(_rig.WhatsNew, new { which = "trending" });

        Assert.Contains("Trending this week, most watched first (as of 2026-10-09):", result.Text);
        Assert.Contains("Films:\n- Rain Check (2026). Rated 7.0 on 9,000 votes. id tt1000060.", result.Text);
        Assert.Contains("Shows:\n- Doorbell (2026). id tt1000061.", result.Text);
        Assert.Equal(["tt1000060", "tt1000061"], ToolRig.CardIds(result));
    }

    [Fact]
    public async Task Trending_for_one_kind_leaves_the_other_out()
    {
        ToolOutcome result = await _rig.RunAsync(_rig.WhatsNew, new { which = "trending", kind = "show" });

        Assert.DoesNotContain("Rain Check", result.Text);
        Assert.Contains("Doorbell", result.Text);
    }

    [Fact]
    public async Task A_database_not_yet_asked_for_the_week_has_no_list_and_the_tool_does_not_invent_one()
    {
        var world = CinemaWorld.Build().Answer("/api/popular", new Popular());
        var rig = new ToolRig(world);

        ToolOutcome result = await rig.RunAsync(rig.WhatsNew, new { which = "trending" });

        Assert.Contains("has not been asked for this week's trending titles yet, so there is no list.", result.Text);
        Assert.Null(result.Card);
    }

    [Fact]
    public async Task Released_asks_for_films_and_shows_and_gives_the_dates()
    {
        ToolOutcome result = await _rig.RunAsync(_rig.WhatsNew, new { which = "released" });

        Assert.Contains("Films released from 2026-08-25 to 2026-10-09, newest first, the most looked at:", result.Text);
        Assert.Contains("Late Bloom (2026), film. Release date 2026-03-05.", result.Text);
        Assert.Contains("Shows released from 2026-08-25 to 2026-10-09, newest first, the most looked at:\nNone.", result.Text);
        Assert.Equal(
            ["GET /api/catalogue/released?kind=film", "GET /api/catalogue/released?kind=show"], _rig.World.Asked.Order());
    }

    [Fact]
    public async Task Upcoming_for_films_asks_only_for_films_and_soonest_first()
    {
        ToolOutcome result = await _rig.RunAsync(_rig.WhatsNew, new { which = "upcoming", kind = "film" });

        Assert.Equal(["GET /api/catalogue/upcoming?kind=film"], _rig.World.Asked);
        Assert.Contains("Films with a release date from 2026-10-09 to 2027-04-07, soonest first, the most looked at:", result.Text);
    }

    [Fact]
    public async Task The_library_lists_what_arrived_newest_first()
    {
        ToolOutcome result = await _rig.RunAsync(_rig.WhatsNew, new { which = "library" });

        Assert.Contains("Newest in the library, newest first:", result.Text);
        Assert.Contains("- Coastal Hours (2021), film. Added 2026-10-08. Drama. Rated 6.9. The person has watched it. id tt1000004.", result.Text);
        Assert.Equal(["tt1000004"], ToolRig.CardIds(result));
    }

    [Fact]
    public async Task Which_is_required_and_has_four_values()
    {
        Assert.True((await _rig.RunAsync(_rig.WhatsNew, new { })).IsError);
        Assert.True((await _rig.RunAsync(_rig.WhatsNew, new { which = "popular" })).IsError);
    }

    // ── my_taste ───────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Taste_reads_what_the_person_watched_saved_and_follows()
    {
        ToolOutcome result = await _rig.RunAsync(_rig.Taste, new { });

        Assert.False(result.IsError);
        Assert.Contains("Watched (newest first, 3 of 3):", result.Text);
        Assert.Contains("- Night Harbor: finished, last watched 2026-10-01.", result.Text);
        Assert.Contains("- Coastal Hours: stopped at 34%, last watched 2026-09-20.", result.Text);
        Assert.Contains("- The Salt Line S01E02: finished, last watched 2026-09-02.", result.Text);
        Assert.Contains("List \"Watch later\" (1 saved, newest saved first):", result.Text);
        Assert.Contains("- The Lantern Keeper (2019), film. Drama. Rated 7.5. Note: Rook, as Nina said id tt1000003.", result.Text);
        Assert.Contains("List \"Friday night\" (0 saved, newest saved first):\nEmpty.", result.Text);
        Assert.Contains("Following (newest first, 1 of 1):", result.Text);
        Assert.Contains("- The Salt Line: followed since 2026-01-04. Next episode season 3 episode 1, 2026-11-02. id tt1000006.", result.Text);
        Assert.Equal(["tt1000003", "tt1000006"], ToolRig.CardIds(result));
    }

    [Fact]
    public async Task Taste_limits_each_part_to_the_number_asked_for()
    {
        ToolOutcome result = await _rig.RunAsync(_rig.Taste, new { limit = 1 });

        Assert.Contains("Watched (newest first, 1 of 3):", result.Text);
        Assert.DoesNotContain("Coastal Hours", result.Text);
    }

    [Fact]
    public async Task A_part_the_account_may_not_read_is_said_and_the_others_still_come()
    {
        var world = CinemaWorld.Build().Answer("/api/me/lists", "{}", 403);
        var rig = new ToolRig(world);

        ToolOutcome result = await rig.RunAsync(rig.Taste, new { });

        Assert.False(result.IsError);
        Assert.Contains("Lists: could not be read (This account does not let the person do that in Krystal Cinema.).", result.Text);
        Assert.Contains("- Night Harbor: finished", result.Text);
        Assert.Contains("Following (newest first, 1 of 1):", result.Text);
    }

    [Fact]
    public async Task A_person_with_nothing_recorded_gets_nothing_recorded_not_a_made_up_taste()
    {
        var world = new CinemaWorld_Empty().World;
        var rig = new ToolRig(world);

        ToolOutcome result = await rig.RunAsync(rig.Taste, new { });

        Assert.Contains("Watched: nothing recorded.", result.Text);
        Assert.Contains("Following: nothing recorded.", result.Text);
        Assert.Null(result.Card);
    }

    // ── when the API cannot answer ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task A_database_that_cannot_be_asked_is_said_so_with_its_own_reason()
    {
        ToolOutcome result = await _rig.RunAsync(_rig.Search, new { query = "down" });

        Assert.True(result.IsError);
        Assert.Equal("The film database could not be asked just now (The film database could not be asked.), and nothing is kept for this.", result.Text);
    }

    [Fact]
    public async Task A_refused_sign_in_is_said_as_one()
    {
        var world = CinemaWorld.Build().Answer("/api/catalogue/search?q=night harbor", "{}", 401);
        var rig = new ToolRig(world);

        ToolOutcome result = await rig.RunAsync(rig.Search, new { query = "Night Harbor" });

        Assert.True(result.IsError);
        Assert.Contains("did not accept this sign-in", result.Text);
    }

    [Fact]
    public async Task An_unreachable_api_is_not_an_empty_answer()
    {
        var api = new CinemaApi(new HttpClient(new Unreachable()) { BaseAddress = new Uri("http://api.test/") });
        var tool = new TheKrystalShip.MovieBot.Assistant.Tools.SearchTitlesTool(api);

        ToolOutcome result = await _rig.RunAsync(tool, new { query = "anything" });

        Assert.True(result.IsError);
        Assert.Contains("could not be reached, so nothing is known about this", result.Text);
    }

    [Fact]
    public async Task A_turn_with_nobody_signed_in_reads_nothing()
    {
        ToolOutcome result = await _rig.RunSignedOutAsync(_rig.Search, new { query = "x" });

        Assert.True(result.IsError);
        Assert.Empty(_rig.World.Asked);
    }

    private sealed class Unreachable : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            throw new HttpRequestException("connection refused");
    }

    /// <summary>A person with no history, no lists and no follows.</summary>
    private sealed class CinemaWorld_Empty
    {
        public ReplayHandler World { get; } = new ReplayHandler()
            .Answer("/api/me/history", new List<HistoryEntry>())
            .Answer("/api/me/lists", new List<ListSummary>())
            .Answer("/api/me/follows", new List<Follow>());
    }
}
