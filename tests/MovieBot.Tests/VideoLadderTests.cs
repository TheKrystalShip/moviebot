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
    private static IngestOptions Options(string stepDown = "4M") => new()
    {
        SourcePath = "/dev/null",
        OutputRoot = "/tmp",
        StepDownBitrate = stepDown
    };

    [Fact]
    public void A_wide_source_gains_a_step_down_under_the_top_rung()
    {
        var ladder = VideoLadder.For(1920, 1080, Options());

        Assert.Equal(2, ladder.Count);
        Assert.Equal("v0", ladder[0].Directory);
        Assert.Equal(9000, ladder[0].BitrateKbps);
        Assert.Null(ladder[0].ScaleFilter);

        Assert.Equal("v1", ladder[1].Directory);
        Assert.Equal(1280, ladder[1].Width);
        Assert.Equal(720, ladder[1].Height);
        Assert.Equal(4000, ladder[1].BitrateKbps);
    }

    /// <summary>
    /// Scope films are the common case here and the one that rounds. 804 scaled by 1280/1920 is
    /// 536 exactly; a ratio landing between two even numbers comes back at the nearer one, which
    /// is what ffmpeg's <c>-2</c> picks and what H.264 can encode.
    /// </summary>
    [Theory]
    [InlineData(1920, 804, 536)]
    [InlineData(1920, 1080, 720)]
    [InlineData(1920, 802, 534)]
    [InlineData(1920, 817, 544)]
    public void The_step_down_height_is_even_and_keeps_the_aspect_ratio(
        int sourceWidth, int sourceHeight, int expected)
    {
        var ladder = VideoLadder.For(sourceWidth, sourceHeight, Options());

        Assert.Equal(expected, ladder[1].Height);
        Assert.Equal(0, ladder[1].Height % 2);
    }

    [Theory]
    [InlineData(1280, 720)]
    [InlineData(854, 480)]
    public void A_source_no_wider_than_the_step_down_keeps_one_rung(int width, int height)
    {
        var ladder = VideoLadder.For(width, height, Options());

        Assert.Single(ladder);
        Assert.Equal("v0", ladder[0].Directory);
    }

    [Fact]
    public void A_step_down_of_zero_writes_one_rung()
    {
        Assert.Single(VideoLadder.For(1920, 1080, Options(stepDown: "0")));
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
