using TheKrystalShip.MovieBot.Ingest.Probe;
using Xunit;

namespace TheKrystalShip.MovieBot.Tests;

/// <summary>
/// Getting this wrong fails silently in the worst direction: a filter that matches nothing leaves
/// a film with no subtitles at all and no error anywhere. Most of these are the spellings that
/// would cause exactly that.
/// </summary>
public class LanguageFilterTests
{
    private static readonly string[] English = ["eng"];

    [Theory]
    [InlineData("eng")]
    [InlineData("en")]
    [InlineData("EN")]
    [InlineData("en-GB")]
    [InlineData("eng-US")]
    public void KeepsEnglishHoweverItIsSpelled(string tag)
    {
        Assert.True(LanguageFilter.Wanted(tag, English));
    }

    [Theory]
    [InlineData("rum")]
    [InlineData("fre")]
    [InlineData("ger")]
    [InlineData("chi")]
    [InlineData("dut")]
    public void DropsEverythingElse(string tag)
    {
        Assert.False(LanguageFilter.Wanted(tag, English));
    }

    [Theory]
    // The bibliographic and terminological codes differ for these, and which one a container uses
    // depends on who muxed it. Matching one spelling and not the other silently keeps nothing.
    [InlineData("rum", "ron")]
    [InlineData("fre", "fra")]
    [InlineData("ger", "deu")]
    [InlineData("cze", "ces")]
    [InlineData("gre", "ell")]
    [InlineData("ice", "isl")]
    [InlineData("per", "fas")]
    [InlineData("chi", "zho")]
    public void TreatsBothCodesForOneLanguageAsTheSame(string bibliographic, string terminological)
    {
        Assert.True(LanguageFilter.Wanted(bibliographic, [terminological]));
        Assert.True(LanguageFilter.Wanted(terminological, [bibliographic]));
        Assert.Equal(LanguageFilter.Normalise(bibliographic), LanguageFilter.Normalise(terminological));
    }

    [Fact]
    public void MatchesATagCarryingARegion()
    {
        Assert.True(LanguageFilter.Wanted("pt-BR", ["por"]));
        Assert.True(LanguageFilter.Wanted("es_MX", ["spa"]));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void AlwaysKeepsATrackWithNoLanguageAtAll(string? tag)
    {
        // Releases ship one untagged subtitle often enough, and it is usually the one worth having.
        // Dropping it on a filter leaves a film with none and nothing to explain why.
        Assert.True(LanguageFilter.Wanted(tag, English));
    }

    [Fact]
    public void KeepsEverythingWhenNothingIsAskedFor()
    {
        foreach (var tag in (string[])["eng", "rum", "vie", "zzz"])
            Assert.True(LanguageFilter.Wanted(tag, []));
    }

    [Fact]
    public void LeavesACodeItDoesNotKnowAlone()
    {
        Assert.Equal("zzz", LanguageFilter.Normalise("ZZZ"));
        Assert.False(LanguageFilter.Wanted("zzz", English));
        Assert.True(LanguageFilter.Wanted("zzz", ["zzz"]));
    }

    [Fact]
    public void KeepsThreeOfOneDiscsThirtyLanguages()
    {
        // The languages one real disc carries, which is the case this exists for.
        string[] disc =
        [
            "eng", "eng", "eng", "ara", "bul", "chi", "chi", "chi", "cze", "dan", "dut", "fin",
            "fre", "ger", "gre", "heb", "hun", "ind", "ita", "jpn", "kor", "nor", "per", "pol",
            "por", "rum", "spa", "swe", "tha", "tur", "vie", "hrv", "ice", "rus"
        ];

        Assert.Equal(3, disc.Count(tag => LanguageFilter.Wanted(tag, English)));
    }
}
