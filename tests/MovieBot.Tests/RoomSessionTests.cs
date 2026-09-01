using Xunit;
using TheKrystalShip.MovieBot.Bot.Sessions;

namespace TheKrystalShip.MovieBot.Tests;

/// <summary>
/// The room is the voice channel. This is the whole of what lets the bot remember nothing, so
/// it is pinned: the same channel has to reach the same session across restarts and processes.
/// </summary>
public sealed class RoomSessionTests
{
    [Fact]
    public void A_channel_always_names_the_same_session()
    {
        Assert.Equal("918273645", RoomSession.IdFor(918273645));
        Assert.Equal(RoomSession.IdFor(918273645), RoomSession.IdFor(918273645));
    }

    [Fact]
    public void Different_channels_are_different_rooms()
    {
        Assert.NotEqual(RoomSession.IdFor(1), RoomSession.IdFor(2));
    }

    [Fact]
    public void A_session_id_needs_no_escaping_in_a_url()
    {
        var id = RoomSession.IdFor(ulong.MaxValue);

        Assert.Equal(id, Uri.EscapeDataString(id));
    }
}
