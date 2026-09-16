using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using TheKrystalShip.Agent;
using TheKrystalShip.Agent.Replies;
using TheKrystalShip.Llm.Models;
using TheKrystalShip.MovieBot.Bot.Api;
using TheKrystalShip.MovieBot.Bot.Assistant;
using TheKrystalShip.MovieBot.Core;
using Xunit;

namespace TheKrystalShip.MovieBot.Tests;

/// <summary>
/// The pieces of the assistant that decide something without a model: which tools exist, what a
/// proposal is worth, how a position is read, and what the model is told about the room.
/// </summary>
public sealed class AssistantPartsTests
{
    private static readonly string Prompts = new AssistantOptions().ResolvePromptDirectory();

    [Fact]
    public void The_shipped_catalog_describes_exactly_the_tools_the_bot_implements()
    {
        var catalog = ToolCatalog.Load(Prompts, RoomTools.Names);

        Assert.Equal(RoomTools.Names.Order(), catalog.All.Select(t => t.Name).Order());
    }

    [Fact]
    public void A_parameter_is_required_unless_the_catalog_says_otherwise()
    {
        var catalog = ToolCatalog.Load(Prompts, RoomTools.Names);

        var seconds = catalog.All.Single(t => t.Name == RoomTools.SeekRelative).Parameters.Single();
        Assert.True(seconds.Required);
        Assert.Equal("integer", seconds.Type);

        var year = catalog.All.Single(t => t.Name == RoomTools.SearchCatalogue).Parameters.Single(p => p.Name == "year");
        Assert.False(year.Required);
    }

    [Fact]
    public void A_catalog_that_disagrees_with_the_bot_is_refused_naming_both_sides()
    {
        const string json = """{ "play": { "description": "Plays.", "params": [] }, "dance": { "description": "Dances.", "params": [] } }""";

        var refused = Assert.Throws<AssistantTextUnavailableException>(() => ToolCatalog.Parse(json, ["play", "pause"]));

        Assert.Contains("no tool for 1 tool(s) the assistant implements: pause", refused.Message);
        Assert.Contains("has no handler for: dance", refused.Message);
    }

    [Fact]
    public void A_parameter_of_a_type_the_model_cannot_be_given_is_refused()
    {
        const string json = """{ "seek": { "description": "Seeks.", "params": [ { "name": "at", "description": "Where.", "type": "timestamp" } ] } }""";

        Assert.Throws<AssistantTextUnavailableException>(() => ToolCatalog.Parse(json, ["seek"]));
    }

    [Theory]
    // The live failure: a refusal that mentioned putting a film on, answered as if it had been.
    [InlineData("I have loaded Heat for you.")]
    [InlineData("I've paused it.")]
    [InlineData("I've put Heat on.")]
    [InlineData("I've downloaded Collateral.")]
    public void A_claim_of_acting_in_a_room_is_caught(string reply) =>
        Assert.True(RoomActionClaim.Check.IsPresentIn(reply));

    [Theory]
    [InlineData("Haru paused it a minute ago.")]
    [InlineData("I can put Heat on if you like.")]
    [InlineData("Heat is already in the library, so nothing needs downloading.")]
    [InlineData("Want me to download it?")]
    public void An_honest_reply_about_a_room_is_left_alone(string reply) =>
        Assert.False(RoomActionClaim.Check.IsPresentIn(reply));

    [Fact]
    public void A_proposal_is_redeemed_once_by_an_unguessable_token()
    {
        var staged = new StagedActions(Options.Create(new AssistantOptions()), TimeProvider.System);
        var action = staged.Stage("keep_film", "keep Heat", Turn(1), _ => Task.FromResult(new StagedOutcome("kept")));

        Assert.Matches("^[0-9a-f]{32}$", action.Token);
        Assert.Same(action, staged.Take(action.Token));
        Assert.Null(staged.Take(action.Token));
    }

    [Fact]
    public void A_proposal_nobody_answered_in_time_cannot_be_redeemed()
    {
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var staged = new StagedActions(Options.Create(new AssistantOptions { OfferMinutes = 10 }), clock);
        var action = staged.Stage("download_film", "download Heat", Turn(1), _ => Task.FromResult(new StagedOutcome("started")));

        clock.Advance(TimeSpan.FromMinutes(11));

        Assert.Null(staged.Take(action.Token));
    }

    [Theory]
    [InlineData("1:02:03", 3723)]
    [InlineData("62:03", 3723)]
    [InlineData("90", 90)]
    [InlineData("90.5", 90.5)]
    public void A_position_is_read_the_way_people_write_one(string text, double seconds) =>
        Assert.Equal(seconds, FilmClock.Parse(text));

    [Theory]
    [InlineData("1:75")]
    [InlineData("an hour in")]
    [InlineData("-30")]
    [InlineData("1:2:3:4")]
    public void A_position_that_cannot_be_read_exactly_is_not_guessed_at(string text) =>
        Assert.Null(FilmClock.Parse(text));

    [Fact]
    public void The_room_is_described_with_the_film_where_it_is_and_who_moved_it()
    {
        var now = DateTimeOffset.UtcNow;
        var state = new SessionState
        {
            SessionId = "1", Epoch = "e", TitleId = "heat-1995", Paused = false, PositionSeconds = 3600,
            AnchorUtc = now.AddSeconds(-15), UpdatedBy = new Actor("7", "Haru"),
        };
        LibraryTitle[] library =
        [
            new() { Id = "heat-1995", Title = "Heat", DurationSeconds = 10_200, Status = TitleStatus.Ready },
        ];

        var line = RoomFacts.Describe("Cinema", state, library, now);

        Assert.Equal("Room Cinema: Heat (id heat-1995) at 1:00:15 of 2:50:00, playing. Last changed by Haru.", line);
    }

    [Fact]
    public void A_room_holding_nothing_says_so() =>
        Assert.Equal("Room Cinema: no film is loaded.", RoomFacts.Describe("Cinema", null, [], DateTimeOffset.UtcNow));

    internal static RoomTurn Turn(ulong channel) => new(1, channel, "Cinema", 4242, "Haru");
}

/// <summary>
/// The tools against the running API, built the way the bot builds them. What is worth catching is
/// the seam: the right room, the right person, a proposal that waits, and nothing done to a room that
/// is watching nothing.
/// </summary>
public sealed class RoomToolsTests(SessionFixture fixture) : IClassFixture<SessionFixture>, IDisposable
{
    private static long _channels = 800_000_000;

    private readonly List<AssistantHost> _hosts = [];

    [Fact]
    public async Task Pause_stops_the_room_as_the_person_who_asked()
    {
        var host = Host();
        var turn = AssistantPartsTests.Turn(NewChannel());
        await StartFilmAsync(host.Api, turn);

        var said = await host.Tools.For(turn).ExecuteAsync(Call(RoomTools.Pause));

        Assert.StartsWith("Paused at ", said.Summary);
        var state = await host.Api.OpenSessionAsync(turn.SessionId, CancellationToken.None);
        Assert.True(state.Paused);
        Assert.Equal("Haru", state.UpdatedBy?.DisplayName);
    }

    [Fact]
    public async Task A_seek_past_what_has_transcoded_says_where_the_film_actually_went()
    {
        var host = Host();
        var turn = AssistantPartsTests.Turn(NewChannel());
        await host.Api.SetSessionTitleAsync(turn.SessionId, SessionFixture.TranscodingTitle, "someone", CancellationToken.None);

        var said = await host.Tools.For(turn).ExecuteAsync(Call(RoomTools.Seek, ("position", "1:00:00")));

        Assert.Contains("only playable up to 5:00", said.Summary);
    }

    [Fact]
    public async Task Moving_a_room_that_holds_no_film_is_refused_and_conjures_no_room()
    {
        var host = Host();
        var turn = AssistantPartsTests.Turn(NewChannel());

        var said = await host.Tools.For(turn).ExecuteAsync(Call(RoomTools.SeekRelative, ("seconds", "-15")));

        Assert.Contains("No film is loaded", said.Summary);
        Assert.DoesNotContain(await host.Api.ListRoomsAsync(CancellationToken.None), r => r.SessionId == turn.SessionId);
    }

    [Fact]
    public async Task Loading_a_film_by_its_name_puts_it_in_the_room_and_hands_the_card_to_the_chat()
    {
        var host = Host();
        var turn = AssistantPartsTests.Turn(NewChannel());
        var room = host.Tools.For(turn);

        await room.ExecuteAsync(Call(RoomTools.LoadTitle, ("title", "collateral")));

        Assert.Single(room.Launched);
        var state = await host.Api.OpenSessionAsync(turn.SessionId, CancellationToken.None);
        Assert.Equal(SessionFixture.Collateral, state.TitleId);
    }

    [Fact]
    public async Task A_download_of_a_release_nobody_was_shown_is_refused()
    {
        // A torrent id the model writes without having been offered it is invented or copied wrong,
        // and a download of the wrong film is what that costs.
        var host = Host();
        var room = host.Tools.For(AssistantPartsTests.Turn(NewChannel()));

        var said = await room.ExecuteAsync(Call(RoomTools.DownloadFilm, ("torrent_id", "41")));

        Assert.Contains("was not offered by search_tracker", said.Summary);
        Assert.Empty(room.Proposed);
    }

    [Fact]
    public async Task A_download_of_an_offered_release_is_proposed_once_and_nothing_starts()
    {
        var host = Host();
        var room = host.Tools.For(AssistantPartsTests.Turn(NewChannel()));

        await room.ExecuteAsync(Call(RoomTools.SearchTracker, ("query", "Inception")));
        var said = await room.ExecuteAsync(Call(RoomTools.DownloadFilm, ("torrent_id", "41")));
        await room.ExecuteAsync(Call(RoomTools.DownloadFilm, ("torrent_id", "41")));

        Assert.StartsWith("Proposed: download Inception (2010)", said.Summary);
        var proposal = Assert.Single(room.Proposed);
        Assert.Equal(RoomTools.DownloadFilm, proposal.Kind);
        Assert.Equal(4242ul, proposal.AskedBy);
    }

    [Fact]
    public async Task What_the_room_verbs_did_is_replayed_to_the_model_as_the_tool_it_stands_for()
    {
        var host = Host();
        var turn = AssistantPartsTests.Turn(NewChannel());
        await StartFilmAsync(host.Api, turn);
        host.Model.Answers.Enqueue(LlmResponse.Text("Haru paused it."));

        host.Assistant.RecordAct(turn, "pause", RoomTools.Pause, new Dictionary<string, string?>(), "Paused at 0:00.");
        await host.Assistant.AskAsync(turn with { SpeakerName = "Mika" }, "why did it stop?", CancellationToken.None);

        var sent = Assert.Single(host.Model.Requests);
        Assert.Contains(sent, m => m.ToolCalls?.Any(c => c.Name.Name == RoomTools.Pause) == true);
        Assert.Contains(sent, m => m.Role == LlmRole.System && m.Content.Contains("Room Cinema: all-done"));
        Assert.Equal("Mika: why did it stop?", sent[^1].Content);
    }

    [Fact]
    public async Task A_room_that_has_been_quiet_past_the_idle_window_replays_nothing_from_before()
    {
        var host = Host();
        var turn = AssistantPartsTests.Turn(NewChannel());
        Said(host, turn, "download the second one", "I need the IMDb ID.", DateTimeOffset.UtcNow.AddHours(-12));
        host.Model.Answers.Enqueue(LlmResponse.Text("Nothing is loaded."));

        await host.Assistant.AskAsync(turn, "what are we watching?", CancellationToken.None);

        var sent = Assert.Single(host.Model.Requests);
        Assert.DoesNotContain(sent, m => m.Content.Contains("IMDb", StringComparison.Ordinal));
        Assert.Equal(2, host.Conversations.GetHistory(turn.ConversationId).Count(e => e.Kind == ConversationEntryKind.Turn));
    }

    [Fact]
    public async Task A_room_still_talking_replays_what_it_just_said()
    {
        var host = Host();
        var turn = AssistantPartsTests.Turn(NewChannel());
        Said(host, turn, "search for heat", "Heat is in the library.", DateTimeOffset.UtcNow.AddMinutes(-2));
        host.Model.Answers.Enqueue(LlmResponse.Text("Heat is from 1995."));

        await host.Assistant.AskAsync(turn, "what year is it from?", CancellationToken.None);

        var sent = Assert.Single(host.Model.Requests);
        Assert.Contains(sent, m => m.Content.Contains("Heat is in the library.", StringComparison.Ordinal));
    }

    private static void Said(AssistantHost host, RoomTurn turn, string prompt, string reply, DateTimeOffset at) =>
        host.Conversations.AppendTurn(new ConversationTurnRecord
        {
            ConversationId = turn.ConversationId,
            UserDisplay = turn.SpeakerName,
            StartedAt = at,
            CompletedAt = at,
            UserPrompt = prompt,
            SystemPromptHash = "test",
            Tools = [],
            Iterations = 1,
            Outcome = TurnOutcome.Ok,
            Think = false,
            Final = reply,
        });

    [Fact]
    public async Task A_turn_that_proposes_hands_the_proposal_back_for_the_chat_to_post()
    {
        var host = Host();
        var turn = AssistantPartsTests.Turn(NewChannel());
        host.Model.Answers.Enqueue(new LlmResponse(null, [Call(RoomTools.SearchTracker, ("query", "Inception"))]));
        host.Model.Answers.Enqueue(new LlmResponse(null, [Call(RoomTools.DownloadFilm, ("torrent_id", "41"))]));
        host.Model.Answers.Enqueue(LlmResponse.Text("Inception is ready to download once somebody confirms."));

        var answer = await host.Assistant.AskAsync(turn, "download inception", CancellationToken.None);

        Assert.Equal("Inception is ready to download once somebody confirms.", answer.Text);
        Assert.Equal(RoomTools.DownloadFilm, Assert.Single(answer.Proposed).Kind);
    }

    [Fact]
    public async Task A_reply_claiming_an_act_nothing_did_is_asked_again_and_then_corrected()
    {
        var host = Host();
        var turn = AssistantPartsTests.Turn(NewChannel());
        host.Model.Answers.Enqueue(LlmResponse.Text("I have loaded Heat for you."));
        host.Model.Answers.Enqueue(LlmResponse.Text("I have loaded Heat for you."));

        var answer = await host.Assistant.AskAsync(turn, "put heat on", CancellationToken.None);

        Assert.Equal(2, host.Model.Requests.Count);
        Assert.Contains("\"put heat on\"", host.Model.Requests[1][^1].Content);
        Assert.Equal("I have loaded Heat for you." + RoomActionClaim.Check.Correction, answer.Text);
    }

    [Fact]
    public async Task A_proposal_the_reply_never_mentions_is_named_under_it()
    {
        var host = Host();
        var turn = AssistantPartsTests.Turn(NewChannel());
        host.Model.Answers.Enqueue(new LlmResponse(null, [Call(RoomTools.SearchTracker, ("query", "Inception"))]));
        host.Model.Answers.Enqueue(new LlmResponse(null, [Call(RoomTools.DownloadFilm, ("torrent_id", "41"))]));
        host.Model.Answers.Enqueue(LlmResponse.Text("Inception it is."));

        var answer = await host.Assistant.AskAsync(turn, "download inception", CancellationToken.None);

        Assert.Equal("Inception it is." + PendingConfirmationNote.For(1), answer.Text);
    }

    [Fact]
    public async Task A_reply_about_an_act_the_turn_did_is_left_alone()
    {
        var host = Host();
        var turn = AssistantPartsTests.Turn(NewChannel());
        await StartFilmAsync(host.Api, turn);
        host.Model.Answers.Enqueue(new LlmResponse(null, [Call(RoomTools.Pause)]));
        host.Model.Answers.Enqueue(LlmResponse.Text("I've paused it."));

        var answer = await host.Assistant.AskAsync(turn, "pause", CancellationToken.None);

        Assert.Equal("I've paused it.", answer.Text);
        Assert.Equal(2, host.Model.Requests.Count);
    }

    private AssistantHost Host()
    {
        var host = new AssistantHost(fixture);
        _hosts.Add(host);
        return host;
    }

    public void Dispose()
    {
        foreach (var host in _hosts) host.Dispose();
    }

    private static async Task StartFilmAsync(MovieBotApiClient api, RoomTurn turn)
    {
        await api.SetSessionTitleAsync(turn.SessionId, SessionFixture.ReadyTitle, "someone", CancellationToken.None);
        await api.PlayAsync(turn.SessionId, "1", "someone", CancellationToken.None);
    }

    private static ulong NewChannel() => (ulong)Interlocked.Increment(ref _channels);

    internal static LlmToolCall Call(string tool, params (string Key, string Value)[] arguments) =>
        new(new Tool(tool), arguments.ToDictionary(a => a.Key, a => (string?)a.Value));
}
