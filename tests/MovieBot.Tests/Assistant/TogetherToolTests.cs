using System.Text.Json;
using TheKrystalShip.Agent.Contracts;
using TheKrystalShip.Agent.Plugin;
using TheKrystalShip.Agent.Plugin.Testing;
using TheKrystalShip.MovieBot.Assistant;
using TheKrystalShip.MovieBot.Core;
using Xunit;

namespace TheKrystalShip.MovieBot.Tests.Assistant;

/// <summary>
/// Wanting and watching together over the invented world: what is shared, the plans a person is in, and
/// the proposals that change them, which send nothing until the person confirms. The person is in Madrid,
/// where the world's Friday 9 October 2026, 12:00 UTC, is 14:00.
/// </summary>
public sealed class TogetherToolTests
{
    private const string Zone = "Europe/Madrid";

    private static readonly PlanPersonView Sam = new() { UserId = "u-sam", Name = "Sam" };
    private static readonly PlanPersonView Ana = new() { UserId = "u-ana", Name = "Ana Ruiz", Maker = true };
    private static readonly PlanPersonView Marc = new() { UserId = "u-marc", Name = "Marc" };

    /// <summary>Friday 16 October, 21:00 in Madrid.</summary>
    private static readonly DateTimeOffset Friday = new(2026, 10, 16, 19, 0, 0, TimeSpan.Zero);

    /// <summary>Saturday 17 October, 21:00 in Madrid.</summary>
    private static readonly DateTimeOffset Saturday = new(2026, 10, 17, 19, 0, 0, TimeSpan.Zero);

    /// <summary>A plan Ana made for Coastal Hours, with Sam and Marc in it: Ana and Marc in on Friday, Sam not answered.</summary>
    private static PlanView AnasPlan() => new()
    {
        Id = "p-ana", ImdbId = "tt1000004", Kind = "film", Title = "Coastal Hours", Card = CinemaWorld.CoastalHours,
        State = PlanStates.Proposed, MadeBy = Ana, MadeAt = new DateTimeOffset(2026, 10, 8, 0, 0, 0, TimeSpan.Zero), Mine = false,
        Times =
        [
            new() { At = Friday, In = 2, SuggestedBy = Ana, Answers = [new() { UserId = "u-ana", Name = "Ana Ruiz", Answer = "in" }, new() { UserId = "u-marc", Name = "Marc", Answer = "in" }] },
            new() { At = Saturday, In = 1, Answers = [new() { UserId = "u-ana", Name = "Ana Ruiz", Answer = "in" }, new() { UserId = "u-marc", Name = "Marc", Answer = "cant" }] },
        ],
        People = [Ana, Sam, Marc], Readiness = PlanReadiness.Fetching, Progress = 0.4,
    };

    /// <summary>Sam's own plan for The Salt Line, picked for Saturday, with Ana in it.</summary>
    private static PlanView SamsPlan() => new()
    {
        Id = "p-sam", ImdbId = "tt1000006", Kind = "show", Title = "The Salt Line", Season = 1, Number = 3,
        State = PlanStates.Fixed, MadeBy = Sam with { Maker = true }, MadeAt = new DateTimeOffset(2026, 10, 7, 0, 0, 0, TimeSpan.Zero),
        Mine = true, FixedTime = Saturday,
        Times = [new() { At = Saturday, In = 1, Mine = "in", Answers = [new() { UserId = "u-sam", Name = "Sam", Answer = "in" }] }],
        People = [Sam with { Maker = true }, Ana with { Maker = false }], Readiness = PlanReadiness.Ready,
    };

    private static ReplayHandler World()
    {
        var world = CinemaWorld.Build();
        world.Answer("/api/me/plans", new List<PlanView> { AnasPlan(), SamsPlan() });
        world.Answer("/api/plans/p-ana", AnasPlan());
        world.Answer("/api/plans/p-sam", SamsPlan());
        world.Answer("/api/people", new List<PersonView>
        {
            new() { UserId = "u-ana", Name = "Ana Ruiz", LastSeenAt = new DateTimeOffset(2026, 10, 8, 0, 0, 0, TimeSpan.Zero) },
            new() { UserId = "u-marc", Name = "Marc" },
            new() { UserId = "u-leo1", Name = "Leo Park" },
            new() { UserId = "u-leo2", Name = "Leo Grant" },
        });
        return world;
    }

    private static FilmAvailability Available(string imdbId, bool notOut = false) =>
        new() { ImdbId = imdbId, NotOut = notOut, Streams = new StreamSlots(0, 2) };

    private static ToolRig Rig(ReplayHandler? world = null) => new(world ?? World(), timeZone: Zone);

    /// <summary>A want's body, as the kind and whether it is visible.</summary>
    private static (string?, bool) Want(string body)
    {
        using JsonDocument json = JsonDocument.Parse(body);
        return (json.RootElement.GetProperty("kind").GetString(), json.RootElement.GetProperty("visible").GetBoolean());
    }

    private static string[] Written(ToolRig rig) => [.. rig.World.Asked.Where(a => !a.StartsWith("GET ", StringComparison.Ordinal))];

    // ── wanted_together ────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task What_is_wanted_together_names_who_wants_it_and_carries_each_title()
    {
        var world = World().Answer("/api/me/together", new List<TogetherView>
        {
            new()
            {
                Want = new() { ImdbId = "tt1000004", Kind = "film", Title = "Coastal Hours", Card = CinemaWorld.CoastalHours, Visible = true,
                               Since = CinemaWorld.CoastalHours.ChangedAt ?? default, State = WantStates.Ready, InLibrary = true },
                With = [new() { UserId = "u-ana", Name = "Ana Ruiz", Since = default }, new() { UserId = "u-marc", Name = "Marc", Since = default }],
            },
            new()
            {
                Want = new() { ImdbId = "tt1000006", Kind = "show", Title = "The Salt Line", Visible = true, Since = default,
                               NextEpisode = new() { Season = 1, Episode = 3 } },
                With = [new() { UserId = "u-ana", Name = "Ana Ruiz", Since = default }],
            },
        });
        var rig = Rig(world);

        ToolOutcome result = await rig.RunAsync(rig.Together, new { });

        Assert.Contains("- Coastal Hours (2021), film. In the library. Also wanted by Ana Ruiz, Marc. id tt1000004.", result.Text);
        Assert.Contains("- The Salt Line, show. Everybody listed is at season 1 episode 3. Also wanted by Ana Ruiz. id tt1000006.", result.Text);
        Assert.Equal(["tt1000004", "tt1000006"], ToolRig.CardIds(result));
    }

    [Fact]
    public async Task Nothing_wanted_together_says_how_others_find_a_want()
    {
        var rig = Rig(World().Answer("/api/me/together", new List<TogetherView>()));

        ToolOutcome result = await rig.RunAsync(rig.Together, new { });

        Assert.Contains("A want everybody can see is how others find something to watch with them.", result.Text);
        Assert.Null(result.Card);
    }

    // ── my_plans ───────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task A_plan_is_read_in_the_persons_clock_with_every_answer_and_what_is_measured_of_the_title()
    {
        var rig = Rig();

        ToolOutcome result = await rig.RunAsync(rig.Plans, new { });

        Assert.Contains("- Coastal Hours (2021), film. Plan id p-ana. Made by Ana Ruiz.", result.Text);
        Assert.Contains("No time is picked yet; only whoever made the plan picks one.", result.Text);
        Assert.Contains("Fri 2026-10-16 21:00: in: Ana Ruiz, Marc; no answer yet: Sam (the person)", result.Text);
        Assert.Contains("Sat 2026-10-17 21:00: in: Ana Ruiz; can't: Marc; no answer yet: Sam (the person)", result.Text);
        Assert.Contains("The title is arriving, 40% there.", result.Text);
        Assert.Contains("Made by Sam (the person).", result.Text);
        Assert.Contains("Starts at season 1 episode 3.", result.Text);
        Assert.Contains("Picked time: Sat 2026-10-17 21:00.", result.Text);

        JsonElement card = result.Card!.Value;
        Assert.Equal("plans", card.GetProperty("kind").GetString());
        Assert.Equal(["p-ana", "p-sam"], card.GetProperty("plans").EnumerateArray().Select(p => p.GetProperty("id").GetString()));
    }

    [Fact]
    public async Task Without_a_zone_from_the_browser_times_are_in_the_hosts_clock()
    {
        var rig = new ToolRig(World());

        ToolOutcome result = await rig.RunAsync(rig.Plans, new { });

        Assert.Contains("Fri 2026-10-16 19:00", result.Text);
    }

    // ── want ───────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Wanting_proposes_only_what_is_not_wanted_and_writes_nothing_until_confirmed()
    {
        var world = World().Answer("PUT", "/api/me/wants/tt1000004", new SetWantReply { Outcome = WantOutcomes.InLibrary });
        var rig = Rig(world);

        ToolOutcome proposed = await rig.RunAsync(rig.Want, new { title = new[] { "tt1000004", "tt1000003" } });

        Assert.NotNull(proposed.Proposal);
        Assert.Contains("Already on the person's Want to watch: The Lantern Keeper (2019).", proposed.Text);
        Assert.Contains("runs only if the person confirms it", proposed.Text);
        Assert.EndsWith("never \"I've added\", \"I've made\" or \"done\".", proposed.Text);
        Assert.Equal("Put Coastal Hours (2021) on your Want to watch\nEverybody signed in sees them.", proposed.Proposal!.Summary);
        Assert.Empty(Written(rig));

        StagedConfirmation confirmed = await proposed.ConfirmAsync(rig.Person);

        Assert.Equal(["PUT /api/me/wants/tt1000004"], Written(rig));
        Assert.Equal(("film", true), Want(rig.Sent.Bodies.Single().Body));
        Assert.Equal("Coastal Hours (2021) is on your Want to watch, and ready to watch.", confirmed.Result.Text);
    }

    [Fact]
    public async Task A_want_kept_to_the_person_is_sent_as_not_visible()
    {
        var world = World().Answer("PUT", "/api/me/wants/tt1000004", new SetWantReply { Outcome = WantOutcomes.Wanted });
        var rig = Rig(world);

        ToolOutcome proposed = await rig.RunAsync(rig.Want, new { title = new[] { "tt1000004" }, just_me = true });
        await proposed.ConfirmAsync(rig.Person);

        Assert.EndsWith("\nKept to you.", proposed.Proposal!.Summary);
        Assert.Equal(("film", false), Want(rig.Sent.Bodies.Single().Body));
    }

    [Fact]
    public async Task Taking_a_title_off_deletes_its_want_once_confirmed()
    {
        var world = World().Answer("DELETE", "/api/me/wants/tt1000003", "", 204);
        var rig = Rig(world);

        ToolOutcome proposed = await rig.RunAsync(rig.Want, new { title = new[] { "The Lantern Keeper (2019)" }, remove = true });
        StagedConfirmation confirmed = await proposed.ConfirmAsync(rig.Person);

        Assert.Equal("Take The Lantern Keeper (2019) off your Want to watch", proposed.Proposal!.Summary);
        Assert.Equal(["DELETE /api/me/wants/tt1000003"], Written(rig));
        Assert.Equal("The Lantern Keeper (2019) is off your Want to watch.", confirmed.Result.Text);
    }

    [Fact]
    public async Task Wanting_what_is_already_wanted_proposes_nothing()
    {
        var rig = Rig();

        ToolOutcome result = await rig.RunAsync(rig.Want, new { title = new[] { "tt1000003" } });

        Assert.Null(result.Proposal);
        Assert.Contains("Nothing was proposed.", result.Text);
    }

    // ── make_plan ──────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task A_plan_is_proposed_in_the_persons_clock_and_made_with_utc_times_and_account_ids()
    {
        var world = World()
            .Answer("/api/films/tt1000003/availability", Available("tt1000003"))
            .Answer("POST", "/api/plans", AnasPlan() with { Id = "p-new", ImdbId = "tt1000003", Title = "The Lantern Keeper", Mine = true });
        var rig = Rig(world);

        ToolOutcome proposed = await rig.RunAsync(rig.MakePlan,
            new { title = "tt1000003", times = new[] { "2026-10-16 21:00", "2026-10-17 21:00" }, people = new[] { "ana", "Marc" } });

        Assert.Equal("Watch The Lantern Keeper (2019) together\nFri 2026-10-16 21:00 · Sat 2026-10-17 21:00\nWith Ana Ruiz and Marc. Not in the library yet.",
            proposed.Proposal!.Summary);
        Assert.Empty(Written(rig));

        StagedConfirmation confirmed = await proposed.ConfirmAsync(rig.Person);

        using JsonDocument body = JsonDocument.Parse(rig.Sent.Bodies.Single().Body);
        Assert.Equal("tt1000003", body.RootElement.GetProperty("imdbId").GetString());
        Assert.Equal([Friday, Saturday], body.RootElement.GetProperty("times").EnumerateArray().Select(t => t.GetDateTimeOffset()));
        Assert.Equal(["u-ana", "u-marc"], body.RootElement.GetProperty("people").EnumerateArray().Select(p => p.GetString()));
        Assert.Contains("Ana Ruiz and Marc are told", confirmed.Result.Text);
        Assert.Equal("plans", confirmed.Result.Card!.Value.GetProperty("kind").GetString());
    }

    [Fact]
    public async Task A_name_that_fits_two_people_is_asked_about_not_guessed()
    {
        var rig = Rig(World().Answer("/api/films/tt1000003/availability", Available("tt1000003")));

        ToolOutcome result = await rig.RunAsync(rig.MakePlan, new { title = "tt1000003", times = new[] { "2026-10-16 21:00" }, people = new[] { "Leo" } });

        Assert.True(result.IsError);
        Assert.Contains("More than one person on Cinema could be 'Leo'", result.Text);
        Assert.Contains("- Leo Park, id u-leo1.", result.Text);
        Assert.Contains("- Leo Grant, id u-leo2.", result.Text);
    }

    [Fact]
    public async Task A_film_that_is_not_out_is_not_planned_and_want_is_offered()
    {
        var rig = Rig(World().Answer("/api/films/tt1000003/availability", Available("tt1000003", notOut: true)));

        ToolOutcome result = await rig.RunAsync(rig.MakePlan, new { title = "tt1000003", times = new[] { "2026-10-16 21:00" } });

        Assert.True(result.IsError);
        Assert.Contains("is not out yet, so it cannot be planned. Offer want instead", result.Text);
    }

    [Theory]
    [InlineData("2026-10-09 14:03", "at least 5 minutes and at most 30 days ahead")]
    [InlineData("2026-11-20 21:00", "at least 5 minutes and at most 30 days ahead")]
    [InlineData("friday at nine", "is not a time")]
    public async Task A_time_the_api_would_refuse_is_refused_before_anything_is_proposed(string time, string why)
    {
        var rig = Rig();

        ToolOutcome result = await rig.RunAsync(rig.MakePlan, new { title = "tt1000003", times = new[] { time } });

        Assert.True(result.IsError);
        Assert.Contains(why, result.Text);
        Assert.Empty(rig.World.Asked);
    }

    [Fact]
    public async Task A_plan_already_made_for_the_title_is_said_beside_the_proposal()
    {
        var rig = Rig(World().Answer("/api/films/tt1000004/availability", Available("tt1000004")));

        ToolOutcome result = await rig.RunAsync(rig.MakePlan, new { title = "tt1000004", times = new[] { "2026-10-18 21:00" } });

        Assert.NotNull(result.Proposal);
        Assert.StartsWith("The person is already in a plan for Coastal Hours (2021) (plan id p-ana).", result.Text);
    }

    // ── answer_plan ────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task An_answer_goes_to_every_time_ahead_by_default_at_the_instant_the_plan_names()
    {
        var world = World()
            .Answer("PUT", "/api/plans/p-ana/answers/2026-10-16T19%3A00%3A00Z", AnasPlan())
            .Answer("PUT", "/api/plans/p-ana/answers/2026-10-17T19%3A00%3A00Z", AnasPlan());
        var rig = Rig(world);

        ToolOutcome proposed = await rig.RunAsync(rig.AnswerPlan, new { plan = "p-ana", answer = "cant" });

        Assert.Equal("Answer can't to Coastal Hours (2021)\nFri 2026-10-16 21:00 · Sat 2026-10-17 21:00", proposed.Proposal!.Summary);
        Assert.Empty(Written(rig));

        StagedConfirmation confirmed = await proposed.ConfirmAsync(rig.Person);

        Assert.Equal(ConfirmationStatuses.Confirmed, confirmed.Status);
        Assert.Equal(["PUT /api/plans/p-ana/answers/2026-10-16T19%3A00%3A00Z", "PUT /api/plans/p-ana/answers/2026-10-17T19%3A00%3A00Z"], Written(rig));
        Assert.All(rig.Sent.Bodies, b => Assert.Equal("cant", JsonDocument.Parse(b.Body).RootElement.GetProperty("answer").GetString()));
    }

    [Fact]
    public async Task A_time_the_plan_does_not_have_is_answered_with_the_ones_it_has()
    {
        var rig = Rig();

        ToolOutcome result = await rig.RunAsync(rig.AnswerPlan, new { plan = "p-ana", answer = "in", times = new[] { "2026-10-16 22:00" } });

        Assert.True(result.IsError);
        Assert.Contains("Its times are: Fri 2026-10-16 21:00, Sat 2026-10-17 21:00.", result.Text);
    }

    [Fact]
    public async Task An_answer_the_person_already_gave_proposes_nothing()
    {
        var rig = Rig();

        ToolOutcome result = await rig.RunAsync(rig.AnswerPlan, new { plan = "p-sam", answer = "in" });

        Assert.Null(result.Proposal);
        Assert.Contains("is already in", result.Text);
    }

    // ── change_plan ────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Only_the_maker_picks_or_cancels_and_anybody_else_is_told_to_suggest()
    {
        var rig = Rig();

        ToolOutcome pick = await rig.RunAsync(rig.ChangePlan, new { plan = "p-ana", change = "pick", at = "2026-10-16 21:00" });
        ToolOutcome cancel = await rig.RunAsync(rig.ChangePlan, new { plan = "p-ana", change = "cancel" });

        Assert.Contains("Only Ana Ruiz, who made the plan, picks its time. Suggest one instead.", pick.Text);
        Assert.Contains("Only Ana Ruiz, who made the plan, can cancel it.", cancel.Text);
        Assert.Null(pick.Proposal);
        Assert.Null(cancel.Proposal);
    }

    [Fact]
    public async Task A_suggested_time_is_put_forward_in_utc_once_confirmed()
    {
        var rig = Rig(World().Answer("POST", "/api/plans/p-ana/times", AnasPlan()));

        ToolOutcome proposed = await rig.RunAsync(rig.ChangePlan, new { plan = "p-ana", change = "suggest", at = "2026-10-18 20:30" });
        await proposed.ConfirmAsync(rig.Person);

        Assert.Equal("Suggest Sun 2026-10-18 20:30 for Coastal Hours (2021)\nEverybody in the plan is told, and you are in for it.", proposed.Proposal!.Summary);
        using JsonDocument body = JsonDocument.Parse(rig.Sent.Bodies.Single().Body);
        Assert.Equal(new DateTimeOffset(2026, 10, 18, 18, 30, 0, TimeSpan.Zero), body.RootElement.GetProperty("at").GetDateTimeOffset());
    }

    [Fact]
    public async Task A_time_the_plan_has_is_answered_not_suggested_again()
    {
        var rig = Rig();

        ToolOutcome result = await rig.RunAsync(rig.ChangePlan, new { plan = "p-ana", change = "suggest", at = "2026-10-16 21:00" });

        Assert.Contains("is already one of the plan's times; answer it with answer_plan", result.Text);
    }

    [Fact]
    public async Task The_maker_cancels_their_own_plan_once_confirmed()
    {
        var rig = Rig(World().Answer("DELETE", "/api/plans/p-sam", "", 204));

        ToolOutcome proposed = await rig.RunAsync(rig.ChangePlan, new { plan = "p-sam", change = "cancel" });
        StagedConfirmation confirmed = await proposed.ConfirmAsync(rig.Person);

        Assert.Equal(["DELETE /api/plans/p-sam"], Written(rig));
        Assert.Equal("The plan to watch The Salt Line is cancelled, and everybody in it is told.", confirmed.Result.Text);
    }

    [Fact]
    public async Task Somebody_already_in_the_plan_is_not_invited_again()
    {
        var rig = Rig();

        ToolOutcome result = await rig.RunAsync(rig.ChangePlan, new { plan = "p-ana", change = "invite", person = "Marc" });

        Assert.Contains("Marc is in the plan already.", result.Text);
    }

    [Fact]
    public async Task A_refusal_at_confirmation_is_the_outcome_the_person_reads()
    {
        var world = World().Answer("POST", "/api/plans/p-sam/fix", """{"error":"The plan has no such time ahead."}""", 404);
        var rig = Rig(world);
        world.Answer("/api/plans/p-sam", SamsPlan() with
        {
            FixedTime = null,
            Times = [.. SamsPlan().Times, new() { At = Friday, Answers = [] }],
        });

        ToolOutcome proposed = await rig.RunAsync(rig.ChangePlan, new { plan = "p-sam", change = "pick", at = "2026-10-16 21:00" });
        StagedConfirmation confirmed = await proposed.ConfirmAsync(rig.Person);

        Assert.Equal(ConfirmationStatuses.Failed, confirmed.Status);
        Assert.Equal("The plan has no such time ahead.", confirmed.Result.Text);
    }

    // ── the person's clock ─────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task A_turn_tells_the_model_the_persons_time_when_their_zone_is_not_the_hosts()
    {
        var context = new CinemaTurnContext(ToolRig.Clock);
        var rig = Rig();

        IReadOnlyList<ContextBlock> blocks = await context.BuildAsync(rig.Person, "c1", CancellationToken.None);
        IReadOnlyList<ContextBlock> plain = await context.BuildAsync(new ToolRig(World()).Person, "c1", CancellationToken.None);

        Assert.Equal(["person", "clock"], blocks.Select(b => b.Name));
        Assert.StartsWith("The person's time zone is Europe/Madrid: for them it is 2026-10-09 14:00 (Friday).", blocks[1].Text);
        Assert.Equal(["person"], plain.Select(b => b.Name));
    }
}
