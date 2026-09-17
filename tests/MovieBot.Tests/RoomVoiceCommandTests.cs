using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TheKrystalShip.Discord.Voice;
using TheKrystalShip.MovieBot.Bot.Api;
using TheKrystalShip.MovieBot.Bot.Assistant;
using TheKrystalShip.MovieBot.Bot.Sessions;
using TheKrystalShip.MovieBot.Bot.Voice;
using Xunit;

namespace TheKrystalShip.MovieBot.Tests;

/// <summary>
/// Something said in a voice channel, all the way to the room — against the running API, because
/// what is worth catching is the seam: the right room, the right person, and no room conjured out of
/// a channel that is watching nothing.
/// </summary>
public sealed class RoomVoiceCommandTests(SessionFixture fixture) : IClassFixture<SessionFixture>
{
    private static long _channels = 900_000_000;

    [Fact]
    public async Task Saying_pause_in_a_room_watching_a_film_pauses_it_as_the_speaker()
    {
        var (api, handler) = Create();
        var channel = NewChannel();
        await StartFilmAsync(api, channel);

        var outcome = await handler.HandleAsync(Said(channel, "pause"));

        Assert.Equal(VoiceOutcome.Acted, outcome);
        var state = await api.OpenSessionAsync(RoomSession.IdFor(channel), CancellationToken.None);
        Assert.True(state.Paused);
        Assert.Equal("Haru", state.UpdatedBy?.DisplayName);
        Assert.Equal("4242", state.UpdatedBy?.UserId);
    }

    [Fact]
    public async Task Saying_back_fifteen_moves_the_room_back_from_where_it_is()
    {
        var (api, handler) = Create();
        var channel = NewChannel();
        await StartFilmAsync(api, channel);
        await handler.HandleAsync(Said(channel, "pause"));
        await fixture.CreateServiceClient().PostAsync(
            $"/api/sessions/{RoomSession.IdFor(channel)}/seek",
            System.Net.Http.Json.JsonContent.Create(new { toSeconds = 600.0 }));

        var outcome = await handler.HandleAsync(Said(channel, "go back fifteen seconds"));

        Assert.Equal(VoiceOutcome.Acted, outcome);
        var state = await api.OpenSessionAsync(RoomSession.IdFor(channel), CancellationToken.None);
        Assert.Equal(585, state.PositionSeconds, 3);
    }

    [Fact]
    public async Task A_verb_in_a_channel_watching_nothing_touches_no_room()
    {
        // Every write creates a room. Acting here would leave an empty one with the speaker's name on
        // its last change, in a channel nobody is watching anything in.
        var (api, handler) = Create();
        var channel = NewChannel();

        var outcome = await handler.HandleAsync(Said(channel, "pause"));

        Assert.Equal(VoiceOutcome.NoFilm, outcome);
        var rooms = await api.ListRoomsAsync(CancellationToken.None);
        Assert.DoesNotContain(rooms, r => r.SessionId == RoomSession.IdFor(channel));
    }

    [Fact]
    public async Task Speech_that_is_not_a_room_verb_leaves_the_room_as_it_was()
    {
        var (api, handler) = Create();
        var channel = NewChannel();
        await StartFilmAsync(api, channel);
        var before = await api.OpenSessionAsync(RoomSession.IdFor(channel), CancellationToken.None);

        var outcome = await handler.HandleAsync(Said(channel, "should we pause?"));

        Assert.Equal(VoiceOutcome.NotHandled, outcome);
        var after = await api.OpenSessionAsync(RoomSession.IdFor(channel), CancellationToken.None);
        Assert.Equal(before.Revision, after.Revision);
    }

    [Fact]
    public async Task Speech_that_is_not_a_room_verb_goes_to_the_assistant_when_there_is_one()
    {
        var assistant = new RecordingAssistant();
        var (api, handler) = Create(assistant);
        var channel = NewChannel();
        await StartFilmAsync(api, channel);
        var before = await api.OpenSessionAsync(RoomSession.IdFor(channel), CancellationToken.None);

        var outcome = await handler.HandleAsync(Said(channel, "should we pause?"));

        Assert.Equal(VoiceOutcome.Asked, outcome);
        Assert.Equal("should we pause?", Assert.Single(assistant.Asked).Text);
        var after = await api.OpenSessionAsync(RoomSession.IdFor(channel), CancellationToken.None);
        Assert.Equal(before.Revision, after.Revision);
    }

    [Fact]
    public async Task A_room_verb_is_written_into_the_room_conversation_as_the_tool_it_stands_for()
    {
        // The model never saw the pause, and "why did it stop?" a minute later has to be answered by
        // something that knows it happened.
        var assistant = new RecordingAssistant();
        var (api, handler) = Create(assistant);
        var channel = NewChannel();
        await StartFilmAsync(api, channel);

        await handler.HandleAsync(Said(channel, "go back fifteen seconds"));

        var act = Assert.Single(assistant.Acts);
        Assert.Equal("seek_relative", act.Tool);
        Assert.Equal("-15", act.Arguments["seconds"]);
        Assert.StartsWith("Moved to ", act.Outcome);
        Assert.Empty(assistant.Asked);
    }

    [Fact]
    public async Task A_reply_to_a_proposal_is_read_as_a_reply()
    {
        var assistant = new RecordingAssistant();
        var (_, handler) = Create(assistant);

        var outcome = await handler.HandleAsync(Said(NewChannel(), "yes go ahead") with
        {
            Answering = new VoiceWaiting(VoiceWaitingFor.Confirmation, DateTimeOffset.UtcNow.AddSeconds(20), ["t"]),
        });

        Assert.Equal(VoiceOutcome.Decided, outcome);
        Assert.Single(assistant.Decided);
        Assert.Empty(assistant.Asked);
    }

    [Fact]
    public async Task A_room_verb_said_while_a_proposal_waits_moves_the_room()
    {
        // Somebody who says "pause" while the bot waits on a yes wants the film paused. The proposal
        // stays under its buttons.
        var assistant = new RecordingAssistant();
        var (api, handler) = Create(assistant);
        var channel = NewChannel();
        await StartFilmAsync(api, channel);

        var outcome = await handler.HandleAsync(Said(channel, "pause") with
        {
            Answering = new VoiceWaiting(VoiceWaitingFor.Confirmation, DateTimeOffset.UtcNow.AddSeconds(20), ["t"]),
        });

        Assert.Equal(VoiceOutcome.Acted, outcome);
        Assert.Empty(assistant.Decided);
    }

    [Fact]
    public async Task The_trigger_on_its_own_does_nothing()
    {
        var (_, handler) = Create();

        Assert.Equal(VoiceOutcome.Nothing, await handler.HandleAsync(Said(NewChannel(), "")));
    }

    [Fact]
    public async Task An_unreachable_api_is_a_failed_request_not_a_crash()
    {
        // The handler runs on the worker that answers every spoken request; one exception escaping it
        // would be the surface failing rather than one request.
        var api = new MovieBotApiClient(new HttpClient { BaseAddress = new Uri("http://127.0.0.1:1/") });
        var handler = new RoomVoiceCommandHandler(
            api, new NoRoomAssistant(), TimeProvider.System, Options.Create(new DiscordVoiceOptions()),
            NullLogger<RoomVoiceCommandHandler>.Instance);

        Assert.Equal(VoiceOutcome.Failed, await handler.HandleAsync(Said(NewChannel(), "pause")));
    }

    private (MovieBotApiClient Api, RoomVoiceCommandHandler Handler) Create(IRoomAssistant? assistant = null)
    {
        var api = new MovieBotApiClient(fixture.CreateServiceClient());
        var handler = new RoomVoiceCommandHandler(
            api, assistant ?? new NoRoomAssistant(), TimeProvider.System, Options.Create(new DiscordVoiceOptions()),
            NullLogger<RoomVoiceCommandHandler>.Instance);
        return (api, handler);
    }

    private sealed class RecordingAssistant : IRoomAssistant
    {
        public List<VoiceCommand> Asked { get; } = [];
        public List<VoiceCommand> Decided { get; } = [];
        public List<(string Tool, IReadOnlyDictionary<string, string?> Arguments, string Outcome)> Acts { get; } = [];

        public bool IsEnabled => true;
        public void Answer(VoiceCommand command) => Asked.Add(command);

        public Task DecideAsync(VoiceCommand command, VoiceWaiting waiting, CancellationToken ct)
        {
            Decided.Add(command);
            return Task.CompletedTask;
        }

        public void RecordAct(VoiceCommand command, string tool, IReadOnlyDictionary<string, string?> arguments, string outcome) =>
            Acts.Add((tool, arguments, outcome));

        public Task HandleButtonAsync(global::Discord.WebSocket.SocketMessageComponent component) => Task.CompletedTask;
    }

    private static async Task StartFilmAsync(MovieBotApiClient api, ulong channel)
    {
        var session = RoomSession.IdFor(channel);
        await api.SetSessionTitleAsync(session, SessionFixture.ReadyTitle, "someone", CancellationToken.None);
        await api.PlayAsync(session, "1", "someone", CancellationToken.None);
    }

    private static ulong NewChannel() => (ulong)Interlocked.Increment(ref _channels);

    private static VoiceCommand Said(ulong channel, string text) => new(
        SpeakerId: 4242, SpeakerName: "Haru", GuildId: 1, ChannelId: channel,
        Text: text, Transcript: "okay computer, " + text, Spoken: TimeSpan.FromSeconds(1),
        EndedAt: DateTimeOffset.UtcNow);
}
