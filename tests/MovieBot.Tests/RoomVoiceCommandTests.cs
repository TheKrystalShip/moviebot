using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TheKrystalShip.Discord.Voice;
using TheKrystalShip.MovieBot.Bot.Api;
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
            api, TimeProvider.System, Options.Create(new DiscordVoiceOptions()),
            NullLogger<RoomVoiceCommandHandler>.Instance);

        Assert.Equal(VoiceOutcome.Failed, await handler.HandleAsync(Said(NewChannel(), "pause")));
    }

    private (MovieBotApiClient Api, RoomVoiceCommandHandler Handler) Create()
    {
        var api = new MovieBotApiClient(fixture.CreateServiceClient());
        var handler = new RoomVoiceCommandHandler(
            api, TimeProvider.System, Options.Create(new DiscordVoiceOptions()),
            NullLogger<RoomVoiceCommandHandler>.Instance);
        return (api, handler);
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
        Text: text, Transcript: "hey moviebot, " + text, Spoken: TimeSpan.FromSeconds(1),
        EndedAt: DateTimeOffset.UtcNow);
}
