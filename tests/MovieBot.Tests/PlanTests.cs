using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using TheKrystalShip.MovieBot.Acquire.Download;
using TheKrystalShip.MovieBot.Acquire.Tmdb;
using TheKrystalShip.MovieBot.Api.Data;
using TheKrystalShip.MovieBot.Api.Library;
using TheKrystalShip.MovieBot.Api.Plans;
using TheKrystalShip.MovieBot.Api.Shows;
using TheKrystalShip.MovieBot.Api.Sessions;
using TheKrystalShip.MovieBot.Api.Subtitles;
using TheKrystalShip.MovieBot.Api.Wants;
using TheKrystalShip.MovieBot.Core;
using Xunit;

namespace TheKrystalShip.MovieBot.Tests;

/// <summary>The rules of a plan, what its store keeps, how its title is prefetched and how its room is kept.</summary>
public sealed class PlanTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 10, 20, 18, 0, 0, TimeSpan.Zero);

    private readonly string _root = Path.Combine(Path.GetTempPath(), "moviebot-plans", Guid.NewGuid().ToString("n"));
    private readonly CinemaDatabase _database;
    private readonly PlanStore _plans;

    public PlanTests()
    {
        Directory.CreateDirectory(_root);
        _database = new CinemaDatabase(Path.Combine(_root, "moviebot.db"));
        _plans = new PlanStore(_database);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    private StoredPlan Make(string id = "p1", string imdb = "tt1", bool mayFetch = true, params DateTimeOffset[] times) => _plans.Make(
        id, imdb, WantKinds.Film, null, null, "maker", mayFetch,
        times.Length == 0 ? [Now.AddHours(1), Now.AddHours(3)] : times, ["bea", "cal"], Now);

    // ---- Inviting -----------------------------------------------------------------

    [Fact]
    public void A_plan_takes_a_person_after_the_others_until_it_invites_its_most()
    {
        Make();
        Assert.Equal(InviteOutcome.Invited, _plans.Invite("p1", "dee", Now));
        Assert.Equal(["maker", "bea", "cal", "dee"], _plans.Get("p1")!.People);
        Assert.Equal(InviteOutcome.AlreadyIn, _plans.Invite("p1", "dee", Now));

        for (var i = 0; i < PlanRules.MaxInvited - 3; i++) Assert.Equal(InviteOutcome.Invited, _plans.Invite("p1", $"guest{i}", Now));
        Assert.Equal(InviteOutcome.Full, _plans.Invite("p1", "one-too-many", Now));

        Make("p2");
        Assert.True(_plans.Cancel("p2"));
        Assert.Equal(InviteOutcome.NoSuchPlan, _plans.Invite("p2", "dee", Now));
        Assert.Equal(InviteOutcome.NoSuchPlan, _plans.Invite("missing", "dee", Now));
    }

    // ---- The rules ----------------------------------------------------------------

    [Fact]
    public void A_plan_proposes_up_to_six_times_between_five_minutes_and_thirty_days_ahead()
    {
        Assert.Null(PlanRules.Times([Now.AddMinutes(5)], Now).Refusal);
        Assert.Null(PlanRules.Times([Now.AddDays(30)], Now).Refusal);
        Assert.NotNull(PlanRules.Times([Now.AddMinutes(4)], Now).Refusal);
        Assert.NotNull(PlanRules.Times([Now.AddDays(31)], Now).Refusal);
        Assert.NotNull(PlanRules.Times([], Now).Refusal);
        Assert.Null(PlanRules.Times(Enumerable.Range(1, 6).Select(h => Now.AddHours(h)), Now).Refusal);
        Assert.NotNull(PlanRules.Times(Enumerable.Range(1, 7).Select(h => Now.AddHours(h)), Now).Refusal);
    }

    [Fact]
    public void A_show_episode_the_database_dates_to_a_day_not_yet_due_is_not_aired_and_one_without_a_date_is_not_refused()
    {
        var today = new DateOnly(2026, 10, 20);
        var show = new StoredShow
        {
            FetchedAt = Now,
            Seasons =
            [
                new SeasonDetails
                {
                    Number = 1,
                    Episodes =
                    [
                        new() { Season = 1, Number = 1, AirDate = "2026-10-19" },
                        new() { Season = 1, Number = 2, AirDate = "2026-10-20" },
                        new() { Season = 1, Number = 3, AirDate = "2026-11-02" },
                        new() { Season = 1, Number = 4 },
                    ],
                },
            ],
        };

        Assert.False(PlanEndpoints.NotAired(show, new WantEpisode { Season = 1, Episode = 1 }, today));
        Assert.True(PlanEndpoints.NotAired(show, new WantEpisode { Season = 1, Episode = 2 }, today));
        Assert.True(PlanEndpoints.NotAired(show, new WantEpisode { Season = 1, Episode = 3 }, today));
        Assert.False(PlanEndpoints.NotAired(show, new WantEpisode { Season = 1, Episode = 4 }, today));
        Assert.False(PlanEndpoints.NotAired(show, new WantEpisode { Season = 2, Episode = 1 }, today));
        Assert.False(PlanEndpoints.NotAired(null, new WantEpisode { Season = 1, Episode = 1 }, today));
    }

    [Fact]
    public void Times_are_kept_to_the_minute_and_a_repeat_is_one_time()
    {
        var (times, refusal) = PlanRules.Times([Now.AddHours(1).AddSeconds(40), Now.AddHours(1).AddSeconds(5)], Now);

        Assert.Null(refusal);
        Assert.Equal([Now.AddHours(1)], times);
    }

    [Fact]
    public void Answers_never_pick_a_time()
    {
        var plan = Make();
        foreach (var user in new[] { "bea", "cal" }) _plans.Answer("p1", plan.Times[0], user, PlanAnswers.In, Now);

        var after = _plans.Get("p1")!;
        Assert.Equal(PlanStates.Proposed, after.State);
        Assert.Null(after.FixedTime);
        Assert.Equal(3, PlanRules.In(after, plan.Times[0]));
    }

    [Fact]
    public void Maybe_and_cant_are_not_counted_in()
    {
        var plan = Make();
        _plans.Answer("p1", plan.Times[0], "bea", PlanAnswers.Maybe, Now);
        _plans.Answer("p1", plan.Times[0], "cal", PlanAnswers.Cant, Now);

        Assert.Equal(1, PlanRules.In(_plans.Get("p1")!, plan.Times[0]));
    }

    [Fact]
    public void The_room_is_due_thirty_minutes_ahead_and_the_reminder_fifteen()
    {
        var plan = Make() with { FixedTime = Now.AddHours(1) };

        Assert.False(PlanRules.OpenDue(plan, Now.AddMinutes(29), 30));
        Assert.True(PlanRules.OpenDue(plan, Now.AddMinutes(30), 30));
        Assert.False(PlanRules.ReminderDue(plan, Now.AddMinutes(44), 15));
        Assert.True(PlanRules.ReminderDue(plan, Now.AddMinutes(45), 15));
        Assert.False(PlanRules.ReminderDue(plan, Now.AddHours(1).AddSeconds(1), 15));
    }

    [Fact]
    public void Readiness_is_what_is_measured_in_order_ready_fetching_waiting_queued()
    {
        var download = new DownloadStatus { Hash = "h", Name = "n", State = DownloadState.Downloading, Progress = 0.4 };

        Assert.Equal((PlanReadiness.Ready, (double?)null), PlanRules.Readiness(true, download, true));
        Assert.Equal((PlanReadiness.Fetching, (double?)0.4), PlanRules.Readiness(false, download, true));
        Assert.Equal((PlanReadiness.Waiting, (double?)null), PlanRules.Readiness(false, null, true));
        Assert.Equal((PlanReadiness.Queued, (double?)null), PlanRules.Readiness(false, null, false));
    }

    // ---- The store ----------------------------------------------------------------

    [Fact]
    public void The_maker_is_in_for_every_time_and_first_among_the_people()
    {
        var plan = Make();

        Assert.Equal("maker", plan.People[0]);
        Assert.Equal(3, plan.People.Count);
        Assert.All(plan.Times, at => Assert.Contains(new StoredAnswer(at, "maker", PlanAnswers.In), plan.Answers));
    }

    [Fact]
    public void Picking_a_time_keeps_every_time_and_answer_and_the_maker_picks_again()
    {
        var plan = Make();
        _plans.Answer("p1", plan.Times[1], "bea", PlanAnswers.In, Now);

        Assert.True(_plans.Fix("p1", plan.Times[0]));
        var picked = _plans.Get("p1")!;
        Assert.Equal(PlanStates.Fixed, picked.State);
        Assert.Equal(plan.Times[0], picked.FixedTime);
        Assert.Equal(plan.Times, picked.Times);
        Assert.Contains(new StoredAnswer(plan.Times[1], "bea", PlanAnswers.In), picked.Answers);

        Assert.True(_plans.Fix("p1", plan.Times[1]));
        Assert.Equal(plan.Times[1], _plans.Get("p1")!.FixedTime);

        _plans.Open("p1", "room");
        Assert.True(_plans.Fix("p1", plan.Times[0]));
        Assert.Equal(PlanStates.Open, _plans.Get("p1")!.State);
        Assert.False(_plans.Fix("p1", Now.AddDays(9)));
    }

    [Fact]
    public void Anybody_in_a_plan_suggests_a_time_they_are_in_for_up_to_six_ahead()
    {
        var plan = Make();
        var later = Now.AddHours(5);

        Assert.Equal(SuggestOutcome.Suggested, _plans.Suggest("p1", later, "bea", Now));
        var after = _plans.Get("p1")!;
        Assert.Contains(later, after.Times);
        Assert.Equal("bea", after.SuggestedBy[later]);
        Assert.Equal("maker", after.SuggestedBy[plan.Times[0]]);
        Assert.Contains(new StoredAnswer(later, "bea", PlanAnswers.In), after.Answers);

        Assert.Equal(SuggestOutcome.AlreadyThere, _plans.Suggest("p1", later, "cal", Now));
        Assert.Equal(SuggestOutcome.NotInPlan, _plans.Suggest("p1", Now.AddHours(6), "stranger", Now));
        for (var h = 6; h <= 8; h++) Assert.Equal(SuggestOutcome.Suggested, _plans.Suggest("p1", Now.AddHours(h), "cal", Now));
        Assert.Equal(SuggestOutcome.Full, _plans.Suggest("p1", Now.AddHours(9), "cal", Now));

        // A time that has passed no longer counts against the plan.
        Assert.Equal(SuggestOutcome.Suggested, _plans.Suggest("p1", Now.AddHours(9), "cal", Now.AddHours(2)));

        _plans.Cancel("p1");
        Assert.Equal(SuggestOutcome.NotInPlan, _plans.Suggest("p1", Now.AddHours(10), "cal", Now));
    }

    [Fact]
    public void Answers_change_while_the_plan_is_kept_and_only_for_the_people_in_it()
    {
        var plan = Make();
        var at = plan.Times[0];

        Assert.Equal(AnswerOutcome.Stored, _plans.Answer("p1", at, "bea", PlanAnswers.Maybe, Now));
        Assert.Equal(AnswerOutcome.Stored, _plans.Answer("p1", at, "bea", PlanAnswers.In, Now));
        Assert.Equal(PlanAnswers.In, _plans.Get("p1")!.Answers.Single(a => a.UserId == "bea" && a.At == at).Answer);
        Assert.Equal(AnswerOutcome.NotInPlan, _plans.Answer("p1", at, "stranger", PlanAnswers.In, Now));
        Assert.Equal(AnswerOutcome.NoSuchTime, _plans.Answer("p1", Now.AddDays(2), "bea", PlanAnswers.In, Now));

        _plans.Fix("p1", at);
        Assert.Equal(AnswerOutcome.Stored, _plans.Answer("p1", at, "cal", PlanAnswers.In, Now));
        _plans.Open("p1", "room");
        Assert.Equal(AnswerOutcome.Stored, _plans.Answer("p1", at, "cal", PlanAnswers.Cant, Now));
        _plans.Finish("p1");
        Assert.Equal(AnswerOutcome.NotInPlan, _plans.Answer("p1", at, "cal", PlanAnswers.In, Now));
    }

    [Fact]
    public void Cancelling_deletes_the_plan_whatever_it_is_at()
    {
        Make("a");
        Make("b");
        Make("c");
        _plans.Fix("b", _plans.Get("b")!.Times[0]);
        _plans.Open("b", "room");
        _plans.Fix("c", _plans.Get("c")!.Times[0]);
        _plans.Open("c", "room-c");
        _plans.Finish("c");

        Assert.True(_plans.Cancel("a"));
        Assert.False(_plans.Cancel("a"));
        Assert.True(_plans.Cancel("b"));
        Assert.False(_plans.Cancel("c"));
        Assert.Null(_plans.Get("a"));
        Assert.Null(_plans.Get("b"));
        Assert.Equal(0L, _database.Read(c => c.Count("SELECT COUNT(*) FROM plan_answers")));
    }

    [Fact]
    public void A_plan_finishes_only_from_open()
    {
        var plan = Make();
        Assert.False(_plans.Finish("p1"));
        _plans.Fix("p1", plan.Times[0]);
        Assert.False(_plans.Finish("p1"));
        Assert.True(_plans.Open("p1", "room"));
        Assert.True(_plans.Finish("p1"));
        Assert.Null(_plans.Get("p1"));
    }

    [Fact]
    public void The_room_comes_to_those_who_said_in_or_maybe_to_the_fixed_time()
    {
        var plan = Make();
        var at = plan.Times[0];
        _plans.Answer("p1", at, "bea", PlanAnswers.Maybe, Now);
        _plans.Answer("p1", at, "cal", PlanAnswers.Cant, Now);
        _plans.Fix("p1", at);

        Assert.Equal(["maker", "bea"], PlanRules.Coming(_plans.Get("p1")!));
    }

    [Fact]
    public void A_person_sees_the_plans_they_are_in_and_nobody_elses()
    {
        Make("mine");
        _plans.Make("theirs", "tt2", WantKinds.Film, null, null, "other", true, [Now.AddHours(1)], ["dee"], Now);

        Assert.Equal(["mine"], _plans.For("bea", Now).Select(p => p.Id));
        Assert.Equal(["theirs"], _plans.For("dee", Now).Select(p => p.Id));
        Assert.Empty(_plans.For("nobody", Now));
    }

    [Fact]
    public void A_watched_or_cancelled_plan_is_listed_nowhere()
    {
        var plan = Make();
        Make("p2");
        _plans.Fix("p1", plan.Times[0]);
        _plans.Open("p1", "room");
        _plans.Finish("p1");
        _plans.Cancel("p2");

        Assert.Empty(_plans.For("bea", Now));
    }

    [Fact]
    public void A_file_brought_forward_keeps_its_going_plans_with_their_times_credited_to_the_maker_and_drops_the_ended()
    {
        var path = Path.Combine(_root, "older.db");
        PreviousSchema.Build(path, ["Tables", "Sorts", "Wants", "Together", "Plans", "TitleScores"], """
            INSERT INTO plans (id, imdb_id, kind, made_by, made_at, may_fetch, state) VALUES
                ('going', 'tt1', 'film', 'maker', '2026-10-01T00:00:00.0000000Z', 1, 'proposed'),
                ('watched', 'tt1', 'film', 'maker', '2026-10-01T00:00:00.0000000Z', 1, 'done'),
                ('off', 'tt1', 'film', 'maker', '2026-10-01T00:00:00.0000000Z', 1, 'cancelled');
            INSERT INTO plan_people (plan_id, user_id, invited_at) VALUES
                ('going', 'maker', '2026-10-01T00:00:00.0000000Z'), ('watched', 'maker', '2026-10-01T00:00:00.0000000Z'),
                ('off', 'maker', '2026-10-01T00:00:00.0000000Z');
            INSERT INTO plan_times (plan_id, at) VALUES
                ('going', '2026-10-21T18:00:00.0000000Z'), ('watched', '2026-10-21T18:00:00.0000000Z'),
                ('off', '2026-10-21T18:00:00.0000000Z');
            INSERT INTO plan_answers (plan_id, at, user_id, answer, answered_at) VALUES
                ('watched', '2026-10-21T18:00:00.0000000Z', 'maker', 'in', '2026-10-01T00:00:00.0000000Z');
            """);

        var store = new PlanStore(new CinemaDatabase(path));

        Assert.Null(store.Get("watched"));
        Assert.Null(store.Get("off"));
        var going = store.Get("going")!;
        Assert.Equal("maker", going.SuggestedBy[going.Times.Single()]);
    }

    // ---- Prefetch ------------------------------------------------------------------

    [Fact]
    public void Only_fixed_plans_whose_maker_may_fetch_name_a_title_to_fetch()
    {
        var a = Make("a", "tt1", mayFetch: true, Now.AddHours(1));
        var b = Make("b", "tt2", mayFetch: false, Now.AddHours(1));
        Make("c", "tt3", mayFetch: true, Now.AddHours(1));
        _plans.Fix("a", a.Times[0]);
        _plans.Fix("b", b.Times[0]);

        var planned = Assert.Single(_plans.Planned(Now));
        Assert.Equal("tt1", planned.ImdbId);
        Assert.Equal(a.Times[0], planned.At);
        Assert.Empty(_plans.Planned(a.Times[0]));
    }

    [Fact]
    public void A_title_only_a_plan_names_is_a_candidate_fetched_for_the_maker_and_goes_first()
    {
        var wanted = new WantCandidate("tt1", WantKinds.Film, 5, ["a"], Now.AddDays(-9));
        var planned = new PlannedTitle("tt2", WantKinds.Film, null, null, "maker", Now, Now.AddHours(2));

        var order = WantFetchRules.Order(WantFetchRules.WithPlans([wanted], [planned]));

        Assert.Equal(["tt2", "tt1"], order.Select(c => c.ImdbId));
        Assert.Equal(["maker"], order[0].Fetchers);
        Assert.Equal(Now.AddHours(2), order[0].PlannedAt);
    }

    [Fact]
    public void A_wanted_title_with_a_plan_takes_the_earliest_time_and_the_maker_as_a_fetcher()
    {
        var wanted = new WantCandidate("tt1", WantKinds.Show, 2, ["a"], Now.AddDays(-1));
        var later = new PlannedTitle("tt1", WantKinds.Show, 1, 4, "maker", Now, Now.AddDays(2));
        var sooner = new PlannedTitle("tt1", WantKinds.Show, 1, 3, "other", Now, Now.AddDays(1));

        var candidate = Assert.Single(WantFetchRules.WithPlans([wanted], [later, sooner]));

        Assert.Equal(Now.AddDays(1), candidate.PlannedAt);
        Assert.Equal(3, candidate.Episode!.Episode);
        Assert.Equal(["a", "other"], candidate.Fetchers);
        Assert.Equal(2, candidate.Wanters);
    }

    [Fact]
    public void The_titles_of_fixed_plans_are_held_in_the_library_until_their_time_passes()
    {
        var plan = Make("a", "tt1");
        _plans.Fix("a", plan.Times[0]);

        Assert.Contains("tt1", _plans.Titles(Now));
        Assert.DoesNotContain("tt1", _plans.Titles(plan.Times[0]));
    }

    // ---- The room ------------------------------------------------------------------

    private SessionStore Sessions(FakeTimeProvider clock)
    {
        var library = new TitleLibrary(
            new MediaRoots(Path.Combine(_root, "media")),
            new SubtitleStore(Path.Combine(_root, "subtitles"), _database),
            new PinStore(_database),
            NullLogger<TitleLibrary>.Instance);
        return new SessionStore(library, clock);
    }

    [Fact]
    public void A_room_made_ahead_is_closed_with_the_people_coming_let_in()
    {
        var sessions = Sessions(new FakeTimeProvider(Now));

        Assert.True(sessions.CreateClosed("plan-room", ["maker", "bea"]));

        Assert.False(sessions.Door("plan-room")!.Open);
        Assert.True(sessions.Enter("plan-room", "bea"));
        Assert.False(sessions.Enter("plan-room", "cal"));
        Assert.False(sessions.CreateClosed("plan-room", ["cal"]));
    }

    [Fact]
    public void The_reaper_leaves_a_planned_room_alone_until_the_idle_window_after_its_time()
    {
        var clock = new FakeTimeProvider(Now);
        var sessions = Sessions(clock);
        sessions.CreateClosed("plan-room", ["maker"]);
        var window = TimeSpan.FromMinutes(30);

        clock.Advance(TimeSpan.FromMinutes(45));
        Assert.Empty(sessions.Sweep(window, new HashSet<string> { "plan-room" }));
        Assert.NotNull(sessions.Get("plan-room"));

        Assert.Equal(["plan-room"], sessions.Sweep(window, new HashSet<string>()));
    }

    [Fact]
    public void The_rooms_held_are_those_of_open_plans_whose_time_is_later_than_the_cutoff()
    {
        var plan = Make();
        _plans.Fix("p1", plan.Times[0]);
        _plans.Open("p1", "room");

        Assert.Contains("room", _plans.RoomsHeld(plan.Times[0].AddMinutes(-30)));
        Assert.DoesNotContain("room", _plans.RoomsHeld(plan.Times[0].AddMinutes(1)));
    }
}
