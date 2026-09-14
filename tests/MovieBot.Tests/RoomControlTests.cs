using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

using Microsoft.AspNetCore.SignalR.Client;

using TheKrystalShip.MovieBot.Api.Sessions;
using TheKrystalShip.MovieBot.Core;

using Xunit;

namespace TheKrystalShip.MovieBot.Tests;

/// <summary>
/// The room's controls as something outside the room reaches them: the bot, and a spoken command.
/// </summary>
/// <remarks>
/// <para>
/// These go over HTTP against the running API rather than calling the store, because what is worth
/// catching lives in the seam — a caller that cannot see the film asking for something that moves
/// it, and a change that reaches the caller but never reaches the room.
/// </para>
/// <para>
/// The room is a moving thing, so anything measured against a playing film is asserted with a
/// tolerance. The tolerance is generous on purpose: a test that pins the position to the
/// millisecond is a test that fails on a slow machine and says nothing about the behaviour.
/// </para>
/// </remarks>
public sealed class RoomControlTests(SessionFixture fixture) : IClassFixture<SessionFixture>
{
    private static readonly JsonSerializerOptions AsTheBotWritesIt = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    private static readonly TimeSpan PushTimeout = TimeSpan.FromSeconds(5);

    /// <summary>How far a position may drift while a request is in flight and still be right.</summary>
    private const double Tolerance = 2.0;

    [Fact]
    public async Task A_pause_that_names_no_position_stops_the_room_where_it_is()
    {
        // The failure this exists for: a caller that is not watching has no position to send, and a
        // missing one read as zero pauses the film AND sends the room back to the opening titles.
        // Nobody who said "pause" asked for that, and the film stopping makes it look like it
        // worked.
        var client = fixture.CreateServiceClient();
        var session = NewSession();

        await LoadAsync(client, session);
        await PostAsync(client, session, "play", new RoomPlaybackRequest(AtSeconds: 400));

        var paused = await PostAsync(client, session, "pause", new RoomPlaybackRequest());

        Assert.True(paused.Push.State.Paused);
        Assert.InRange(paused.Push.State.PositionSeconds, 400 - Tolerance, 400 + 60);
    }

    [Fact]
    public async Task A_play_that_names_no_position_carries_on_from_where_the_room_is()
    {
        var client = fixture.CreateServiceClient();
        var session = NewSession();

        await LoadAsync(client, session);
        await PostAsync(client, session, "play", new RoomPlaybackRequest(AtSeconds: 250));
        await PostAsync(client, session, "pause", new RoomPlaybackRequest(AtSeconds: 250));

        var playing = await PostAsync(client, session, "play", new RoomPlaybackRequest());

        Assert.False(playing.Push.State.Paused);
        Assert.InRange(playing.Push.State.PositionSeconds, 250 - Tolerance, 250 + Tolerance);
    }

    [Fact]
    public async Task Going_back_is_measured_from_where_the_film_is_now()
    {
        var client = fixture.CreateServiceClient();
        var session = NewSession();

        await LoadAsync(client, session);
        await PostAsync(client, session, "play", new RoomPlaybackRequest(AtSeconds: 500));

        var back = await PostAsync(client, session, "seek-relative", new RoomNudgeRequest(-15));

        // Not 485 exactly: the room has been playing since the play landed, so the fifteen comes
        // off wherever it had reached. That is the whole point of asking relatively.
        Assert.InRange(back.Push.State.PositionSeconds, 485 - Tolerance, 485 + 60);
    }

    [Fact]
    public async Task Going_forward_moves_the_room_on()
    {
        var client = fixture.CreateServiceClient();
        var session = NewSession();

        await LoadAsync(client, session);
        await PostAsync(client, session, "pause", new RoomPlaybackRequest(AtSeconds: 1000));

        var on = await PostAsync(client, session, "seek-relative", new RoomNudgeRequest(30));

        Assert.InRange(on.Push.State.PositionSeconds, 1030 - Tolerance, 1030 + Tolerance);
    }

    [Fact]
    public async Task Going_back_further_than_the_film_has_run_lands_at_the_start()
    {
        // Asking to go back an hour four minutes in is a request to start again, not a mistake to
        // refuse: whoever said it wants the beginning.
        var client = fixture.CreateServiceClient();
        var session = NewSession();

        await LoadAsync(client, session);
        await PostAsync(client, session, "pause", new RoomPlaybackRequest(AtSeconds: 240));

        var back = await PostAsync(client, session, "seek-relative", new RoomNudgeRequest(-3600));

        Assert.Equal(0, back.Push.State.PositionSeconds, 3);
    }

    [Fact]
    public async Task A_control_over_http_reaches_the_players_watching()
    {
        // The reason there is one implementation behind both doors. A pause the caller is told
        // about and the room is not is a film that stops for nobody.
        var client = fixture.CreateServiceClient();
        var session = NewSession();
        await using var viewer = await fixture.ConnectAsync(session, "u1", "Alice");

        await LoadAsync(client, session);

        var seen = NextPush(viewer, "StateChanged");
        await PostAsync(client, session, "play", new RoomPlaybackRequest(AtSeconds: 120));
        var push = await seen;

        Assert.False(push.State.Paused);
        Assert.InRange(push.State.PositionSeconds, 120 - Tolerance, 120 + Tolerance);
    }

    [Fact]
    public async Task A_seek_past_what_has_been_transcoded_is_clamped_and_the_caller_is_told()
    {
        // Over the hub the clamp goes to the caller alone. An HTTP caller is the one thing a
        // broadcast cannot reach, so it comes back in the answer.
        var client = fixture.CreateServiceClient();
        var session = NewSession();

        await LoadAsync(client, session, SessionFixture.TranscodingTitle);

        var far = await PostAsync(client, session, "seek", new RoomSeekRequest(5000));

        Assert.NotNull(far.Clamped);
        Assert.Equal(5000, far.Clamped!.RequestedSeconds, 3);
        Assert.Equal(SessionFixture.TranscodingHead, far.Clamped.HeadSeconds, 3);
        Assert.True(far.Push.State.PositionSeconds < SessionFixture.TranscodingHead);
    }

    [Fact]
    public async Task A_seek_inside_the_film_is_not_clamped()
    {
        var client = fixture.CreateServiceClient();
        var session = NewSession();

        await LoadAsync(client, session);

        var landed = await PostAsync(client, session, "seek", new RoomSeekRequest(1200));

        Assert.Null(landed.Clamped);
        Assert.Equal(1200, landed.Push.State.PositionSeconds, 3);
    }

    [Fact]
    public async Task The_room_records_who_asked()
    {
        var client = fixture.CreateServiceClient();
        var session = NewSession();

        await LoadAsync(client, session);
        var paused = await PostAsync(client, session, "pause",
            new RoomPlaybackRequest(AtSeconds: 10, UserId: "u9", DisplayName: "Haru"));

        Assert.Equal("Haru", paused.Push.State.UpdatedBy?.DisplayName);
        Assert.Equal("u9", paused.Push.State.UpdatedBy?.UserId);
    }

    [Fact]
    public async Task A_control_naming_nobody_is_recorded_as_the_bot()
    {
        // Better than attributing it to whoever last touched the room: a change nobody asked for
        // should not be signed with a person's name.
        var client = fixture.CreateServiceClient();
        var session = NewSession();

        await LoadAsync(client, session);
        var paused = await PostAsync(client, session, "pause", new RoomPlaybackRequest(AtSeconds: 10));

        Assert.Equal("MovieBot", paused.Push.State.UpdatedBy?.DisplayName);
    }

    [Fact]
    public async Task Every_control_advances_the_revision()
    {
        // A client discards any state whose revision it has already seen, so a control that did not
        // advance it would be applied by nobody and look like the room ignoring the request.
        var client = fixture.CreateServiceClient();
        var session = NewSession();

        await LoadAsync(client, session);

        var revisions = new List<long>
        {
            (await PostAsync(client, session, "play", new RoomPlaybackRequest(AtSeconds: 5))).Push.State.Revision,
            (await PostAsync(client, session, "pause", new RoomPlaybackRequest())).Push.State.Revision,
            (await PostAsync(client, session, "seek", new RoomSeekRequest(60))).Push.State.Revision,
            (await PostAsync(client, session, "seek-relative", new RoomNudgeRequest(-10))).Push.State.Revision
        };

        Assert.Equal(revisions.OrderBy(r => r), revisions);
        Assert.Equal(revisions.Count, revisions.Distinct().Count());
    }

    [Fact]
    public async Task A_room_control_is_closed_to_a_caller_with_no_token()
    {
        var open = fixture.CreateClient();

        var refused = await open.PostAsJsonAsync(
            $"/api/sessions/{NewSession()}/pause", new RoomPlaybackRequest(), AsTheBotWritesIt);

        Assert.Equal(HttpStatusCode.Unauthorized, refused.StatusCode);
    }

    // ---- helpers -------------------------------------------------------------------

    private static string NewSession() => $"room-{Guid.NewGuid():n}";

    private async Task LoadAsync(HttpClient client, string session, string title = SessionFixture.ReadyTitle)
    {
        var response = await client.PostAsJsonAsync(
            $"/api/sessions/{session}/title", new SetTitleRequest(title, null, null), AsTheBotWritesIt);
        response.EnsureSuccessStatusCode();
    }

    private async Task<RoomChanged> PostAsync<T>(
        HttpClient client, string session, string control, T body)
    {
        var response = await client.PostAsJsonAsync(
            $"/api/sessions/{session}/{control}", body, AsTheBotWritesIt);
        response.EnsureSuccessStatusCode();

        var changed = await response.Content.ReadFromJsonAsync<RoomChanged>(AsTheBotWritesIt);
        Assert.NotNull(changed);
        return changed!;
    }

    private static Task<SessionStatePush> NextPush(HubConnection connection, string method)
    {
        var waiting = new TaskCompletionSource<SessionStatePush>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        var subscription = connection.On<SessionStatePush>(method, push => waiting.TrySetResult(push));
        _ = Task.Delay(PushTimeout).ContinueWith(_ =>
        {
            waiting.TrySetException(new TimeoutException($"No {method} arrived."));
            subscription.Dispose();
        });

        return waiting.Task;
    }
}
