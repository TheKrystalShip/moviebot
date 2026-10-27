using Microsoft.Extensions.Time.Testing;
using TheKrystalShip.MovieBot.Api.Acquisition;
using Xunit;

namespace TheKrystalShip.MovieBot.Tests;

/// <summary>A room is told what it waits on is late once none of it has arrived for a minute.</summary>
public sealed class ArrivalWatchTests
{
    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 10, 27, 12, 0, 0, TimeSpan.Zero));

    [Fact]
    public void Nothing_arriving_for_the_patience_is_stalled()
    {
        var watch = new ArrivalWatch(_clock);

        Assert.False(watch.Stalled("room", "a", 0.1));
        _clock.Advance(ArrivalWatch.Patience - TimeSpan.FromSeconds(1));
        Assert.False(watch.Stalled("room", "a", 0.1));
        _clock.Advance(TimeSpan.FromSeconds(1));
        Assert.True(watch.Stalled("room", "a", 0.1));
    }

    [Fact]
    public void Anything_arriving_starts_the_count_again()
    {
        var watch = new ArrivalWatch(_clock);
        watch.Stalled("room", "a", 0.1);
        _clock.Advance(ArrivalWatch.Patience);

        Assert.False(watch.Stalled("room", "a", 0.11));
        _clock.Advance(ArrivalWatch.Patience - TimeSpan.FromSeconds(1));
        Assert.False(watch.Stalled("room", "a", 0.11));
    }

    [Fact]
    public void Another_item_or_a_forgotten_room_starts_the_count_again()
    {
        var watch = new ArrivalWatch(_clock);
        watch.Stalled("room", "a", 0.1);
        _clock.Advance(ArrivalWatch.Patience);

        Assert.False(watch.Stalled("room", "b", 0.1));
        _clock.Advance(ArrivalWatch.Patience);
        watch.Forget("room");
        Assert.False(watch.Stalled("room", "b", 0.1));
    }
}
