using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using TheKrystalShip.Agent.Confirmations;
using TheKrystalShip.Agent.Service;
using TheKrystalShip.Agent.Service.Push;
using TheKrystalShip.Llm.Interfaces;
using TheKrystalShip.Llm.Models;
using TheKrystalShip.MovieBot.Assistant;
using TheKrystalShip.MovieBot.Client.Assistant;
using Xunit;

namespace TheKrystalShip.MovieBot.Tests;

/// <summary>
/// What every TheKrystalShip assistant offers around the conversation, as this one serves it over its
/// own callers: the commands, a person's memory, and notification devices. The routes are the shared
/// package's; what these hold is this service's composition of them — which commands, which
/// conversation a room's command addresses, that a room remembers nothing, and that a notification can
/// cancel a proposal but not agree to it.
/// </summary>
public sealed class AssistantBaselineTests(SessionFixture fixture) : IClassFixture<SessionFixture>, IDisposable
{
    private static long _rooms = 950_000_000;

    private readonly AssistantService _service = new(fixture);

    public void Dispose() => _service.Dispose();

    private IConversationStore Conversations => _service.Services.GetRequiredService<IConversationStore>();

    private IMemoryStore Memories => _service.Services.GetRequiredService<IMemoryStore>();

    private static string Room() => Interlocked.Increment(ref _rooms).ToString(System.Globalization.CultureInfo.InvariantCulture);

    private HttpClient BotIn(string session, string userId = "4242", string name = "Haru")
    {
        var client = _service.Bot(userId, name);
        client.DefaultRequestHeaders.Add(CinemaCallers.RoomHeader, session);
        return client;
    }

    private static async Task<JsonElement> BodyOf(HttpResponseMessage response) =>
        await response.Content.ReadFromJsonAsync<JsonElement>();

    // ── Commands ─────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task The_commands_are_the_baseline_without_think_and_each_needs_watching()
    {
        var listed = await BodyOf(await _service.As("4242", "Haru").GetAsync("/commands"));

        Assert.Equal(["help", "tools", "compact", "new", "memory"],
            listed.EnumerateArray().Select(c => c.GetProperty("name").GetString()));
        Assert.All(listed.EnumerateArray(), c => Assert.Equal(Callers.Watch, c.GetProperty("action").GetString()));
    }

    [Fact]
    public async Task Help_lists_the_commands_and_tools_lists_what_a_person_s_chat_is_offered()
    {
        var client = _service.As("4242", "Haru");

        var help = await BodyOf(await client.PostAsJsonAsync("/commands/help", new { }));
        Assert.Equal(5, help.GetProperty("commands").GetArrayLength());

        var tools = (await BodyOf(await client.PostAsJsonAsync("/commands/tools", new { })))
            .GetProperty("tools").EnumerateArray().Select(t => t.GetProperty("name").GetString()).ToList();
        Assert.Contains(RoomTools.WatchFilm, tools);
        Assert.Contains(RoomTools.Remember, tools);
        Assert.DoesNotContain(RoomTools.Pause, tools);
    }

    [Fact]
    public async Task New_for_a_person_starts_the_chat_they_offered_and_answers_its_row()
    {
        var reply = await BodyOf(await _service.As("4242", "Haru").PostAsJsonAsync("/commands/new", new { conversationId = "fresh-1" }));

        Assert.Equal("fresh-1", reply.GetProperty("conversationId").GetString());
        Assert.Equal("fresh-1", reply.GetProperty("conversation").GetProperty("id").GetString());
        Assert.Contains(Conversations.ListConversations(AssistantTurn.PersonScope("4242")),
            c => c.ConversationId == AssistantTurn.PersonConversation("4242", "fresh-1"));
    }

    [Fact]
    public async Task New_in_a_room_starts_the_room_s_conversation_over()
    {
        var session = Room();
        await BotIn(session).PostAsJsonAsync("/rooms/record", new
        {
            room = new { sessionId = session, name = "Cinema" }, said = "pause", tool = RoomTools.Pause,
            arguments = new Dictionary<string, string?>(), outcome = "Paused at 0:10.",
        });
        Assert.NotEmpty(Conversations.GetModelContext(AssistantTurn.RoomConversation(session)));

        using var response = await BotIn(session).PostAsJsonAsync("/commands/new", new { });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Empty(Conversations.GetModelContext(AssistantTurn.RoomConversation(session)));
        Assert.Single(Conversations.GetHistory(AssistantTurn.RoomConversation(session)), e => e.Turn is not null);
    }

    [Fact]
    public async Task A_room_named_by_a_person_rather_than_a_relaying_client_is_ignored()
    {
        var session = Room();
        var client = _service.As("4242", "Haru");
        client.DefaultRequestHeaders.Add(CinemaCallers.RoomHeader, session);

        var reply = await BodyOf(await client.PostAsJsonAsync("/commands/new", new { conversationId = "mine" }));

        Assert.Equal("mine", reply.GetProperty("conversationId").GetString());
        Assert.Empty(Conversations.GetHistory(AssistantTurn.RoomConversation(session)));
    }

    // ── Memory ───────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task A_person_writes_corrects_and_drops_a_memory_by_hand()
    {
        var client = _service.As("6161", "Ines");

        using var written = await client.PutAsJsonAsync("/memories/favourite-genre", new { summary = "Likes heist films." });
        Assert.Equal(HttpStatusCode.OK, written.StatusCode);
        await client.PutAsJsonAsync("/memories/favourite-genre", new { summary = "Likes heist films and westerns." });

        var listed = await BodyOf(await client.GetAsync("/memories"));
        var memory = Assert.Single(listed.EnumerateArray());
        Assert.Equal("Likes heist films and westerns.", memory.GetProperty("summary").GetString());
        Assert.Equal("you", memory.GetProperty("source").GetString());
        Assert.NotNull(Memories.Get(AssistantTurn.PersonScope("6161"), "favourite-genre"));

        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync("/memories/favourite-genre")).StatusCode);
        Assert.Empty(Memories.List(AssistantTurn.PersonScope("6161")));
    }

    [Fact]
    public async Task A_turn_on_a_person_s_chat_remembers_and_the_next_one_is_told()
    {
        _service.Model.Answers.Enqueue(new LlmResponse(null,
            [RoomToolsTests.Call(RoomTools.Remember, ("key", "favourite-genre"), ("summary", "Likes heist films."))]));
        _service.Model.Answers.Enqueue(LlmResponse.Text(""));
        var client = _service.As("6262", "Ola");

        var reply = await (await client.PostAsJsonAsync("/turn", new { prompt = "remember that I like heist films" }))
            .Content.ReadFromJsonAsync(AssistantJsonContext.Default.TurnReply);

        Assert.Equal("I'll remember that.", reply!.Text);
        Assert.Equal("Likes heist films.", Memories.Get(AssistantTurn.PersonScope("6262"), "favourite-genre")?.Summary);

        _service.Model.Answers.Enqueue(LlmResponse.Text("Heat."));
        await client.PostAsJsonAsync("/turn", new { prompt = "what should I watch" });
        Assert.Contains(_service.Model.Requests[^1], m => m.Content?.Contains("- favourite-genre: Likes heist films.") == true);
    }

    [Fact]
    public async Task A_room_remembers_nothing()
    {
        var session = Room();
        var room = new { sessionId = session, name = "Cinema", channelId = session };

        _service.Model.Answers.Enqueue(new LlmResponse(null,
            [RoomToolsTests.Call(RoomTools.Remember, ("key", "house-rule"), ("summary", "No spoilers."))]));
        _service.Model.Answers.Enqueue(LlmResponse.Text("Noted."));
        await _service.Bot("4242", "Haru").PostAsJsonAsync("/turn", new { prompt = "remember no spoilers", room });

        Assert.DoesNotContain(_service.Model.Offered[^1], t => RoomTools.Memories.Contains(t.Name));
        Assert.Empty(Memories.List(AssistantTurn.RoomConversation(session)));
        Assert.Empty(Memories.List(""));

        var memory = await BodyOf(await BotIn(session).PostAsJsonAsync("/commands/memory", new { }));
        Assert.Equal("Nothing is remembered in a conversation several people share. Only your own chats keep memories.",
            memory.GetProperty("message").GetString());
    }

    // ── Notifications ────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task A_browser_registers_against_the_assistant_s_own_key()
    {
        var client = _service.As("6363", "Pau");

        var key = await BodyOf(await client.GetAsync("/push/key"));
        Assert.False(string.IsNullOrEmpty(key.GetProperty("publicKey").GetString()));
        Assert.True(key.GetProperty("enabled").GetBoolean());

        using var subscribed = await client.PostAsJsonAsync("/push/subscribe",
            new { endpoint = "https://push.example/device-1", p256dh = PushKeys.P256dh, auth = PushKeys.Auth });
        Assert.Equal(HttpStatusCode.NoContent, subscribed.StatusCode);

        var devices = await BodyOf(await client.GetAsync("/push/devices"));
        Assert.Equal("https://push.example/device-1", Assert.Single(devices.GetProperty("endpoints").EnumerateArray()).GetString());
    }

    [Fact]
    public async Task A_notification_cannot_agree_to_a_proposal_but_can_cancel_it()
    {
        _service.Model.Answers.Enqueue(new LlmResponse(null, [RoomToolsTests.Call(RoomTools.AddWish, ("imdb_id", "tt0449088"))]));
        _service.Model.Answers.Enqueue(LlmResponse.Text("That needs confirming."));
        var reply = await (await _service.As("6464", "Rui").PostAsJsonAsync("/turn", new { prompt = "tell me when at worlds end is out" }))
            .Content.ReadFromJsonAsync(AssistantJsonContext.Default.TurnReply);
        var token = Assert.Single(reply!.Proposals).Token;

        var host = _service.Services.GetRequiredService<IConfirmationPushHost<StagedAction>>();
        var waiting = Assert.Single(host.Unannounced(), w => w.Handle == token);
        Assert.Equal(new ConfirmationStager(CinemaCallers.Provider, "6464", "Rui"), waiting.Stager);
        Assert.Equal("Get notified about", ConfirmationNotice.Title(host.Describe(waiting.Action))[..18]);

        var buttons = _service.Services.GetRequiredService<IPushActionStore>();
        var confirm = buttons.Mint(PushActionVerb.Confirm, token, waiting.Stager, "https://push.example/d", waiting.ExpiresAt);
        var tapped = await (await _service.CreateClient().PostAsync($"/push/actions/{confirm}", null))
            .Content.ReadFromJsonAsync<PushActionResponse>();
        Assert.Equal(new PushActionResponse(false, CinemaConfirmationPushHost.ConfirmUnavailable), tapped);
        Assert.Contains(_service.Services.GetRequiredService<StagedActions>().Unannounced(), a => a.Token == token);

        var cancel = buttons.Mint(PushActionVerb.Cancel, token, waiting.Stager, "https://push.example/d", waiting.ExpiresAt);
        var cancelled = await (await _service.CreateClient().PostAsync($"/push/actions/{cancel}", null))
            .Content.ReadFromJsonAsync<PushActionResponse>();
        Assert.Equal(new PushActionResponse(true, "Cancelled."), cancelled);

        using var confirmedInChat = await _service.As("6464", "Rui").PostAsJsonAsync("/confirm", new { token });
        Assert.Equal(HttpStatusCode.NotFound, confirmedInChat.StatusCode);
    }

    [Fact]
    public async Task A_room_s_proposals_are_not_announced()
    {
        var session = Room();
        var room = new { sessionId = session, name = "Cinema", channelId = session };
        _service.Model.Answers.Enqueue(new LlmResponse(null, [RoomToolsTests.Call(RoomTools.AddWish, ("imdb_id", "tt0449088"))]));
        _service.Model.Answers.Enqueue(LlmResponse.Text("That needs confirming."));

        var reply = await (await _service.Bot("4242", "Haru").PostAsJsonAsync("/turn", new { prompt = "tell us when at worlds end is out", room }))
            .Content.ReadFromJsonAsync(AssistantJsonContext.Default.TurnReply);

        var token = Assert.Single(reply!.Proposals).Token;
        Assert.DoesNotContain(_service.Services.GetRequiredService<IConfirmationPushHost<StagedAction>>().Unannounced(), w => w.Handle == token);
    }

    /// <summary>A browser's subscription keys: a P-256 point and a 16-byte secret, base64url.</summary>
    private static class PushKeys
    {
        public const string P256dh = "BNcRdreALRFXTkOOUHK1EtK2wtaz5Ry4YfYCA_0QTpQtUbVlUls0VJXg7A8u-Ts1XbjhazAkj7I99e8QcYP7DkM";
        public const string Auth = "tBHItJI5svbpez7KI4CCXg";
    }
}
