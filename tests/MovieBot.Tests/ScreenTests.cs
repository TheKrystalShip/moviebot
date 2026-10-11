using System.Net.Http.Json;
using System.Threading.Channels;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;
using TheKrystalShip.MovieBot.Core;
using TheKrystalShip.MovieBot.Api.Auth;
using TheKrystalShip.MovieBot.Api.Sessions;
using Xunit;

namespace TheKrystalShip.MovieBot.Tests;

/// <summary>
/// A person's screens: a connection signed in through the television client is one, the person's other
/// connections hear it arrive, change room and leave, and nobody else does.
/// </summary>
public sealed class ScreenTests(SessionFixture fixture) : IClassFixture<SessionFixture>
{
    private static string Unique(string prefix) => prefix + Guid.NewGuid().ToString("n")[..8];

    /// <summary>What a connection hears of one message, in the order it arrives.</summary>
    private sealed class Heard<T>
    {
        private readonly Channel<T> _messages = Channel.CreateUnbounded<T>();

        public Heard(HubConnection connection, string method) =>
            connection.On<T>(method, value => _messages.Writer.TryWrite(value));

        public async Task<T> Next() =>
            await _messages.Reader.ReadAsync(new CancellationTokenSource(TimeSpan.FromSeconds(10)).Token);

        /// <summary>The next message that satisfies <paramref name="match"/>, skipping those that do not.</summary>
        public async Task<T> Until(Func<T, bool> match)
        {
            while (true)
            {
                var value = await Next();
                if (match(value)) return value;
            }
        }

        public bool Silent => !_messages.Reader.TryPeek(out _);
    }

    [Fact]
    public async Task A_television_arrives_as_a_screen_with_its_name()
    {
        var person = Unique("p-");
        var phone = await fixture.ConnectOutsideAsync(person, "Alice");
        var arrived = new Heard<ScreenView>(phone, "ScreenArrived");

        var sid = Unique("sid-");
        await fixture.ConnectScreenAsync(person, sid, "Living room");

        var screen = await arrived.Next();
        Assert.Equal(sid, screen.Id);
        Assert.Equal("Living room", screen.Name);
        Assert.True(screen.Connected);
        Assert.Null(screen.Room);
    }

    [Fact]
    public async Task A_phone_that_connects_later_asks_for_the_screens_there_are()
    {
        var person = Unique("p-");
        var sid = Unique("sid-");
        await fixture.ConnectScreenAsync(person, sid, "Bedroom");

        var phone = await fixture.ConnectOutsideAsync(person, "Alice");
        var screens = await phone.InvokeAsync<List<ScreenView>>("Screens");

        var screen = Assert.Single(screens);
        Assert.Equal(sid, screen.Id);
        Assert.Equal("Bedroom", screen.Name);
        Assert.True(screen.Connected);
    }

    [Fact]
    public async Task A_screen_without_a_name_has_none()
    {
        var person = Unique("p-");
        var phone = await fixture.ConnectOutsideAsync(person, "Alice");
        var arrived = new Heard<ScreenView>(phone, "ScreenArrived");

        await fixture.ConnectScreenAsync(person, Unique("sid-"));

        Assert.Null((await arrived.Next()).Name);
    }

    [Fact]
    public async Task A_screen_reports_the_room_it_joins_and_what_the_room_holds()
    {
        var person = Unique("p-");
        var phone = await fixture.ConnectOutsideAsync(person, "Alice");
        var changed = new Heard<ScreenView>(phone, "ScreenChanged");

        var room = Unique("screen-room-");
        var tv = await fixture.ConnectScreenAsync(person, Unique("sid-"), "Living room");
        Assert.True((await tv.InvokeAsync<JoinReply>("Join", room)).Admitted);

        var joined = await changed.Until(s => s.Room == room);
        Assert.Null(joined.TitleId);

        await tv.InvokeAsync<SessionStatePush>("LoadTitle", SessionFixture.Heat, null);
        var holding = await changed.Until(s => s.TitleId == SessionFixture.Heat);
        Assert.Equal(room, holding.Room);
        Assert.Contains("Heat", holding.Title);
        Assert.True(holding.Paused);

        await tv.InvokeAsync<SessionStatePush>("Play", 5.0);
        Assert.False((await changed.Until(s => s.Paused == false)).Paused);

        var listed = Assert.Single(await phone.InvokeAsync<List<ScreenView>>("Screens"));
        Assert.Equal(room, listed.Room);
        Assert.Equal(SessionFixture.Heat, listed.TitleId);
    }

    [Fact]
    public async Task A_screen_follows_its_connection_into_another_room()
    {
        var person = Unique("p-");
        var phone = await fixture.ConnectOutsideAsync(person, "Alice");
        var changed = new Heard<ScreenView>(phone, "ScreenChanged");
        var first = Unique("screen-room-");
        var second = Unique("screen-room-");

        var tv = await fixture.ConnectScreenAsync(person, Unique("sid-"));
        await tv.InvokeAsync<JoinReply>("Join", first);
        await changed.Until(s => s.Room == first);
        await tv.InvokeAsync<JoinReply>("Join", second);

        Assert.Equal(second, (await changed.Until(s => s.Room == second)).Room);
    }

    [Fact]
    public async Task A_screen_whose_connection_closes_is_not_connected_and_in_no_room()
    {
        var person = Unique("p-");
        var phone = await fixture.ConnectOutsideAsync(person, "Alice");
        var changed = new Heard<ScreenView>(phone, "ScreenChanged");

        var tv = await fixture.ConnectScreenAsync(person, Unique("sid-"), "Living room");
        await tv.InvokeAsync<JoinReply>("Join", Unique("screen-room-"));
        await changed.Until(s => s.Room is not null);

        await tv.StopAsync();

        var gone = await changed.Until(s => !s.Connected);
        Assert.Null(gone.Room);
        Assert.Equal("Living room", gone.Name);
        Assert.False(Assert.Single(await phone.InvokeAsync<List<ScreenView>>("Screens")).Connected);
    }

    [Fact]
    public async Task A_screen_connecting_again_arrives_again()
    {
        var person = Unique("p-");
        var phone = await fixture.ConnectOutsideAsync(person, "Alice");
        var arrived = new Heard<ScreenView>(phone, "ScreenArrived");
        var sid = Unique("sid-");

        var tv = await fixture.ConnectScreenAsync(person, sid, "Living room");
        await arrived.Next();
        await tv.StopAsync();
        await fixture.ConnectScreenAsync(person, sid, "Living room");

        var again = await arrived.Next();
        Assert.Equal(sid, again.Id);
        Assert.True(again.Connected);
        Assert.Single(await phone.InvokeAsync<List<ScreenView>>("Screens"));
    }

    [Fact]
    public async Task A_television_signed_in_again_is_a_new_screen_and_the_old_one_is_forgotten()
    {
        var person = Unique("p-");
        var phone = await fixture.ConnectOutsideAsync(person, "Alice");
        var arrived = new Heard<ScreenView>(phone, "ScreenArrived");
        var left = new Heard<string>(phone, "ScreenLeft");
        var oldSid = Unique("old-");
        var newSid = Unique("new-");

        var old = await fixture.ConnectScreenAsync(person, oldSid, "Living room");
        await arrived.Next();
        await old.StopAsync();
        await fixture.ConnectScreenAsync(person, newSid, "Living room");

        Assert.Equal(newSid, (await arrived.Next()).Id);
        Assert.Equal(oldSid, await left.Next());
        Assert.Equal(newSid, Assert.Single(await phone.InvokeAsync<List<ScreenView>>("Screens")).Id);
    }

    [Fact]
    public async Task Two_televisions_that_are_both_connected_are_two_screens()
    {
        var person = Unique("p-");
        await fixture.ConnectScreenAsync(person, Unique("a-"), "Living room");
        await fixture.ConnectScreenAsync(person, Unique("b-"), "Living room");

        var phone = await fixture.ConnectOutsideAsync(person, "Alice");

        Assert.Equal(2, (await phone.InvokeAsync<List<ScreenView>>("Screens")).Count);
    }

    [Fact]
    public async Task Another_person_neither_hears_a_screen_nor_is_handed_it()
    {
        var alice = Unique("alice-");
        var bob = Unique("bob-");
        var alicesPhone = await fixture.ConnectOutsideAsync(alice, "Alice");
        var bobsPhone = await fixture.ConnectOutsideAsync(bob, "Bob");
        var heardByAlice = new Heard<ScreenView>(alicesPhone, "ScreenArrived");
        var heardByBob = new Heard<ScreenView>(bobsPhone, "ScreenArrived");
        var bobsChanges = new Heard<ScreenView>(bobsPhone, "ScreenChanged");

        var tv = await fixture.ConnectScreenAsync(alice, Unique("sid-"), "Living room");
        await tv.InvokeAsync<JoinReply>("Join", Unique("screen-room-"));
        await heardByAlice.Next();

        // Alice's phone has heard the arrival, so the server has finished telling anyone.
        await Task.Delay(300);
        Assert.True(heardByBob.Silent);
        Assert.True(bobsChanges.Silent);
        Assert.Empty(await bobsPhone.InvokeAsync<List<ScreenView>>("Screens"));
    }

    [Fact]
    public async Task A_screen_is_not_told_about_screens()
    {
        var person = Unique("p-");
        var first = await fixture.ConnectScreenAsync(person, Unique("a-"));
        var heard = new Heard<ScreenView>(first, "ScreenArrived");

        await fixture.ConnectScreenAsync(person, Unique("b-"));
        await Task.Delay(300);

        Assert.True(heard.Silent);
    }

    [Theory]
    [InlineData("cinema-site")]
    [InlineData("cinema-activity")]
    public async Task A_connection_not_signed_in_through_the_television_client_is_no_screen(string client)
    {
        var person = Unique("p-");
        var phone = await fixture.ConnectOutsideAsync(person, "Alice");
        var arrived = new Heard<ScreenView>(phone, "ScreenArrived");

        // The token even carries a session and a device name; the client is what decides.
        await fixture.ConnectScreenAsync(person, Unique("sid-"), "Pretending", clientId: client);
        await Task.Delay(300);

        Assert.True(arrived.Silent);
        Assert.Empty(await phone.InvokeAsync<List<ScreenView>>("Screens"));
    }

    [Fact]
    public async Task A_television_token_with_no_session_is_no_screen()
    {
        var person = Unique("p-");
        var phone = await fixture.ConnectOutsideAsync(person, "Alice");
        var arrived = new Heard<ScreenView>(phone, "ScreenArrived");

        await fixture.ConnectScreenAsync(person, sid: "", clientId: "cinema-tv");
        await Task.Delay(300);

        Assert.True(arrived.Silent);
        Assert.Empty(await phone.InvokeAsync<List<ScreenView>>("Screens"));
    }

    [Fact]
    public async Task A_guest_joins_no_persons_group_and_asks_for_no_screens()
    {
        var alice = Unique("alice-");
        var room = Unique("screen-room-");
        var inRoom = await fixture.ConnectAsync(room, alice, "Alice");
        using var response = await fixture.CreatePersonClient(alice, "Alice").PostAsync($"/api/rooms/{room}/guests", null);
        response.EnsureSuccessStatusCode();
        var link = (await response.Content.ReadFromJsonAsync<GuestLink>())!;

        var guest = new HubConnectionBuilder()
            .WithUrl(new Uri(fixture.Server.BaseAddress, "/hub/session"), o =>
            {
                o.HttpMessageHandlerFactory = _ => fixture.Server.CreateHandler();
                o.Transports = Microsoft.AspNetCore.Http.Connections.HttpTransportType.LongPolling;
                o.AccessTokenProvider = () => Task.FromResult<string?>(GuestCodes.CredentialPrefix + link.Path["/g/".Length..]);
            })
            .AddJsonProtocol(o => o.PayloadSerializerOptions.Converters.Add(
                new System.Text.Json.Serialization.JsonStringEnumConverter()))
            .Build();
        await guest.StartAsync();
        var heardByGuest = new Heard<ScreenView>(guest, "ScreenArrived");
        var heardByAlice = new Heard<ScreenView>(inRoom, "ScreenArrived");

        await fixture.ConnectScreenAsync(alice, Unique("sid-"), "Living room");
        await heardByAlice.Next();
        await Task.Delay(300);

        Assert.True(heardByGuest.Silent);
        await Assert.ThrowsAsync<HubException>(() => guest.InvokeAsync<List<ScreenView>>("Screens"));
    }
}
