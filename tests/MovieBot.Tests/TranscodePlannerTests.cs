using TheKrystalShip.MovieBot.Api.Transcode;
using TheKrystalShip.MovieBot.Core;
using Xunit;

namespace TheKrystalShip.MovieBot.Tests;

/// <summary>What the planner ranks, what it admits and what it leaves for a room that is watching.</summary>
public sealed class TranscodePlannerTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 4, 12, 0, 0, TimeSpan.Zero);
    private const double Duration = 6000;

    private static TranscodeOptions Options(Action<TranscodeOptions>? tweak = null)
    {
        var options = new TranscodeOptions();
        tweak?.Invoke(options);
        return options;
    }

    private static readonly TranscodeRung Top = new("v0", 1920, 1080, 9000);
    private static readonly TranscodeRung Step = new("v1", 1280, 720, 4000);
    private static readonly TranscodeRung Small = new("v2", 854, 480, Ladder.StepDowns[1].BitrateKbps);

    /// <summary>A film still being made, both rungs and its audio at the same head.</summary>
    private static PlanTitle Making(string id, double head, double? audio = null, params TranscodeRung[] rungs)
    {
        rungs = rungs.Length == 0 ? [Top, Step] : rungs;
        return new PlanTitle(id, TitleStatus.Transcoding, Duration, 1920, 1080, rungs, true,
            new TitleHeads(rungs.ToDictionary(r => r.Directory, _ => head), audio ?? head));
    }

    private static PlanTitle Finished(string id, params TranscodeRung[] rungs) =>
        new(id, TitleStatus.Ready, Duration, 1920, 1080, rungs.Length == 0 ? [Top, Step, Small] : rungs, true,
            new TitleHeads(new Dictionary<string, double>(), Duration));

    private static PlanRoom Room(string title, double playhead, params int[] wanted) =>
        new("room-" + title, title, playhead, 1, wanted.Length == 0 ? [IViewerQuality.Top] : wanted);

    private static TranscodeUnitReport Report(
        string id, TranscodeUnitState state, double? sinceSecondsAgo = null) => new()
    {
        Id = id,
        State = state,
        Since = sinceSecondsAgo is { } ago ? Now.AddSeconds(-ago) : null
    };

    private static TranscodePlan Plan(
        PlanTitle[] titles, PlanRoom[]? rooms = null, TranscodeUnitReport[]? reports = null, TranscodeOptions? options = null) =>
        TranscodePlanner.Plan(new PlanInput(
            Now, rooms ?? [], titles,
            reports is null ? null : new TranscodeStatus { Units = reports },
            options ?? Options()));

    private static TranscodePlanUnit Unit(TranscodePlan plan, string id) => plan.Units.Single(u => u.Id == id);

    private static string Video(string title, string dir) => TranscodePlanner.UnitId(TranscodeUnitKind.VideoRung, title, dir);
    private static string Audio(string title) => TranscodePlanner.UnitId(TranscodeUnitKind.Audio, title, null);

    [Fact]
    public void A_watched_film_short_of_the_lead_beats_a_film_a_room_just_asked_for()
    {
        var plan = Plan(
            [Making("watched", head: 100), Making("fresh", head: 0)],
            [Room("watched", playhead: 60), Room("fresh", playhead: 0)],
            options: Options(o => o.NvencBudget = 2));

        Assert.Equal(1, Unit(plan, Video("watched", "v0")).Tier);
        Assert.Equal(2, Unit(plan, Video("fresh", "v0")).Tier);
        Assert.True(Unit(plan, Video("watched", "v0")).Rank < Unit(plan, Video("fresh", "v0")).Rank);
        Assert.Equal(TranscodeDisposition.Run, Unit(plan, Video("watched", "v0")).Disposition);
    }

    [Fact]
    public void A_room_that_just_asked_for_a_film_preempts_work_nobody_is_watching()
    {
        var plan = Plan(
            [Making("unwatched", head: 500), Making("fresh", head: 0)],
            [Room("fresh", playhead: 0)],
            [Report(Video("unwatched", "v0"), TranscodeUnitState.Running, 600),
             Report(Video("unwatched", "v1"), TranscodeUnitState.Running, 600),
             Report(Audio("unwatched"), TranscodeUnitState.Running, 600)],
            Options(o => o.NvencBudget = 1.5));

        Assert.Equal(TranscodeDisposition.Run, Unit(plan, Video("fresh", "v0")).Disposition);
        Assert.Equal(TranscodeDisposition.Run, Unit(plan, Video("fresh", "v1")).Disposition);
        // What was running is stopped in place, with its state kept, for a wait of minutes.
        Assert.Equal(TranscodeDisposition.Hold, Unit(plan, Video("unwatched", "v0")).Disposition);
    }

    [Fact]
    public void The_top_rung_of_a_film_a_room_just_asked_for_comes_first()
    {
        var plan = Plan([Making("fresh", head: 0)], [Room("fresh", 0)], options: Options(o => o.PerRung = true));

        Assert.True(Unit(plan, Video("fresh", "v0")).Rank < Unit(plan, Video("fresh", "v1")).Rank);
    }

    [Fact]
    public void A_rung_comfortably_ahead_falls_below_a_rung_that_is_short()
    {
        var plan = Plan(
            [Making("ahead", head: 1000), Making("short", head: 100)],
            [Room("ahead", playhead: 100), Room("short", playhead: 60)]);

        Assert.Equal(3, Unit(plan, Video("ahead", "v0")).Tier);
        Assert.Equal(1, Unit(plan, Video("short", "v0")).Tier);
    }

    [Fact]
    public void A_quality_nobody_wants_is_below_one_somebody_does()
    {
        var plan = Plan(
            [Making("film", head: 1000)],
            [Room("film", playhead: 100, 1080)],
            options: Options(o => o.PerRung = true));

        Assert.Equal(3, Unit(plan, Video("film", "v0")).Tier);
        Assert.Equal(4, Unit(plan, Video("film", "v1")).Tier);
    }

    [Fact]
    public void A_viewer_capped_at_720_wants_the_720_rung_and_not_the_top()
    {
        var plan = Plan(
            [Making("film", head: 100)],
            [Room("film", playhead: 90, 720)],
            options: Options(o => o.PerRung = true));

        Assert.Equal(4, Unit(plan, Video("film", "v0")).Tier);
        Assert.Equal(1, Unit(plan, Video("film", "v1")).Tier);
    }

    [Fact]
    public void Unwatched_rungs_of_films_in_rooms_come_before_films_nobody_holds()
    {
        var plan = Plan(
            [Making("held", head: 1000), Making("nobody", head: 300)],
            [Room("held", playhead: 100, 1080)],
            options: Options(o => o.PerRung = true));

        Assert.Equal(4, Unit(plan, Video("held", "v1")).Tier);
        Assert.Equal(5, Unit(plan, Video("nobody", "v0")).Tier);
        Assert.True(Unit(plan, Video("held", "v1")).Rank < Unit(plan, Video("nobody", "v0")).Rank);
    }

    [Fact]
    public void Backfill_runs_while_no_room_is_playing_and_is_parked_while_one_is()
    {
        var owed = Finished("old", Top);

        var idle = Plan([owed]);
        var busy = Plan([owed, Making("film", head: 1000)], [Room("film", 100)]);

        var add = TranscodePlanner.UnitId(TranscodeUnitKind.RungAdd, "old", "v1");
        Assert.Equal(TranscodeDisposition.Run, Unit(idle, add).Disposition);
        Assert.Equal(6, Unit(idle, add).Tier);
        Assert.Equal(TranscodeDisposition.Park, Unit(busy, add).Disposition);
    }

    [Fact]
    public void Backfill_is_below_every_first_pass()
    {
        var plan = Plan([Finished("old", Top), Making("nobody", head: 100)]);

        var add = TranscodePlanner.UnitId(TranscodeUnitKind.RungAdd, "old", "v1");
        Assert.True(Unit(plan, Video("nobody", "v0")).Rank < Unit(plan, add).Rank);
    }

    [Fact]
    public void The_encoder_budget_is_spent_by_pixel_area_and_no_more()
    {
        // 1080p costs 1.0 and 720p about 0.44. Of two films, the first gets both rungs (1.44); the
        // second's top rung would make 2.44 and its step-down 1.88, so neither runs.
        var plan = Plan(
            [Making("a", head: 100), Making("b", head: 100)],
            options: Options(o => { o.NvencBudget = 1.5; o.PerRung = true; }));

        var running = plan.Units
            .Where(u => u.Disposition == TranscodeDisposition.Run && u.Kind == TranscodeUnitKind.VideoRung)
            .ToList();
        Assert.True(running.Sum(u => u.Rung!.Width * (double)u.Rung.Height / (1920.0 * 1080.0)) <= 1.5);
        Assert.Equal(["a", "a"], running.Select(u => u.TitleId));
    }

    [Fact]
    public void The_decoder_budget_counts_one_decode_per_video_unit()
    {
        var plan = Plan(
            [Making("a", head: 100), Making("b", head: 100)],
            options: Options(o => { o.NvdecBudget = 2; o.PerRung = true; }));

        Assert.Equal(2, plan.Units.Count(u => u.Disposition == TranscodeDisposition.Run && u.Kind == TranscodeUnitKind.VideoRung));
        Assert.Contains(plan.Units, u => u.Reason.Contains("decoder budget"));
    }

    [Fact]
    public void Audio_costs_neither_budget()
    {
        var plan = Plan(
            [Making("a", head: 100, audio: 0, Top)],
            options: Options(o => { o.NvencBudget = 1; o.NvdecBudget = 1; o.PerRung = true; }));

        Assert.All(plan.Units, u => Assert.Equal(TranscodeDisposition.Run, u.Disposition));
    }

    [Fact]
    public void A_film_dearer_than_the_whole_budget_still_runs_when_nothing_else_does()
    {
        var plan = Plan([Making("a", head: 100)], options: Options(o => o.NvencBudget = 0.1));

        Assert.All(plan.Units, u => Assert.Equal(TranscodeDisposition.Run, u.Disposition));
    }

    [Fact]
    public void A_films_rungs_and_audio_run_or_hold_together_by_default()
    {
        // The budget carries one rung and not two, so the whole film waits.
        var plan = Plan(
            [Making("a", head: 100), Making("b", head: 100)],
            [Room("a", playhead: 90)],
            [Report(Video("b", "v0"), TranscodeUnitState.Running, 600),
             Report(Video("b", "v1"), TranscodeUnitState.Running, 600),
             Report(Audio("b"), TranscodeUnitState.Running, 600)],
            Options(o => o.NvencBudget = 1.5));

        Assert.All(plan.Units.Where(u => u.TitleId == "a"), u => Assert.Equal(TranscodeDisposition.Run, u.Disposition));
        Assert.All(plan.Units.Where(u => u.TitleId == "b"), u => Assert.Equal(TranscodeDisposition.Hold, u.Disposition));
    }

    [Fact]
    public void Hysteresis_keeps_a_running_unit_short_of_lead_until_it_is_the_lead_plus_the_band_ahead()
    {
        var title = Making("film", head: 190, rungs: Top);   // 190 - 60 = 130 ahead: between 90 and 150
        var rooms = new[] { Room("film", playhead: 60) };

        var held = Plan([title], rooms, [Report(Video("film", "v0"), TranscodeUnitState.Held, 10)]);
        var running = Plan([title], rooms, [Report(Video("film", "v0"), TranscodeUnitState.Running, 100)]);

        Assert.Equal(3, Unit(held, Video("film", "v0")).Tier);
        Assert.Equal(1, Unit(running, Video("film", "v0")).Tier);
    }

    [Fact]
    public void A_unit_in_the_minimum_run_time_is_lifted_over_work_of_lower_urgency()
    {
        // "b" is a film nobody holds (tier 5) and "a" has an unwatched rung in a room (tier 4), so
        // "a" outranks it unless "b" has only just started.
        var titles = new[] { Making("a", head: 1000, rungs: [Top, Step]), Making("b", head: 100, rungs: Top) };
        var rooms = new[] { Room("a", playhead: 100, 1080) };
        var options = Options(o => { o.NvencBudget = 1; o.PerRung = true; });

        var fresh = Plan(titles, rooms, [Report(Video("b", "v0"), TranscodeUnitState.Running, 5)], options);
        var settled = Plan(titles, rooms, [Report(Video("b", "v0"), TranscodeUnitState.Running, 600)], options);

        Assert.Equal(3, Unit(fresh, Video("b", "v0")).Tier);
        Assert.Equal(TranscodeDisposition.Run, Unit(fresh, Video("b", "v0")).Disposition);
        Assert.Equal(TranscodeDisposition.Hold, Unit(settled, Video("b", "v0")).Disposition);
    }

    [Fact]
    public void A_watched_film_short_of_lead_still_displaces_a_unit_in_its_minimum_run_time()
    {
        var plan = Plan(
            [Making("watched", head: 100, rungs: Top), Making("b", head: 100, rungs: Top)],
            [Room("watched", playhead: 90)],
            [Report(Video("b", "v0"), TranscodeUnitState.Running, 5)],
            Options(o => { o.NvencBudget = 1; o.PerRung = true; }));

        Assert.Equal(TranscodeDisposition.Run, Unit(plan, Video("watched", "v0")).Disposition);
        Assert.Equal(TranscodeDisposition.Hold, Unit(plan, Video("b", "v0")).Disposition);
    }

    [Fact]
    public void A_hold_that_has_lasted_is_parked_and_so_is_one_beyond_the_count_the_card_keeps()
    {
        var titles = new[] { Making("w", head: 100, rungs: Top) };
        var rooms = new[] { Room("w", playhead: 90) };
        var options = Options(o => { o.NvencBudget = 1; o.PerRung = true; o.MaxHeld = 1; });

        var plan = Plan(
            [titles[0], Making("long", head: 100, rungs: Top), Making("recent", head: 100, rungs: Top), Making("extra", head: 100, rungs: Top)],
            rooms,
            [Report(Video("long", "v0"), TranscodeUnitState.Held, 600),
             Report(Video("recent", "v0"), TranscodeUnitState.Held, 10),
             Report(Video("extra", "v0"), TranscodeUnitState.Held, 10)],
            options);

        Assert.Equal(TranscodeDisposition.Park, Unit(plan, Video("long", "v0")).Disposition);
        var states = new[] { "recent", "extra" }.Select(t => Unit(plan, Video(t, "v0")).Disposition).ToList();
        Assert.Single(states, TranscodeDisposition.Hold);
        Assert.Single(states, TranscodeDisposition.Park);
    }

    [Fact]
    public void A_unit_the_executor_reports_failed_is_parked_and_spends_nothing()
    {
        var plan = Plan(
            [Making("bad", head: 100, rungs: Top), Making("good", head: 100, rungs: Top)],
            reports: [Report(Video("bad", "v0"), TranscodeUnitState.Failed)],
            options: Options(o => { o.NvencBudget = 1; o.PerRung = true; }));

        Assert.Equal(TranscodeDisposition.Park, Unit(plan, Video("bad", "v0")).Disposition);
        Assert.Equal(TranscodeDisposition.Run, Unit(plan, Video("good", "v0")).Disposition);
    }

    [Fact]
    public void The_executors_measured_head_replaces_the_manifests()
    {
        var title = Making("film", head: 50, rungs: Top);
        var rooms = new[] { Room("film", playhead: 40) };

        var plan = Plan([title], rooms, [new TranscodeUnitReport
        {
            Id = Video("film", "v0"), State = TranscodeUnitState.Running,
            Heads = new Dictionary<string, double> { ["v0"] = 900 }
        }], Options(o => o.PerRung = true));

        var unit = Unit(plan, Video("film", "v0"));
        Assert.Equal(3, unit.Tier);
        Assert.Equal(450, unit.FromSegment);
    }

    [Fact]
    public void A_unit_resumes_from_the_segment_its_output_has_reached()
    {
        var plan = Plan([Making("film", head: 301.5, audio: 100, Top)]);

        Assert.Equal(150, Unit(plan, Video("film", "v0")).FromSegment);
        Assert.Equal(50, Unit(plan, Audio("film")).FromSegment);
    }

    [Fact]
    public void Unit_ids_do_not_change_as_the_work_progresses()
    {
        var early = Plan([Making("film", head: 10)]);
        var later = Plan([Making("film", head: 4000)]);

        Assert.Equal(early.Units.Select(u => u.Id).Order(), later.Units.Select(u => u.Id).Order());
    }

    [Fact]
    public void A_finished_output_is_no_unit()
    {
        var plan = Plan([Making("film", head: Duration - 1, audio: Duration)]);

        Assert.Empty(plan.Units);
    }

    [Fact]
    public void A_film_may_settle_when_it_is_ready_and_no_room_holds_it()
    {
        var plan = Plan(
            [Finished("watched"), Finished("idle"), Making("making", head: 10)],
            [Room("watched", 10)]);

        Assert.Equal(2, plan.Titles.Count);
        Assert.False(plan.Titles.Single(t => t.TitleId == "watched").MaySettle);
        Assert.True(plan.Titles.Single(t => t.TitleId == "idle").MaySettle);
    }

    [Fact]
    public void An_idle_room_holding_a_finished_film_does_not_block_it_settling()
    {
        var plan = Plan(
            [Finished("film")],
            [new PlanRoom("idle", "film", 90, 0, [IViewerQuality.Top])]);

        Assert.True(plan.Titles.Single().MaySettle);
    }

    [Fact]
    public void A_paused_room_with_somebody_in_it_blocks_settling()
    {
        var plan = Plan([Finished("film")], [Room("film", 90)]);

        Assert.False(plan.Titles.Single().MaySettle);
    }

    [Fact]
    public void A_room_nobody_is_in_is_not_demand()
    {
        var plan = Plan(
            [Making("film", head: 100, rungs: Top), Finished("done")],
            [new PlanRoom("empty", "film", 90, 0, [IViewerQuality.Top])]);

        Assert.Equal(5, Unit(plan, Video("film", "v0")).Tier);
        Assert.True(plan.Titles.Single().MaySettle);
    }

    [Fact]
    public void A_plan_made_without_the_executor_says_so()
    {
        Assert.False(Plan([Making("film", head: 10)]).ExecutorReporting);
        Assert.True(Plan([Making("film", head: 10)], reports: []).ExecutorReporting);
    }

    // Owed work.

    [Fact]
    public void A_finished_film_with_one_rung_of_a_wide_source_is_owed_both_step_downs()
    {
        var plan = Plan([Finished("film", Top)]);

        Assert.Equal(2, plan.Units.Count);
        Assert.All(plan.Units, u => Assert.Equal(TranscodeUnitKind.RungAdd, u.Kind));

        var step = Unit(plan, TranscodePlanner.UnitId(TranscodeUnitKind.RungAdd, "film", "v1"));
        Assert.Equal(1280, step.Rung!.Width);
        Assert.Equal(720, step.Rung.Height);
        Assert.Equal(4000, step.Rung.BitrateKbps);
        Assert.Equal(0, step.FromSegment);

        var small = Unit(plan, TranscodePlanner.UnitId(TranscodeUnitKind.RungAdd, "film", "v2"));
        Assert.Equal(854, small.Rung!.Width);
        Assert.Equal(480, small.Rung.Height);
    }

    [Fact]
    public void A_finished_film_with_two_rungs_is_owed_the_small_one()
    {
        var plan = Plan([Finished("film", Top, Step)]);

        var owed = Assert.Single(plan.Units);
        Assert.Equal(TranscodePlanner.UnitId(TranscodeUnitKind.RungAdd, "film", "v2"), owed.Id);
        Assert.Equal(new TranscodeRung("v2", 854, 480, Ladder.StepDowns[1].BitrateKbps), owed.Rung);
        Assert.Equal(6, owed.Tier);
    }

    [Fact]
    public void A_finished_film_with_its_whole_ladder_is_owed_nothing()
    {
        Assert.Empty(Plan([Finished("film")]).Units);
    }

    /// <summary>A viewer capped at 480p is served the small rung rather than the step-down.</summary>
    [Fact]
    public void A_low_wanted_height_is_served_by_the_small_rung()
    {
        Assert.Equal("v2", TranscodePlanner.RungFor([Top, Step, Small], 480)!.Directory);
        Assert.Equal("v2", TranscodePlanner.RungFor([Top, Step, Small], 360)!.Directory);
        Assert.Equal("v1", TranscodePlanner.RungFor([Top, Step, Small], 720)!.Directory);
    }

    [Fact]
    public void A_source_between_the_step_downs_is_owed_the_small_rung_alone()
    {
        var narrow = new PlanTitle("narrow", TitleStatus.Ready, Duration, 1280, 720, [new("v0", 1280, 720, 9000)], true,
            new TitleHeads(new Dictionary<string, double>(), Duration));

        var owed = Assert.Single(Plan([narrow]).Units);
        Assert.Equal("v2", owed.Rung!.Directory);
    }

    [Fact]
    public void A_source_no_wider_than_the_small_rung_is_owed_no_second_rung()
    {
        var narrow = new PlanTitle("narrow", TitleStatus.Ready, Duration, 854, 480, [new("v0", 854, 480, 9000)], true,
            new TitleHeads(new Dictionary<string, double>(), Duration));

        Assert.Empty(Plan([narrow]).Units);
    }

    [Fact]
    public void A_failed_title_is_owed_nothing()
    {
        var failed = Finished("film", Top) with { Status = TitleStatus.Failed };

        Assert.Empty(Plan([failed]).Units);
    }

    [Fact]
    public void A_title_still_transcoding_contributes_its_unfinished_rungs_and_audio_and_no_backfill()
    {
        var plan = Plan([Making("film", head: 100, audio: Duration, Top, Step)]);

        Assert.Equal(2, plan.Units.Count);
        Assert.All(plan.Units, u => Assert.Equal(TranscodeUnitKind.VideoRung, u.Kind));
    }

    [Fact]
    public void The_ladder_the_planner_compares_against_is_the_one_the_ingest_makes()
    {
        foreach (var (width, height) in new[] { (1920, 1080), (1920, 800), (3840, 2160), (1280, 720), (640, 480) })
        {
            var core = Ladder.For(width, height);
            var ingest = TheKrystalShip.MovieBot.Ingest.Pipeline.VideoLadder.For(
                width, height, new TheKrystalShip.MovieBot.Ingest.Pipeline.IngestOptions
                {
                    SourcePath = "/dev/null", OutputRoot = "/tmp"
                });

            Assert.Equal(core.Select(r => (r.Directory, r.Width, r.Height, r.BitrateKbps)),
                ingest.Select(r => (r.Directory, r.Width, r.Height, r.BitrateKbps)));
        }
    }
}
