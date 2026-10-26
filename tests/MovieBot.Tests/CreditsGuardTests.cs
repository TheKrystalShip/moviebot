using TheKrystalShip.MovieBot.Core;
using TheKrystalShip.MovieBot.Ingest.Markers;
using Xunit;

namespace TheKrystalShip.MovieBot.Tests;

/// <summary>
/// The closing music starts under the last lines of the scene often enough that the credits the
/// audio finds can begin with somebody still talking; a voice deeper in is audio every episode
/// shares, which is the credits' own.
/// </summary>
public class CreditsGuardTests
{
    private static Marker Credits(double start = 2400, double end = 2440) =>
        new() { Kind = MarkerKind.Credits, StartSeconds = start, EndSeconds = end, From = MarkerSource.Audio };

    private static Cue Say(double start, string text = "Glad one of us does.") => new(start, start + 2, text);

    [Fact]
    public void Credits_with_nobody_speaking_at_their_head_are_unchanged()
    {
        var credits = Credits();

        Assert.Equal(credits, CreditsGuard.Guard(credits, [Say(2390), new Cue(2402, 2405, "<i>♪ Let me start again ♪</i>")]));
    }

    [Fact]
    public void A_line_at_the_head_moves_the_start_past_it()
    {
        var guarded = CreditsGuard.Guard(Credits(), [Say(2399)]);

        Assert.NotNull(guarded);
        Assert.Equal(2401.5, guarded.StartSeconds);
        Assert.Equal(2440, guarded.EndSeconds);
    }

    [Fact]
    public void Each_line_at_the_new_head_moves_it_again()
    {
        var guarded = CreditsGuard.Guard(Credits(), [Say(2401), Say(2409)]);

        Assert.Equal(2411.5, guarded!.StartSeconds);
    }

    [Fact]
    public void A_line_deeper_in_is_part_of_the_credits()
    {
        var credits = Credits();

        Assert.Equal(credits, CreditsGuard.Guard(credits, [new Cue(2431, 2432, "<i>Damn it.</i>")]));
    }

    [Fact]
    public void Credits_left_too_short_are_dropped()
    {
        Assert.Null(CreditsGuard.Guard(Credits(2400, 2420), [Say(2405)]));
    }
}
