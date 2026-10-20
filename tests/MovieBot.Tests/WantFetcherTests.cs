using TheKrystalShip.MovieBot.Acquire.Download;
using TheKrystalShip.MovieBot.Acquire.Search;
using TheKrystalShip.MovieBot.Api.Wants;
using TheKrystalShip.MovieBot.Core;
using Xunit;

namespace TheKrystalShip.MovieBot.Tests;

/// <summary>The rules that decide whether and what the prefetcher fetches.</summary>
public sealed class WantFetcherTests
{
    private static readonly DateTimeOffset Day = new(2026, 10, 1, 0, 0, 0, TimeSpan.Zero);

    private static StoredWant Want(string user, string imdb, int days, bool mayFetch = true, string kind = WantKinds.Film) => new()
    {
        UserId = user, ImdbId = imdb, Kind = kind, Visible = true, MayFetch = mayFetch, MayWish = true, Since = Day.AddDays(days),
    };

    private static BudgetReading Disk(long used = 0, long free = 100L << 30, long reserved = 10L << 30) =>
        new(used, 500L << 30, 450L << 30, free, reserved);

    [Fact]
    public void A_title_nobody_may_fetch_for_is_no_candidate()
    {
        var candidates = WantFetchRules.Candidates([Want("a", "tt1", 0, mayFetch: false)]);

        Assert.Empty(candidates);
    }

    [Fact]
    public void Every_want_counts_as_a_wanter_but_only_the_fetching_ones_are_fetched_for()
    {
        var candidate = Assert.Single(WantFetchRules.Candidates([
            Want("b", "tt1", 2), Want("a", "tt1", 1), Want("c", "tt1", 3, mayFetch: false)]));

        Assert.Equal(3, candidate.Wanters);
        Assert.Equal(["a", "b"], candidate.Fetchers);
        Assert.Equal(Day.AddDays(1), candidate.Oldest);
    }

    [Fact]
    public void The_most_wanted_title_is_first_then_the_oldest_want()
    {
        var order = WantFetchRules.Order(WantFetchRules.Candidates([
            Want("a", "tt1", 0), Want("a", "tt2", 5), Want("b", "tt2", 6), Want("a", "tt3", 1)]));

        Assert.Equal(["tt2", "tt1", "tt3"], order.Select(c => c.ImdbId));
    }

    [Fact]
    public void A_title_with_a_plan_comes_first_the_earliest_plan_first()
    {
        var order = WantFetchRules.Order([
            new WantCandidate("tt1", WantKinds.Film, 9, ["a"], Day),
            new WantCandidate("tt2", WantKinds.Film, 1, ["a"], Day, Day.AddDays(5)),
            new WantCandidate("tt3", WantKinds.Film, 1, ["a"], Day, Day.AddDays(2))]);

        Assert.Equal(["tt3", "tt2", "tt1"], order.Select(c => c.ImdbId));
    }

    [Fact]
    public void Nothing_is_started_while_a_room_plays_or_a_room_waits_on_a_download()
    {
        Assert.NotNull(WantFetchRules.Withhold(somebodyPlaying: true, watchNowArriving: 0, prefetchArriving: 0, concurrent: 1));
        Assert.NotNull(WantFetchRules.Withhold(false, watchNowArriving: 1, 0, 1));
        Assert.Null(WantFetchRules.Withhold(false, 0, 0, 1));
    }

    [Fact]
    public void The_prefetcher_holds_at_its_concurrent_limit()
    {
        Assert.NotNull(WantFetchRules.Withhold(false, 0, prefetchArriving: 1, concurrent: 1));
        Assert.Null(WantFetchRules.Withhold(false, 0, prefetchArriving: 1, concurrent: 2));
        Assert.NotNull(WantFetchRules.Withhold(false, 0, prefetchArriving: 1, concurrent: 0));
    }

    [Fact]
    public void A_release_fits_only_when_the_budget_is_ok_and_the_headroom_holds_it()
    {
        Assert.True(WantFetchRules.Fits(Disk(), 4L << 30));
        Assert.False(WantFetchRules.Fits(Disk(), 95L << 30));
        Assert.False(WantFetchRules.Fits(Disk(used: 460L << 30), 1L << 30));
        Assert.False(WantFetchRules.Fits(Disk(free: 10L << 30), 1L << 30));
    }

    [Fact]
    public void The_quality_is_the_best_any_wanter_watches_at()
    {
        Assert.Equal(Resolution.Hd1080, WantFetchRules.Quality([Resolution.Hd720, Resolution.Hd1080]));
        Assert.Equal(Resolution.Hd720, WantFetchRules.Quality([Resolution.Hd720]));
        Assert.Equal(Resolution.Hd720, WantFetchRules.Quality([]));
    }

    [Fact]
    public void A_show_is_fetched_from_the_earliest_episode_any_wanter_is_up_to()
    {
        var earliest = WantFetchRules.Earliest([
            new WantEpisode { Season = 2, Episode = 1 }, null, new WantEpisode { Season = 1, Episode = 8 }]);

        Assert.Equal((1, 8), (earliest!.Season, earliest.Episode));
        Assert.Null(WantFetchRules.Earliest([null]));
    }
}
