using TheKrystalShip.MovieBot.Bot.Assistant;
using Xunit;

namespace TheKrystalShip.MovieBot.Tests;

/// <summary>
/// A reply announcing a film going on is sent back when the turn put nothing on, and left alone when
/// it did.
/// </summary>
public sealed class RoomLoadingClaimTests
{
    [Theory]
    [InlineData("Loading Pirates of the Caribbean: The Curse of the Black Pearl...")]
    [InlineData("Now playing Heat.")]
    [InlineData("Switching to Collateral.")]
    public void An_announcement_on_a_turn_that_loaded_nothing_is_sent_back(string reply)
    {
        var fault = RoomLoadingClaim.Check("the first one", () => false).Inspect(reply);

        Assert.NotNull(fault);
        Assert.Contains("load_title", fault.Nudge);
        Assert.Contains("the first one", fault.Nudge);
    }

    [Fact]
    public void An_announcement_on_a_turn_that_did_load_passes() =>
        Assert.Null(RoomLoadingClaim.Check("put heat on", () => true).Inspect("Loading Heat."));

    [Theory]
    [InlineData("Playing from 0:18.")]
    [InlineData("Which Pirates of the Caribbean film would you like?")]
    [InlineData("That film is not in the library.")]
    public void An_ordinary_reply_passes(string reply) =>
        Assert.Null(RoomLoadingClaim.Check("anything", () => false).Inspect(reply));
}
