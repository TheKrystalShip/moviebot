using TheKrystalShip.MovieBot.Core;
using TheKrystalShip.MovieBot.Ingest.Pipeline;
using Xunit;

namespace TheKrystalShip.MovieBot.Tests;

/// <summary>
/// The ladder is what a player falls back to, so what it advertises has to be what is encoded.
/// A rung whose stated size disagrees with its segments describes the picture wrongly to every
/// client that sizes a buffer from it, and nothing downstream measures the frames to check.
/// </summary>
public sealed class VideoLadderTests
{
    private static IngestOptions Options() => new()
    {
        SourcePath = "/dev/null",
        OutputRoot = "/tmp"
    };

    [Fact]
    public void A_wide_source_gains_both_step_downs_under_the_top_rung()
    {
        var ladder = VideoLadder.For(1920, 1080, Options());

        Assert.Equal(3, ladder.Count);
        Assert.Equal("v0", ladder[0].Directory);
        Assert.Equal(9000, ladder[0].BitrateKbps);
        Assert.Null(ladder[0].ScaleFilter);

        Assert.Equal("v1", ladder[1].Directory);
        Assert.Equal(1280, ladder[1].Width);
        Assert.Equal(720, ladder[1].Height);
        Assert.Equal(4000, ladder[1].BitrateKbps);
        Assert.Equal("scale=1280:-2", ladder[1].ScaleFilter);

        Assert.Equal("v2", ladder[2].Directory);
        Assert.Equal(854, ladder[2].Width);
        Assert.Equal(480, ladder[2].Height);
        Assert.Equal("480p", ladder[2].Name);
        Assert.Equal(Ladder.StepDowns[1].BitrateKbps, ladder[2].BitrateKbps);
        Assert.Equal("scale=854:-2", ladder[2].ScaleFilter);
    }

    /// <summary>
    /// The small rung joins anything wider than it, so a 720p episode gains it without the 1280
    /// step-down it is no wider than.
    /// </summary>
    [Theory]
    [InlineData(1280, 720, 480)]
    [InlineData(1280, 532, 354)]
    [InlineData(1024, 576, 480)]
    public void A_source_between_the_step_downs_gains_the_small_rung_alone(int width, int height, int small)
    {
        var ladder = VideoLadder.For(width, height, Options());

        Assert.Equal(["v0", "v2"], ladder.Select(r => r.Directory));
        Assert.Equal(854, ladder[1].Width);
        Assert.Equal(small, ladder[1].Height);
    }

    /// <summary>
    /// Scope films are the common case here and the one that rounds. 804 scaled by 1280/1920 is
    /// 536 exactly; a ratio landing between two even numbers comes back at the nearer one, which
    /// is what ffmpeg's <c>-2</c> picks and what H.264 can encode.
    /// </summary>
    [Theory]
    [InlineData(1920, 804, 536, 358)]
    [InlineData(1920, 1080, 720, 480)]
    [InlineData(1920, 802, 534, 356)]
    [InlineData(1920, 817, 544, 364)]
    [InlineData(1916, 796, 532, 354)]
    public void The_step_down_heights_are_even_and_keep_the_aspect_ratio(
        int sourceWidth, int sourceHeight, int expected, int small)
    {
        var ladder = VideoLadder.For(sourceWidth, sourceHeight, Options());

        Assert.Equal(expected, ladder[1].Height);
        Assert.Equal(small, ladder[2].Height);
        Assert.All(ladder.Skip(1), r => Assert.Equal(0, r.Height % 2));
    }

    [Theory]
    [InlineData(854, 480)]
    [InlineData(720, 300)]
    public void A_source_no_wider_than_the_small_rung_keeps_one_rung(int width, int height)
    {
        var ladder = VideoLadder.For(width, height, Options());

        Assert.Single(ladder);
        Assert.Equal("v0", ladder[0].Directory);
    }

    [Fact]
    public void No_step_downs_writes_one_rung()
    {
        Assert.Single(VideoLadder.For(1920, 1080, Options() with { StepDowns = [] }));
    }

    /// <summary>
    /// The ceiling and the averaging window follow the rung's own target, so each rung is held to
    /// the bitrate it is sized for and the step-down stays under the one it exists to fit within.
    /// </summary>
    [Fact]
    public void Each_rung_carries_its_own_ceiling_and_window()
    {
        var ladder = VideoLadder.For(1920, 1080, Options());

        Assert.Equal("9000k", ladder[0].Bitrate);
        Assert.Equal("12000k", ladder[0].Maxrate);
        Assert.Equal("24000k", ladder[0].Bufsize);

        Assert.Equal("4000k", ladder[1].Bitrate);
        Assert.Equal("5333k", ladder[1].Maxrate);
        Assert.Equal("10666k", ladder[1].Bufsize);
    }

    [Theory]
    [InlineData("9M", 9000)]
    [InlineData("4500k", 4500)]
    [InlineData("1.5M", 1500)]
    [InlineData("800", 800)]
    [InlineData("nonsense", 0)]
    public void Bitrates_are_read_in_every_spelling_a_caller_writes(string value, int kbps)
    {
        Assert.Equal(kbps, VideoLadder.ParseBitrate(value));
    }
}
