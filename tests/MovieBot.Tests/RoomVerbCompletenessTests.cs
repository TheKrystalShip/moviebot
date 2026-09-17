using TheKrystalShip.MovieBot.Bot.Voice;
using Xunit;

namespace TheKrystalShip.MovieBot.Tests;

/// <summary>
/// What may move the film before the speaker has stopped talking. Only what the gate would act on
/// unmistakably: anything else is read again once the speaker goes quiet.
/// </summary>
public sealed class RoomVerbCompletenessTests
{
    private readonly RoomVerbCompleteness _completeness = new();

    [Theory]
    [InlineData("pause")]
    [InlineData("pause the movie")]
    [InlineData("resume the film")]
    [InlineData("back fifteen")]
    public void A_room_verb_is_complete_as_soon_as_it_is_said(string request) =>
        Assert.True(_completeness.IsComplete(request));

    [Theory]
    [InlineData("go back")]
    [InlineData("should we pause")]
    [InlineData("don't pause it")]
    [InlineData("download the second Pirates of the Caribbean")]
    public void Anything_else_waits_for_the_speaker_to_finish(string request) =>
        Assert.False(_completeness.IsComplete(request));
}
