using TheKrystalShip.Agent.Plugin;
using TheKrystalShip.MovieBot.Assistant.Api;
using Xunit;

namespace TheKrystalShip.MovieBot.Tests.Assistant;

/// <summary>The recording of the API's answers, which a behavioural suite replays through the same tools.</summary>
public sealed class RecordingTests : IDisposable
{
    private readonly string _file = Path.Combine(Path.GetTempPath(), "moviebot-tests", $"recording-{Guid.NewGuid():N}.jsonl");

    public RecordingTests() => Directory.CreateDirectory(Path.GetDirectoryName(_file)!);

    public void Dispose() => File.Delete(_file);

    [Fact]
    public async Task What_the_api_answers_is_recorded_and_replays_to_the_same_tool_result()
    {
        var recorder = new RecordingHandler(_file) { InnerHandler = CinemaWorld.Build() };
        var live = new ToolRig();
        var recording = new CinemaApi(new HttpClient(recorder) { BaseAddress = new Uri("http://api.test/") });
        var tool = new TheKrystalShip.MovieBot.Assistant.Tools.SearchTitlesTool(
            recording, Microsoft.Extensions.Logging.Abstractions.NullLogger<TheKrystalShip.MovieBot.Assistant.Tools.SearchTitlesTool>.Instance);

        PluginToolResult recorded = await live.RunAsync(tool, new { query = "Night Harbor" });

        RecordedAnswer answer = Assert.Single(ApiRecording.Read(_file));
        Assert.Equal("GET", answer.Method);
        Assert.Equal("/api/catalogue/search?q=Night%20Harbor", answer.Target);
        Assert.Equal(200, answer.Status);

        var replayed = new ToolRig(new ReplayHandler(ApiRecording.Read(_file)));
        PluginToolResult again = await replayed.RunAsync(replayed.Search, new { query = "Night Harbor" });
        Assert.Equal(recorded.Text, again.Text);
        Assert.Equal(["tt1000001", "tt1000002"], ToolRig.CardIds(again));
    }

    [Fact]
    public async Task A_question_the_recording_does_not_hold_is_a_404_the_tool_words()
    {
        var rig = new ToolRig(new ReplayHandler());

        PluginToolResult result = await rig.RunAsync(rig.Search, new { query = "anything" });

        Assert.True(result.IsError);
        Assert.Contains("Nothing is recorded for GET /api/catalogue/search?q=anything.", result.Text);
    }

    [Fact]
    public void The_order_of_the_query_the_case_of_a_name_and_how_a_space_is_spelled_do_not_change_the_key()
    {
        string a = ApiRecording.Key("GET", new Uri("http://x/api/catalogue/discover?genre=Horror&from=2000"));
        string b = ApiRecording.Key("get", new Uri("http://x/api/catalogue/discover?from=2000&genre=horror"));

        Assert.Equal(a, b);
        Assert.Equal(
            ApiRecording.Key("GET", new Uri("http://x/s?q=night%20harbor")), ApiRecording.Key("GET", new Uri("http://x/s?q=Night+Harbor")));
    }
}
