using Microsoft.Extensions.DependencyInjection;
using TheKrystalShip.MovieBot.Api.Sessions;
using Xunit;
using Microsoft.AspNetCore.SignalR.Client;
using TheKrystalShip.MovieBot.Core;

namespace TheKrystalShip.MovieBot.Tests;

/// <summary>
/// Two clients on one session, which is the whole product in miniature. These exercise the hub
/// over a real connection rather than calling the store directly, because the failures worth
/// catching — a push that never arrives, a clamp delivered to the wrong client — live in the
/// wiring and not in the logic.
/// </summary>
public sealed class SessionSyncTests(SessionFixture fixture) : IClassFixture<SessionFixture>
{
    private static readonly TimeSpan PushTimeout = TimeSpan.FromSeconds(5);

    [Fact]
    public async Task A_play_by_one_client_reaches_the_other()
    {
        var session = NewSession();
        await using var alice = await fixture.ConnectAsync(session, "u1", "Alice");
        await using var bob = await fixture.ConnectAsync(session, "u2", "Bob");

        var bobSaw = NextPush(bob, "StateChanged");
        await alice.InvokeAsync<SessionStatePush>("LoadTitle", SessionFixture.ReadyTitle, null);
        await bobSaw;

        var playSeen = NextPush(bob, "StateChanged");
        await alice.InvokeAsync<SessionStatePush>("Play", 42.0);
        var push = await playSeen;

        Assert.False(push.State.Paused);
        Assert.Equal(42.0, push.State.PositionSeconds, 3);
        Assert.Equal("Alice", push.State.UpdatedBy?.DisplayName);
    }

    [Fact]
    public async Task Either_client_can_drive_and_revisions_only_increase()
    {
        var session = NewSession();
        await using var alice = await fixture.ConnectAsync(session, "u1", "Alice");
        await using var bob = await fixture.ConnectAsync(session, "u2", "Bob");

        await alice.InvokeAsync<SessionStatePush>("LoadTitle", SessionFixture.ReadyTitle, null);

        var revisions = new List<long>();
        revisions.Add((await alice.InvokeAsync<SessionStatePush>("Play", 10.0)).State.Revision);
        revisions.Add((await bob.InvokeAsync<SessionStatePush>("Pause", 25.0)).State.Revision);
        revisions.Add((await bob.InvokeAsync<SessionStatePush>("Seek", 90.0)).State.Revision);
        revisions.Add((await alice.InvokeAsync<SessionStatePush>("Play", 90.0)).State.Revision);

        Assert.Equal(revisions.OrderBy(r => r), revisions);
        Assert.Equal(revisions.Distinct().Count(), revisions.Count);

        // Bob paused and seeked, so the last write standing before Alice's play was his.
        var final = await alice.InvokeAsync<SessionStatePush>("Seek", 90.0);
        Assert.Equal("Alice", final.State.UpdatedBy?.DisplayName);
    }

    [Fact]
    public async Task Seeking_past_the_transcode_head_is_refused_with_a_reason()
    {
        var session = NewSession();
        await using var alice = await fixture.ConnectAsync(session, "u1", "Alice");
        await using var bob = await fixture.ConnectAsync(session, "u2", "Bob");

        await alice.InvokeAsync<SessionStatePush>("LoadTitle", SessionFixture.TranscodingTitle, null);

        var clampNotice = Next<SeekClamped>(alice, "SeekClamped");
        var bobGotClamp = Next<SeekClamped>(bob, "SeekClamped");

        var result = await alice.InvokeAsync<SessionStatePush>("Seek", 5000.0);
        var clamped = await clampNotice;

        Assert.Equal(5000.0, clamped.RequestedSeconds, 3);
        Assert.Equal(SessionFixture.TranscodingHead, clamped.HeadSeconds, 3);
        Assert.True(clamped.GrantedSeconds < SessionFixture.TranscodingHead,
            "a clamped seek must land short of the head, not on it");
        Assert.Equal(clamped.GrantedSeconds, result.State.PositionSeconds, 3);

        // The notice explains one person's refused click; broadcasting it would have the whole
        // room show an error nobody else triggered.
        Assert.False(bobGotClamp.IsCompleted);
    }

    [Fact]
    public async Task Seeking_within_the_head_is_granted_untouched()
    {
        var session = NewSession();
        await using var alice = await fixture.ConnectAsync(session, "u1", "Alice");

        await alice.InvokeAsync<SessionStatePush>("LoadTitle", SessionFixture.TranscodingTitle, null);
        var result = await alice.InvokeAsync<SessionStatePush>("Seek", 120.0);

        Assert.Equal(120.0, result.State.PositionSeconds, 3);
        Assert.Equal(SessionFixture.TranscodingHead, result.State.TranscodeHead);
    }

    [Fact]
    public async Task A_finished_title_is_seekable_anywhere()
    {
        var session = NewSession();
        await using var alice = await fixture.ConnectAsync(session, "u1", "Alice");

        await alice.InvokeAsync<SessionStatePush>("LoadTitle", SessionFixture.ReadyTitle, null);
        var result = await alice.InvokeAsync<SessionStatePush>("Seek", 5000.0);

        Assert.Equal(5000.0, result.State.PositionSeconds, 3);
        Assert.Null(result.State.TranscodeHead);
    }

    [Fact]
    public async Task Joining_and_leaving_is_broadcast()
    {
        var session = NewSession();
        await using var alice = await fixture.ConnectAsync(session, "u1", "Alice");

        var sawBob = Next<List<Participant>>(alice, "ParticipantsChanged");
        var bob = await fixture.ConnectAsync(session, "u2", "Bob");
        Assert.Equal(2, (await sawBob).Count);

        var sawLeave = Next<List<Participant>>(alice, "ParticipantsChanged");
        await bob.DisposeAsync();
        var remaining = await sawLeave;

        Assert.Single(remaining);
        Assert.Equal("Alice", remaining[0].DisplayName);
    }

    [Fact]
    public async Task A_viewer_waiting_for_the_film_is_marked_on_the_roster()
    {
        var session = NewSession();
        await using var alice = await fixture.ConnectAsync(session, "u1", "Alice");
        await using var bob = await fixture.ConnectAsync(session, "u2", "Bob");

        // Bob's arrival reaches Alice as a roster push too, and can do so after she starts listening.
        var sawWaiting = Next<List<Participant>>(alice, "ParticipantsChanged", r => r.Any(p => p.Buffering));
        await bob.InvokeAsync("Buffering", true, 0.0);
        var waiting = await sawWaiting;
        Assert.True(waiting.Single(p => p.DisplayName == "Bob").Buffering);
        Assert.False(waiting.Single(p => p.DisplayName == "Alice").Buffering);

        var sawPlaying = Next<List<Participant>>(alice, "ParticipantsChanged");
        await bob.InvokeAsync("Buffering", false, 0.0);
        Assert.All(await sawPlaying, p => Assert.False(p.Buffering));
    }

    [Fact]
    public async Task One_viewer_waiting_does_not_hold_the_others()
    {
        var session = NewSession();
        await using var alice = await fixture.ConnectAsync(session, "u1", "Alice");
        await using var bob = await fixture.ConnectAsync(session, "u2", "Bob");
        await alice.InvokeAsync<SessionStatePush>("LoadTitle", SessionFixture.ReadyTitle, null);
        await alice.InvokeAsync<SessionStatePush>("Play", 100.0);

        await bob.InvokeAsync("Buffering", true, 95.0);
        var state = (await alice.InvokeAsync<JoinReply>("Join", session)).Push!.State;

        Assert.False(state.Waiting);
        Assert.False(state.Paused);
    }

    [Fact]
    public async Task A_room_whose_every_player_waits_is_held_where_it_stopped_and_let_go_when_one_plays()
    {
        var session = NewSession();
        await using var alice = await fixture.ConnectAsync(session, "u1", "Alice");
        await alice.InvokeAsync<SessionStatePush>("LoadTitle", SessionFixture.ReadyTitle, null);
        var playing = (await alice.InvokeAsync<SessionStatePush>("Play", 100.0)).State;

        var sawHold = NextPush(alice, "StateChanged");
        await alice.InvokeAsync("Buffering", true, 99.5);
        var held = (await sawHold).State;

        Assert.True(held.Waiting);
        Assert.False(held.Paused);
        Assert.Null(held.UpdatedBy);
        Assert.True(held.Revision > playing.Revision);
        // Where the player stopped, which is behind where the room had got to.
        Assert.Equal(99.5, held.PositionSeconds, 3);
        Assert.Equal(99.5, held.PositionAt(held.AnchorUtc.AddMinutes(5)), 3);

        var sawGo = NextPush(alice, "StateChanged");
        await alice.InvokeAsync("Buffering", false, 99.5);
        var going = (await sawGo).State;

        Assert.False(going.Waiting);
        Assert.False(going.Paused);
        Assert.Equal(99.5, going.PositionSeconds, 3);
        Assert.Equal(109.5, going.PositionAt(going.AnchorUtc.AddSeconds(10)), 3);
    }

    [Fact]
    public async Task A_held_room_is_let_go_when_somebody_arrives_who_is_not_waiting()
    {
        var session = NewSession();
        await using var alice = await fixture.ConnectAsync(session, "u1", "Alice");
        await alice.InvokeAsync<SessionStatePush>("LoadTitle", SessionFixture.ReadyTitle, null);
        await alice.InvokeAsync<SessionStatePush>("Play", 100.0);
        var sawHold = NextPush(alice, "StateChanged");
        await alice.InvokeAsync("Buffering", true, 100.0);
        Assert.True((await sawHold).State.Waiting);

        var sawGo = NextPush(alice, "StateChanged");
        await using var bob = await fixture.ConnectAsync(session, "u2", "Bob");

        Assert.False((await sawGo).State.Waiting);
    }

    [Fact]
    public async Task A_held_room_stays_held_through_a_seek_while_its_players_still_wait()
    {
        var session = NewSession();
        await using var alice = await fixture.ConnectAsync(session, "u1", "Alice");
        await alice.InvokeAsync<SessionStatePush>("LoadTitle", SessionFixture.ReadyTitle, null);
        await alice.InvokeAsync<SessionStatePush>("Play", 100.0);
        var sawHold = NextPush(alice, "StateChanged");
        await alice.InvokeAsync("Buffering", true, 100.0);
        await sawHold;

        var sought = (await alice.InvokeAsync<SessionStatePush>("Seek", 200.0)).State;

        Assert.True(sought.Waiting);
        Assert.Equal(200.0, sought.PositionAt(sought.AnchorUtc.AddSeconds(30)), 3);
    }

    [Fact]
    public void Position_is_derived_from_the_anchor_not_transmitted()
    {
        var anchor = DateTimeOffset.UtcNow;
        var playing = new SessionState
        {
            SessionId = "s", Epoch = "run", Paused = false, PositionSeconds = 100,
            AnchorUtc = anchor, Rate = 1.0
        };

        // A client that missed thirty seconds of pushes still computes the right answer.
        Assert.Equal(130, playing.PositionAt(anchor.AddSeconds(30)), 3);

        var paused = playing with { Paused = true };
        Assert.Equal(100, paused.PositionAt(anchor.AddSeconds(30)), 3);
    }

    [Fact]
    public async Task A_viewer_back_alone_carries_the_room_on_from_their_own_playhead()
    {
        var session = NewSession();
        var sessions = fixture.Services.GetRequiredService<SessionStore>();
        var phone = await fixture.ConnectAsync(session, "u1", "Alice");
        await phone.InvokeAsync<SessionStatePush>("LoadTitle", SessionFixture.ReadyTitle, null);
        var applied = (await phone.InvokeAsync<SessionStatePush>("Play", 100.0)).State;

        await phone.DisposeAsync();
        var deadline = DateTime.UtcNow + PushTimeout;
        while (sessions.Get(session) is not { Waiting: true } && DateTime.UtcNow < deadline) await Task.Delay(20);
        Assert.True(sessions.Get(session)!.Waiting);

        await using var back = await fixture.ConnectOutsideAsync("u1", "Alice");
        var account = new PlayerAccount
        {
            Epoch = applied.Epoch, Revision = applied.Revision, TitleId = SessionFixture.ReadyTitle,
            PositionSeconds = 340, Paused = false
        };
        var reply = await back.InvokeAsync<JoinReply>("Rejoin", session, account);

        Assert.True(reply.Admitted);
        Assert.Equal(340, reply.Push!.State.PositionSeconds, 3);
        Assert.False(reply.Push.State.Waiting);
        Assert.False(reply.Push.State.Paused);
        Assert.Null(reply.Push.State.UpdatedBy);
    }

    [Fact]
    public async Task A_viewer_coming_back_to_a_room_others_are_watching_takes_it_as_it_is()
    {
        var session = NewSession();
        await using var bob = await fixture.ConnectAsync(session, "u2", "Bob");
        var phone = await fixture.ConnectAsync(session, "u1", "Alice");
        await phone.InvokeAsync<SessionStatePush>("LoadTitle", SessionFixture.ReadyTitle, null);
        var applied = (await phone.InvokeAsync<SessionStatePush>("Play", 100.0)).State;
        await phone.DisposeAsync();

        await using var back = await fixture.ConnectOutsideAsync("u1", "Alice");
        var account = new PlayerAccount
        {
            Epoch = applied.Epoch, Revision = applied.Revision, TitleId = SessionFixture.ReadyTitle,
            PositionSeconds = 20, Paused = true
        };
        var state = (await back.InvokeAsync<JoinReply>("Rejoin", session, account)).Push!.State;

        Assert.False(state.Paused);
        Assert.True(state.PositionSeconds >= 100);
    }

    // ---- helpers --------------------------------------------------------------------

    private static string NewSession() => Guid.NewGuid().ToString("n");

    private static Task<SessionStatePush> NextPush(HubConnection connection, string method) =>
        Next<SessionStatePush>(connection, method);

    private static Task<T> Next<T>(HubConnection connection, string method, Func<T, bool>? match = null)
    {
        var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        var subscription = connection.On<T>(method, value =>
        {
            if (match is null || match(value)) completion.TrySetResult(value);
        });

        _ = Task.Delay(PushTimeout).ContinueWith(_ =>
        {
            completion.TrySetException(new TimeoutException($"No {method} within {PushTimeout}."));
            subscription.Dispose();
        }, TaskScheduler.Default);

        return completion.Task;
    }
}
