using TheKrystalShip.Agent.Plugin.Testing;
using TheKrystalShip.MovieBot.Assistant.Api;
using TheKrystalShip.MovieBot.Client;
using TheKrystalShip.MovieBot.Core;
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
        Assert.Equal(
            ["GET /api/catalogue/facets", "GET /api/catalogue/titles?from=2000&genre=horror&kind=film&minrating=6.5&minvotes=200&notgenre=animation&notgenre=family&to=2005"],
            _rig.World.Asked);
        Assert.Contains("3 films, in the database's popular order.", result.Text);
        Assert.Contains("Matched: genres Horror; leaving out Animation, Family; rated at least 6.5 on at least 200 votes.", result.Text);
        Assert.Contains("- Hollow House (2002), film. Directed by Dara Quill. With Mina Ford, Cass Hale. Horror. Rated 7.2 on 410,000 votes at IMDb.", result.Text);
        Assert.Contains("  A family hears the walls breathe.", result.Text);
        Assert.Equal(["tt1000010", "tt1000011", "tt1000012"], ToolRig.CardIds(result));
    }

    [Fact]
    public async Task Anime_is_asked_for_as_a_category_and_said_as_the_kind()
    {
        ToolOutcome result = await _rig.RunAsync(_rig.Discover, new { kind = "show", anime = true });

        Assert.False(result.IsError);
        Assert.Equal(["GET /api/catalogue/titles?category=anime&kind=show&minrating=6.5&minvotes=200"], _rig.World.Asked);
        Assert.Contains("1 anime show, in the database's popular order.", result.Text);
        Assert.Contains("Matched: only anime; rated at least 6.5", result.Text);
        Assert.Contains("- Paper Lanterns (2019), anime show.", result.Text);
    }

    [Fact]
    public async Task The_library_s_newest_anime_is_asked_for_by_category()
    {
        ToolOutcome result = await _rig.RunAsync(_rig.WhatsNew, new { which = "library", kind = "show", anime = true });

        Assert.Equal(["GET /api/catalogue/titles?category=anime&here=ready&kind=show&size=20"], _rig.World.Asked);
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
        Assert.Equal(
            ["GET /api/catalogue/facets", "GET /api/catalogue/titles?genre=horror&kind=film&minrating=6.5&minvotes=200&notgenre=crime"], _rig.World.Asked);
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
            ["GET /api/catalogue/facets", "GET /api/catalogue/titles?genre=horror&genre=mystery&kind=film&notgenre=animation&notgenre=family&sort=rating"],
            _rig.World.Asked);
    }

    [Fact]
    public async Task A_silent_film_with_a_person_is_a_keyword_and_a_name()
    {
        ToolOutcome result = await _rig.RunAsync(
            _rig.Discover, new { about = new[] { "silent film" }, with = new[] { "Julian Rook" }, good_only = false });

        Assert.Contains("Matched: people Julian Rook; keywords silent film.", result.Text);
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

        Assert.Contains("genre=horror&genre=mystery", _rig.World.Asked[^1]);
    }

    [Fact]
    public async Task Nothing_that_fits_says_the_database_has_none_and_what_was_searched()
    {
        ToolOutcome result = await _rig.RunAsync(_rig.Discover, new { genre = new[] { "Horror" }, language = "Korean" });

        Assert.False(result.IsError);
        Assert.StartsWith("The film database has no film that fits all of that. Matched: genres Horror; leaving out Animation, Family; original language Korean;", result.Text);
        Assert.Contains("call discover again without the people, the language or the similar title.", result.Text);
        Assert.EndsWith("A title no tool returned is not to be named.", result.Text);
        Assert.Null(result.Card);
    }

    [Fact]
    public async Task An_empty_discovery_names_the_filter_to_take_away_keywords_first()
    {
        var world = CinemaWorld.Build().Answer(
            "/api/catalogue/titles?kind=film&genre=Horror&notGenre=Animation&notGenre=Family&keyword=k90&minRating=6.5&minVotes=200",
            CinemaWorld.Page());
        var rig = new ToolRig(world);

        ToolOutcome result = await rig.RunAsync(rig.Discover, new { genre = new[] { "Horror" }, about = new[] { "ghost story" } });

        Assert.Contains("a keyword the most: call discover again without about (ghost story).", result.Text);
    }

    [Fact]
    public async Task A_genre_the_database_does_not_have_is_refused_with_the_genres_it_does()
    {
        ToolOutcome result = await _rig.RunAsync(_rig.Discover, new { genre = new[] { "Spooky" } });

        Assert.True(result.IsError);
        Assert.Contains("Its genres: Action, Animation, Comedy, Crime, Drama, Family, Horror, Mystery, Sci-Fi, Thriller.", result.Text);
    }

    [Fact]
    public async Task Shows_are_filtered_by_the_people_in_them()
    {
        ToolOutcome result = await _rig.RunAsync(_rig.Discover, new { kind = "show", with = new[] { "Nell Aldous" } });

        Assert.False(result.IsError);
        Assert.Contains("Matched: people Nell Aldous;", result.Text);
        Assert.Contains("Night Harbor (2012), show.", result.Text);
    }

    [Fact]
    public async Task A_person_the_database_does_not_know_is_refused_by_name()
    {
        ToolOutcome result = await _rig.RunAsync(_rig.Discover, new { with = new[] { "Nobody Here" } });

        Assert.True(result.IsError);
        Assert.Equal("The film database has nobody named 'Nobody Here'.", result.Text);
    }

    [Fact]
    public async Task A_keyword_the_database_does_not_hold_is_refused_by_name()
    {
        ToolOutcome result = await _rig.RunAsync(_rig.Discover, new { about = new[] { "zzz" } });

        Assert.True(result.IsError);
        Assert.StartsWith("The film database has no keyword 'zzz'.", result.Text);
    }

    [Fact]
    public async Task A_genre_said_in_words_is_the_catalogues_genre()
    {
        var world = CinemaWorld.Build().Answer(
            "/api/catalogue/titles?kind=film&genre=Sci-Fi&notGenre=Animation&notGenre=Family&minRating=6.5&minVotes=200", CinemaWorld.Page(CinemaWorld.QuietRoom));
        var rig = new ToolRig(world);

        ToolOutcome result = await rig.RunAsync(rig.Discover, new { genre = new[] { "science fiction" } });

        Assert.Contains("Matched: genres Sci-Fi;", result.Text);
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

        Assert.Contains("Trending this week, most watched first:", result.Text);
        Assert.Contains("Films:\n- Rain Check (2026). Rated 7.0 on 9,000 votes at IMDb. Not in the library. id tt1000060.", result.Text);
        Assert.Contains("Shows:\n- Doorbell (2026). Not in the library. id tt1000061.", result.Text);
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
    public async Task An_empty_ranking_is_said_as_none_and_the_tool_does_not_invent_one()
    {
        var world = CinemaWorld.Build()
            .Answer("/api/catalogue/titles?ranking=trending-films&size=20", CinemaWorld.Page())
            .Answer("/api/catalogue/titles?ranking=trending-shows&size=20", CinemaWorld.Page());
        var rig = new ToolRig(world);

        ToolOutcome result = await rig.RunAsync(rig.WhatsNew, new { which = "trending" });

        Assert.Contains("Films:\nNone listed.\nShows:\nNone listed.", result.Text);
        Assert.Null(result.Card);
    }

    [Fact]
    public async Task Released_asks_for_films_and_shows_and_gives_the_dates()
    {
        ToolOutcome result = await _rig.RunAsync(_rig.WhatsNew, new { which = "released" });

        Assert.Contains("Films released from 2026-08-25 to 2026-10-09, newest first, the most looked at:", result.Text);
        Assert.Contains("- Late Bloom (2026), film. Release date 2026-09-30.", result.Text);
        Assert.True(result.Text.IndexOf("Late Bloom", StringComparison.Ordinal) < result.Text.IndexOf("Old Lamp", StringComparison.Ordinal));
        Assert.Contains("Shows released from 2026-08-25 to 2026-10-09, newest first, the most looked at:\nNone.", result.Text);
        Assert.Equal(
            [
                "GET /api/catalogue/titles?kind=film&released=2026-08-25&releasedbefore=2026-10-10&size=20&sort=popular",
                "GET /api/catalogue/titles?kind=show&released=2026-08-25&releasedbefore=2026-10-10&size=20&sort=popular",
            ],
            _rig.World.Asked.Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task Upcoming_for_films_asks_only_for_films_and_soonest_first()
    {
        ToolOutcome result = await _rig.RunAsync(_rig.WhatsNew, new { which = "upcoming", kind = "film" });

        Assert.Equal(["GET /api/catalogue/titles?kind=film&released=2026-10-09&releasedbefore=2027-04-08&size=20&sort=popular"], _rig.World.Asked);
        Assert.Contains("Films with a release date from 2026-10-09 to 2027-04-07, soonest first, the most looked at:", result.Text);
    }

    [Fact]
    public async Task The_library_lists_what_arrived_newest_first()
    {
        ToolOutcome result = await _rig.RunAsync(_rig.WhatsNew, new { which = "library" });

        Assert.Contains("Newest in the library, newest first:", result.Text);
        Assert.Equal(["GET /api/catalogue/titles?here=ready&size=20"], _rig.World.Asked);
        Assert.Contains("- Coastal Hours (2021), film. Drama. Rated 6.9 on 800 votes at IMDb. The person has watched it. id tt1000004.", result.Text);
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
        Assert.Contains("Wants to watch (newest first, 2 of 2):", result.Text);
        Assert.Contains("- The Lantern Keeper (2019), film. Drama, Mystery. Rated 7.5. Not out digitally yet. id tt1000003.", result.Text);
        Assert.Contains("- The Salt Line, show. In the library. Their next episode is season 1 episode 3. id tt1000006.", result.Text);
        Assert.Contains("Following (newest first, 1 of 1):", result.Text);
        Assert.Contains("- The Salt Line (2020): followed since 2026-01-04. Next episode season 3 episode 1, 2026-11-02. id tt1000006.", result.Text);
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
        var world = CinemaWorld.Build().Answer("/api/me/wants", "{}", 403);
        var rig = new ToolRig(world);

        ToolOutcome result = await rig.RunAsync(rig.Taste, new { });

        Assert.False(result.IsError);
        Assert.Contains("Wants to watch: could not be read (This account does not let the person do that in Cinema.).", result.Text);
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
        Assert.Equal("The film database could not be read just now (The catalogue is being rebuilt.), so nothing is known about this.", result.Text);
    }

    [Fact]
    public async Task A_refused_sign_in_is_said_as_one()
    {
        var world = CinemaWorld.Build().Answer("/api/catalogue/titles?q=night harbor&size=10", "{}", 401);
        var rig = new ToolRig(world);

        ToolOutcome result = await rig.RunAsync(rig.Search, new { query = "Night Harbor" });

        Assert.True(result.IsError);
        Assert.Contains("did not accept this sign-in", result.Text);
    }

    [Fact]
    public async Task An_unreachable_api_is_not_an_empty_answer()
    {
        var api = new CinemaApi(new MovieBotApiClient(new HttpClient(new Unreachable()) { BaseAddress = new Uri("http://api.test/") }));
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

    /// <summary>A person with no history, no wants and no follows.</summary>
    private sealed class CinemaWorld_Empty
    {
        public ReplayHandler World { get; } = new ReplayHandler()
            .Answer("/api/me/history", new List<HistoryEntry>())
            .Answer("/api/me/wants", new List<WantView>())
            .Answer("/api/me/follows", new List<FollowView>());
    }
}
