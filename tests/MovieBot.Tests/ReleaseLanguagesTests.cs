using TheKrystalShip.MovieBot.Acquire.Search;
using TheKrystalShip.MovieBot.Api.Shows;
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
    // A token in the title is the title's, and one before a subtitle marker names the subtitles.
    [InlineData("Cat.2019.1080p.WEB.H264-GRP", "ca", Advertised.None)]
    [InlineData("Ita.2021.1080p.WEB.H264-GRP", "it", Advertised.None)]
    [InlineData("Parasite.2019.1080p.BluRay.KOR.ENG.SUBS.x264-GRP", "en", Advertised.None)]
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

    [Theory]
    [InlineData("Grimm.S02E01.720p.BluRay.Rus.Eng.HDCLUB", "en", true)]
    [InlineData("The.Mummy.1999.1080p.BluRay.Castellano.x264-GRP", "en", true)]
    [InlineData("The.Mummy.1999.TRUEFRENCH.1080p.WEB.H264-GRP", "en", true)]
    [InlineData("The.Mummy.1999.PLDUB.1080p.WEB.H264-GRP", "en", true)]
    [InlineData("The.Mummy.1999.1080p.WEB.H264-GRP", "en", false)]
    // The original's own language is no dub, nor a language read out of the title or a subtitle marker.
    [InlineData("Amelie.2001.1080p.BluRay.FRENCH.x264-GRP", "fr", false)]
    [InlineData("The.Cat.2019.1080p.WEB.H264-GRP", "en", false)]
    [InlineData("Ita.2021.1080p.WEB.H264-GRP", "en", false)]
    [InlineData("NL.2020.1080p.WEB.H264-GRP", "en", false)]
    [InlineData("Parasite.2019.1080p.BluRay.KOR.ENG.SUBS.x264-GRP", "ko", false)]
    [InlineData("The.Mummy.1999.MULTi.1080p.WEB.H264-GRP", "en", false)]
    // Without the original, nothing is known to be a dub of something else.
    [InlineData("Grimm.S02E01.720p.BluRay.Rus.Eng.HDCLUB", null, false)]
    public void A_release_names_another_dub_by_a_token_for_a_language_other_than_the_original(string name, string? original, bool expected) =>
        Assert.Equal(expected, NamesAnotherDub(ReleaseParser.ParseName(name), original));

    [Fact]
    public void A_clean_film_release_at_the_wanted_resolution_comes_before_one_naming_another_dub()
    {
        var ranked = new[]
        {
            ReleaseParser.ParseName("The.Mummy.1999.1080p.BluRay.Rus.Eng.x264-DUB"),
            ReleaseParser.ParseName("The.Mummy.1999.1080p.WEB.H264-CLEAN"),
            ReleaseParser.ParseName("The.Mummy.1999.720p.BluRay.x264-LOWER"),
        };

        Assert.Equal("CLEAN", Choose(ranked, Resolution.Hd1080, null, "en")!.Group);
        Assert.Equal("CLEAN", Choose(ranked, Resolution.Hd1080, "en", "en")!.Group);
        // A dub at the wanted resolution still beats a clean release at another.
        Assert.Equal("DUB", Choose([ranked[0], ranked[2]], Resolution.Hd1080, null, "en")!.Group);
        // The only release is taken, whatever it names.
        Assert.Equal("DUB", Choose(ranked[..1], Resolution.Hd720, null, "en")!.Group);
        // A dub asked for is still preferred.
        Assert.Equal("DUB", Choose(ranked, Resolution.Hd1080, "ru", "en")!.Group);
        // An unknown original leaves the ranker's order.
        Assert.Equal("DUB", Choose(ranked, Resolution.Hd1080, null, null)!.Group);
    }

    [Fact]
    public void An_episode_comes_from_a_clean_release_before_a_season_pack_naming_another_dub()
    {
        var pack = ReleaseParser.ParseName("Grimm.S02.720p.BluRay.Rus.Eng.HDCLUB");
        var dubbed = ReleaseParser.ParseName("Grimm.S02E01.720p.BluRay.Rus.Eng.HDCLUB");
        var web = ReleaseParser.ParseName("Grimm.S02E01.720p.AMZN.WEB-DL.DD+5.1.H.264-playWEB");
        var offers = new ShowReleases(
            [new SeasonOffer(2, [pack], [new EpisodeOffer(1, [dubbed, web]), new EpisodeOffer(2, [])])], []);

        Assert.Equal(web, ShowOfferCache.Pick(offers, new EpisodeNumber(2, 1), Resolution.Hd720, original: "en"));
        Assert.Equal(web, ShowOfferCache.Pick(offers, new EpisodeNumber(2, 1), Resolution.Hd720, "en", "en"));
        // With nothing else at the quality, the pack is still taken.
        Assert.Equal(pack, ShowOfferCache.Pick(offers, new EpisodeNumber(2, 2), Resolution.Hd720, original: "en"));
        // The Russian asked for is still preferred, out of the pack.
        Assert.Equal(pack, ShowOfferCache.Pick(offers, new EpisodeNumber(2, 1), Resolution.Hd720, "ru", "en"));
    }

    [Fact]
    public void A_season_pack_comes_before_an_episode_s_own_release_when_neither_names_another_dub()
    {
        var pack = ReleaseParser.ParseName("Grimm.S02.720p.WEB-DL.DD5.1.H.264-GRP");
        var web = ReleaseParser.ParseName("Grimm.S02E01.720p.AMZN.WEB-DL.DD+5.1.H.264-playWEB");
        var offers = new ShowReleases([new SeasonOffer(2, [pack], [new EpisodeOffer(1, [web])])], []);

        Assert.Equal(pack, ShowOfferCache.Pick(offers, new EpisodeNumber(2, 1), Resolution.Hd720, original: "en"));
    }
}
