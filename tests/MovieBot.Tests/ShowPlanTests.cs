using TheKrystalShip.MovieBot.Client;
using TheKrystalShip.MovieBot.Core;
using Xunit;

namespace TheKrystalShip.MovieBot.Tests;

/// <summary>
/// Which releases fetch the episodes somebody picked: a season's pack at the quality first, single
/// episodes where there is no pack, another quality only where the asked one is not offered.
/// </summary>
public class ShowPlanTests
{
    private static ReleaseOffer Offer(long id, string resolution, string episodic, long size, int count = 1) => new()
    {
        TorrentId = id, ReleaseName = $"Show.{episodic}.{resolution}", Title = "Show", Display = $"Show {episodic}",
        Summary = resolution, Resolution = resolution, Episodic = episodic, SizeBytes = size,
        EpisodeCount = count, SizePerEpisode = size / count,
    };

    private static readonly ShowOffers Offers = new(
    [
        new SeasonOffers(1, [Offer(10, "720p", "S01", 6_000, 6), Offer(11, "1080p", "S01", 18_000, 6)], []),
        new SeasonOffers(6, [],
        [
            new EpisodeOffers(1, [Offer(61, "720p", "S06E01", 1_200), Offer(62, "1080p", "S06E01", 3_600)]),
            new EpisodeOffers(2, [Offer(63, "1080p", "S06E02", 3_500)]),
        ]),
    ]);

    [Fact]
    public void Picked_episodes_of_a_packed_season_are_one_download_of_the_pack()
    {
        var plan = ShowPlan.For(Offers, [new(1, 1), new(1, 3)], "720p");

        var download = Assert.Single(plan.Downloads);
        Assert.Equal(10, download.Release.TorrentId);
        Assert.Equal([new EpisodePick(1, 1), new EpisodePick(1, 3)], download.Episodes);
        Assert.Equal(2_000, plan.Bytes);
    }

    [Fact]
    public void An_airing_season_comes_episode_by_episode_and_says_what_came_at_another_quality()
    {
        var plan = ShowPlan.For(Offers, [new(6, 1), new(6, 2), new(6, 3)], "720p");

        Assert.Equal([61L, 63L], plan.Downloads.Select(d => d.Release.TorrentId));
        Assert.All(plan.Downloads, d => Assert.Null(d.Episodes));
        Assert.Equal(1, plan.OtherQuality);
        Assert.Equal([new EpisodePick(6, 3)], plan.NotOffered);
    }

    [Fact]
    public void Asking_for_1080p_takes_the_1080p_pack()
    {
        var plan = ShowPlan.For(Offers, [new(1, 2)], "1080p");

        Assert.Equal(11, Assert.Single(plan.Downloads).Release.TorrentId);
    }

    [Theory]
    [InlineData("1-3, 5", new[] { 1, 2, 3, 5 })]
    [InlineData("E2,e4", new[] { 2, 4 })]
    public void Episodes_are_named_by_numbers_and_ranges(string named, int[] episodes) =>
        Assert.Equal(episodes, ShowPlan.Episodes(Offers, 1, named)!.Select(p => p.Episode));

    [Fact]
    public void Leaving_the_episodes_out_takes_every_one_on_offer() =>
        Assert.Equal([1, 2, 3, 4, 5, 6], ShowPlan.Episodes(Offers, 1, null)!.Select(p => p.Episode));

    [Fact]
    public void Text_that_names_no_episodes_is_no_answer() =>
        Assert.Null(ShowPlan.Episodes(Offers, 1, "the first few"));
}
