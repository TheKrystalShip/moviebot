using TheKrystalShip.MovieBot.Core;
using TheKrystalShip.MovieBot.Ingest.Markers;
using Xunit;

namespace TheKrystalShip.MovieBot.Tests;

/// <summary>
/// A title sequence has music and no speech, so a spoken line inside one means the stretch would
/// skip story: it is cut short of the line, or dropped when too little is left.
/// </summary>
public class IntroGuardTests
{
    private static Marker Intro(double start = 100, double end = 140, MarkerSource from = MarkerSource.Audio) =>
        new() { Kind = MarkerKind.Intro, StartSeconds = start, EndSeconds = end, From = from };

    private static Cue Say(double start, string text = "Where were you?") => new(start, start + 2, text);

    [Fact]
    public void A_sequence_with_nobody_speaking_in_it_is_unchanged()
    {
        var intro = Intro();

        Assert.Equal(intro, IntroGuard.Guard(intro, [Say(90), Say(141)]));
    }

    [Fact]
    public void An_episode_with_no_subtitles_is_unchanged()
    {
        var intro = Intro();

        Assert.Equal(intro, IntroGuard.Guard(intro, []));
    }

    [Fact]
    public void A_line_inside_cuts_the_sequence_short_of_it()
    {
        var guarded = IntroGuard.Guard(Intro(100, 140), [Say(125)]);

        Assert.NotNull(guarded);
        Assert.Equal(100, guarded.StartSeconds);
        Assert.Equal(125 - IntroGuard.MarginSeconds, guarded.EndSeconds, 3);
    }

    [Fact]
    public void The_first_line_inside_is_the_one_that_counts()
    {
        var guarded = IntroGuard.Guard(Intro(100, 160), [Say(140), Say(120)]);

        Assert.Equal(120 - IntroGuard.MarginSeconds, guarded!.EndSeconds, 3);
    }

    [Fact]
    public void What_is_left_may_be_exactly_the_shortest_sequence()
    {
        var guarded = IntroGuard.Guard(Intro(100, 140), [Say(100 + IntroFinder.MinSeconds + IntroGuard.MarginSeconds)]);

        Assert.NotNull(guarded);
        Assert.Equal(100 + IntroFinder.MinSeconds, guarded.EndSeconds, 3);
    }

    [Fact]
    public void A_line_leaving_too_little_drops_the_marker()
    {
        Assert.Null(IntroGuard.Guard(Intro(100, 140), [Say(110)]));
    }

    [Fact]
    public void A_line_at_the_very_start_drops_the_marker()
    {
        Assert.Null(IntroGuard.Guard(Intro(100, 140), [Say(100)]));
    }

    [Fact]
    public void A_line_that_began_before_the_sequence_is_not_inside_it()
    {
        var intro = Intro();

        Assert.Equal(intro, IntroGuard.Guard(intro, [new Cue(97, 103, "And that was the last of it.")]));
    }

    [Fact]
    public void A_line_where_the_sequence_ends_is_not_inside_it()
    {
        var intro = Intro();

        Assert.Equal(intro, IntroGuard.Guard(intro, [Say(140)]));
    }

    [Theory]
    [InlineData("♪ Strange game ♪")]
    [InlineData("♪♪")]
    [InlineData("# Simply irresistible #")]
    [InlineData("<i>♪ la la la ♪</i>")]
    [InlineData("♫ music ♫\n♫ more ♫")]
    [InlineData("[theme music playing]")]
    [InlineData("(theme music playing)")]
    [InlineData("[\"Strange Game\" playing]")]
    [InlineData("[panting]\n[groans]")]
    [InlineData("- [door slams]\n- (gasps)")]
    [InlineData("<i>[engine revving]</i>")]
    [InlineData("   ")]
    public void Music_and_descriptions_of_sound_are_not_speech(string text)
    {
        Assert.False(IntroGuard.IsDialogue(text));

        var intro = Intro();
        Assert.Equal(intro, IntroGuard.Guard(intro, [Say(120, text)]));
    }

    [Theory]
    [InlineData("Where were you?")]
    [InlineData("<i>It doesn't matter.</i>")]
    [InlineData("[in Arabic] I didn't get him.")]
    [InlineData("- [door slams]\n- Who's there?")]
    [InlineData("[Flyte] The gridlock is focused mainly")]
    [InlineData("♪ la la ♪\nStop it.")]
    public void A_line_somebody_says_is_speech(string text) =>
        Assert.True(IntroGuard.IsDialogue(text));

    [Fact]
    public void Reads_the_cues_of_a_webvtt_document()
    {
        var cues = IntroGuard.ParseWebVtt(
            "WEBVTT\r\n\r\nNOTE a comment\r\n\r\n02:35.489 --> 02:38.075 line:90%\r\nI should warn you,\r\nI'm armed!\r\n\r\n"
            + "1:02:03.500 --> 1:02:05.000\r\n[theme music playing]\r\n");

        Assert.Equal(2, cues.Count);
        Assert.Equal(155.489, cues[0].StartSeconds, 3);
        Assert.Equal(158.075, cues[0].EndSeconds, 3);
        Assert.Equal("I should warn you,\nI'm armed!", cues[0].Text);
        Assert.Equal(3723.5, cues[1].StartSeconds, 3);
        Assert.False(IntroGuard.IsDialogue(cues[1].Text));
    }

    [Fact]
    public void A_document_with_no_cues_gives_none()
    {
        Assert.Empty(IntroGuard.ParseWebVtt("WEBVTT\n\n"));
        Assert.Empty(IntroGuard.ParseWebVtt(""));
    }
}
