using TheKrystalShip.MovieBot.Bot.Voice;
using Xunit;

namespace TheKrystalShip.MovieBot.Tests;

/// <summary>
/// The gate between hearing somebody and moving the film for everyone. What it gets wrong is not
/// symmetric — a missed verb is a slower answer, a misread one moves the room — so most of what is
/// pinned here is what it must refuse.
/// </summary>
public sealed class RoomVerbsTests
{
    [Theory]
    [InlineData("pause")]
    [InlineData("Pause.")]
    [InlineData("pause the movie")]
    [InlineData("Pause the film, please.")]
    [InlineData("please pause")]
    [InlineData("stop")]
    [InlineData("Stop it.")]
    [InlineData("just pause now")]
    public void A_pause_is_read_as_a_pause(string said)
    {
        var reading = RoomVerbs.Read(said);

        Assert.Equal(RoomVerbMatch.Match, reading.Match);
        Assert.Equal(RoomVerbKind.Pause, reading.Kind);
    }

    [Theory]
    [InlineData("play")]
    [InlineData("Resume.")]
    [InlineData("unpause")]
    [InlineData("play the movie")]
    [InlineData("Continue, thank you.")]
    public void A_play_is_read_as_a_play(string said)
    {
        var reading = RoomVerbs.Read(said);

        Assert.Equal(RoomVerbMatch.Match, reading.Match);
        Assert.Equal(RoomVerbKind.Play, reading.Kind);
    }

    [Theory]
    [InlineData("back fifteen", -15)]
    [InlineData("Go back 15 seconds.", -15)]
    [InlineData("rewind thirty", -30)]
    [InlineData("rewind 30s", -30)]
    [InlineData("back two minutes", -120)]
    [InlineData("go back twenty five seconds", -25)]
    [InlineData("skip back a minute", -60)]
    [InlineData("fifteen seconds back", -15)]
    [InlineData("skip forward ten", 10)]
    [InlineData("forward a minute", 60)]
    [InlineData("skip ahead forty-five seconds", 45)]
    [InlineData("fast forward one minute", 60)]
    [InlineData("skip thirty seconds", 30)]
    [InlineData("half a minute back", -30)]
    [InlineData("thirty seconds forward please", 30)]
    public void A_nudge_is_read_with_its_amount_and_direction(string said, double seconds)
    {
        var reading = RoomVerbs.Read(said);

        Assert.Equal(RoomVerbMatch.Match, reading.Match);
        Assert.Equal(RoomVerbKind.Nudge, reading.Kind);
        Assert.Equal(seconds, reading.Seconds);
    }

    [Theory]
    // The plan's own examples: containing the word is not being the verb.
    [InlineData("should we pause?")]
    [InlineData("don't pause it")]
    [InlineData("Don’t stop.")]
    [InlineData("I love this part")]
    [InlineData("what is this film")]
    [InlineData("who paused it")]
    // Agreement, not a direction. Read as one, the film skips the first time somebody says yes.
    [InlineData("go ahead")]
    [InlineData("the meeting is at 1:30")]
    public void Speech_that_is_not_a_verb_is_left_for_something_that_understands_it(string said) =>
        Assert.Equal(RoomVerbMatch.NoMatch, RoomVerbs.Read(said).Match);

    [Theory]
    // A direction and no amount: moving the film by a guess is worse than asking.
    [InlineData("go back")]
    [InlineData("rewind")]
    [InlineData("skip")]
    [InlineData("rewind a bit")]
    [InlineData("back a few seconds")]
    // The punctuation trap: flattened, these become 130 and 15.
    [InlineData("go back 1:30")]
    [InlineData("skip 1.5 minutes")]
    [InlineData("rewind 1,5 minutes")]
    // Two acts in one breath, and an amount that cannot be meant.
    [InlineData("go back fifteen seconds and pause")]
    [InlineData("back zero")]
    [InlineData("back 90000")]
    // Starts like a verb, asks for something else.
    [InlineData("skip this film")]
    [InlineData("pause for a second")]
    [InlineData("stop talking")]
    [InlineData("play something else")]
    public void A_phrasing_the_gate_nearly_reads_is_not_guessed_at(string said) =>
        Assert.Equal(RoomVerbMatch.Ambiguous, RoomVerbs.Read(said).Match);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("please")]
    public void Nothing_to_act_on_is_not_a_verb(string? said) =>
        Assert.Equal(RoomVerbMatch.NoMatch, RoomVerbs.Read(said).Match);

    [Fact]
    public void The_same_words_always_read_the_same_way()
    {
        // The reason there is no model here. Asked a thousand times, a pause is a pause every time.
        var first = RoomVerbs.Read("go back fifteen seconds");

        for (var i = 0; i < 1000; i++)
            Assert.Equal(first, RoomVerbs.Read("go back fifteen seconds"));
    }
}
