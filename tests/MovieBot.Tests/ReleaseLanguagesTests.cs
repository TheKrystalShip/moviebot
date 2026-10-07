using TheKrystalShip.MovieBot.Acquire.Search;
using Xunit;
using static TheKrystalShip.MovieBot.Api.Acquisition.ReleaseLanguages;

namespace TheKrystalShip.MovieBot.Tests;

/// <summary>Which audio a release name says it carries, and which release a language prefers.</summary>
public sealed class ReleaseLanguagesTests
{
    [Theory]
    [InlineData("The.Mummy.1999.1080p.BluRay.Castellano.x264-GRP", "es", Advertised.Named)]
    [InlineData("The.Mummy.1999.1080p.BluRay.SPANISH.ENGLISH.x264-GRP", "spa", Advertised.Named)]
    [InlineData("The.Mummy.1999.MULTi.1080p.WEB.H264-GRP", "es", Advertised.Several)]
    [InlineData("The.Mummy.1999.DUAL.1080p.WEB.H264-GRP", "fr", Advertised.Several)]
    [InlineData("The.Mummy.1999.TRUEFRENCH.1080p.WEB.H264-GRP", "fr", Advertised.Named)]
    [InlineData("The.Mummy.1999.1080p.WEB.H264-GRP", "es", Advertised.None)]
    // A group or a title that happens to hold a token's letters inside a word says nothing.
    [InlineData("Catacombs.2007.1080p.WEB.H264-ITALIANO", "es", Advertised.None)]
    public void A_release_name_advertises_a_language_by_a_token_or_by_carrying_several(string name, string language, Advertised expected) =>
        Assert.Equal(expected, Of(name, language));

    [Fact]
    public void A_release_naming_the_language_comes_before_one_carrying_several_and_before_the_ranker_s_best()
    {
        var ranked = new[]
        {
            ReleaseParser.ParseName("The.Mummy.1999.1080p.BluRay.x264-BEST"),
            ReleaseParser.ParseName("The.Mummy.1999.MULTi.1080p.WEB.H264-MULTI"),
            ReleaseParser.ParseName("The.Mummy.1999.1080p.WEB.CASTELLANO.H264-DUB"),
        };

        Assert.Equal("DUB", Preferring(ranked, "es")!.Group);
        Assert.Equal("MULTI", Preferring(ranked[..2], "es")!.Group);
        Assert.Null(Preferring(ranked[..1], "es"));
    }

    [Theory]
    [InlineData("es", "en", "es")]
    [InlineData("en", "en", null)]
    [InlineData("eng", "en", null)]
    [InlineData(null, "en", null)]
    [InlineData("es", null, "es")]
    public void A_language_is_worth_preferring_only_when_it_is_not_the_title_s_own(string? asked, string? original, string? expected) =>
        Assert.Equal(expected, WorthPreferring(asked, original));
}
