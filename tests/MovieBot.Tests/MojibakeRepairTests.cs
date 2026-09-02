using TheKrystalShip.MovieBot.Core.Subtitles;
using Xunit;

namespace TheKrystalShip.MovieBot.Tests;

/// <summary>
/// The repair has to be aggressive enough to fix a track that is wrong on nearly every line and
/// timid enough to leave every other language on the disc alone. The second half is what most of
/// these cover: a Portuguese or Romanian track is full of the same characters the corruption
/// produces, and a repair that could not tell them apart would break far more than it fixed.
/// </summary>
public class MojibakeRepairTests
{
    [Theory]
    [InlineData("thatâ€™s mine", "that’s mine")]
    [InlineData("wait â€” listen", "wait — listen")]
    [InlineData("â€œquotedâ€ word", "“quoted” word")]
    [InlineData("CafÃ© au lait", "Café au lait")]
    [InlineData("â™ªâ™ª", "♪♪")]
    public void UndoesTextEncodedTwice(string mangled, string expected)
    {
        var result = MojibakeRepair.Repair(mangled);

        Assert.Equal(expected, result.Text);
        Assert.True(result.Changed);
    }

    [Theory]
    [InlineData("NÃO É POSSÍVEL")]
    [InlineData("ÎMBRACĂ HAINE")]
    [InlineData("Ấy là chuyện")]
    [InlineData("Le café était fermé")]
    [InlineData("Plain ASCII dialogue.")]
    public void LeavesTextThatWasNeverMangledAlone(string text)
    {
        var result = MojibakeRepair.Repair(text);

        Assert.Equal(text, result.Text);
        Assert.False(result.Changed);
    }

    [Fact]
    public void RepairingTwiceChangesNothingTheSecondTime()
    {
        var once = MojibakeRepair.Repair("thatâ€™s mine");
        var twice = MojibakeRepair.Repair(once.Text);

        Assert.Equal(once.Text, twice.Text);
        Assert.False(twice.Changed);
    }

    [Fact]
    public void RestoresAClosingQuoteWhoseLastByteWasDiscarded()
    {
        // An encoder that drops the bytes Windows-1252 has no character for loses the third byte of
        // a closing double quote entirely. Guessing is only safe because the opening quote came
        // through intact, which proves what produced it.
        var result = MojibakeRepair.Repair("â€œquotedâ€ word");

        Assert.Equal("“quoted” word", result.Text);
    }

    [Fact]
    public void LeavesADiscardedByteAloneWithNothingToProveWhatItWas()
    {
        var result = MojibakeRepair.Repair("nothing here but â€ on its own");

        Assert.Equal("nothing here but â€ on its own", result.Text);
        Assert.Equal(1, result.Unrepairable);
    }

    [Fact]
    public void CountsRunsItCouldNotReadBack()
    {
        var result = MojibakeRepair.Repair("thatâ€™s the café");

        Assert.Equal("that’s the café", result.Text);
        Assert.Equal(1, result.Repaired);
        Assert.Equal(1, result.Unrepairable);
    }

    [Fact]
    public void LeavesCueTimingsUntouched()
    {
        var result = MojibakeRepair.Repair("00:41:51.120 --> 00:41:53.480\nthatâ€™s mine\n");

        Assert.Equal("00:41:51.120 --> 00:41:53.480\nthat’s mine\n", result.Text);
    }
}
