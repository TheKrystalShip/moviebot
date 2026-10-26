using TheKrystalShip.MovieBot.Acquire.Tmdb;
using TheKrystalShip.MovieBot.Api.Shows;
using Xunit;

namespace TheKrystalShip.MovieBot.Tests;

/// <summary>
/// The film database's description put into the releases' numbering. The case is Superstore's second
/// season, which the database opens with a promotion, Olympics, that the releases count as a special.
/// </summary>
public sealed class EpisodeNumberingTests
{
    private const string Show = "tt4477976";

    private static readonly string[] Database =
        ["Olympics", "Strike", "Back to Work", "Guns, Pills and Birds", "Spokesman Scandal", "Dog Adoption Day",
         "Halloween Theft", "Election Day", "Seasonal Help", "Black Friday"];

    private static StoredShow Described() => new()
    {
        FetchedAt = DateTimeOffset.UtcNow,
        Details = new ShowDetails
        {
            ImdbId = Show, TmdbId = 62649, Title = "Superstore",
            NextEpisodeToAir = new AiringEpisode { Season = 2, Number = 10, Name = "Black Friday" },
        },
        Seasons =
        [
            new SeasonDetails { Number = 1, Episodes = [new() { Season = 1, Number = 1, Name = "Pilot" }, new() { Season = 1, Number = 2, Name = "Magazine Profile" }] },
            new SeasonDetails { Number = 2, Episodes = [.. Database.Select((name, i) => new EpisodeDetails { Season = 2, Number = i + 1, Name = name })] },
        ],
    };

    [Theory]
    [InlineData("Superstore.S02E07.Election.Day.720p.DSNP.WEB-DL.AAC2.0.H.264-playWEB", "Election Day")]
    [InlineData("Superstore.S02E04.Spokesman.Scandal.720p.DSNP.WEB-DL.AAC2.0.H.264-playWEB.mkv", "Spokesman Scandal")]
    [InlineData("Show.S01E02.1080p.WEB.h264-GRP", null)]
    [InlineData("Show.S01.1080p.WEB.h264-GRP", null)]
    public void An_episode_name_is_read_between_the_marker_and_the_encode(string release, string? name) =>
        Assert.Equal(name, EpisodeNumbering.EpisodeName(release));

    [Fact]
    public void A_season_the_database_opens_with_a_promotion_is_numbered_as_the_releases_number_it()
    {
        var numbering = new EpisodeNumbering();
        numbering.Learn(Show, [
            "Superstore.S02E01.Strike.720p.DSNP.WEB-DL.AAC2.0.H.264-playWEB",
            "Superstore.S02E07.Election.Day.720p.DSNP.WEB-DL.AAC2.0.H.264-playWEB",
            "Superstore.S02E08.Seasonal.Help.720p.DSNP.WEB-DL.AAC2.0.H.264-playWEB",
        ]);

        var numbered = numbering.Apply(Show, Described());
        var season = numbered.Seasons.Single(s => s.Number == 2).Episodes;

        // The promotion is a special and leaves the season; every episode after it moves back one,
        // those the releases do not name included.
        Assert.DoesNotContain(season, e => e.Name == "Olympics");
        Assert.Equal("Strike", season.Single(e => e.Number == 1).Name);
        Assert.Equal("Election Day", season.Single(e => e.Number == 7).Name);
        Assert.Equal("Black Friday", season.Single(e => e.Number == 9).Name);
        Assert.Equal(9, numbered.Details!.NextEpisodeToAir!.Number);

        // A season nothing is known of keeps the database's numbering.
        Assert.Equal("Magazine Profile", numbered.Seasons.Single(s => s.Number == 1).Episodes.Single(e => e.Number == 2).Name);
    }

    [Fact]
    public void A_season_the_releases_order_differently_keeps_each_name_on_its_own_episode()
    {
        // Superstore's fifth season: the database lists the episodes as they aired, the releases in
        // production order, which moves Carol's Back from 19 to 21 and the two after it back one.
        string[] aired = ["Employee App", "Zephra Cares", "Playdate", "Carol's Back", "Customer Safari", "California (1)"];
        var described = new StoredShow
        {
            FetchedAt = DateTimeOffset.UtcNow,
            Details = new ShowDetails { ImdbId = Show, TmdbId = 62649, Title = "Superstore" },
            Seasons =
            [
                new SeasonDetails { Number = 5, Episodes = [.. aired.Select((name, i) => new EpisodeDetails { Season = 5, Number = i + 16, Name = name })] },
            ],
        };

        var numbering = new EpisodeNumbering();
        numbering.Learn(Show, [
            "Superstore.S05E16.Employee.App.720p.DSNP.WEB-DL.AAC2.0.H.264-playWEB.mkv",
            "Superstore.S05E17.Zephra.Cares.720p.DSNP.WEB-DL.AAC2.0.H.264-playWEB.mkv",
            "Superstore.S05E18.Playdate.720p.DSNP.WEB-DL.AAC2.0.H.264-playWEB.mkv",
            "Superstore.S05E19.Customer.Safari.720p.DSNP.WEB-DL.AAC2.0.H.264-playWEB.mkv",
            "Superstore.S05E20.California.Part.1.720p.DSNP.WEB-DL.AAC2.0.H.264-playWEB.mkv",
            "Superstore.S05E21.Carol's.Back.720p.DSNP.WEB-DL.AAC2.0.H.264-playWEB.mkv",
        ]);

        var season = numbering.Apply(Show, described).Seasons.Single().Episodes;

        Assert.Equal([16, 17, 18, 19, 20, 21], season.Select(e => e.Number).Order());
        Assert.Equal("Playdate", season.Single(e => e.Number == 18).Name);
        Assert.Equal("Customer Safari", season.Single(e => e.Number == 19).Name);
        Assert.Equal("California (1)", season.Single(e => e.Number == 20).Name);
        Assert.Equal("Carol's Back", season.Single(e => e.Number == 21).Name);
    }

    [Fact]
    public void A_show_whose_numberings_agree_is_left_as_it_is()
    {
        var numbering = new EpisodeNumbering();
        numbering.Learn(Show, ["Superstore.S02E02.Strike.720p.WEB-DL-GRP"]);
        var described = Described();

        Assert.Same(described, numbering.Apply(Show, described));
    }
}
