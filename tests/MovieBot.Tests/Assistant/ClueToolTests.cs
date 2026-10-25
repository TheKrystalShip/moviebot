using TheKrystalShip.Agent.Plugin.Testing;
using Xunit;

namespace TheKrystalShip.MovieBot.Tests.Assistant;

/// <summary>find_by_clues over the invented world: what it searches, how it ranks, and what it says it did not check.</summary>
public sealed class ClueToolTests
{
    private readonly ToolRig _rig = new();

    [Fact]
    public async Task A_person_and_a_look_find_the_title_that_has_both_and_rank_it_first()
    {
        ToolOutcome result = await _rig.RunAsync(_rig.Clues, new { people = new[] { "Julian Rook" }, look = new[] { "silent film" } });

        Assert.False(result.IsError);
        Assert.StartsWith("Clues: Julian Rook; silent film. Up to 8 candidates, the best fit first.", result.Text);
        string[] lines = [.. result.Text.Split('\n').Where(l => l.StartsWith("- ", StringComparison.Ordinal))];
        Assert.StartsWith("- The Lantern Keeper (2019), film. Released 2019-10-18.", lines[0]);
        Assert.Contains("Matches: Julian Rook; silent film.", result.Text);
        Assert.Contains("id tt1000003.", lines[0]);
    }

    [Fact]
    public async Task A_wrong_clue_does_not_exclude_and_a_person_and_a_look_outweigh_recent()
    {
        ToolOutcome result = await _rig.RunAsync(
            _rig.Clues, new { people = new[] { "Julian Rook" }, look = new[] { "silent film" }, released = "announced" });

        string[] lines = [.. result.Text.Split('\n').Where(l => l.StartsWith("- ", StringComparison.Ordinal))];
        Assert.StartsWith("- The Lantern Keeper (2019)", lines[0]);
        Assert.Contains("Matches: Julian Rook; silent film. Does not match: announced (it is released, 2019).", result.Text);
        // Winter Quarters is announced and has the person, but not the look it was never checked for.
        Assert.True(Array.FindIndex(lines, l => l.StartsWith("- Winter Quarters", StringComparison.Ordinal)) > 0);
    }

    [Fact]
    public async Task The_verdict_comes_before_the_candidates()
    {
        ToolOutcome none = await _rig.RunAsync(
            _rig.Clues, new { people = new[] { "Julian Rook" }, look = new[] { "silent film" }, released = "announced" });
        Assert.Contains(
            "No candidate fits every clue. Closest: The Lantern Keeper (2019), which fits Julian Rook, silent film but not announced (it is released, 2019).",
            none.Text);

        ToolOutcome fit = await _rig.RunAsync(_rig.Clues, new { people = new[] { "Julian Rook" }, look = new[] { "silent film" } });
        Assert.Contains("Fits every clue: The Lantern Keeper (2019).\n", fit.Text);
        Assert.True(fit.Text.IndexOf("Fits every clue", StringComparison.Ordinal) < fit.Text.IndexOf("- The Lantern Keeper", StringComparison.Ordinal));
        Assert.Contains("Nothing contradicts these, but a clue was not checked: Winter Quarters.", fit.Text);
    }

    [Fact]
    public async Task A_candidate_that_has_the_look_and_not_the_person_is_kept_as_a_near_miss_with_the_reason()
    {
        ToolOutcome result = await _rig.RunAsync(_rig.Clues, new { people = new[] { "Julian Rook" }, look = new[] { "silent film" } });

        string moth = result.Text[result.Text.IndexOf("- Moth Light", StringComparison.Ordinal)..];
        Assert.Contains("Matches: silent film.", moth);
        Assert.Contains("Does not match: Julian Rook (not among their credits in those years).", moth);
        Assert.True(result.Text.IndexOf("The Lantern Keeper", StringComparison.Ordinal) < result.Text.IndexOf("Moth Light", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_credit_whose_keywords_and_genres_lack_the_look_is_a_miss()
    {
        ToolOutcome result = await _rig.RunAsync(_rig.Clues, new { people = new[] { "Julian Rook" }, look = new[] { "silent film" } });

        string cinder = result.Text[result.Text.IndexOf("- Cinder", StringComparison.Ordinal)..];
        Assert.Contains("silent film (not among its keywords or genres)", cinder);
    }

    [Fact]
    public async Task A_credit_whose_page_cannot_be_read_leaves_the_look_not_checked()
    {
        ToolOutcome result = await _rig.RunAsync(_rig.Clues, new { people = new[] { "Julian Rook" }, look = new[] { "silent film" } });

        string winter = result.Text[result.Text.IndexOf("- Winter Quarters", StringComparison.Ordinal)..];
        Assert.Contains("Not checked: silent film", winter);
    }

    [Fact]
    public async Task Recently_announced_is_years_and_a_status_and_the_undated_announcement_leads()
    {
        ToolOutcome result = await _rig.RunAsync(
            _rig.Clues, new { people = new[] { "Julian Rook" }, from = 2024, released = "announced" });

        Assert.Contains("Clues: Julian Rook; released from 2024; announced.", result.Text);
        string[] lines = [.. result.Text.Split('\n').Where(l => l.StartsWith("- ", StringComparison.Ordinal))];
        Assert.StartsWith("- Winter Quarters, film. Announced, no release date.", lines[0]);
        Assert.Contains("Matches: Julian Rook; released from 2024; announced.", result.Text);
        Assert.Contains("Does not match: announced (it is upcoming, 2027)", result.Text);
        Assert.Contains("Does not match: announced (it is released, 2024)", result.Text);
        Assert.Equal(["GET /api/catalogue/terms?q=julian rook", "GET /api/catalogue/titles?from=2024&person=601&size=100&sort=oldest"], _rig.World.Asked.Take(2));
    }

    [Fact]
    public async Task A_plot_word_is_a_keyword_search_and_a_check_of_the_overview()
    {
        ToolOutcome result = await _rig.RunAsync(_rig.Clues, new { people = new[] { "Julian Rook" }, about = new[] { "lighthouse" } });

        // Cinder's overview names a lighthouse island, which no keyword of it does.
        string cinder = result.Text[result.Text.IndexOf("- Cinder", StringComparison.Ordinal)..];
        Assert.Contains("Matches: Julian Rook; \"lighthouse\".", cinder);
        Assert.Contains("\"lighthouse\"", result.Text);
    }

    [Fact]
    public async Task Clues_that_name_nobody_the_database_knows_say_so_and_the_rest_still_search()
    {
        ToolOutcome result = await _rig.RunAsync(_rig.Clues, new { people = new[] { "Nobody Here" }, look = new[] { "silent film" } });

        Assert.Contains("The database has nobody named 'Nobody Here'.", result.Text);
        Assert.Contains("The Lantern Keeper (2019)", result.Text);
    }

    [Fact]
    public async Task A_keyword_the_database_lacks_is_reported_not_hidden()
    {
        ToolOutcome result = await _rig.RunAsync(_rig.Clues, new { about = new[] { "zzz" } });

        Assert.Contains("Nothing in the database fits any of these clues: \"zzz\".", result.Text);
        Assert.Contains("The database has no keyword 'zzz'.", result.Text);
        Assert.Contains("web_search", result.Text);
        Assert.Null(result.Card);
    }

    [Fact]
    public async Task The_cards_are_the_candidates_with_a_page_in_ranked_order()
    {
        ToolOutcome result = await _rig.RunAsync(_rig.Clues, new { people = new[] { "Julian Rook" }, look = new[] { "silent film" } });

        Assert.Equal("tt1000003", ToolRig.CardIds(result)[0]);
        Assert.DoesNotContain("tt1000999", ToolRig.CardIds(result));
    }

    [Fact]
    public async Task At_least_one_clue_is_needed_and_every_call_carries_the_persons_token()
    {
        Assert.True((await _rig.RunAsync(_rig.Clues, new { })).IsError);
        Assert.Empty(_rig.World.Asked);

        await _rig.RunAsync(_rig.Clues, new { look = new[] { "silent film" } });
        Assert.All(_rig.World.Tokens, t => Assert.Equal(CinemaWorld.Token, t));
    }

    [Fact]
    public async Task The_result_sends_a_clue_the_database_cannot_hold_to_web_search_and_a_candidate_to_title_details()
    {
        ToolOutcome result = await _rig.RunAsync(_rig.Clues, new { look = new[] { "silent film" } });

        Assert.Contains(
            "A clue the database cannot hold, like a trailer, a scene or news, is for web_search. Confirm a candidate with title_details before naming it.",
            result.Text);
    }

    [Fact]
    public async Task The_result_asks_for_an_answer_that_invites_the_kinds_of_clue_that_would_help()
    {
        ToolOutcome result = await _rig.RunAsync(_rig.Clues, new { people = new[] { "Julian Rook" }, look = new[] { "silent film" } });

        Assert.EndsWith(
            "End your answer with one sentence inviting more clues: \"If this is not what you had in mind, tell me more: other actors in it, a scene you remember, or whether it is recent (the last couple of years).\"",
            result.Text);

        ToolOutcome look = await _rig.RunAsync(_rig.Clues, new { about = new[] { "lighthouse" } });
        Assert.Contains("tell me more: who is in it, a scene you remember, whether it is recent (the last couple of years), or how it looks.", look.Text);
    }

    [Fact]
    public async Task The_closest_candidate_is_offered_as_the_answer_when_a_person_or_a_look_fits()
    {
        ToolOutcome result = await _rig.RunAsync(
            _rig.Clues, new { people = new[] { "Julian Rook" }, look = new[] { "silent film" }, released = "announced" });

        Assert.Contains("Offer it as the answer and say which clue it misses: what people remember is often a little off.", result.Text);
    }
}
