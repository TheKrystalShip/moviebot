using TheKrystalShip.MovieBot.Core;
using TheKrystalShip.MovieBot.Ingest.Markers;
using Xunit;

namespace TheKrystalShip.MovieBot.Tests;

/// <summary>
/// A chapter names a stretch exactly, so it is believed whole or not at all: "Opening Heist" is an
/// act of the episode, and a button that skipped it would skip the story.
/// </summary>
public class ChapterMarkerTests
{
    [Theory]
    [InlineData("Intro")]
    [InlineData("INTRO")]
    [InlineData("Introduction")]
    [InlineData("Opening")]
    [InlineData("Opening Credits")]
    [InlineData("Opening Titles")]
    [InlineData("Opening Theme")]
    [InlineData("Main Title")]
    [InlineData("Main Titles")]
    [InlineData("Title Sequence")]
    [InlineData("OP")]
    [InlineData("OP1")]
    [InlineData("Intro 2")]
    [InlineData("  intro. ")]
    [InlineData("(Intro)")]
    public void Names_of_a_title_sequence_are_an_intro(string title) =>
        Assert.Equal(MarkerKind.Intro, ChapterMarkers.Classify(title));

    [Theory]
    [InlineData("Recap")]
    [InlineData("Previously")]
    [InlineData("Previously On")]
    [InlineData("Previously on Slow Horses")]
    [InlineData("PREVIOUSLY ON...")]
    [InlineData("The Story So Far")]
    [InlineData("Last Time On Severance")]
    public void Names_of_a_recap_are_a_recap(string title) =>
        Assert.Equal(MarkerKind.Recap, ChapterMarkers.Classify(title));

    [Theory]
    [InlineData("Chapter 3")]
    [InlineData("Chapter 1")]
    [InlineData("3")]
    [InlineData("Opening Heist")]
    [InlineData("Opening Scene")]
    [InlineData("Operation")]
    [InlineData("Hope")]
    [InlineData("The Introduction of Mr Smith")]
    [InlineData("Previously Unseen Footage Of The Chase")]
    [InlineData("End Credits")]
    [InlineData("Closing Credits")]
    [InlineData("00:03:27.707")]
    [InlineData("")]
    [InlineData(null)]
    public void Everything_else_is_neither(string? title) =>
        Assert.Null(ChapterMarkers.Classify(title));

    [Fact]
    public void A_marker_runs_from_its_chapter_to_the_next()
    {
        var markers = ChapterMarkers.From(
            [
                new Chapter { StartSeconds = 0, Title = "Chapter 1" },
                new Chapter { StartSeconds = 488.999, Title = "Intro" },
                new Chapter { StartSeconds = 523.458, Title = "Chapter 2" }
            ],
            2875.957);

        var intro = Assert.Single(markers);
        Assert.Equal(MarkerKind.Intro, intro.Kind);
        Assert.Equal(488.999, intro.StartSeconds);
        Assert.Equal(523.458, intro.EndSeconds);
        Assert.Equal(MarkerSource.Chapter, intro.From);
    }

    [Fact]
    public void The_last_chapter_runs_to_the_end_of_the_episode()
    {
        var markers = ChapterMarkers.From(
            [new Chapter { StartSeconds = 0, Title = "Recap" }, new Chapter { StartSeconds = 60, Title = "Chapter 1" }],
            2400);

        Assert.Equal(60, Assert.Single(markers).EndSeconds);

        var last = ChapterMarkers.From([new Chapter { StartSeconds = 2380, Title = "Intro" }], 2400);
        Assert.Equal(2400, Assert.Single(last).EndSeconds);
    }

    [Fact]
    public void A_recap_and_a_title_sequence_in_one_episode_are_both_found()
    {
        var markers = ChapterMarkers.From(
            [
                new Chapter { StartSeconds = 0, Title = "Previously On" },
                new Chapter { StartSeconds = 62, Title = "Intro" },
                new Chapter { StartSeconds = 97, Title = "Chapter 1" }
            ],
            2500);

        Assert.Equal([MarkerKind.Recap, MarkerKind.Intro], markers.Select(m => m.Kind));
    }

    [Fact]
    public void An_episode_has_one_marker_of_each_kind()
    {
        var markers = ChapterMarkers.From(
            [
                new Chapter { StartSeconds = 100, Title = "Intro" },
                new Chapter { StartSeconds = 120, Title = "Opening Credits" },
                new Chapter { StartSeconds = 140, Title = "Chapter 2" }
            ],
            2500);

        Assert.Equal(100, Assert.Single(markers).StartSeconds);
    }

    [Fact]
    public void A_chapter_too_long_to_be_one_is_an_act_not_a_sequence()
    {
        // "Opening" over ten minutes is the first act of the episode.
        var markers = ChapterMarkers.From(
            [new Chapter { StartSeconds = 0, Title = "Opening" }, new Chapter { StartSeconds = 600, Title = "Chapter 2" }],
            2500);

        Assert.Empty(markers);
    }

    [Fact]
    public void A_chapter_too_short_to_be_worth_a_button_is_left_out()
    {
        var markers = ChapterMarkers.From(
            [new Chapter { StartSeconds = 100, Title = "Intro" }, new Chapter { StartSeconds = 103, Title = "Chapter 2" }],
            2500);

        Assert.Empty(markers);
    }

    [Fact]
    public void A_container_without_chapters_gives_none()
    {
        Assert.Empty(ChapterMarkers.From([], 2500));
    }
}
