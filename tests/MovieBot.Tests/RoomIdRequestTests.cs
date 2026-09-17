using TheKrystalShip.MovieBot.Bot.Assistant;
using Xunit;

namespace TheKrystalShip.MovieBot.Tests;

/// <summary>
/// A reply asking the room for an id is sent back, and an ordinary reply is not.
/// </summary>
public sealed class RoomIdRequestTests
{
    [Theory]
    [InlineData("I need the IMDb ID for the second Pirates of the Caribbean movie to search for a download.")]
    [InlineData("I need the IMDb ID for the second movie to search for a download.")]
    [InlineData("Could you give me the torrent id you want?")]
    [InlineData("What is the imdb id?")]
    public void A_reply_asking_for_an_id_is_sent_back(string reply)
    {
        var fault = RoomIdRequest.Check("download the second one").Inspect(reply);

        Assert.NotNull(fault);
        Assert.Contains("watch_film", fault.Nudge);
        Assert.Contains("download the second one", fault.Nudge);
    }

    [Theory]
    [InlineData("Please confirm that you want to download Pirates of the Caribbean: Dead Man's Chest.")]
    [InlineData("We are watching Heat (1995).")]
    [InlineData("I couldn't find that film on IMDb. Did you mean another title?")]
    public void An_ordinary_reply_passes(string reply)
    {
        Assert.Null(RoomIdRequest.Check("anything").Inspect(reply));
    }
}
