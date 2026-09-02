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
        await alice.InvokeAsync<SessionStatePush>("LoadTitle", SessionFixture.ReadyTitle);
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

        await alice.InvokeAsync<SessionStatePush>("LoadTitle", SessionFixture.ReadyTitle);

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

        await alice.InvokeAsync<SessionStatePush>("LoadTitle", SessionFixture.TranscodingTitle);

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

        await alice.InvokeAsync<SessionStatePush>("LoadTitle", SessionFixture.TranscodingTitle);
        var result = await alice.InvokeAsync<SessionStatePush>("Seek", 120.0);

        Assert.Equal(120.0, result.State.PositionSeconds, 3);
        Assert.Equal(SessionFixture.TranscodingHead, result.State.TranscodeHead);
    }

    [Fact]
    public async Task A_finished_title_is_seekable_anywhere()
    {
        var session = NewSession();
        await using var alice = await fixture.ConnectAsync(session, "u1", "Alice");

        await alice.InvokeAsync<SessionStatePush>("LoadTitle", SessionFixture.ReadyTitle);
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

    // ---- helpers --------------------------------------------------------------------

    private static string NewSession() => Guid.NewGuid().ToString("n");

    private static Task<SessionStatePush> NextPush(HubConnection connection, string method) =>
        Next<SessionStatePush>(connection, method);

    private static Task<T> Next<T>(HubConnection connection, string method)
    {
        var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        var subscription = connection.On<T>(method, value => completion.TrySetResult(value));

        _ = Task.Delay(PushTimeout).ContinueWith(_ =>
        {
            completion.TrySetException(new TimeoutException($"No {method} within {PushTimeout}."));
            subscription.Dispose();
        }, TaskScheduler.Default);

        return completion.Task;
    }
}
