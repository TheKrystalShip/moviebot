using TheKrystalShip.Agent.Plugin;
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
        PluginToolResult result = await _rig.RunAsync(_rig.Search, new { query = "Night Harbor" });

        Assert.False(result.IsError);
        Assert.Contains("2 matches for 'Night Harbor', best first:", result.Text);
        Assert.Contains("- Night Harbor (1995), film.", result.Text);
        Assert.Contains("Directed by Vera Lund.", result.Text);
        Assert.Contains("With Ada Calloway, Theo Marsh.", result.Text);
        Assert.Contains("In the library. The person has watched it. id tt1000001.", result.Text);
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
            ["GET /api/catalogue/search?kind=film&q=night harbor", "GET /api/catalogue/search?q=night harbor&year=1995"],
            _rig.World.Asked);
        Assert.All(_rig.World.Tokens, t => Assert.Equal(CinemaWorld.Token, t));
    }

    [Fact]
    public async Task A_search_attaches_a_card_for_every_title_that_has_a_page()
    {
        PluginToolResult result = await _rig.RunAsync(_rig.Search, new { query = "Night Harbor" });

        Assert.Equal(["tt1000001", "tt1000002"], ToolRig.CardIds(result));
        var first = result.Card!.Value.GetProperty("titles")[0];
        Assert.Equal("titles", result.Card.Value.GetProperty("kind").GetString());
        Assert.Equal("Night Harbor", first.GetProperty("title").GetString());
        Assert.Equal(1995, first.GetProperty("year").GetInt32());
        Assert.Equal("/glances/tt1000001/poster.jpg", first.GetProperty("posterUrl").GetString());
        Assert.True(first.GetProperty("inLibrary").GetBoolean());
        Assert.True(first.GetProperty("watched").GetBoolean());
    }

    [Fact]
    public async Task A_title_with_no_imdb_id_is_named_without_a_card()
    {
        PluginToolResult result = await _rig.RunAsync(_rig.Search, new { query = "harbour" });

        Assert.Contains("Untitled Harbour Project (2027)", result.Text);
        Assert.Contains("No IMDb id: it has no page here.", result.Text);
        Assert.Equal(["tt1000001"], ToolRig.CardIds(result));
    }

    [Fact]
    public async Task A_search_with_only_unpaged_titles_carries_no_card()
    {
        PluginToolResult result = await _rig.RunAsync(_rig.Search, new { query = "unborn harbour" });

        Assert.False(result.IsError);
        Assert.Null(result.Card);
    }

    [Fact]
    public async Task A_search_that_finds_nothing_says_so_and_has_no_card()
    {
        PluginToolResult result = await _rig.RunAsync(_rig.Search, new { query = "nothing like it", year = (int?)null });

        Assert.StartsWith("The film database has no film or show matching 'nothing like it'. Do not name another film from memory.", result.Text);
        Assert.Null(result.Card);
    }

    [Fact]
    public async Task A_search_needs_a_query_and_a_known_kind()
    {
        Assert.True((await _rig.RunAsync(_rig.Search, new { })).IsError);

        PluginToolResult wrong = await _rig.RunAsync(_rig.Search, new { query = "x", kind = "documentary" });
        Assert.True(wrong.IsError);
        Assert.Contains("one of film, show", wrong.Text);
        Assert.Empty(_rig.World.Asked);
    }

    // ── title_details ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task A_title_with_its_year_is_found_through_the_search_and_read_as_a_page()
    {
        PluginToolResult result = await _rig.RunAsync(_rig.Details, new { title = "The Lantern Keeper (2019)" });

        Assert.False(result.IsError);
        Assert.Equal(
            ["GET /api/catalogue/search?q=the lantern keeper&year=2019", "GET /api/films/tt1000003"], _rig.World.Asked);
        Assert.Contains("The Lantern Keeper (2019), film. id tt1000003.", result.Text);
        Assert.Contains("Release date: 2019-10-18.", result.Text);
        Assert.Contains("Runtime: 109 minutes.", result.Text);
        Assert.Contains("Genres: Drama, Mystery.", result.Text);
        Assert.Contains("Rated 7.5 on 61,000 votes.", result.Text);
        Assert.Contains("Directed by: Rowan Pike.", result.Text);
        Assert.Contains("Written by: Rowan Pike, Sol Pike.", result.Text);
        Assert.Contains("Main cast: Julian Rook (Thomas), Willa Dane (Ephraim).", result.Text);
        Assert.Contains("Original language: en.", result.Text);
        Assert.Contains("Keywords: black and white, lighthouse, isolation.", result.Text);
        Assert.Contains("Not in the library.", result.Text);
        Assert.Equal(["tt1000003"], ToolRig.CardIds(result));
    }

    [Fact]
    public async Task An_id_a_tool_returned_is_read_without_a_search()
    {
        PluginToolResult result = await _rig.RunAsync(_rig.Details, new { title = "tt1000004" });

        Assert.Equal(["GET /api/films/tt1000004"], _rig.World.Asked);
        Assert.Contains("In the library. The person has watched it.", result.Text);
    }

    [Fact]
    public async Task An_id_that_is_a_show_is_read_as_a_show_after_the_film_route_says_no()
    {
        PluginToolResult result = await _rig.RunAsync(_rig.Details, new { title = "tt1000006" });

        Assert.Equal(["GET /api/films/tt1000006", "GET /api/shows/tt1000006"], _rig.World.Asked);
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
    public async Task A_title_a_search_finds_as_a_show_is_read_as_a_show_without_trying_films()
    {
        await _rig.RunAsync(_rig.Details, new { title = "The Salt Line" });

        Assert.Equal(
            ["GET /api/catalogue/search?q=the salt line", "GET /api/shows/tt1000006"], _rig.World.Asked);
    }

    [Fact]
    public async Task Two_titles_with_one_name_come_back_as_candidates_with_their_ids()
    {
        PluginToolResult result = await _rig.RunAsync(_rig.Details, new { title = "Night Harbor" });

        Assert.True(result.IsError);
        Assert.Contains("More than one title is named 'Night Harbor'.", result.Text);
        Assert.Contains("id tt1000001", result.Text);
        Assert.Contains("id tt1000002", result.Text);
        Assert.DoesNotContain(_rig.World.Asked, a => a.StartsWith("GET /api/films", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_year_tells_two_titles_of_one_name_apart()
    {
        PluginToolResult result = await _rig.RunAsync(_rig.Details, new { title = "Night Harbor (1995)" });

        Assert.False(result.IsError);
        Assert.Contains("Night Harbor (1995), film. id tt1000001.", result.Text);
    }

    [Fact]
    public async Task A_title_the_database_has_no_page_for_is_refused_with_a_sentence()
    {
        PluginToolResult result = await _rig.RunAsync(_rig.Details, new { title = "Unborn Harbour" });

        Assert.True(result.IsError);
        Assert.Contains("no title matching 'Unborn Harbour' that has a page here", result.Text);
    }

    [Fact]
    public async Task A_title_tagged_black_and_white_says_so()
    {
        PluginToolResult result = await _rig.RunAsync(_rig.Details, new { title = "tt1000003" });

        Assert.Contains("Black and white: the database tags it black and white.", result.Text);
    }

    [Fact]
    public async Task A_title_not_tagged_is_never_called_colour()
    {
        PluginToolResult result = await _rig.RunAsync(_rig.Details, new { title = "tt1000004" });

        Assert.Contains("the database does not tag it black and white. It has no tag for colour, so this does not say it is in colour.", result.Text);
        Assert.DoesNotContain("is in colour.", result.Text.Replace("does not say it is in colour.", ""));
    }

    [Fact]
    public async Task A_title_whose_keywords_have_not_arrived_is_not_known_either_way()
    {
        PluginToolResult result = await _rig.RunAsync(_rig.Details, new { title = "tt1000005" });

        Assert.Contains("Black and white: not known; the database's keywords for it have not arrived.", result.Text);
        Assert.DoesNotContain("Keywords:", result.Text);
    }

    [Fact]
    public async Task A_value_the_database_left_out_is_not_written()
    {
        PluginToolResult result = await _rig.RunAsync(_rig.Details, new { title = "tt1000005" });

        Assert.DoesNotContain("Runtime", result.Text);
        Assert.DoesNotContain("Certification", result.Text);
        Assert.DoesNotContain("Main cast", result.Text);
    }

    // ── person_credits ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task A_year_of_credits_is_listed_oldest_first_with_dates_and_what_is_out_yet()
    {
        PluginToolResult result = await _rig.RunAsync(
            _rig.Credits, new { person = "Ada Calloway", from = 2026, to = 2026, role = "acting" });

        Assert.False(result.IsError);
        Assert.Contains("Several people are named Ada Calloway.", result.Text);
        Assert.Contains("Credits of Ada Calloway (known for Acting), acting, films and shows, in 2026, as of 2026-10-09.", result.Text);
        Assert.Contains("7 credits, oldest first.", result.Text);

        string[] lines = [.. result.Text.Split('\n').Where(l => l.StartsWith("- ", StringComparison.Ordinal))];
        Assert.Equal(7, lines.Length);
        Assert.StartsWith("- 2026-04-16 | released | Mother Tongue (2026), film. Role: Anna.", lines[0]);
        Assert.Contains("id tt1000030.", lines[0]);
        Assert.StartsWith("- 2026-04-29 | released | The Wedding Party 2 (2026)", lines[1]);
        Assert.Contains("The person has watched it.", lines[2]);
        Assert.StartsWith("- 2026-12-18 | upcoming (dated in the future) | Winter Street (2026)", lines[3]);
        Assert.StartsWith("- no date | announced (no release date, or only planned) | Salt Marsh, film.", lines[4]);
        Assert.Contains("- no date | announced (no release date, or only planned) | Fear Not, show.", lines[6]);
    }

    [Fact]
    public async Task Credits_ask_the_api_for_the_years_and_the_role_the_question_named()
    {
        await _rig.RunAsync(_rig.Credits, new { person = "Ada Calloway", from = 2026, to = 2026, role = "acting" });

        Assert.Equal(
            ["GET /api/catalogue/people?q=ada calloway", "GET /api/catalogue/people/501/credits?from=2026&role=acting&to=2026"],
            _rig.World.Asked);
    }

    [Fact]
    public async Task An_announced_credit_without_an_imdb_id_is_named_and_has_no_card()
    {
        PluginToolResult result = await _rig.RunAsync(
            _rig.Credits, new { person = "Ada Calloway", from = 2026, to = 2026, role = "acting" });

        Assert.Contains("Untitled Harbour Sting, film. No IMDb id: it has no page here.", result.Text);
        Assert.Equal(
            ["tt1000030", "tt1000031", "tt1000032", "tt1000033", "tt1000034", "tt1000035"], ToolRig.CardIds(result));
    }

    [Fact]
    public async Task Several_people_with_one_name_are_all_named_and_the_one_the_question_makes_obvious_is_read()
    {
        PluginToolResult result = await _rig.RunAsync(_rig.Credits, new { person = "Ada Calloway", role = "directing" });

        Assert.Contains("Credits of Ada Calloway (known for Directing)", result.Text);
        Assert.Contains("Small Rooms (2011)", result.Text);
        Assert.Contains("The others: Ada Calloway, Acting, known for Night Harbor (1995), Coastal Hours (2021)", result.Text);
        Assert.Contains("'Ada Calloway (Title)'", result.Text);
    }

    [Fact]
    public async Task A_known_title_in_brackets_picks_among_people_with_one_name()
    {
        PluginToolResult result = await _rig.RunAsync(_rig.Credits, new { person = "Ada Calloway (Small Rooms)", role = "directing" });

        Assert.Contains("Credits of Ada Calloway (known for Directing)", result.Text);
        Assert.Contains("people?q=ada calloway", _rig.World.Asked[0]);
    }

    [Fact]
    public async Task Without_a_role_or_a_title_the_best_match_is_read_and_the_others_named()
    {
        PluginToolResult result = await _rig.RunAsync(_rig.Credits, new { person = "Ada Calloway" });

        Assert.Contains("the one known for Night Harbor (1995), Coastal Hours (2021)", result.Text);
        Assert.Contains("people/501/credits", _rig.World.Asked[1]);
    }

    [Fact]
    public async Task One_person_with_the_name_needs_no_note()
    {
        PluginToolResult result = await _rig.RunAsync(_rig.Credits, new { person = "Julian Rook" });

        Assert.DoesNotContain("Several people", result.Text);
        Assert.StartsWith("Credits of Julian Rook (known for Acting), cast and crew, films and shows, as of 2026-10-09. 4 credits, oldest first.", result.Text);
        Assert.Contains("- no date | announced (no release date, or only planned) | Winter Quarters, film.", result.Text);
        Assert.Contains("- 2027-02-12 | upcoming (dated in the future) | Cinder (2027)", result.Text);
    }

    [Fact]
    public async Task A_name_the_database_only_nearly_has_names_the_closest_and_says_it_used_the_first()
    {
        PluginToolResult result = await _rig.RunAsync(_rig.Credits, new { person = "Rook" });

        Assert.Contains("Nobody is named exactly 'Rook'. These are the closest: Julian Rook, Acting, known for The Lantern Keeper (2019); Edda Rookwood", result.Text);
        Assert.Contains("Using the first.", result.Text);
    }

    [Fact]
    public async Task Nobody_by_that_name_is_said_plainly()
    {
        PluginToolResult result = await _rig.RunAsync(_rig.Credits, new { person = "Nobody Here" });

        Assert.False(result.IsError);
        Assert.Equal("The film database has nobody named 'Nobody Here'.", result.Text);
        Assert.Null(result.Card);
    }

    [Fact]
    public async Task A_year_with_no_credits_says_none_and_has_no_card()
    {
        PluginToolResult result = await _rig.RunAsync(_rig.Credits, new { person = "Ada Calloway", from = 2030, to = 2030 });

        Assert.EndsWith("None.", result.Text);
        Assert.Null(result.Card);
    }

    [Fact]
    public async Task A_list_cut_to_the_newest_says_how_many_the_database_holds()
    {
        PluginToolResult result = await _rig.RunAsync(_rig.Credits, new { person = "Ada Calloway" });

        Assert.Contains("The database holds 120; these are the newest 1.", result.Text);
    }

    [Fact]
    public async Task Years_the_wrong_way_round_come_back_as_the_apis_sentence()
    {
        PluginToolResult result = await _rig.RunAsync(_rig.Credits, new { person = "Ada Calloway", from = 2027, to = 2026 });

        Assert.True(result.IsError);
        Assert.Equal("from is after to.", result.Text);
    }
}
