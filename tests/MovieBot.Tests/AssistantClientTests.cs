using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TheKrystalShip.Llm.Interfaces;
using TheKrystalShip.Llm.Models;
using TheKrystalShip.MovieBot.Assistant;
using TheKrystalShip.MovieBot.Bot.Api;
using TheKrystalShip.MovieBot.Bot.Assistant;
using TheKrystalShip.MovieBot.Client.Assistant;
using TheKrystalShip.MovieBot.Core;
using Xunit;

namespace TheKrystalShip.MovieBot.Tests;

/// <summary>
/// The bot's side of the seam: its client against moviebot-assistant hosted in-process, as the bot
/// reaches it on hotbox — with the token it exchanged for whoever spoke or pressed.
/// </summary>
public sealed class AssistantClientTests : IClassFixture<SessionFixture>, IDisposable
{
    private static long _rooms = 950_000_000;

    /// <summary>Who the provider says each Discord account is.</summary>
    private static readonly Dictionary<string, string> Linked = new()
    {
        ["4242"] = "Haru", ["4243"] = "Zoë | 映画", ["6161"] = "Haru", ["77"] = "Bea", ["1"] = "Haru",
    };

    private readonly SessionFixture _fixture;
    private readonly AssistantService _service;

    public AssistantClientTests(SessionFixture fixture)
    {
        _fixture = fixture;
        _service = new AssistantService(fixture);

        // The provider exchanges the bot's naming of a Discord user for that person's token, with the bot
        // as the client acting for them.
        fixture.Issuer.Exchange = form =>
            form["subject_token_type"] == TokenExchangeTypes.DiscordUserId && Linked.TryGetValue(form["subject_token"], out var name)
                ? (HttpStatusCode.OK, fixture.Issuer.Minted(fixture.Issuer.Mint("acct-" + form["subject_token"], name, actor: "cinema-bot")))
                : (HttpStatusCode.BadRequest, """{"error":"invalid_grant","account_status":"pending"}""");
    }

    public void Dispose() => _service.Dispose();

    private AssistantClient Client()
    {
        var tokens = new PersonTokens(
            new TokenExchangeClient(new HttpClient(_fixture.Issuer.Handler()), TimeProvider.System),
            Options.Create(new BotOidcOptions { Issuer = TestIssuer.Issuer, BotClientSecret = "a-test-bot-secret" }),
            TimeProvider.System, NullLogger<PersonTokens>.Instance);
        var http = new HttpClient(new PersonAuthorization(tokens, serviceKey: null) { InnerHandler = _service.Server.CreateHandler() })
        {
            BaseAddress = _service.Server.BaseAddress,
        };
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

        var state = (await new MovieBotApiClient(_fixture.CreateServiceClient()).OpenSessionAsync(room.SessionId, CancellationToken.None));
        Assert.Equal(SessionFixture.Collateral, state.TitleId);
        Assert.Equal(new Actor("acct-4242", "Haru", "cinema-bot"), state.UpdatedBy);
    }

    /// <summary>A Discord name is anything at all; the person's name arrives in their token, as it was written.</summary>
    [Fact]
    public async Task A_name_arrives_as_it_was_written()
    {
        var room = NewRoom();
        _service.Model.Answers.Enqueue(LlmResponse.Text("Hello."));

        await Client().TurnAsync(room, new Speaker(4243, "Zoë | 映画"), "hello", CancellationToken.None);

        var store = _service.Services.GetRequiredService<IConversationStore>();
        var turn = Assert.Single(store.GetHistory(AssistantTurn.RoomConversation(room.SessionId))).Turn!;
        Assert.Equal("Zoë | 映画", turn.UserDisplay);
    }

    [Fact]
    public async Task Anybody_in_the_room_may_agree_once_and_the_act_is_theirs()
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
        Assert.Equal("acct-77", Assert.Single(agreed!.Result!.Wish!.Wish!.Subscribers).UserId);

        Assert.Null(await client.ConfirmAsync(proposal.Token, new Speaker(77, "Bea"), CancellationToken.None));

        await _fixture.CreatePersonClient("acct-77", "Bea").DeleteAsync("/api/wishes/tt0449088");
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
    public async Task A_discord_account_no_approved_account_holds_is_told_so()
    {
        var refused = await Assert.ThrowsAsync<AccountRefusedException>(() =>
            Client().TurnAsync(NewRoom(), new Speaker(999, "Nobody"), "hello", CancellationToken.None));

        Assert.Equal("pending", refused.AccountStatus);
        Assert.Contains("approve", refused.Sentence);
    }

    [Fact]
    public async Task An_assistant_that_is_not_there_is_said_rather_than_thrown_through()
    {
        var client = new AssistantClient(new HttpClient { BaseAddress = new Uri("http://127.0.0.1:1/") });

        await Assert.ThrowsAsync<AssistantUnavailableException>(() =>
            client.TurnAsync(NewRoom(), new Speaker(1, "Haru"), "hello", CancellationToken.None));
    }
}
