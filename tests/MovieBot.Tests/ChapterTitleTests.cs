using TheKrystalShip.MovieBot.Ingest.Probe;
using Xunit;

namespace TheKrystalShip.MovieBot.Tests;

/// <summary>
/// Muxers write the chapter's own start time into its title often enough that one real disc here
/// does it for all thirty-six. Shown beside the time under the pointer, that reads as a second
/// clock disagreeing with the first, so it has to count as no name at all.
/// </summary>
public class ChapterTitleTests
{
    [Theory]
    [InlineData("00:03:27.707")]
    [InlineData("0:03:27")]
    [InlineData("00:03:27,707")]
    [InlineData("03:27")]
    public void ReadsAClockAsNoNameAtAll(string title)
    {
        Assert.True(ProbeChapter.IsTimestamp(title));
        Assert.Null(Chapter(title).Title);
    }

    [Theory]
    [InlineData("Hell Unleashed")]
    [InlineData("Far From Home (Main Title)")]
    [InlineData("Chapter 12")]
    [InlineData("12")]
    [InlineData("Act II: 1999")]
    public void KeepsAnythingThatActuallyNamesSomething(string title)
    {
        Assert.False(ProbeChapter.IsTimestamp(title));
        Assert.Equal(title, Chapter(title).Title);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void ReadsAnEmptyTitleAsNone(string title)
    {
        Assert.Null(Chapter(title).Title);
    }

    [Fact]
    public void ReadsAChapterWithNoTagsAtAll()
    {
        Assert.Null(new ProbeChapter().Title);
    }

    [Fact]
    public void ReadsTheStartTimeAsSeconds()
    {
        var chapter = new ProbeChapter { StartTime = "433.308000", EndTime = "612.500000" };

        Assert.Equal(433.308, chapter.StartSeconds, 3);
        Assert.Equal(612.5, chapter.EndSeconds, 3);
    }

    private static ProbeChapter Chapter(string title) =>
        new() { Tags = new Dictionary<string, string> { ["title"] = title } };
}
