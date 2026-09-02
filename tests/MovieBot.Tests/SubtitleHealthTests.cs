using TheKrystalShip.MovieBot.Core.Subtitles;
using Xunit;

namespace TheKrystalShip.MovieBot.Tests;

/// <summary>
/// The check is worth having only if it stays quiet on ordinary English. A warning that fires on
/// every film is one nobody reads, and then the one film it should have caught goes past unread
/// too, so most of these are the cases it has to let through.
/// </summary>
public class SubtitleHealthTests
{
    [Theory]
    [InlineData("Plain ASCII dialogue.")]
    [InlineData("that’s mine")]
    [InlineData("wait — listen")]
    [InlineData("“quoted” word")]
    [InlineData("at the café")]
    [InlineData("a naïve Noël in Zürich")]
    [InlineData("♪♪")]
    [InlineData("and then…")]
    public void StaysQuietOnOrdinaryEnglish(string text)
    {
        Assert.True(SubtitleHealth.Inspect(text).Clean);
    }

    [Theory]
    [InlineData("thatâ€™s mine")]
    [InlineData("CafÃ© au lait")]
    [InlineData("wait â€” listen")]
    public void FlagsTextThatIsStillMangled(string text)
    {
        var report = SubtitleHealth.Inspect(text);

        Assert.False(report.Clean);
        Assert.NotEmpty(report.Samples);
    }

    [Fact]
    public void RepairsManglingThroughLatin1AsWellAsWindows1252()
    {
        // Latin-1 differs from Windows-1252 only in the block the corruption passes through, and
        // the reversal carries those characters straight back to their bytes, so both are covered.
        var mangled = System.Text.Encoding.Latin1.GetString("that’s mine"u8);

        Assert.Equal("that’s mine", MojibakeRepair.Repair(mangled).Text);
    }

    [Fact]
    public void FlagsManglingThroughACodepageTheRepairDoesNotKnow()
    {
        // Mac Roman puts different characters in that block, so the reversal produces bytes that
        // are not valid UTF-8 and the run is left alone. Being caught anyway is the whole point of
        // checking adjacency rather than a list of sequences already met.
        const string throughMacRoman = "that‚Ä™s mine";

        Assert.Equal(throughMacRoman, MojibakeRepair.Repair(throughMacRoman).Text);
        Assert.False(SubtitleHealth.Inspect(throughMacRoman).Clean);
    }

    [Fact]
    public void CountsEveryOccurrenceButKeepsOnlyAFewSamples()
    {
        var report = SubtitleHealth.Inspect(string.Concat(Enumerable.Repeat("thatâ€™s mine. ", 20)));

        Assert.Equal(20, report.Count);
        Assert.Single(report.Samples);
    }

    [Fact]
    public void CompletesADiscardedByteWithoutProofWhenTheTrackIsPrioritised()
    {
        const string orphaned = "nothing here but â€ on its own";

        Assert.Equal(orphaned, MojibakeRepair.Repair(orphaned).Text);
        Assert.Equal(
            "nothing here but ” on its own",
            MojibakeRepair.Repair(orphaned, completeDiscardedBytes: true).Text);
    }
}
