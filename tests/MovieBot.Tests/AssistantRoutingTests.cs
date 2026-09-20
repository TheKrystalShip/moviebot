using System.Diagnostics;
using System.Globalization;
using TheKrystalShip.Llm.Models;
using TheKrystalShip.MovieBot.Bot.Assistant;
using Xunit;
using Xunit.Abstractions;

namespace TheKrystalShip.MovieBot.Tests;

/// <summary>
/// Runs only against a real model: <c>MOVIEBOT_LIVE_LLM</c> names its endpoint.
/// </summary>
/// <remarks>
/// A routing check against a scripted model checks the script. These are what the shipped prompt and
/// catalog make of real requests on the model hotbox runs, reached from here with
/// <c>ssh -N -L 8190:127.0.0.1:8190 hotbox</c>.
/// </remarks>
public sealed class LiveModelFactAttribute : FactAttribute
{
    public const string Variable = "MOVIEBOT_LIVE_LLM";

    public LiveModelFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(Variable)))
            Skip = $"{Variable} names no model to route against.";
    }
}

/// <summary>The same condition as <see cref="LiveModelFactAttribute"/>, for a request said several ways.</summary>
public sealed class LiveModelTheoryAttribute : TheoryAttribute
{
    public LiveModelTheoryAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(LiveModelFactAttribute.Variable)))
            Skip = $"{LiveModelFactAttribute.Variable} names no model to route against.";
    }
}

/// <summary>
/// Which tool the model reaches for, given what the room is doing. Each request is one a room verb
/// does not cover, because those never reach the model.
/// </summary>
public sealed class AssistantRoutingTests(SessionFixture fixture, ITestOutputHelper output)
    : IClassFixture<SessionFixture>, IDisposable
{
    private static long _channels = 700_000_000;

    private readonly AssistantHost _host = new(
        fixture, Environment.GetEnvironmentVariable(LiveModelFactAttribute.Variable));

    [LiveModelFact]
    public async Task Carry_on_in_a_paused_room_plays_it()
    {
        var turn = await RoomAsync(SessionFixture.Heat, at: 600, paused: true);

        var calls = await AskAsync(turn, "ok carry on");

        Assert.Contains(calls, c => c.Name.Name == RoomTools.Resume);
        Assert.False((await _host.Api.OpenSessionAsync(turn.SessionId, CancellationToken.None)).Paused);
    }

    [LiveModelFact]
    public async Task Going_back_a_bit_moves_back_by_an_amount_it_chose()
    {
        var turn = await RoomAsync(SessionFixture.Heat, at: 1800, paused: false);

        var calls = await AskAsync(turn, "go back a bit, I missed that");

        var seek = Assert.Single(calls, c => c.Name.Name == RoomTools.SeekRelative);
        Assert.True(double.Parse(seek.Arg("seconds")!, CultureInfo.InvariantCulture) < 0);
    }

    [LiveModelFact]
    public async Task Skipping_ahead_two_minutes_converts_the_unit()
    {
        var turn = await RoomAsync(SessionFixture.Heat, at: 1800, paused: false);

        var calls = await AskAsync(turn, "can you skip ahead like two minutes");

        var seek = Assert.Single(calls, c => c.Name.Name == RoomTools.SeekRelative);
        Assert.Equal(120, double.Parse(seek.Arg("seconds")!, CultureInfo.InvariantCulture));
    }

    [LiveModelFact]
    public async Task Going_to_an_hour_in_seeks_to_a_position()
    {
        var turn = await RoomAsync(SessionFixture.Heat, at: 300, paused: false);

        var calls = await AskAsync(turn, "jump to an hour in");

        var seek = Assert.Single(calls, c => c.Name.Name == RoomTools.Seek);
        Assert.Equal(3600, FilmClock.Parse(seek.Arg("position")));
    }

    [LiveModelFact]
    public async Task Switching_films_loads_the_one_named_by_its_id()
    {
        var turn = await RoomAsync(SessionFixture.Heat, at: 300, paused: false);

        var calls = await AskAsync(turn, "put on collateral instead");

        var load = Assert.Single(calls, c => c.Name.Name == RoomTools.WatchFilm);
        Assert.Equal(SessionFixture.Collateral, (await _host.Api.OpenSessionAsync(turn.SessionId, CancellationToken.None)).TitleId);
        output.WriteLine($"watch_film title={load.Arg("title")}");
    }

    [LiveModelFact]
    public async Task Downloading_a_film_that_is_not_here_finds_it_and_then_proposes()
    {
        var turn = await RoomAsync(SessionFixture.Heat, at: 300, paused: false);

        var answer = await _host.Assistant.AskAsync(turn, "download inception", CancellationToken.None);
        var calls = Calls(turn);

        Assert.Contains(calls, c => c.Name.Name is RoomTools.WatchFilm);
        Assert.Equal(RoomTools.DownloadFilm, Assert.Single(answer.Proposed).Kind);
        Assert.Equal(SessionFixture.Heat, (await _host.Api.OpenSessionAsync(turn.SessionId, CancellationToken.None)).TitleId);
    }

    [LiveModelTheory]
    [InlineData("Play Inception")]
    [InlineData("put on inception")]
    [InlineData("I want to watch inception")]
    public async Task Asking_to_play_a_film_that_is_not_here_proposes_downloading_it(string said)
    {
        // The room that asked for Twilight and Step Up was told "not in the library" and nothing else.
        var turn = await RoomAsync(SessionFixture.Heat, at: 300, paused: false);

        var answer = await _host.Assistant.AskAsync(turn, said, CancellationToken.None);

        output.WriteLine($"{string.Join(", ", Calls(turn).Select(c => $"{c.Name}({string.Join(", ", c.Arguments.Select(a => $"{a.Key}={a.Value}"))})"))}; "
                         + $"proposed: {string.Join("; ", answer.Proposed.Select(p => p.Describes))}; reply: {answer.Text}");
        Assert.Equal(RoomTools.DownloadFilm, Assert.Single(answer.Proposed).Kind);
    }

    [LiveModelTheory]
    [InlineData("Download the second Pirates of the Caribbean movie")]
    [InlineData("get pirates of the caribbean 2")]
    public async Task A_film_named_by_its_place_in_a_series_is_found_and_proposed(string said)
    {
        var turn = await RoomAsync(SessionFixture.Heat, at: 300, paused: false);

        var answer = await _host.Assistant.AskAsync(turn, said, CancellationToken.None);

        output.WriteLine($"{string.Join(", ", Calls(turn).Select(c => $"{c.Name}({string.Join(", ", c.Arguments.Select(a => $"{a.Key}={a.Value}"))})"))}; "
                         + $"proposed: {string.Join("; ", answer.Proposed.Select(p => p.Describes))}; reply: {answer.Text}");
        var proposal = Assert.Single(answer.Proposed);
        Assert.Equal(RoomTools.DownloadFilm, proposal.Kind);
        Assert.Contains("Dead Man", proposal.Describes);
    }

    [LiveModelTheory]
    [InlineData("download inception for later")]
    [InlineData("can you grab inception for tomorrow, don't stop this")]
    [InlineData("get inception ready for after this one")]
    public async Task Asking_for_a_film_for_later_fetches_it_without_taking_the_room(string said)
    {
        // The request this tool exists for: a room halfway through a film wants the next one made
        // ready. Routing it to watch_film would propose a download that replaces what is playing.
        var turn = await RoomAsync(SessionFixture.Heat, at: 2700, paused: false);

        var answer = await _host.Assistant.AskAsync(turn, said, CancellationToken.None);

        output.WriteLine($"{string.Join(", ", Calls(turn).Select(c => $"{c.Name}({string.Join(", ", c.Arguments.Select(a => $"{a.Key}={a.Value}"))})"))}; "
                         + $"proposed: {string.Join("; ", answer.Proposed.Select(p => p.Describes))}; reply: {answer.Text}");
        Assert.Equal(RoomTools.DownloadOnly, Assert.Single(answer.Proposed).Kind);
        Assert.Equal(SessionFixture.Heat, (await _host.Api.OpenSessionAsync(turn.SessionId, CancellationToken.None)).TitleId);
    }

    [LiveModelTheory]
    [InlineData("put on inception")]
    [InlineData("let's watch inception")]
    public async Task Asking_to_watch_a_film_now_is_not_read_as_fetching_it_for_later(string said)
    {
        // The other direction of the same measurement. A second download tool beside watch_film is
        // exactly the change that quietly stops "put it on" meaning the room.
        var turn = await RoomAsync(SessionFixture.Heat, at: 300, paused: false);

        var answer = await _host.Assistant.AskAsync(turn, said, CancellationToken.None);

        output.WriteLine($"{string.Join(", ", Calls(turn).Select(c => $"{c.Name}({string.Join(", ", c.Arguments.Select(a => $"{a.Key}={a.Value}"))})"))}; "
                         + $"proposed: {string.Join("; ", answer.Proposed.Select(p => p.Describes))}; reply: {answer.Text}");
        Assert.Equal(RoomTools.DownloadFilm, Assert.Single(answer.Proposed).Kind);
    }

    [LiveModelFact]
    public async Task Keep_going_in_a_paused_room_plays_it()
    {
        var turn = await RoomAsync(SessionFixture.Collateral, at: 600, paused: true);

        var calls = await AskAsync(turn, "keep going");

        Assert.Contains(calls, c => c.Name.Name == RoomTools.Resume);
    }

    [LiveModelFact]
    public async Task Asking_to_download_a_film_already_here_proposes_no_download()
    {
        var turn = await RoomAsync(SessionFixture.Collateral, at: 300, paused: false);

        var answer = await _host.Assistant.AskAsync(turn, "can we download heat", CancellationToken.None);

        output.WriteLine($"{string.Join(", ", Calls(turn).Select(c => $"{c.Name}({string.Join(", ", c.Arguments.Select(a => $"{a.Key}={a.Value}"))})"))}; "
                         + $"proposed: {string.Join("; ", answer.Proposed.Select(p => p.Describes))}; reply: {answer.Text}");
        Assert.DoesNotContain(answer.Proposed, p => p.Kind == RoomTools.DownloadFilm);

        // A refusal that suggested putting the film on was answered "I have loaded Heat" with nothing
        // loaded. A claim of an act the turn did not take is the failure worth pinning.
        if (Calls(turn).All(c => c.Name.Name != RoomTools.WatchFilm))
            Assert.DoesNotContain("loaded", answer.Text, StringComparison.OrdinalIgnoreCase);
    }

    [LiveModelFact]
    public async Task Why_it_stopped_is_answered_from_what_the_room_verbs_did()
    {
        var turn = await RoomAsync(SessionFixture.Heat, at: 900, paused: false);
        await _host.Api.PauseAsync(turn.SessionId, turn.SpeakerUserId, turn.SpeakerName, CancellationToken.None);
        _host.Assistant.RecordAct(turn, "pause", RoomTools.Pause, new Dictionary<string, string?>(), "Paused at 15:00.");

        var answer = await _host.Assistant.AskAsync(turn with { SpeakerId = 99, SpeakerName = "Mika" }, "why did it stop?", CancellationToken.None);

        Assert.Contains("Haru", answer.Text);
        Assert.DoesNotContain(Calls(turn), c => RoomTools.RoomMoves.Contains(c.Name.Name));
        output.WriteLine($"reply: {answer.Text}");
    }

    [LiveModelFact]
    public async Task What_is_on_is_answered_from_the_room_without_moving_it()
    {
        var turn = await RoomAsync(SessionFixture.Heat, at: 900, paused: false);

        var answer = await _host.Assistant.AskAsync(turn, "what are we watching?", CancellationToken.None);

        Assert.Contains("Heat", answer.Text);
        Assert.DoesNotContain(Calls(turn), c => RoomTools.RoomMoves.Contains(c.Name.Name));
        output.WriteLine($"reply: {answer.Text}");
    }

    public void Dispose() => _host.Dispose();

    private async Task<RoomTurn> RoomAsync(string title, double at, bool paused)
    {
        var turn = AssistantPartsTests.Turn((ulong)Interlocked.Increment(ref _channels));
        await _host.Api.SetSessionTitleAsync(turn.SessionId, title, "someone", CancellationToken.None);
        await _host.Api.SeekAsync(turn.SessionId, at, "1", "someone", CancellationToken.None);
        if (!paused) await _host.Api.PlayAsync(turn.SessionId, "1", "someone", CancellationToken.None);
        return turn;
    }

    private async Task<IReadOnlyList<LlmToolCall>> AskAsync(RoomTurn turn, string said)
    {
        var started = Stopwatch.GetTimestamp();
        var answer = await _host.Assistant.AskAsync(turn, said, CancellationToken.None);
        var calls = Calls(turn);

        output.WriteLine($"\"{said}\" -> {string.Join(", ", calls.Select(c => $"{c.Name}({string.Join(", ", c.Arguments.Select(a => $"{a.Key}={a.Value}"))})"))}"
                         + $" in {Stopwatch.GetElapsedTime(started).TotalMilliseconds:0} ms; reply: {answer.Text}");
        Assert.False(answer.Failed, answer.Text);
        return calls;
    }

    private IReadOnlyList<LlmToolCall> Calls(RoomTurn turn) =>
    [
        .. _host.Conversations.GetHistory(turn.ConversationId)
            .Where(e => e.Turn is { SystemPromptHash: not "room-verb" })
            .SelectMany(e => e.Turn!.Tools)
            .Select(t => new LlmToolCall(t.Name, t.Arguments)),
    ];
}
