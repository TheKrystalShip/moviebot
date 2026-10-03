using Microsoft.Extensions.DependencyInjection;
using TheKrystalShip.Llm.Interfaces;
using TheKrystalShip.Llm.Models;
using TheKrystalShip.MovieBot.Assistant;
using TheKrystalShip.MovieBot.Bot.Assistant;
using TheKrystalShip.MovieBot.Client.Assistant;
using Xunit;

namespace TheKrystalShip.MovieBot.Tests;

/// <summary>
/// The bot's side of the seam: its client against moviebot-assistant hosted in-process, as the bot
/// reaches it on hotbox — with the service key, speaking for whoever spoke or pressed.
/// </summary>
public sealed class AssistantClientTests(SessionFixture fixture) : IClassFixture<SessionFixture>, IDisposable
{
    private static long _rooms = 950_000_000;

    private readonly AssistantService _service = new(fixture);

    public void Dispose() => _service.Dispose();

    private AssistantClient Client()
    {
        var http = _service.CreateClient();
        http.DefaultRequestHeaders.Add(Callers.ServiceHeader, SessionFixture.ServiceKey);
        return new AssistantClient(http);
    }

    private static TurnRoom NewRoom()
    {
        var id = Interlocked.Increment(ref _rooms).ToString(System.Globalization.CultureInfo.InvariantCulture);
        return new TurnRoom(id, "Cinema", id);
    }

    [Fact]
    public async Task A_turn_in_a_room_puts_the_film_on_and_hands_back_the_launch()
    {
        var room = NewRoom();
        _service.Model.Answers.Enqueue(new LlmResponse(null, [RoomToolsTests.Call(RoomTools.WatchFilm, ("title", "collateral"))]));
        _service.Model.Answers.Enqueue(LlmResponse.Text("Enjoy."));

        var reply = await Client().TurnAsync(room, new Speaker(4242, "Haru"), "put collateral on", CancellationToken.None);

        var launch = Assert.Single(reply.Launches);
        Assert.Equal(room.SessionId, launch.SessionId);
        Assert.Equal("Haru", launch.RequestedBy);
        Assert.Equal(SessionFixture.Collateral,
            (await new MovieBotApiClient(fixture.CreateServiceClient()).OpenSessionAsync(room.SessionId, CancellationToken.None)).TitleId);
    }

    /// <summary>A Discord name is anything at all, and a header is ASCII; the name arrives as it was.</summary>
    [Fact]
    public async Task A_name_no_header_could_carry_arrives_as_it_was_written()
    {
        var room = NewRoom();
        _service.Model.Answers.Enqueue(LlmResponse.Text("Hello."));

        await Client().TurnAsync(room, new Speaker(4242, "Zoë | 映画"), "hello", CancellationToken.None);

        var store = _service.Services.GetRequiredService<IConversationStore>();
        var turn = Assert.Single(store.GetHistory(AssistantTurn.RoomConversation(room.SessionId))).Turn!;
        Assert.Equal("Zoë | 映画", turn.UserDisplay);
    }

    [Fact]
    public async Task Anybody_in_the_room_may_agree_once_and_the_act_is_the_asker_s()
    {
        var room = NewRoom();
        _service.Model.Answers.Enqueue(new LlmResponse(null, [RoomToolsTests.Call(RoomTools.AddWish, ("imdb_id", "tt0449088"))]));
        _service.Model.Answers.Enqueue(LlmResponse.Text("That needs confirming."));
        var client = Client();

        var reply = await client.TurnAsync(room, new Speaker(6161, "Haru"), "tell me when at worlds end is out", CancellationToken.None);
        var proposal = Assert.Single(reply.Proposals);
        Assert.Equal("Haru", proposal.AskedBy);
        Assert.Contains("At World's End", proposal.Describes);

        var agreed = await client.ConfirmAsync(proposal.Token, new Speaker(77, "Bea"), CancellationToken.None);
        Assert.Equal("subscribed", agreed?.Result?.Wish?.Outcome);
        Assert.Equal("6161", agreed!.Result!.Wish!.Wish!.Subscribers.Single(s => s.UserId == "6161").UserId);

        Assert.Null(await client.ConfirmAsync(proposal.Token, new Speaker(77, "Bea"), CancellationToken.None));

        await fixture.CreateServiceClient().DeleteAsync("/api/wishes/tt0449088?userId=6161");
    }

    [Fact]
    public async Task A_room_verb_is_recorded_as_the_tool_it_stands_for()
    {
        var room = NewRoom();

        await Client().RecordAsync(
            new RecordRequest(room, "pause", RoomVerbTools.Pause, new Dictionary<string, string?>(), "Paused at 0:10."),
            new Speaker(4242, "Haru"), CancellationToken.None);

        var store = _service.Services.GetRequiredService<IConversationStore>();
        Assert.Equal(RoomVerbTools.Pause,
            Assert.Single(store.GetHistory(AssistantTurn.RoomConversation(room.SessionId))).Turn!.Tools[0].Name.Name);
    }

    [Fact]
    public async Task An_assistant_that_is_not_there_is_said_rather_than_thrown_through()
    {
        var client = new AssistantClient(new HttpClient { BaseAddress = new Uri("http://127.0.0.1:1/") });

        await Assert.ThrowsAsync<AssistantUnavailableException>(() =>
            client.TurnAsync(NewRoom(), new Speaker(1, "Haru"), "hello", CancellationToken.None));
    }
}
