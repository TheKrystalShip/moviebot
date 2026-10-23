using TheKrystalShip.Agent.Plugin.Testing;
using TheKrystalShip.MovieBot.Assistant.Api;
using Xunit;

namespace TheKrystalShip.MovieBot.Tests.Assistant;

/// <summary>anticipated over the invented world: the counts are the API's, and a refusal is worded for the model.</summary>
public sealed class AnticipatedToolTests
{
    [Fact]
    public async Task Titles_are_listed_with_their_waiters_and_whether_the_person_is_one()
    {
        var world = CinemaWorld.Build().Answer("/api/anticipated", new List<AnticipatedTitle>
        {
            new() { ImdbId = "tt1000090", Title = "Long Tide", Year = 2027, Waiters = 4, Waiting = true },
            new() { ImdbId = "tt1000091", Title = "Quiet Hour", Waiters = 1 },
        });
        var rig = new ToolRig(world);

        ToolOutcome result = await rig.RunAsync(rig.Anticipated, new { });

        Assert.False(result.IsError);
        Assert.Equal(["GET /api/anticipated"], world.Asked);
        Assert.Contains("- Long Tide (2027), film. 4 waiting. The person is one of them. id tt1000090.", result.Text);
        Assert.Contains("- Quiet Hour, film. 1 waiting. id tt1000091.", result.Text);
        Assert.DoesNotContain("release", result.Text, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(["tt1000090", "tt1000091"], ToolRig.CardIds(result));
    }

    [Fact]
    public async Task Nobody_waiting_is_said_and_carries_no_card()
    {
        var rig = new ToolRig(CinemaWorld.Build().Answer("/api/anticipated", new List<AnticipatedTitle>()));

        ToolOutcome result = await rig.RunAsync(rig.Anticipated, new { });

        Assert.Equal("Nobody is waiting on a film that is not out yet.", result.Text);
        Assert.Null(result.Card);
    }

    [Fact]
    public async Task An_account_without_the_right_to_wait_is_refused_in_words()
    {
        var rig = new ToolRig(CinemaWorld.Build().Answer("/api/anticipated", """{"error":"forbidden"}""", 403));

        ToolOutcome result = await rig.RunAsync(rig.Anticipated, new { });

        Assert.True(result.IsError);
        Assert.Contains("This account may not wait on films in Cinema", result.Text);
    }
}
