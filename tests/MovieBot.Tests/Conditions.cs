using Xunit;

namespace TheKrystalShip.MovieBot.Tests;

/// <summary>
/// Waiting for something the server does after the call that caused it has returned, such as what a
/// connection's going leads to: a test waits on the condition, never on a length of time.
/// </summary>
internal static class Conditions
{
    public static async Task Eventually(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (!condition() && DateTime.UtcNow < deadline) await Task.Delay(20);
        Assert.True(condition());
    }
}
