using TheKrystalShip.Agent.Plugin.Testing;
using TheKrystalShip.MovieBot.Assistant.Api;
using Xunit;

namespace TheKrystalShip.MovieBot.Tests.Assistant;

/// <summary>search_titles, title_details and person_credits over the invented world.</summary>
public sealed class FindingToolTests
{
    private readonly ToolRig _rig = new();

    // ── search_titles ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task A_search_lists_each_title_with_its_year_kind_maker_leads_and_library()
    {
        ToolOutcome result = await _rig.RunAsync(_rig.Search, new { query = "Night Harbor" });

        Assert.False(result.IsError);
        Assert.Contains("2 matches for 'Night Harbor', best first:", result.Text);
        Assert.Contains("- Night Harbor (1995), film.", result.Text);
        Assert.Contains("Directed by Vera Lund.", result.Text);
        Assert.Contains("With Ada Calloway, Theo Marsh.", result.Text);
        Assert.Contains("Rated 8.3 on 700,000 votes at IMDb. In the library. The person has watched it. id tt1000001.", result.Text);
        Assert.Contains("- Night Harbor (2012), show.", result.Text);
        Assert.Contains("Created by Imre Voss.", result.Text);
        Assert.Contains("Not in the library. id tt1000002.", result.Text);
    }

    [Fact]
    public async Task A_search_passes_the_kind_and_the_year_and_asks_as_the_person()
    {
        await _rig.RunAsync(_rig.Search, new { query = "Night Harbor", kind = "film" });
        await _rig.RunAsync(_rig.Search, new { query = "Night Harbor", year = 1995 });

        Assert.Equal(
            ["GET /api/catalogue/titles?kind=film&q=night harbor&size=10", "GET /api/catalogue/titles?from=1994&q=night harbor&size=10&to=1996"],
            _rig.World.Asked);
        Assert.All(_rig.World.Tokens, t => Assert.Equal(CinemaWorld.Token, t));
    }

    [Fact]
    public async Task A_search_attaches_a_card_for_every_title_that_has_a_page()
    {
        ToolOutcome result = await _rig.RunAsync(_rig.Search, new { query = "Night Harbor" });

        Assert.Equal(["tt1000001", "tt1000002"], ToolRig.CardIds(result));
        var first = result.Card!.Value.GetProperty("titles")[0];
        Assert.Equal("titles", result.Card.Value.GetProperty("kind").GetString());
        Assert.Equal("Night Harbor", first.GetProperty("title").GetString());
        Assert.Equal(1995, first.GetProperty("year").GetInt32());
        Assert.Equal("/images/w500/tt1000001.jpg", first.GetProperty("posterUrl").GetString());
        Assert.True(first.GetProperty("inLibrary").GetBoolean());
        Assert.True(first.GetProperty("watched").GetBoolean());
    }

    [Fact]
    public async Task A_search_that_finds_nothing_says_so_and_has_no_card()
    {
        ToolOutcome result = await _rig.RunAsync(_rig.Search, new { query = "nothing like it", year = (int?)null });

        Assert.StartsWith("The film database has no film or show matching 'nothing like it'. Do not name another film from memory.", result.Text);
        Assert.Null(result.Card);
    }

    [Fact]
    public async Task A_search_needs_a_query_and_a_known_kind()
    {
        Assert.True((await _rig.RunAsync(_rig.Search, new { })).IsError);

        ToolOutcome wrong = await _rig.RunAsync(_rig.Search, new { query = "x", kind = "documentary" });
        Assert.True(wrong.IsError);
        Assert.Contains("one of film, show", wrong.Text);
        Assert.Empty(_rig.World.Asked);
    }

    // ── title_details ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task A_title_with_its_year_is_found_through_the_search_and_read_as_a_page()
    {
        ToolOutcome result = await _rig.RunAsync(_rig.Details, new { title = "The Lantern Keeper (2019)" });

        Assert.False(result.IsError);
        Assert.Equal(
            ["GET /api/catalogue/titles?from=2018&q=the lantern keeper&size=10&to=2020", "GET /api/catalogue/titles/tt1000003"], _rig.World.Asked);
        Assert.Contains("The Lantern Keeper (2019), film. id tt1000003.", result.Text);
        Assert.Contains("Release date: 2019-10-18.", result.Text);
        Assert.Contains("Runtime: 109 minutes.", result.Text);
        Assert.Contains("Genres: Drama, Mystery.", result.Text);
        Assert.Contains("Rated 7.5 on 61,000 votes at IMDb.", result.Text);
        Assert.Contains("Certificates: R (US).", result.Text);
        Assert.Contains("Directed by: Rowan Pike.", result.Text);
        Assert.Contains("Written by: Rowan Pike, Sol Pike.", result.Text);
        Assert.Contains("Main cast: Julian Rook (Thomas), Willa Dane (Ephraim).", result.Text);
        Assert.Contains("Original language: en.", result.Text);
        Assert.Contains("Keywords: silent film, lighthouse, isolation.", result.Text);
        Assert.Contains("Not in the library.", result.Text);
        Assert.Equal(["tt1000003"], ToolRig.CardIds(result));
    }

    [Fact]
    public async Task An_id_a_tool_returned_is_read_without_a_search()
    {
        ToolOutcome result = await _rig.RunAsync(_rig.Details, new { title = "tt1000004" });

        Assert.Equal(["GET /api/catalogue/titles/tt1000004"], _rig.World.Asked);
        Assert.Contains("In the library. The person has watched it.", result.Text);
    }

    [Fact]
    public async Task An_id_that_is_a_show_is_read_as_a_show_with_what_the_person_has_of_it()
    {
        ToolOutcome result = await _rig.RunAsync(_rig.Details, new { title = "tt1000006" });

        Assert.Equal(["GET /api/catalogue/titles/tt1000006", "GET /api/me/shows/tt1000006"], _rig.World.Asked);
        Assert.Contains("The Salt Line (2020), show. id tt1000006.", result.Text);
        Assert.Contains("Ran from 2020 to 2022.", result.Text);
        Assert.Contains("Status: Ended.", result.Text);
        Assert.Contains("Created by: Imre Voss.", result.Text);
        Assert.Contains("Season 1: 2 episodes, first aired 2020-02-02.", result.Text);
        Assert.Contains("Season 2: 1 episodes, first aired 2021-02-02.", result.Text);
        Assert.Contains("The person follows it, since 2026-01-04.", result.Text);
        Assert.Contains("In the library.", result.Text);
    }

    [Fact]
    public async Task A_title_a_search_finds_as_a_show_is_read_by_its_id()
    {
        await _rig.RunAsync(_rig.Details, new { title = "The Salt Line" });

        Assert.Equal(
            ["GET /api/catalogue/titles?q=the salt line&size=10", "GET /api/catalogue/titles/tt1000006", "GET /api/me/shows/tt1000006"], _rig.World.Asked);
    }

    [Fact]
    public async Task Two_titles_with_one_name_come_back_as_candidates_with_their_ids()
    {
        ToolOutcome result = await _rig.RunAsync(_rig.Details, new { title = "Night Harbor" });

        Assert.True(result.IsError);
        Assert.Contains("More than one title is named 'Night Harbor'.", result.Text);
        Assert.Contains("id tt1000001", result.Text);
        Assert.Contains("id tt1000002", result.Text);
        Assert.DoesNotContain(_rig.World.Asked, a => a.StartsWith("GET /api/catalogue/titles/", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_year_tells_two_titles_of_one_name_apart()
    {
        ToolOutcome result = await _rig.RunAsync(_rig.Details, new { title = "Night Harbor (1995)" });

        Assert.False(result.IsError);
        Assert.Contains("Night Harbor (1995), film. id tt1000001.", result.Text);
    }

    [Fact]
    public async Task A_title_the_database_has_no_page_for_is_refused_with_a_sentence()
    {
        ToolOutcome result = await _rig.RunAsync(_rig.Details, new { title = "Unborn Harbour" });

        Assert.True(result.IsError);
        Assert.Contains("no title matching 'Unborn Harbour' that has a page here", result.Text);
    }

    [Fact]
    public async Task A_title_whose_keywords_have_not_arrived_lists_none()
    {
        ToolOutcome result = await _rig.RunAsync(_rig.Details, new { title = "tt1000005" });

        Assert.DoesNotContain("Keywords:", result.Text);
    }

    [Fact]
    public async Task A_value_the_database_left_out_is_not_written()
    {
        ToolOutcome result = await _rig.RunAsync(_rig.Details, new { title = "tt1000005" });

        Assert.DoesNotContain("Runtime", result.Text);
        Assert.DoesNotContain("Certificates", result.Text);
        Assert.DoesNotContain("Main cast", result.Text);
    }

    // ── person_credits ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task A_year_of_credits_is_listed_oldest_first_with_dates_and_what_is_out_yet()
    {
        ToolOutcome result = await _rig.RunAsync(
            _rig.Credits, new { person = "Ada Calloway", from = 2026, to = 2026, role = "acting" });

        Assert.False(result.IsError);
        Assert.Contains("Several people are named Ada Calloway.", result.Text);
        Assert.Contains("Credits of Ada Calloway (known for Acting), acting, films and shows, in 2026.", result.Text);
        Assert.Contains("7 credits, oldest first.", result.Text);

        string[] lines = [.. result.Text.Split('\n').Where(l => l.StartsWith("- ", StringComparison.Ordinal))];
        Assert.Equal(7, lines.Length);
        Assert.StartsWith("- 2026-04-16 | released | Mother Tongue (2026), film. Role: Anna. Rated 6.8 on 1,200 votes at IMDb.", lines[0]);
        Assert.Contains("id tt1000030.", lines[0]);
        Assert.StartsWith("- 2026-04-29 | released | The Wedding Party 2 (2026)", lines[1]);
        Assert.Contains("The person has watched it.", lines[2]);
        Assert.StartsWith("- 2026-12-18 | upcoming (dated in the future) | Winter Street (2026)", lines[3]);
        Assert.StartsWith("- no date | announced (no release date, or only planned) | Salt Marsh, film.", lines[4]);
        Assert.StartsWith("- no date | announced (no release date, or only planned) | Fear Not, show.", lines[6]);
    }

    /// <summary>A person with many credits: thirty released parts of rising renown, four minor ones that are the most voted, and two dated ahead.</summary>
    private ToolRig LongFilmography()
    {
        ReplayHandler world = CinemaWorld.Build();
        List<CatalogueCard> credits = [];
        for (int n = 1; n <= 30; n++)
            credits.Add(CinemaWorld.Credit($"tt20000{n:00}", $"Part {n:00}", $"{1990 + n}-06-{n:00}", role: $"Role {n}", votes: 100 * n));
        credits.Add(CinemaWorld.Credit("tt2000101", "Late Night Chat", "2015-01-01", kind: "show", role: "Self", votes: 99000));
        credits.Add(CinemaWorld.Credit("tt2000102", "Morning Show", "2016-01-01", kind: "show", votes: 50000));
        credits.Add(CinemaWorld.Credit("tt2000103", "Big Picture", "2017-01-01", role: "Waiter (uncredited)", votes: 80000));
        credits.Add(CinemaWorld.Credit("tt2000104", "Old Reel", "2018-01-01", role: "Archive footage", votes: 70000));
        credits.Add(CinemaWorld.Credit("tt2000105", "Next Part", "2027-02-02", role: "Lead"));
        credits.Add(CinemaWorld.Credit("tt2000106", "Untitled", null));
        credits = [.. credits.OrderBy(c => c.Released is null).ThenBy(c => c.Released, StringComparer.Ordinal)];

        world.Answer("/api/catalogue/terms?q=mara vance", new List<Term> { new("person", "701", "Mara Vance", "Acting") });
        world.Answer("/api/catalogue/titles?person=701&sort=oldest&size=100", CinemaWorld.Page([.. credits]));
        return new ToolRig(world);
    }

    [Fact]
    public async Task A_long_filmography_lists_the_best_known_named_parts_with_exact_dates_and_counts_the_rest()
    {
        ToolRig rig = LongFilmography();
        ToolOutcome result = await rig.RunAsync(rig.Credits, new { person = "Mara Vance" });

        string[] lines = [.. result.Text.Split('\n').Where(l => l.StartsWith("- ", StringComparison.Ordinal))];
        Assert.Equal(21, lines.Length);
        Assert.Contains("36 credits, oldest first. Listed: the 21 best known with a named part or job, upcoming and announced ones included.", result.Text);
        Assert.Contains("Not listed: 5 with no named part, as oneself or uncredited and 10 less known. Call person_credits again with all true", result.Text);
        Assert.StartsWith("- 2001-06-11 | released | Part 11 (2001), film. Role: Role 11.", lines[0]);
        Assert.StartsWith("- 2020-06-30 | released | Part 30 (2020), film. Role: Role 30.", lines[19]);
        Assert.StartsWith("- 2027-02-02 | upcoming (dated in the future) | Next Part (2027)", lines[20]);
        Assert.DoesNotContain("Late Night Chat", result.Text);
        Assert.DoesNotContain("Waiter", result.Text);
        Assert.DoesNotContain("Part 10 ", result.Text);
        Assert.Equal(21, ToolRig.CardIds(result).Length);
    }

    [Fact]
    public async Task All_lists_every_credit_of_a_long_filmography()
    {
        ToolRig rig = LongFilmography();
        ToolOutcome result = await rig.RunAsync(rig.Credits, new { person = "Mara Vance", all = true });

        Assert.Equal(36, result.Text.Split('\n').Count(l => l.StartsWith("- ", StringComparison.Ordinal)));
        Assert.DoesNotContain("Not listed", result.Text);
        Assert.Contains("Late Night Chat (2015), show.", result.Text);
    }

    [Fact]
    public async Task Credits_ask_the_api_for_the_years_and_the_role_the_question_named()
    {
        await _rig.RunAsync(_rig.Credits, new { person = "Ada Calloway", from = 2026, to = 2026, role = "acting" });

        Assert.Equal(
            [
                "GET /api/catalogue/terms?q=ada calloway",
                "GET /api/catalogue/titles?from=2026&person=501&role=cast&size=100&sort=oldest&to=2026",
                "GET /api/catalogue/titles?person=501&size=3&sort=votes",
                "GET /api/catalogue/titles?person=502&size=3&sort=votes",
            ],
            _rig.World.Asked.Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task Several_people_with_one_name_are_all_named_and_the_one_the_question_makes_obvious_is_read()
    {
        ToolOutcome result = await _rig.RunAsync(_rig.Credits, new { person = "Ada Calloway", role = "directing" });

        Assert.Contains("Credits of Ada Calloway (known for Directing)", result.Text);
        Assert.Contains("Small Rooms (2011)", result.Text);
        Assert.Contains("The others: Ada Calloway, Acting, known for Night Harbor (1995), Coastal Hours (2021)", result.Text);
        Assert.Contains("'Ada Calloway (Title)'", result.Text);
    }

    [Fact]
    public async Task A_known_title_in_brackets_picks_among_people_with_one_name()
    {
        ToolOutcome result = await _rig.RunAsync(_rig.Credits, new { person = "Ada Calloway (Small Rooms)", role = "directing" });

        Assert.Contains("Credits of Ada Calloway (known for Directing)", result.Text);
        Assert.Contains("GET /api/catalogue/titles?person=502&q=small rooms&size=1", _rig.World.Asked);
        Assert.Contains("GET /api/catalogue/titles?person=502&role=directing&size=100&sort=oldest", _rig.World.Asked);
    }

    [Fact]
    public async Task Without_a_role_or_a_title_the_best_match_is_read_and_the_others_named()
    {
        ToolOutcome result = await _rig.RunAsync(_rig.Credits, new { person = "Ada Calloway" });

        Assert.Contains("the one known for Night Harbor (1995), Coastal Hours (2021)", result.Text);
        Assert.Contains("GET /api/catalogue/titles?person=501&size=100&sort=oldest", _rig.World.Asked);
    }

    [Fact]
    public async Task One_person_with_the_name_needs_no_note()
    {
        ToolOutcome result = await _rig.RunAsync(_rig.Credits, new { person = "Julian Rook" });

        Assert.DoesNotContain("Several people", result.Text);
        Assert.StartsWith("Credits of Julian Rook (known for Acting), cast and crew, films and shows. 4 credits, oldest first.", result.Text);
        Assert.Contains("- no date | announced (no release date, or only planned) | Winter Quarters, film.", result.Text);
        Assert.Contains("- 2027-02-12 | upcoming (dated in the future) | Cinder (2027)", result.Text);
    }

    [Fact]
    public async Task A_name_the_database_only_nearly_has_names_the_closest_and_says_it_used_the_first()
    {
        ToolOutcome result = await _rig.RunAsync(_rig.Credits, new { person = "Rook" });

        Assert.Contains("Nobody is named exactly 'Rook'. These are the closest: Julian Rook, Acting, known for The Lantern Keeper (2019); Edda Rookwood", result.Text);
        Assert.Contains("Using the first.", result.Text);
    }

    [Fact]
    public async Task Nobody_by_that_name_is_said_plainly()
    {
        ToolOutcome result = await _rig.RunAsync(_rig.Credits, new { person = "Nobody Here" });

        Assert.False(result.IsError);
        Assert.Equal("The film database has nobody named 'Nobody Here'.", result.Text);
        Assert.Null(result.Card);
    }

    [Fact]
    public async Task A_year_with_no_credits_says_none_and_has_no_card()
    {
        ToolOutcome result = await _rig.RunAsync(_rig.Credits, new { person = "Ada Calloway", from = 2030, to = 2030 });

        Assert.EndsWith("None.", result.Text);
        Assert.Null(result.Card);
    }

    [Fact]
    public async Task A_list_cut_to_the_newest_says_how_many_the_database_holds()
    {
        ToolOutcome result = await _rig.RunAsync(_rig.Credits, new { person = "Ada Calloway" });

        Assert.Contains("The database holds 120; these are the newest 100.", result.Text);
        Assert.Contains("Late 120 (2020)", result.Text);
        Assert.DoesNotContain("Early 020 ", result.Text);
    }

    [Fact]
    public async Task Years_the_wrong_way_round_are_refused_before_anything_is_asked()
    {
        ToolOutcome result = await _rig.RunAsync(_rig.Credits, new { person = "Ada Calloway", from = 2027, to = 2026 });

        Assert.True(result.IsError);
        Assert.Equal("from is after to.", result.Text);
        Assert.Empty(_rig.World.Asked);
    }
}
