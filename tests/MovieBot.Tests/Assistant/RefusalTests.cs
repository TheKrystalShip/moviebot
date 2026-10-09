using TheKrystalShip.Agent.Plugin.Testing;
using TheKrystalShip.MovieBot.Assistant.Api;
using TheKrystalShip.MovieBot.Assistant.Tools;
using Xunit;

namespace TheKrystalShip.MovieBot.Tests.Assistant;

/// <summary>What the model reads when a call does not go through: the reason, in the API's or the tool's own words.</summary>
public sealed class RefusalTests
{
    private readonly ToolRig _rig = new();

    [Fact]
    public async Task A_title_the_database_does_not_hold_is_said_in_words_the_model_can_act_on()
    {
        var rig = new ToolRig(CinemaWorld.Build().Answer("/api/catalogue/search?q=Nothing Like This&year=1901", new SearchResult()));

        ToolOutcome result = await rig.RunAsync(rig.Details, new { title = "Nothing Like This (1901)" });

        Assert.True(result.IsError);
        Assert.Contains("The film database has no title matching 'Nothing Like This (1901)' that has a page here.", result.Text);
    }

    [Fact]
    public async Task A_refusal_of_the_api_carries_its_own_reason()
    {
        var world = CinemaWorld.Build().Answer("/api/popular", """{"error":"The week is closed."}""", 400);
        var rig = new ToolRig(world);

        ToolOutcome result = await rig.RunAsync(rig.WhatsNew, new { which = "trending" });

        Assert.True(result.IsError);
        Assert.Equal("The week is closed.", result.Text);
    }

    [Fact]
    public async Task Shows_cannot_be_filtered_by_people_and_the_request_is_not_sent()
    {
        ToolOutcome result = await _rig.RunAsync(_rig.Discover, new { kind = "show", with = new[] { "Ada Calloway" } });

        Assert.True(result.IsError);
        Assert.Contains("Shows cannot be filtered by people here.", result.Text);
        Assert.Empty(_rig.World.Asked);
    }

    [Fact]
    public async Task A_discovery_the_api_declines_is_a_note_in_the_clue_search_and_the_rest_still_search()
    {
        var world = CinemaWorld.Build().Answer("/api/catalogue/discover?kind=film&goodonly=false&about=heist", """{"error":"No such keyword."}""", 404);
        var rig = new ToolRig(world);

        ToolOutcome result = await rig.RunAsync(rig.Clues, new { about = new[] { "heist" } });

        Assert.False(result.IsError);
        Assert.Contains("No such keyword.", result.Text);
    }

    [Fact]
    public async Task A_failure_nobody_foresaw_is_logged_and_answered_without_its_message()
    {
        var api = new CinemaApi(new HttpClient(new Failing()) { BaseAddress = new Uri("http://api.test/") });

        ToolOutcome result = await _rig.RunAsync(new SearchTitlesTool(api), new { query = "anything" });

        Assert.True(result.IsError);
        Assert.Equal("search_titles could not finish.", result.Text);
    }

    [Fact]
    public async Task An_argument_the_model_got_wrong_is_answered_with_what_it_takes()
    {
        ToolOutcome result = await _rig.RunAsync(_rig.Search, new { query = "heat", kind = "serial" });

        Assert.True(result.IsError);
        Assert.Contains("kind", result.Text);
        Assert.Contains("film, show", result.Text);
        Assert.Empty(_rig.World.Asked);
    }

    private sealed class Failing : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            throw new InvalidOperationException("a detail only the log should hold");
    }
}
