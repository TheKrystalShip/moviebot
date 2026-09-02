using System.Text;
using TheKrystalShip.MovieBot.Core.Subtitles;
using Xunit;

namespace TheKrystalShip.MovieBot.Tests;

/// <summary>
/// A downloaded subtitle arrives in whatever encoding its uploader had at the time, sometimes
/// mangled on top of that, and sometimes already in the target format. All three land here, and
/// the timings have to survive every one of them intact.
/// </summary>
public class SubRipTests
{
    private const string OneCue = "1\n00:01:58,991 --> 00:02:00,367\nGood luck.\n";

    private static byte[] Utf8(string text) => Encoding.UTF8.GetBytes(text);

    [Fact]
    public void WritesAWebVttHeader()
    {
        Assert.StartsWith("WEBVTT\n\n", SubRip.Convert(Utf8(OneCue)).WebVtt);
    }

    [Fact]
    public void ConvertsTimestampsAndCountsCues()
    {
        var result = SubRip.Convert(Utf8(OneCue));

        Assert.Contains("00:01:58.991 --> 00:02:00.367", result.WebVtt);
        Assert.Equal(1, result.Cues);
    }

    [Fact]
    public void DropsTheCueNumbering()
    {
        // WebVTT reads a bare number as a cue identifier: harmless, but it means nothing here.
        Assert.DoesNotContain("\n1\n", SubRip.Convert(Utf8(OneCue)).WebVtt);
    }

    [Fact]
    public void KeepsTheTextAndItsMarkup()
    {
        var result = SubRip.Convert(Utf8("1\n00:00:10,000 --> 00:00:12,000\n<i>whispered</i>\n"));

        Assert.Contains("<i>whispered</i>", result.WebVtt);
    }

    [Theory]
    [InlineData(0, "00:01:58.991")]
    [InlineData(2.5, "00:02:01.491")]
    [InlineData(-1.991, "00:01:57.000")]
    public void AppliesAMeasuredShiftToEveryTimestamp(double shift, string expected)
    {
        Assert.Contains(expected, SubRip.Convert(Utf8(OneCue), shift).WebVtt);
    }

    [Fact]
    public void PinsACueShiftedBeforeTheStartRatherThanLosingIt()
    {
        // The line is still spoken. Showing it a moment early beats not showing it at all.
        var result = SubRip.Convert(Utf8("1\n00:00:01,000 --> 00:00:03,000\nline\n"), -10);

        Assert.Contains("00:00:00.000 --> 00:00:00.000", result.WebVtt);
        Assert.Equal(1, result.Cues);
    }

    [Fact]
    public void ReadsAFileThatIsNotUtf8()
    {
        // Windows-1252 bytes, which are not valid UTF-8 and would otherwise arrive as question
        // marks or throw.
        var bytes = new List<byte>();
        bytes.AddRange(Encoding.ASCII.GetBytes("1\n00:00:10,000 --> 00:00:12,000\ncaf"));
        bytes.Add(0xE9);
        bytes.AddRange("\n"u8.ToArray());

        Assert.Contains("café", SubRip.Convert(bytes.ToArray()).WebVtt);
    }

    [Fact]
    public void RepairsTextThatWasEncodedTwice()
    {
        var result = SubRip.Convert(Utf8("1\n00:00:10,000 --> 00:00:12,000\nthatâ€™s mine\n"));

        Assert.Contains("that’s mine", result.WebVtt);
        Assert.True(result.Repaired > 0);
        Assert.False(result.LooksWrong);
    }

    [Fact]
    public void ReportsAFileItCouldNotRepair()
    {
        var result = SubRip.Convert(Utf8("1\n00:00:10,000 --> 00:00:12,000\nthat‚Ä™s mine\n"));

        Assert.True(result.LooksWrong);
    }

    [Fact]
    public void AcceptsAFileThatIsAlreadyWebVtt()
    {
        var result = SubRip.Convert(Utf8("WEBVTT\n\n00:01:58.991 --> 00:02:00.367\nGood luck.\n"));

        Assert.StartsWith("WEBVTT\n\n", result.WebVtt);
        Assert.Contains("00:01:58.991 --> 00:02:00.367", result.WebVtt);
        Assert.Equal(1, result.Cues);
    }

    [Fact]
    public void StripsAByteOrderMark()
    {
        var bytes = new List<byte> { 0xEF, 0xBB, 0xBF };
        bytes.AddRange(Utf8(OneCue));

        Assert.StartsWith("WEBVTT", SubRip.Convert(bytes.ToArray()).WebVtt);
    }

    [Fact]
    public void KeepsCueSettingsWrittenBesideTheTimestamps()
    {
        var result = SubRip.Convert(
            Utf8("WEBVTT\n\n00:00:10.000 --> 00:00:12.000 line:90% align:middle\nline\n"));

        Assert.Contains("line:90% align:middle", result.WebVtt);
    }
}
