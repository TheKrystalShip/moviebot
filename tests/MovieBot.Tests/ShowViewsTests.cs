using System.Net;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using TheKrystalShip.MovieBot.Api.Catalogue;
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
        var shows = Catalogue.Summaries([Episode(2, 1), Episode(1, 2), Episode(1, 1), Episode(1, 1, show: "tt5875444") with { Id = "slow-horses-s01e01" }]);

        Assert.Equal(2, shows.Count);
        var severance = shows.Single(s => s.ImdbId == "tt11280740");
        Assert.Equal(3, severance.Episodes);
        Assert.Equal([1, 2], severance.Seasons);
        Assert.Equal("severance-s01e01", severance.FirstTitleId);
        Assert.Null(severance.Card); // The catalogue does not list it, so it is named by its episodes.
        Assert.Equal("Severance", severance.Title);
    }

    [Fact]
    public void A_show_the_catalogue_lists_is_shelved_with_its_card()
    {
        var show = Assert.Single(Catalogue.Summaries([Episode(1, 1, show: TestCatalogue.Chernobyl) with { Id = "chernobyl-s01e01" }]));

        Assert.Equal("Chernobyl", show.Title);
        Assert.Equal((TestCatalogue.Chernobyl, "show"), (show.Card!.Id, show.Card.Kind));
    }

    [Fact]
    public void A_view_holds_every_season_the_library_has_even_one_the_catalogue_does_not_list()
    {
        var episodes = new[] { Episode(1, 2, show: TestCatalogue.Chernobyl), Episode(2, 1, show: TestCatalogue.Chernobyl) };

        var view = Catalogue.View(TestCatalogue.Chernobyl, episodes);

        Assert.Equal([1, 2], view.Seasons.Select(s => s.Number));
        Assert.Equal("Season 1", view.Seasons[0].Name);
        Assert.Null(view.Seasons[0].Episodes[0].TitleId);
        Assert.Equal(("1:23:45", 60, "/images/w300/e1.jpg"), (view.Seasons[0].Episodes[0].Name, view.Seasons[0].Episodes[0].Runtime, view.Seasons[0].Episodes[0].Still));
        Assert.Equal("severance-s01e02", view.Seasons[0].Episodes[1].TitleId);
        Assert.Equal("severance-s02e01", Assert.Single(view.Seasons[1].Episodes).TitleId);
        Assert.False(view.Followable); // Chernobyl has ended.
    }

    [Fact]
    public async Task The_shelf_of_shows_is_served()
    {
        var response = await fixture.CreatePersonClient().GetAsync("/api/shows");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(JsonValueKind.Array, JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.ValueKind);
    }

    private CatalogueTitles Catalogue => fixture.Services.GetRequiredService<CatalogueTitles>();
}
