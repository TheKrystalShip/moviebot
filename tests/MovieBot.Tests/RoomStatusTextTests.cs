using TheKrystalShip.MovieBot.Bot.Presence;
using TheKrystalShip.MovieBot.Core;
using Xunit;

namespace TheKrystalShip.MovieBot.Tests;

/// <summary>
/// The words Discord shows for what rooms are watching. Each sentence is the whole of what a
/// person sees, so each one is pinned.
/// </summary>
public sealed class RoomStatusTextTests
{
    private static RoomSummary Room(
        string? name = "Dune (2021)", int participants = 2, bool paused = false,
        double position = 3731, double? duration = 9300, string sessionId = "1") => new()
    {
        SessionId = sessionId,
        TitleId = name is null ? null : "dune",
        Name = name,
        DurationSeconds = duration,
        Paused = paused,
        PositionSeconds = position,
        Participants = participants
    };

    [Fact]
    public void One_watched_room_names_its_film()
    {
        Assert.Equal("Dune (2021)", RoomStatusText.BotActivity([Room()]));
    }

    [Fact]
    public void Several_watched_rooms_are_counted()
    {
        Assert.Equal("2 films", RoomStatusText.BotActivity([Room(), Room(name: "Heat (1995)", sessionId: "2")]));
    }

    [Fact]
    public void An_empty_room_and_a_room_with_no_film_say_nothing()
    {
        Assert.Null(RoomStatusText.BotActivity([Room(participants: 0), Room(name: null)]));
        Assert.Null(RoomStatusText.BotActivity([]));
    }

    [Fact]
    public void A_playing_room_shows_where_it_is_against_the_whole()
    {
        Assert.Equal("Dune (2021) · 1:02 / 2:35", RoomStatusText.VoiceChannelLine(Room()));
    }

    [Fact]
    public void A_paused_room_says_so_and_carries_no_running_clock()
    {
        Assert.Equal("Dune (2021) · paused at 1:02", RoomStatusText.VoiceChannelLine(Room(paused: true)));
    }

    [Fact]
    public void A_short_film_is_written_in_minutes()
    {
        Assert.Equal("Short · 4:05 / 12:00",
            RoomStatusText.VoiceChannelLine(Room(name: "Short", position: 245, duration: 720)));
    }

    [Fact]
    public void Nothing_is_written_under_an_empty_room()
    {
        Assert.Null(RoomStatusText.VoiceChannelLine(Room(participants: 0)));
        Assert.Null(RoomStatusText.VoiceChannelLine(Room(name: null)));
    }

    [Fact]
    public void The_bot_recognises_its_own_lines_and_nobody_elses()
    {
        Assert.True(RoomStatusText.IsOwnLine("Dune (2021) · 1:02 / 2:35"));
        Assert.True(RoomStatusText.IsOwnLine("Dune (2021) · paused at 1:02"));
        Assert.True(RoomStatusText.IsOwnLine("Short · 4:05 / 12:00"));
        Assert.False(RoomStatusText.IsOwnLine("movie night, bring snacks"));
        Assert.False(RoomStatusText.IsOwnLine("meeting at 1:02"));
        Assert.False(RoomStatusText.IsOwnLine(null));
        Assert.False(RoomStatusText.IsOwnLine(""));
    }
}
