using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using TheKrystalShip.MovieBot.Api.Auth;
using TheKrystalShip.MovieBot.Api.Sessions;
using TheKrystalShip.MovieBot.Core;
using Xunit;

namespace TheKrystalShip.MovieBot.Tests;

/// <summary>Moving a room through a show: the server decides what is either side and puts it on.</summary>
public sealed class RoomEpisodesTests(SessionFixture fixture) : IClassFixture<SessionFixture>
{
    private const string Show = "tt-room-show";

    private static readonly CinemaPerson Watcher =
        new("re1", "Ren", new HashSet<string> { CinemaActions.Watch }, null);

    private SessionStore Sessions => fixture.Services.GetRequiredService<SessionStore>();
    private RoomEpisodes Episodes => fixture.Services.GetRequiredService<RoomEpisodes>();

    private string Room(string name)
    {
        foreach (var (season, number) in new[] { (1, 1), (1, 2), (2, 1) })
            fixture.AddEpisode($"room-show-s{season:00}e{number:00}", Show, "Room Show", season, number);
        Sessions.Enter(name, Watcher.UserId);
        Sessions.LoadTitle(name, "room-show-s01e02", Watcher.Actor);
        return name;
    }

    [Fact]
    public async Task The_room_says_what_it_holds_and_what_is_either_side()
    {
        var room = Room("re-around");
        var around = await fixture.CreatePersonClient(Watcher.UserId).GetFromJsonAsync<RoomAround>(
            $"/api/sessions/{room}/around", new JsonSerializerOptions(JsonSerializerDefaults.Web));

        Assert.Equal((1, 2), (around!.Current!.Season, around.Current.Number));
        Assert.Equal("room-show-s01e01", around.Previous!.TitleId);
        Assert.Equal("room-show-s02e01", around.Next!.TitleId);
    }

    [Fact]
    public async Task Next_and_previous_move_the_room_across_seasons()
    {
        var room = Room("re-step");

        Assert.True(await Episodes.NextAsync(room, Watcher, default));
        Assert.Equal("room-show-s02e01", Sessions.GetOrCreate(room).TitleId);

        // The last episode here, for somebody who may not fetch, has nothing after it.
        Assert.False(await Episodes.NextAsync(room, Watcher, default));

        Assert.True(await Episodes.PreviousAsync(room, Watcher, default));
        Assert.Equal("room-show-s01e02", Sessions.GetOrCreate(room).TitleId);
    }

    [Fact]
    public async Task An_episode_is_put_on_by_its_place()
    {
        var room = Room("re-pick");

        Assert.True(await Episodes.PlayAsync(room, 1, 1, Watcher, default));
        Assert.Equal("room-show-s01e01", Sessions.GetOrCreate(room).TitleId);
        Assert.False(await Episodes.PlayAsync(room, 3, 1, Watcher, default));
    }

    [Fact]
    public void A_film_has_nothing_either_side()
    {
        Sessions.LoadTitle("re-film", SessionFixture.Heat, Watcher.Actor);
        Assert.Equal(new RoomAround(null, null, null), Episodes.Around("re-film", mayFetch: true));
    }
}
