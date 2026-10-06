using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using TheKrystalShip.Llm.Interfaces;
using TheKrystalShip.Llm.Models;
using TheKrystalShip.MovieBot.Acquire.Imdb;
using TheKrystalShip.MovieBot.Assistant;
using TheKrystalShip.MovieBot.Client.Assistant;
using TheKrystalShip.MovieBot.Core;
using Xunit;

namespace TheKrystalShip.MovieBot.Tests;

/// <summary>
/// moviebot-assistant hosted in-process against the in-process API, with the model scripted. It trusts
/// the API's test issuer, because a person's token is one the provider minted for Cinema, and it
/// reaches the API with no credential of its own: every call carries the person's token.
/// </summary>
internal sealed class AssistantService(SessionFixture api) : WebApplicationFactory<TheKrystalShip.MovieBot.Assistant.Program>
{
    private readonly string _state = Path.Combine(
        Path.GetTempPath(), "moviebot-tests", "assistant-service-" + Guid.NewGuid().ToString("N"));

    public AssistantHost.ScriptedModel Model => (AssistantHost.ScriptedModel)Services.GetRequiredService<ILlmClient>();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        Directory.CreateDirectory(_state);
        builder.ConfigureAppConfiguration((_, config) =>
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Oidc:Issuer"] = TestIssuer.Issuer,
                ["Assistant:DatabasePath"] = Path.Combine(_state, "assistant.db"),
                ["Llm:Provider"] = "LlamaCpp",
                ["Llm:Endpoint"] = "http://127.0.0.1:1",
            }));

        builder.ConfigureServices(services =>
        {
            services.RemoveAll<IHostedService>();
            services.RemoveAll<MovieBotApiClient>();
            services.AddSingleton(new MovieBotApiClient(api.CreateClient()));
            services.Configure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, o => o.BackchannelHttpHandler = api.Issuer.Handler());
            services.AddHttpClient<ImdbClient>().ConfigurePrimaryHttpMessageHandler(() => new AssistantHost.CatalogueStub());
            services.RemoveAll<ILlmClient>();
            services.AddSingleton<ILlmClient, AssistantHost.ScriptedModel>();
        });
    }

    /// <summary>A person on the site, carrying the token the provider minted for them: every Cinema action unless named.</summary>
    public HttpClient As(string userId, string name, IEnumerable<string>? actions = null)
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", api.TokenFor(userId, name, actions));
        return client;
    }

    /// <summary>The bot, relaying somebody in a voice channel with the token it exchanged for them.</summary>
    public HttpClient Bot(string userId, string name, IEnumerable<string>? actions = null)
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", api.TokenFor(userId, name, actions, via: "cinema-bot"));
        return client;
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        if (disposing && Directory.Exists(_state)) Directory.Delete(_state, recursive: true);
    }
}

public sealed class AssistantServiceTests(SessionFixture fixture) : IClassFixture<SessionFixture>, IDisposable
{
    private static long _rooms = 900_000_000;

    private readonly AssistantService _service = new(fixture);

    public void Dispose() => _service.Dispose();

    [Fact]
    public async Task Nobody_without_a_token_or_the_key_is_answered()
    {
        using var response = await _service.CreateClient().PostAsJsonAsync("/turn", new { prompt = "hello" });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Somebody_who_may_not_watch_is_not_answered()
    {
        using var response = await _service.As("4242", "Haru", TestIssuer.SelfOnly).PostAsJsonAsync("/turn", new { prompt = "hello" });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task A_token_for_another_audience_is_not_answered()
    {
        var client = _service.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", fixture.Issuer.Mint("4242", "Haru", audience: "a-kgsm-cluster"));

        using var response = await client.PostAsJsonAsync("/turn", new { prompt = "hello" });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    /// <summary>
    /// The tools call the API as the person, so what they may not do through the site they may not do
    /// through the assistant: a person who may only watch is offered no download.
    /// </summary>
    [Fact]
    public async Task The_tools_act_with_the_person_s_own_token()
    {
        _service.Model.Answers.Enqueue(new LlmResponse(null, [RoomToolsTests.Call(RoomTools.WatchFilm, ("title", "inception"))]));
        _service.Model.Answers.Enqueue(LlmResponse.Text("Sorry."));

        var reply = await (await _service.As("4343", "Vi", [Api.Auth.CinemaActions.Watch])
                .PostAsJsonAsync("/turn", new { prompt = "download inception" }))
            .Content.ReadFromJsonAsync(AssistantJsonContext.Default.TurnReply);

        Assert.Empty(reply!.Proposals);
    }

    [Fact]
    public async Task A_person_s_turn_answers_whole_and_lands_in_their_own_conversation()
    {
        _service.Model.Answers.Enqueue(LlmResponse.Text("Hello, Haru."));
        var client = _service.As("4242", "Haru");

        var reply = await (await client.PostAsJsonAsync("/turn", new { prompt = "hello", conversationId = "chat-1" }))
            .Content.ReadFromJsonAsync(AssistantJsonContext.Default.TurnReply);

        Assert.Equal("Hello, Haru.", reply?.Text);
        Assert.True(reply!.TurnId > 0);

        var listed = await client.GetFromJsonAsync<JsonElement>("/conversations");
        Assert.Equal("chat-1", Assert.Single(listed.EnumerateArray()).GetProperty("id").GetString());

        var detail = await client.GetFromJsonAsync<JsonElement>("/conversations/chat-1");
        var entry = Assert.Single(detail.GetProperty("entries").EnumerateArray());
        Assert.Equal("turn", entry.GetProperty("kind").GetString());
        Assert.Equal("hello", entry.GetProperty("turn").GetProperty("prompt").GetString());
        Assert.Equal("Hello, Haru.", entry.GetProperty("turn").GetProperty("final").GetString());

        // Somebody else signed in sees none of it.
        var other = await _service.As("77", "Bea").GetFromJsonAsync<JsonElement>("/conversations");
        Assert.Equal(0, other.GetArrayLength());
    }

    [Fact]
    public async Task A_streamed_turn_sends_its_steps_as_they_happen_and_ends_once()
    {
        _service.Model.Answers.Enqueue(new LlmResponse(null, [RoomToolsTests.Call(RoomTools.WatchFilm, ("title", "heat"))]));
        _service.Model.Answers.Enqueue(LlmResponse.Text("Here it is."));
        var client = _service.As("4242", "Haru");

        var frames = await StreamAsync(client, "/turn", new { prompt = "show me heat" });

        Assert.Equal(["turn.attach", "tool.start", "tool.result", "launch", "text.delta", "done"], frames.Select(f => f.Type));
        var launch = frames.Single(f => f.Type == "launch").Data;
        Assert.Equal(SessionFixture.Heat, launch.GetProperty("titleId").GetString());
        Assert.False(launch.TryGetProperty("sessionId", out _));

        // The film is the tool's card too, which is how a chat draws the way into it — live, and
        // read back from the conversation's history.
        var card = frames.Single(f => f.Type == "tool.result").Data.GetProperty("result");
        Assert.Equal(RoomTools.WatchFilm, card.GetProperty("tool").GetString());
        Assert.Equal(SessionFixture.Heat, card.GetProperty("data").GetProperty("titleId").GetString());

        var history = await client.GetFromJsonAsync<JsonElement>("/conversations/main");
        var stored = Assert.Single(Assert.Single(history.GetProperty("entries").EnumerateArray())
            .GetProperty("turn").GetProperty("tools").EnumerateArray()).GetProperty("result");
        Assert.Equal(SessionFixture.Heat, stored.GetProperty("data").GetProperty("titleId").GetString());
    }

    [Fact]
    public async Task A_room_on_a_person_s_turn_is_ignored_and_one_from_the_bot_is_used()
    {
        var session = Interlocked.Increment(ref _rooms).ToString(System.Globalization.CultureInfo.InvariantCulture);
        var room = new { sessionId = session, name = "Cinema", channelId = session };

        _service.Model.Answers.Enqueue(new LlmResponse(null, [RoomToolsTests.Call(RoomTools.WatchFilm, ("title", "collateral"))]));
        _service.Model.Answers.Enqueue(LlmResponse.Text("Done."));
        await _service.As("4242", "Haru").PostAsJsonAsync("/turn", new { prompt = "put collateral on", room });
        Assert.DoesNotContain(await fixture.CreateServiceClient().GetFromJsonAsync(
            "/api/sessions", Core.ManifestJsonContext.Default.IReadOnlyListRoomSummary) ?? [], r => r.SessionId == session);

        _service.Model.Answers.Enqueue(new LlmResponse(null, [RoomToolsTests.Call(RoomTools.WatchFilm, ("title", "collateral"))]));
        _service.Model.Answers.Enqueue(LlmResponse.Text("Done."));
        var reply = await (await _service.Bot("4242", "Haru").PostAsJsonAsync("/turn", new { prompt = "put collateral on", room }))
            .Content.ReadFromJsonAsync(AssistantJsonContext.Default.TurnReply);

        Assert.Equal(session, Assert.Single(reply!.Launches).SessionId);
    }

    [Fact]
    public async Task A_proposal_is_confirmed_once_and_answers_with_what_happened()
    {
        _service.Model.Answers.Enqueue(new LlmResponse(null, [RoomToolsTests.Call(RoomTools.AddWish, ("imdb_id", "tt0449088"))]));
        _service.Model.Answers.Enqueue(LlmResponse.Text("That needs confirming."));
        var haru = _service.As("5151", "Haru");

        var reply = await (await haru.PostAsJsonAsync("/turn", new { prompt = "tell me when at worlds end is out" }))
            .Content.ReadFromJsonAsync(AssistantJsonContext.Default.TurnReply);
        var token = Assert.Single(reply!.Proposals).Token;

        // Somebody else cannot answer what Haru was offered, and trying spends nothing.
        using var stranger = await _service.As("77", "Bea").PostAsJsonAsync("/confirm", new { token });
        Assert.Equal(HttpStatusCode.NotFound, stranger.StatusCode);

        var confirmed = await (await haru.PostAsJsonAsync("/confirm", new { token }))
            .Content.ReadFromJsonAsync(AssistantJsonContext.Default.ConfirmReply);
        Assert.True(confirmed!.Success, confirmed.Text);
        Assert.Equal("accepted", confirmed.Outcome.Verdict);
        Assert.Equal("wish", confirmed.Outcome.Verb);
        Assert.Equal("subscribed", confirmed.Result?.Wish?.Outcome);

        using var again = await haru.PostAsJsonAsync("/confirm", new { token });
        Assert.Equal(HttpStatusCode.NotFound, again.StatusCode);

        await fixture.CreatePersonClient("5151", "Haru").DeleteAsync("/api/wishes/tt0449088");
    }

    [Fact]
    public async Task A_room_verb_the_bot_carried_out_is_in_the_room_s_conversation()
    {
        var session = Interlocked.Increment(ref _rooms).ToString(System.Globalization.CultureInfo.InvariantCulture);

        using var response = await _service.Bot("4242", "Haru").PostAsJsonAsync("/rooms/record", new
        {
            room = new { sessionId = session, name = "Cinema" },
            said = "pause",
            tool = RoomTools.Pause,
            arguments = new Dictionary<string, string?>(),
            outcome = "Paused at 0:10.",
        });

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var store = _service.Services.GetRequiredService<IConversationStore>();
        Assert.Equal(RoomTools.Pause, Assert.Single(store.GetHistory(AssistantTurn.RoomConversation(session))).Turn!.Tools[0].Name.Name);
    }

    [Fact]
    public async Task A_person_cannot_write_into_a_room()
    {
        using var response = await _service.As("4242", "Haru").PostAsJsonAsync("/rooms/record", new
        {
            room = new { sessionId = "1", name = "Cinema" }, said = "pause", tool = RoomTools.Pause,
            arguments = new Dictionary<string, string?>(), outcome = "Paused.",
        });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private sealed record SseFrame(string Type, JsonElement Data);

    private static async Task<List<SseFrame>> StreamAsync(HttpClient client, string path, object body)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = JsonContent.Create(body) };
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
        response.EnsureSuccessStatusCode();

        var frames = new List<SseFrame>();
        using var reader = new StreamReader(await response.Content.ReadAsStreamAsync());
        while (await reader.ReadLineAsync() is { } line)
        {
            if (!line.StartsWith("data: ", StringComparison.Ordinal)) continue;
            var data = JsonDocument.Parse(line["data: ".Length..]).RootElement.Clone();
            var type = data.GetProperty("type").GetString()!;
            frames.Add(new SseFrame(type, data));
            if (type is "done" or "error") break;
        }

        return frames;
    }
}
