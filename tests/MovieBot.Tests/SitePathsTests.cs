using TheKrystalShip.MovieBot.Api.Notifications;
using Xunit;

namespace TheKrystalShip.MovieBot.Tests;

public sealed class SitePathsTests
{
    [Fact]
    public void A_title_is_under_movies() => Assert.Equal("/movies/heat-1995", SitePaths.Title("heat-1995"));

    [Fact]
    public void A_show_is_under_tv_and_opens_on_a_season_when_given_one()
    {
        Assert.Equal("/tv/tt11280740", SitePaths.Show("tt11280740"));
        Assert.Equal("/tv/tt11280740/season/2", SitePaths.Show("tt11280740", 2));
    }

    [Fact]
    public void A_film_not_held_is_under_discover() => Assert.Equal("/discover/film/tt0113277", SitePaths.Film("tt0113277"));

    [Fact]
    public void The_default_profile_tab_is_the_page_itself()
    {
        Assert.Equal("/profile", SitePaths.Profile());
        Assert.Equal("/profile/requests", SitePaths.Profile("requests"));
    }

    [Fact]
    public void A_room_carries_the_title_it_is_opened_on()
    {
        Assert.Equal("/r/a1b2", SitePaths.Room("a1b2"));
        Assert.Equal("/r/a1b2?title=heat-1995", SitePaths.Room("a1b2", "heat-1995"));
    }
}
