using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.SignalR.Client;
using TheKrystalShip.MovieBot.Core;
using Xunit;

namespace TheKrystalShip.MovieBot.Tests;

/// <summary>
/// A room is private to whoever made it until somebody in it opens it, and every door into a room —
/// the hub, the room's routes, the listing, an invite, Watch now — asks the same question.
/// </summary>
public sealed class RoomDoorTests(SessionFixture fixture) : IClassFixture<SessionFixture>
{
    private static string NewRoom() => "door-" + Guid.NewGuid().ToString("n")[..8];

    /// <summary>The next time the connection hears <paramref name="method"/>.</summary>
    private static Task<T> Next<T>(HubConnection connection, string method)
    {
        var heard = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        IDisposable? subscription = null;
        subscription = connection.On<T>(method, value =>
        {
            subscription?.Dispose();
            heard.TrySetResult(value);
        });
        return heard.Task.WaitAsync(TimeSpan.FromSeconds(10));
    }

    /// <summary>The next door the room is told of that satisfies <paramref name="matches"/>.</summary>
    private static Task<RoomDoor> NextDoor(HubConnection connection, Func<RoomDoor, bool> matches)
    {
        var heard = new TaskCompletionSource<RoomDoor>(TaskCreationOptions.RunContinuationsAsynchronously);
        connection.On<RoomDoor>("DoorChanged", door =>
        {
            if (matches(door)) heard.TrySetResult(door);
        });
        return heard.Task.WaitAsync(TimeSpan.FromSeconds(10));
    }

    [Fact]
    public async Task A_room_is_private_to_everybody_but_whoever_made_it()
    {
        var room = NewRoom();
        await using var alice = await fixture.ConnectOutsideAsync("d-alice-1", "Alice");
        await using var bob = await fixture.ConnectOutsideAsync("d-bob-1", "Bob");

        var made = await alice.InvokeAsync<JoinReply>("Join", room);
        var refused = await bob.InvokeAsync<JoinReply>("Join", room);

        Assert.True(made.Admitted);
        Assert.False(made.Door!.Open);
        Assert.False(refused.Admitted);
        Assert.Null(refused.Push);
        Assert.Null(refused.Door);
    }

    /// <summary>
    /// The Activity's room is the call's, and Discord decides who is in the call: a room made through
    /// the Activity or the bot is open from the moment it is made, so everybody in the call is in it.
    /// </summary>
    [Theory]
    [InlineData("cinema-activity")]
    [InlineData("cinema-bot")]
    public async Task A_room_made_through_Discord_is_open_to_everybody(string client)
    {
        var room = NewRoom();
        await using var alice = await fixture.ConnectOutsideAsync("d-alice-9" + client, "Alice", via: client);
        await using var bob = await fixture.ConnectOutsideAsync("d-bob-9" + client, "Bob");

        var made = await alice.InvokeAsync<JoinReply>("Join", room);
        var joined = await bob.InvokeAsync<JoinReply>("Join", room);

        Assert.True(made.Door!.Open);
        Assert.True(joined.Admitted);
    }

    /// <summary>Only making a room opens it: a room somebody made private stays so, whoever arrives.</summary>
    [Fact]
    public async Task Arriving_through_Discord_opens_no_room_somebody_made_private()
    {
        var room = NewRoom();
        await using var alice = await fixture.ConnectOutsideAsync("d-alice-10", "Alice");
        await using var bob = await fixture.ConnectOutsideAsync("d-bob-10", "Bob", via: "cinema-activity");

        Assert.True((await alice.InvokeAsync<JoinReply>("Join", room)).Admitted);
        Assert.False((await bob.InvokeAsync<JoinReply>("Join", room)).Admitted);
    }

    /// <summary>The room's routes make a room the way the hub does: the Activity's first read of the call's room opens it.</summary>
    [Fact]
    public async Task A_room_first_read_through_the_Activity_is_open()
    {
        var room = NewRoom();
        using var activity = fixture.CreatePersonClient("d-alice-11", "Alice", via: "cinema-activity");
        Assert.Equal(HttpStatusCode.OK, (await activity.GetAsync($"/api/sessions/{room}")).StatusCode);

        using var bob = fixture.CreatePersonClient("d-bob-11", "Bob");
        Assert.Equal(HttpStatusCode.OK, (await bob.GetAsync($"/api/sessions/{room}")).StatusCode);
    }

    [Fact]
    public async Task Closing_a_room_keeps_everybody_who_has_been_in_and_lets_in_nobody_new()
    {
        var room = NewRoom();
        await using var alice = await fixture.ConnectOpenAsync(room, "d-alice-2", "Alice");
        var bob = await fixture.ConnectAsync(room, "d-bob-2", "Bob");

        await alice.InvokeAsync("SetOpen", false);
        await bob.DisposeAsync();

        // Bob reloading his page is Bob leaving and coming back.
        await using var bobAgain = await fixture.ConnectOutsideAsync("d-bob-2", "Bob");
        await using var carol = await fixture.ConnectOutsideAsync("d-carol-2", "Carol");
        Assert.True((await bobAgain.InvokeAsync<JoinReply>("Join", room)).Admitted);
        Assert.False((await carol.InvokeAsync<JoinReply>("Join", room)).Admitted);
    }

    [Fact]
    public async Task Somebody_asking_is_shown_to_the_room_and_let_in_when_answered()
    {
        var room = NewRoom();
        await using var alice = await fixture.ConnectAsync(room, "d-alice-3", "Alice");
        await using var bob = await fixture.ConnectOutsideAsync("d-bob-3", "Bob");

        var asking = NextDoor(alice, d => d.Knocking.Count == 1);
        Assert.False(await bob.InvokeAsync<bool>("Knock", room));
        Assert.Equal("Bob", Assert.Single((await asking).Knocking).DisplayName);

        var answered = Next<bool>(bob, "KnockAnswered");
        var cleared = NextDoor(alice, d => d.Knocking.Count == 0);
        await alice.InvokeAsync("Answer", "d-bob-3", true);

        Assert.True(await answered);
        await cleared;
        Assert.True((await bob.InvokeAsync<JoinReply>("Join", room)).Admitted);
    }

    [Fact]
    public async Task Somebody_turned_away_is_told_and_stays_out()
    {
        var room = NewRoom();
        await using var alice = await fixture.ConnectAsync(room, "d-alice-4", "Alice");
        await using var bob = await fixture.ConnectOutsideAsync("d-bob-4", "Bob");
        var asking = NextDoor(alice, d => d.Knocking.Count == 1);
        await bob.InvokeAsync<bool>("Knock", room);
        await asking;

        var answered = Next<bool>(bob, "KnockAnswered");
        await alice.InvokeAsync("Answer", "d-bob-4", false);

        Assert.False(await answered);
        Assert.False((await bob.InvokeAsync<JoinReply>("Join", room)).Admitted);
    }

    [Fact]
    public async Task A_page_that_goes_away_stops_asking()
    {
        var room = NewRoom();
        await using var alice = await fixture.ConnectAsync(room, "d-alice-5", "Alice");
        var bob = await fixture.ConnectOutsideAsync("d-bob-5", "Bob");
        var asking = NextDoor(alice, d => d.Knocking.Count == 1);
        await bob.InvokeAsync<bool>("Knock", room);
        await asking;

        var gone = NextDoor(alice, d => d.Knocking.Count == 0);
        await bob.DisposeAsync();

        await gone;
    }

    [Fact]
    public async Task Opening_a_room_lets_in_everybody_asking()
    {
        var room = NewRoom();
        await using var alice = await fixture.ConnectAsync(room, "d-alice-6", "Alice");
        await using var bob = await fixture.ConnectOutsideAsync("d-bob-6", "Bob");
        var asking = NextDoor(alice, d => d.Knocking.Count == 1);
        await bob.InvokeAsync<bool>("Knock", room);
        await asking;

        var answered = Next<bool>(bob, "KnockAnswered");
        await alice.InvokeAsync("SetOpen", true);

        Assert.True(await answered);
        Assert.True((await bob.InvokeAsync<JoinReply>("Join", room)).Admitted);
    }

    [Fact]
    public async Task Asking_at_an_open_room_needs_no_answer()
    {
        var room = NewRoom();
        await using var alice = await fixture.ConnectOpenAsync(room, "d-alice-7", "Alice");
        await using var bob = await fixture.ConnectOutsideAsync("d-bob-7", "Bob");

        Assert.True(await bob.InvokeAsync<bool>("Knock", room));
    }

    [Fact]
    public async Task A_private_room_s_routes_refuse_a_stranger_and_not_machine_work()
    {
        var room = NewRoom();
        await using var alice = await fixture.ConnectAsync(room, "d-alice-8", "Alice");
        var stranger = fixture.CreatePersonClient("d-stranger-8", "Stranger");

        Assert.Equal(HttpStatusCode.Forbidden, (await stranger.GetAsync($"/api/sessions/{room}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await stranger.GetAsync($"/api/sessions/{room}/participants")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await stranger.PostAsJsonAsync($"/api/sessions/{room}/play", new RoomPlaybackRequest())).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await stranger.PostAsJsonAsync($"/api/sessions/{room}/title", new SetTitleRequest(SessionFixture.ReadyTitle))).StatusCode);

        var service = fixture.CreateServiceClient();
        Assert.Equal(HttpStatusCode.OK,
            (await service.PostAsJsonAsync($"/api/sessions/{room}/title", new SetTitleRequest(SessionFixture.ReadyTitle))).StatusCode);
    }

    [Fact]
    public async Task A_person_is_listed_only_the_rooms_they_may_enter()
    {
        var mine = NewRoom();
        var theirs = NewRoom();
        var open = NewRoom();
        await using var alice = await fixture.ConnectAsync(mine, "d-alice-9", "Alice");
        await using var bob = await fixture.ConnectAsync(theirs, "d-bob-9", "Bob");
        await using var carol = await fixture.ConnectOpenAsync(open, "d-carol-9", "Carol");

        var listed = await fixture.CreatePersonClient("d-alice-9", "Alice")
            .GetFromJsonAsync("api/sessions", ManifestJsonContext.Default.IReadOnlyListRoomSummary);
        var ids = listed!.Select(r => r.SessionId).ToHashSet();
        Assert.Contains(mine, ids);
        Assert.Contains(open, ids);
        Assert.DoesNotContain(theirs, ids);

        var all = await fixture.CreateServiceClient()
            .GetFromJsonAsync("api/sessions", ManifestJsonContext.Default.IReadOnlyListRoomSummary);
        Assert.False(Assert.Single(all!, r => r.SessionId == theirs).Open);
        Assert.True(Assert.Single(all!, r => r.SessionId == open).Open);
    }

    [Fact]
    public async Task An_invite_lets_the_person_in_and_only_somebody_let_in_can_send_one()
    {
        var room = NewRoom();
        await using var alice = await fixture.ConnectAsync(room, "d-alice-10", "Alice");
        await using var bob = await fixture.ConnectOutsideAsync("d-bob-10", "Bob");
        // The directory learns a name from a token it has validated.
        await fixture.CreatePersonClient("d-carol-10", "Carol").GetAsync("/api/people");

        var stranger = await fixture.CreatePersonClient("d-carol-10", "Carol")
            .PostAsJsonAsync($"/api/rooms/{room}/invite", new InviteRequest { UserId = "d-bob-10" });
        Assert.Equal(HttpStatusCode.Forbidden, stranger.StatusCode);
        Assert.False((await bob.InvokeAsync<JoinReply>("Join", room)).Admitted);

        var invited = await fixture.CreatePersonClient("d-alice-10", "Alice")
            .PostAsJsonAsync($"/api/rooms/{room}/invite", new InviteRequest { UserId = "d-bob-10" });
        invited.EnsureSuccessStatusCode();
        Assert.True((await bob.InvokeAsync<JoinReply>("Join", room)).Admitted);
    }

    [Fact]
    public async Task Watch_now_into_somebody_else_s_private_room_is_refused()
    {
        var room = NewRoom();
        await using var alice = await fixture.ConnectAsync(room, "d-alice-11", "Alice");

        var response = await fixture.CreatePersonClient("d-bob-11", "Bob")
            .PostAsJsonAsync("/api/watch", new WatchNowRequest { ImdbId = "tt0113277", Room = room });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }
}
