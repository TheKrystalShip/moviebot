using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Reflection;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;
using TheKrystalShip.MovieBot.Api.Auth;
using TheKrystalShip.MovieBot.Api.Sessions;
using TheKrystalShip.MovieBot.Core;
using Xunit;

namespace TheKrystalShip.MovieBot.Tests;

/// <summary>A host whose rooms keep guests a second past their last person, and take two at once.</summary>
public sealed class GuestFixture : SessionFixture
{
    protected override Dictionary<string, string?> MoreSettings() => new()
    {
        ["Rooms:GuestGrace"] = "00:00:01",
        ["Rooms:GuestsPerRoom"] = "2",
    };
}

/// <summary>
/// A room's guest link: somebody with no account watches the room on it, and can change nothing about
/// the room, reach nothing else, or stay once nobody with an account is watching with them.
/// </summary>
public sealed class GuestTests(GuestFixture fixture) : IClassFixture<GuestFixture>
{
    private static string NewRoom() => "guest-" + Guid.NewGuid().ToString("n")[..8];

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

    /// <summary>The room's guest link, asked for by somebody in it.</summary>
    private async Task<string> LinkFor(string room, string userId, string name = "Someone")
    {
        using var response = await fixture.CreatePersonClient(userId, name).PostAsync($"/api/rooms/{room}/guests", null);
        response.EnsureSuccessStatusCode();
        var link = (await response.Content.ReadFromJsonAsync<GuestLink>())!;
        Assert.StartsWith("/g/", link.Path);
        return link.Path["/g/".Length..];
    }

    /// <summary>A hub connection opened on a guest link, in no room yet.</summary>
    private async Task<HubConnection> GuestConnection(string code)
    {
        var connection = new HubConnectionBuilder()
            .WithUrl(new Uri(fixture.Server.BaseAddress, "/hub/session"), o =>
            {
                o.HttpMessageHandlerFactory = _ => fixture.Server.CreateHandler();
                o.Transports = Microsoft.AspNetCore.Http.Connections.HttpTransportType.LongPolling;
                o.AccessTokenProvider = () => Task.FromResult<string?>(GuestCodes.CredentialPrefix + code);
            })
            .AddJsonProtocol(o => o.PayloadSerializerOptions.Converters.Add(
                new System.Text.Json.Serialization.JsonStringEnumConverter()))
            .Build();
        await connection.StartAsync();
        return connection;
    }

    private HttpClient GuestClient(string code)
    {
        var client = fixture.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", GuestCodes.CredentialPrefix + code);
        return client;
    }

    /// <summary>Alice in a room of her own holding Heat, and the room's guest link.</summary>
    private async Task<(HubConnection Alice, string Code)> RoomWithLink(string room, string alice = "g-alice")
    {
        var connection = await fixture.ConnectAsync(room, alice, "Alice");
        await connection.InvokeAsync<SessionStatePush>("LoadTitle", SessionFixture.Heat, null);
        return (connection, await LinkFor(room, alice, "Alice"));
    }

    [Fact]
    public async Task The_link_says_which_room_it_opens_and_what_it_is_watching()
    {
        var room = NewRoom();
        var (alice, code) = await RoomWithLink(room);
        await using var _ = alice;

        var invitation = await fixture.CreateClient().GetFromJsonAsync<GuestInvitation>($"/api/guest/{code}");

        Assert.Equal(room, invitation!.SessionId);
        Assert.Contains("Heat", invitation.Name);
        Assert.True(invitation.ExpiresAt > DateTimeOffset.UtcNow.AddHours(23));
    }

    [Fact]
    public async Task Everybody_in_the_room_is_handed_the_same_link_and_the_door_says_there_is_one()
    {
        var room = NewRoom();
        await using var alice = await fixture.ConnectAsync(room, "g-alice-2", "Alice");
        await using var bob = await fixture.ConnectAsync(room, "g-bob-2", "Bob");

        var told = Next<RoomDoor>(bob, "DoorChanged");
        var first = await LinkFor(room, "g-alice-2");
        var second = await LinkFor(room, "g-bob-2");

        Assert.Equal(first, second);
        Assert.NotNull((await told).Guests);
    }

    [Fact]
    public async Task A_guest_watches_the_room_and_hears_it_move()
    {
        var room = NewRoom();
        var (alice, code) = await RoomWithLink(room);
        await using var _ = alice;
        await using var guest = await GuestConnection(code);

        var roster = Next<List<Participant>>(alice, "ParticipantsChanged");
        var reply = await guest.InvokeAsync<JoinReply>("JoinAsGuest", "  Sam  ");

        Assert.True(reply.Admitted);
        Assert.Null(reply.Door);
        Assert.Equal(SessionFixture.Heat, reply.Push!.State.TitleId);
        Assert.Equal("Sam", reply.You!.DisplayName);
        Assert.True(reply.You.Guest);
        Assert.Contains(await roster, p => p.Guest && p.DisplayName == "Sam");

        var heard = Next<SessionStatePush>(guest, "StateChanged");
        await alice.InvokeAsync<SessionStatePush>("Play", 30.0);
        Assert.False((await heard).State.Paused);
    }

    /// <summary>Every method the hub answers, which is every one a client can name.</summary>
    private static IEnumerable<MethodInfo> HubMethods() =>
        typeof(SessionHub).GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(m => !m.IsSpecialName && m.Name != nameof(SessionHub.OnConnectedAsync)
                        && m.Name != nameof(SessionHub.OnDisconnectedAsync));

    /// <summary>The methods a guest may call; every other one is closed to them.</summary>
    private static readonly string[] OpenToGuests = ["JoinAsGuest", "ReportQuality", "ReportStats", "ReportTrouble", "ServerTime"];

    /// <summary>A plausible argument for a hub parameter, so a refusal is the method's and not the binder's.</summary>
    private static object? ArgumentFor(Type type, string room) =>
        type == typeof(string) ? room
        : type == typeof(double) || type == typeof(double?) ? 10.0
        : type == typeof(int) || type == typeof(int?) ? 1
        : type == typeof(bool) ? true
        : null;

    [Fact]
    public async Task A_guest_can_call_nothing_on_the_hub_but_what_is_open_to_them()
    {
        var room = NewRoom();
        var (alice, code) = await RoomWithLink(room);
        await using var _ = alice;
        await using var guest = await GuestConnection(code);
        var before = (await guest.InvokeAsync<JoinReply>("JoinAsGuest", "Sam")).Push!.State;

        var closed = HubMethods().Where(m => !OpenToGuests.Contains(m.Name)).ToList();
        Assert.Contains(closed, m => m.Name == "Play");
        foreach (var method in closed)
        {
            var args = method.GetParameters().Select(p => ArgumentFor(p.ParameterType, room)).ToArray();
            var refused = await Assert.ThrowsAsync<HubException>(() => guest.InvokeCoreAsync(method.Name, typeof(object), args));
            Assert.Contains("A guest watches", refused.Message);
        }

        var after = (await alice.InvokeAsync<JoinReply>("Join", room)).Push!.State;
        Assert.Equal(before.Revision, after.Revision);
        Assert.False(after.Waiting);
    }

    /// <summary>A hub caller's context as SignalR would hand one over, for calling the hub without its filter.</summary>
    private sealed class CallerContext(System.Security.Claims.ClaimsPrincipal user) : HubCallerContext
    {
        public override string ConnectionId { get; } = "guest-under-test";
        public override string? UserIdentifier => null;
        public override System.Security.Claims.ClaimsPrincipal? User => user;
        public override IDictionary<object, object?> Items { get; } = new Dictionary<object, object?>();
        public override Microsoft.AspNetCore.Http.Features.IFeatureCollection Features { get; } =
            new Microsoft.AspNetCore.Http.Features.FeatureCollection();
        public override CancellationToken ConnectionAborted => CancellationToken.None;
        public override void Abort() { }
    }

    [Fact]
    public async Task Every_method_that_moves_the_room_refuses_a_guest_without_the_gate()
    {
        // The hub itself, as a guest's connection would reach it if the filter were not there: every
        // method closed to guests still refuses, because each needs a person or an actor a guest has not got.
        var room = NewRoom();
        var (alice, code) = await RoomWithLink(room);
        await using var _ = alice;
        var passId = fixture.Services.GetRequiredService<GuestCodes>().PassIdOf(code)!;
        var guest = new System.Security.Claims.ClaimsPrincipal(new System.Security.Claims.ClaimsIdentity(
            [new System.Security.Claims.Claim("guest-pass", passId), new System.Security.Claims.Claim("room", room)],
            CinemaSchemes.Guest));
        Assert.NotNull(GuestViewer.Of(guest));

        using var scope = fixture.Services.CreateScope();
        var hub = ActivatorUtilities.CreateInstance<SessionHub>(scope.ServiceProvider);
        hub.Context = new CallerContext(guest);
        // As a guest that joined would hold it: the room it is watching.
        hub.Context.Items["sessionId"] = room;

        foreach (var method in HubMethods().Where(m => !OpenToGuests.Contains(m.Name)))
        {
            var args = method.GetParameters().Select(p => ArgumentFor(p.ParameterType, room)).ToArray();
            var refused = await Assert.ThrowsAsync<HubException>(async () =>
            {
                try
                {
                    if (method.Invoke(hub, args) is Task task) await task;
                }
                catch (TargetInvocationException ex) when (ex.InnerException is not null)
                {
                    throw ex.InnerException;
                }
            });
            Assert.DoesNotContain("A guest watches", refused.Message);
        }
    }

    [Fact]
    public void Only_the_methods_marked_for_guests_are_open_to_them() =>
        Assert.Equal(OpenToGuests,
            HubMethods().Where(m => m.IsDefined(typeof(GuestAllowedAttribute))).Select(m => m.Name).Order(StringComparer.Ordinal));

    [Fact]
    public async Task Every_route_not_opened_to_guests_refuses_a_guest_link()
    {
        var room = NewRoom();
        var (alice, code) = await RoomWithLink(room);
        await using var _ = alice;
        var guest = GuestClient(code);

        var endpoints = fixture.Services.GetRequiredService<Microsoft.AspNetCore.Routing.EndpointDataSource>().Endpoints
            .OfType<Microsoft.AspNetCore.Routing.RouteEndpoint>()
            .Where(e => e.Metadata.GetMetadata<RequiredAccess>() is not null)
            .ToList();

        // The whole of what a guest link may open, named here so that opening anything more is a change
        // to this test as well as to the route.
        var opened = endpoints.Where(e => e.Metadata.GetMetadata<RequiredAccess>()!.Guest is not null)
            .Select(e => e.RoutePattern.RawText!).Distinct().Order(StringComparer.Ordinal);
        Assert.Equal(["/api/titles/{id}", "/api/titles/{id}/ticket", "/hub/session", "/hub/session/negotiate"], opened);

        var asked = 0;
        foreach (var endpoint in endpoints.Where(e => e.Metadata.GetMetadata<RequiredAccess>()!.Guest is null))
        {
            var path = System.Text.RegularExpressions.Regex.Replace(endpoint.RoutePattern.RawText!, @"\{\*{0,2}([^}:]+)(:[^}]*)?\}",
                m => m.Groups[1].Value is "room" or "sessionId" ? room : "1");
            var methods = endpoint.Metadata.GetMetadata<Microsoft.AspNetCore.Routing.HttpMethodMetadata>()?.HttpMethods ?? ["GET"];
            foreach (var method in methods)
            {
                // A route that takes a body is chosen only for a request that carries JSON; without one
                // the request would reach the site's fallback and never the route under test.
                using var request = new HttpRequestMessage(new HttpMethod(method), path)
                {
                    Content = method is "GET" or "HEAD" or "DELETE" ? null
                        : new StringContent("{}", System.Text.Encoding.UTF8, "application/json"),
                };
                using var response = await guest.SendAsync(request);
                asked++;
                Assert.True(response.StatusCode == HttpStatusCode.Unauthorized,
                    $"{method} {path} answered a guest {(int)response.StatusCode}.");
            }
        }
        Assert.True(asked > 50, $"Only {asked} routes were asked.");
    }

    [Fact]
    public async Task A_guest_reaches_the_film_the_room_holds_and_nothing_else()
    {
        var room = NewRoom();
        var (alice, code) = await RoomWithLink(room);
        await using var _ = alice;
        var guest = GuestClient(code);

        Assert.Equal(HttpStatusCode.OK, (await guest.GetAsync($"/api/titles/{SessionFixture.Heat}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await guest.GetAsync($"/api/titles/{SessionFixture.Heat}/ticket")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await guest.GetAsync($"/api/titles/{SessionFixture.Collateral}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await guest.GetAsync($"/api/titles/{SessionFixture.Collateral}/ticket")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await guest.GetAsync("/api/titles")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await guest.GetAsync("/api/sessions")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await guest.GetAsync($"/api/sessions/{room}/participants")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await guest.GetAsync("/api/me/preferences")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await guest.PostAsync($"/api/rooms/{room}/guests", null)).StatusCode);
    }

    [Fact]
    public async Task A_guest_follows_the_room_to_the_next_film_it_holds()
    {
        var room = NewRoom();
        var (alice, code) = await RoomWithLink(room);
        await using var _ = alice;
        var guest = GuestClient(code);

        await alice.InvokeAsync<SessionStatePush>("LoadTitle", SessionFixture.Collateral, null);

        Assert.Equal(HttpStatusCode.OK, (await guest.GetAsync($"/api/titles/{SessionFixture.Collateral}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await guest.GetAsync($"/api/titles/{SessionFixture.Heat}")).StatusCode);
    }

    [Fact]
    public async Task A_guest_hears_nothing_of_the_door()
    {
        var room = NewRoom();
        var (alice, code) = await RoomWithLink(room);
        await using var _ = alice;
        await alice.InvokeAsync("SetOpen", false);
        await using var guest = await GuestConnection(code);
        await guest.InvokeAsync<JoinReply>("JoinAsGuest", "Sam");
        var doors = 0;
        guest.On<RoomDoor>("DoorChanged", _ => Interlocked.Increment(ref doors));
        await using var bob = await fixture.ConnectOutsideAsync("g-bob-3", "Bob");

        var asking = Next<RoomDoor>(alice, "DoorChanged");
        await bob.InvokeAsync<bool>("Knock", room);
        await asking;
        // Pushes reach a connection in the order they were sent, so once the guest hears this, it has
        // heard everything sent to it before.
        var marker = Next<SessionStatePush>(guest, "StateChanged");
        await alice.InvokeAsync<SessionStatePush>("Pause", 5.0);
        await marker;

        Assert.Equal(0, doors);
    }

    [Fact]
    public async Task Taking_the_link_away_sends_its_guests_away_and_opens_nothing_after()
    {
        var room = NewRoom();
        var (alice, code) = await RoomWithLink(room);
        await using var _ = alice;
        await using var guest = await GuestConnection(code);
        await guest.InvokeAsync<JoinReply>("JoinAsGuest", "Sam");

        var ended = Next<string>(guest, "GuestAccessEnded");
        var closed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        guest.Closed += _ => { closed.TrySetResult(); return Task.CompletedTask; };
        using var revoked = await fixture.CreatePersonClient("g-alice", "Alice").DeleteAsync($"/api/rooms/{room}/guests");

        Assert.Equal(HttpStatusCode.NoContent, revoked.StatusCode);
        Assert.Equal(GuestRefusals.Ended, await ended);
        await closed.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(HttpStatusCode.NotFound, (await fixture.CreateClient().GetAsync($"/api/guest/{code}")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await GuestClient(code).GetAsync($"/api/titles/{SessionFixture.Heat}")).StatusCode);
        await Assert.ThrowsAnyAsync<Exception>(() => GuestConnection(code));
    }

    [Fact]
    public async Task Guests_neither_hold_the_room_nor_are_recorded_as_watching()
    {
        var room = NewRoom();
        var (alice, code) = await RoomWithLink(room);
        await using var _ = alice;
        await using var guest = await GuestConnection(code);
        await guest.InvokeAsync<JoinReply>("JoinAsGuest", "Sam");
        await alice.InvokeAsync<SessionStatePush>("Play", 100.0);

        var viewing = fixture.Services.GetRequiredService<SessionStore>().Viewing().Single(v => v.SessionId == room);
        Assert.Equal(["g-alice"], viewing.Viewers.Select(v => v.UserId));

        // Alice is the only one whose player counts, so her waiting holds the room.
        var held = Next<SessionStatePush>(alice, "StateChanged");
        await alice.InvokeAsync("Buffering", true, 100.0);
        Assert.True((await held).State.Waiting);
    }

    [Fact]
    public async Task Guests_left_with_nobody_who_has_an_account_are_sent_away()
    {
        var room = NewRoom();
        var (alice, code) = await RoomWithLink(room);
        await using var guest = await GuestConnection(code);
        await guest.InvokeAsync<JoinReply>("JoinAsGuest", "Sam");
        var ended = Next<string>(guest, "GuestAccessEnded");

        await alice.DisposeAsync();
        await Task.Delay(TimeSpan.FromSeconds(1.5));
        await fixture.Services.GetRequiredService<GuestKeeper>().PassAsync();

        Assert.Equal(GuestRefusals.Unattended, await ended);
        await using var late = await GuestConnection(code);
        var refused = await late.InvokeAsync<JoinReply>("JoinAsGuest", "Lee");
        Assert.False(refused.Admitted);
        Assert.Equal(GuestRefusals.Unattended, refused.Refused);
        Assert.Null(refused.Push);
    }

    [Fact]
    public async Task A_room_takes_as_many_guests_as_it_is_set_to()
    {
        var room = NewRoom();
        var (alice, code) = await RoomWithLink(room);
        await using var _ = alice;
        await using var one = await GuestConnection(code);
        await using var two = await GuestConnection(code);
        await using var three = await GuestConnection(code);

        var first = await one.InvokeAsync<JoinReply>("JoinAsGuest", "One");
        Assert.True((await two.InvokeAsync<JoinReply>("JoinAsGuest", "Two")).Admitted);
        var refused = await three.InvokeAsync<JoinReply>("JoinAsGuest", "Three");
        var again = await one.InvokeAsync<JoinReply>("JoinAsGuest", "One");

        Assert.Equal(GuestRefusals.Full, refused.Refused);
        Assert.True(again.Admitted);
        Assert.Equal(first.You!.UserId, again.You!.UserId);
    }

    [Fact]
    public async Task Handing_out_a_link_takes_its_action_and_a_place_in_the_room()
    {
        var room = NewRoom();
        await using var alice = await fixture.ConnectPrivateAsync(room, "g-alice-9", "Alice");

        var withoutAction = fixture.CreatePersonClient("g-alice-9", "Alice",
            TestIssuer.Everything.Where(a => a != CinemaActions.Guests));
        var outsider = fixture.CreatePersonClient("g-carol-9", "Carol");

        Assert.Equal(HttpStatusCode.Forbidden, (await withoutAction.PostAsync($"/api/rooms/{room}/guests", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await outsider.PostAsync($"/api/rooms/{room}/guests", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await outsider.DeleteAsync($"/api/rooms/{room}/guests")).StatusCode);
    }

    [Fact]
    public async Task A_code_this_service_did_not_sign_opens_nothing()
    {
        var room = NewRoom();
        var (alice, code) = await RoomWithLink(room);
        await using var _ = alice;
        var forged = code[..^1] + (code[^1] == 'A' ? 'B' : 'A');

        Assert.Equal(HttpStatusCode.NotFound, (await fixture.CreateClient().GetAsync($"/api/guest/{forged}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await fixture.CreateClient().GetAsync("/api/guest/nothing")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await GuestClient(forged).GetAsync($"/api/titles/{SessionFixture.Heat}")).StatusCode);
    }

    [Fact]
    public async Task A_room_s_guest_link_is_kept_with_the_room()
    {
        var room = NewRoom();
        var (alice, code) = await RoomWithLink(room);
        await using var _ = alice;
        var kept = fixture.Services.GetRequiredService<SessionStore>().Snapshot().Where(r => r.State.SessionId == room).ToList();

        var restored = new SessionStore(fixture.Services.GetRequiredService<Api.Library.TitleLibrary>(), TimeProvider.System);
        restored.Restore(kept);

        var passId = fixture.Services.GetRequiredService<GuestCodes>().PassIdOf(code)!;
        Assert.Equal(room, restored.RoomOfGuests(passId));
        Assert.NotNull(restored.Door(room)!.Guests);
    }

    [Theory]
    [InlineData("  Sam  ", "Sam")]
    [InlineData("Sam\t\n the   man", "Sam the man")]
    [InlineData("‮evil\u0007", "evil")]
    [InlineData("", "Guest")]
    [InlineData(null, "Guest")]
    [InlineData("abcdefghijklmnopqrstuvwxyzabcdefghij", "abcdefghijklmnopqrstuvwxyzabcdef")]
    public void A_guest_is_shown_by_the_name_they_gave_cleaned(string? typed, string shown) =>
        Assert.Equal(shown, RoomGuests.NameOf(typed));
}
