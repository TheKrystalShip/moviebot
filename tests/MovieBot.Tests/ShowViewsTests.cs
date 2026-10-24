using System.Net;
using System.Text.Json;
using TheKrystalShip.MovieBot.Api.Data;
using TheKrystalShip.MovieBot.Api.Library;
using TheKrystalShip.MovieBot.Api.People;
using TheKrystalShip.MovieBot.Api.Shows;
using TheKrystalShip.MovieBot.Core;
using Xunit;

namespace TheKrystalShip.MovieBot.Tests;

/// <summary>
/// A show as its page and the shelves show it: where a person picks up, and the library's
/// episodes grouped under the show they belong to.
/// </summary>
public sealed class ShowViewsTests(SessionFixture fixture) : IClassFixture<SessionFixture>
{
    private static readonly DateTimeOffset Then = new(2026, 10, 1, 20, 0, 0, TimeSpan.Zero);

    private static TitleSummary Episode(int season, int number, TitleProgress? progress = null, string show = "tt11280740") => new()
    {
        Id = $"severance-s{season:00}e{number:00}",
        Title = $"Severance S{season:00}E{number:00}",
        DurationSeconds = 3000,
        Status = TitleStatus.Ready,
        Kind = TitleKind.Show,
        Episode = new EpisodeIdentity { Season = season, Number = number },
        Unit = $"{show}:s{season:00}",
        Film = new FilmIdentity { ImdbId = show, Name = "Severance", Year = 2022 },
        Progress = progress,
    };

    [Fact]
    public void A_person_picks_up_in_the_episode_they_did_not_finish()
    {
        var resume = ShowViews.ResumeIn(
        [
            Episode(1, 1, new TitleProgress(3000, true, Then)),
            Episode(1, 2, new TitleProgress(1260, false, Then.AddHours(1))),
            Episode(1, 3),
        ]);

        Assert.Equal(new ResumePoint("severance-s01e02", 1, 2, 1260, 3000), resume);
    }

    [Fact]
    public void A_person_who_finished_an_episode_picks_up_at_the_next()
    {
        var resume = ShowViews.ResumeIn(
        [
            Episode(1, 9, new TitleProgress(3000, true, Then)),
            Episode(2, 1),
        ]);

        Assert.Equal("severance-s02e01", resume!.TitleId);
        Assert.Equal(0, resume.PositionSeconds);
    }

    [Fact]
    public void Nothing_watched_is_nowhere_to_pick_up() =>
        Assert.Null(ShowViews.ResumeIn([Episode(1, 1), Episode(1, 2)]));

    [Fact]
    public void A_shelf_lists_a_show_once_however_many_episodes_it_holds()
    {
        var store = NewShowStore();

        var shows = store.Summaries([Episode(2, 1), Episode(1, 2), Episode(1, 1), Episode(1, 1, show: "tt5875444") with { Id = "slow-horses-s01e01" }]);

        Assert.Equal(2, shows.Count);
        var severance = shows.Single(s => s.ImdbId == "tt11280740");
        Assert.Equal(3, severance.Episodes);
        Assert.Equal([1, 2], severance.Seasons);
        Assert.Equal("severance-s01e01", severance.FirstTitleId);
    }

    [Fact]
    public void A_page_holds_every_season_the_library_has_even_one_the_database_does_not_list()
    {
        var store = NewShowStore();
        var stored = new StoredShow
        {
            FetchedAt = Then,
            Seasons =
            [
                new Acquire.Tmdb.SeasonDetails
                {
                    Number = 1,
                    Episodes =
                    [
                        new Acquire.Tmdb.EpisodeDetails { Season = 1, Number = 1, Name = "Good News About Hell", RuntimeMinutes = 57 },
                        new Acquire.Tmdb.EpisodeDetails { Season = 1, Number = 2, Name = "Half Loop" },
                    ],
                },
            ],
        };

        var view = store.View("tt11280740", stored, [Episode(1, 2), Episode(2, 1)]);

        Assert.Equal([1, 2], view.Seasons.Select(s => s.Number));
        Assert.Null(view.Seasons[0].Episodes[0].TitleId);
        Assert.Equal("Good News About Hell", view.Seasons[0].Episodes[0].Name);
        Assert.Equal("severance-s01e02", view.Seasons[0].Episodes[1].TitleId);
        Assert.Equal("severance-s02e01", Assert.Single(view.Seasons[1].Episodes).TitleId);
    }

    [Fact]
    public async Task The_shelf_of_shows_is_served()
    {
        var response = await fixture.CreatePersonClient().GetAsync("/api/shows");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(JsonValueKind.Array, JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.ValueKind);
    }


    private static ShowStore NewShowStore()
    {
        var root = Path.Combine(Path.GetTempPath(), "moviebot-shows-" + Guid.NewGuid().ToString("n"));
        var database = new CinemaDatabase(Path.Combine(root, "moviebot.db"));
        return new ShowStore(root, Microsoft.Extensions.Options.Options.Create(new Api.Films.FilmStoreOptions()), null!,
            new Api.Films.PosterPins(database), new Api.Films.KeptImages(database), null!, database, new Api.Films.TitleScores(database, new Api.Films.ImdbRatings(database)), new EpisodeNumbering(), TimeProvider.System,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<ShowStore>.Instance);
    }
}
