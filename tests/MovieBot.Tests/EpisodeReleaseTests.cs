using TheKrystalShip.MovieBot.Acquire.Download;
using TheKrystalShip.MovieBot.Acquire.Search;
using TheKrystalShip.MovieBot.Handoff;
using Xunit;

namespace TheKrystalShip.MovieBot.Tests;

/// <summary>
/// An episode a room moved past is let go of only when nothing else wants its file, and the
/// hand-off's verdict on an episode that failed while its file arrived is not final.
/// </summary>
public class PassedEpisodesTests
{
    private static readonly EpisodeNumber Four = new(2, 4);
    private static readonly EpisodeFile FourFile = new("Show.S02/Show.S02E04.mkv", 10, [Four]);

    private static string[] Picked(params string[] more) => [TorrentTags.PicksNamed, .. more];

    [Fact]
    public void An_episode_only_the_room_that_left_asked_for_is_let_go_of() =>
        Assert.True(PassedEpisodes.LetGo(Picked(), Four, FourFile, started: false));

    [Fact]
    public void Another_room_waiting_on_it_keeps_it() =>
        Assert.False(PassedEpisodes.LetGo(Picked(TorrentTags.Watch("other", Four)), Four, FourFile, started: false));

    [Fact]
    public void A_room_waiting_on_another_episode_does_not_keep_it() =>
        Assert.True(PassedEpisodes.LetGo(Picked(TorrentTags.Watch("other", new EpisodeNumber(2, 5))), Four, FourFile, started: false));

    [Theory]
    [InlineData("keep:s02e04")]
    [InlineData("keep")]
    public void Something_that_kept_it_keeps_it(string keep) =>
        Assert.False(PassedEpisodes.LetGo(Picked(keep), Four, FourFile, started: false));

    [Fact]
    public void Its_transcode_having_begun_keeps_it() =>
        Assert.False(PassedEpisodes.LetGo(Picked(), Four, FourFile, started: true));

    [Fact]
    public void A_torrent_whose_picks_did_not_all_say_why_keeps_everything() =>
        Assert.False(PassedEpisodes.LetGo([], Four, FourFile, started: false));

    [Fact]
    public void A_file_holding_two_episodes_is_kept() =>
        Assert.False(PassedEpisodes.LetGo(Picked(), Four,
            new EpisodeFile("Show.S02E04E05.mkv", 10, [Four, new EpisodeNumber(2, 5)]), started: false));

    [Fact]
    public void An_episode_the_torrent_has_no_file_for_is_left_alone() =>
        Assert.False(PassedEpisodes.LetGo(Picked(), Four, null, started: false));
}

public class EpisodeFailuresTests
{
    private const string Hash = "abc";
    private const string File = "Show.S02/Show.S02E04.mkv";

    [Fact]
    public void An_episode_that_never_failed_may_start() =>
        Assert.True(new EpisodeFailures().MayStart(Hash, File, 0.1));

    [Fact]
    public void A_failure_while_the_file_arrived_waits_for_the_file_and_is_tried_once_it_is_whole()
    {
        var failures = new EpisodeFailures();
        failures.Failed(Hash, File, whole: false);

        Assert.False(failures.MayStart(Hash, File, 0.5));
        Assert.True(failures.Waiting(Hash, File, 0.5));

        Assert.True(failures.MayStart(Hash, File, 1));
        Assert.False(failures.Waiting(Hash, File, 1));
    }

    [Fact]
    public void A_failure_on_a_whole_file_is_final()
    {
        var failures = new EpisodeFailures();
        failures.Failed(Hash, File, whole: true);

        Assert.False(failures.MayStart(Hash, File, 1));
        Assert.False(failures.Waiting(Hash, File, 1));
    }

    [Fact]
    public void A_torrent_given_up_on_and_owed_again_has_its_failures_forgotten()
    {
        var failures = new EpisodeFailures();
        failures.Failed(Hash, File, whole: true);
        failures.Failed("other", File, whole: true);
        failures.GaveUp(Hash);

        failures.Owed(Hash);

        Assert.True(failures.MayStart(Hash, File, 1));
        Assert.False(failures.MayStart("other", File, 1));
    }

    [Fact]
    public void A_torrent_owed_without_having_been_given_up_on_keeps_its_failures()
    {
        var failures = new EpisodeFailures();
        failures.Failed(Hash, File, whole: true);

        failures.Owed(Hash);

        Assert.False(failures.MayStart(Hash, File, 1));
    }
}
