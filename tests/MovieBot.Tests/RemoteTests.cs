using System.Net.Http.Json;
using System.Reflection;
using System.Threading.Channels;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;
using TheKrystalShip.MovieBot.Api.Auth;
using TheKrystalShip.MovieBot.Api.Sessions;
using TheKrystalShip.MovieBot.Core;
using Xunit;

namespace TheKrystalShip.MovieBot.Tests;

/// <summary>
/// A phone paired with a screen drives the screen's room as a remote: it moves the film like any member,
/// follows its screen from room to room, and is in nothing the room counts.
/// </summary>
public sealed class RemoteTests(SessionFixture fixture) : IClassFixture<SessionFixture>
{
    private static string Unique(string prefix) => prefix + Guid.NewGuid().ToString("n")[..8];

    private sealed class Heard<T>
    {
        private readonly Channel<T> _messages = Channel.CreateUnbounded<T>();

        public Heard(HubConnection connection, string method) =>
            connection.On<T>(method, value => _messages.Writer.TryWrite(value));

        public async Task<T> Next() =>
            await _messages.Reader.ReadAsync(new CancellationTokenSource(TimeSpan.FromSeconds(10)).Token);

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

    private SessionStore Sessions => fixture.Services.GetRequiredService<SessionStore>();
    private ViewerQuality Quality => fixture.Services.GetRequiredService<ViewerQuality>();

    /// <summary>A television in a room holding Heat, and a phone of the person's paired with it and connected.</summary>
    private async Task<(HubConnection Tv, string TvSid, string Room, HubConnection Phone, string PhoneSid, string Person)> Setup()
    {
        var person = Unique("rp-");
        var tvSid = Unique("tv-");
        var phoneSid = Unique("phone-");
        var tv = await fixture.ConnectScreenAsync(person, tvSid, "Living room");
        var room = Unique("remote-room-");
        Assert.True((await tv.InvokeAsync<JoinReply>("Join", room)).Admitted);
        await tv.InvokeAsync<SessionStatePush>("LoadTitle", SessionFixture.Heat, null);
        var phone = await fixture.ConnectPhoneAsync(person, phoneSid);
        using var paired = await fixture.PairAsync(tv, person, phoneSid);
        paired.EnsureSuccessStatusCode();
        return (tv, tvSid, room, phone, phoneSid, person);
    }

    [Fact]
    public async Task A_paired_phone_joins_its_screens_room_as_a_remote_and_is_handed_where_the_room_is()
    {
        var (_, tvSid, room, phone, _, _) = await Setup();

        var reply = await phone.InvokeAsync<JoinReply>("JoinAsRemote", tvSid);

        Assert.True(reply.Admitted);
        Assert.Equal(room, reply.Push!.State.SessionId);
        Assert.Equal(SessionFixture.Heat, reply.Push.State.TitleId);
    }

    [Fact]
    public async Task A_phone_that_is_not_paired_with_the_screen_is_refused()
    {
        var (_, tvSid, _, _, _, person) = await Setup();
        var strangerOfTheSamePerson = await fixture.ConnectPhoneAsync(person, Unique("phone-"));
        var anotherPersons = await fixture.ConnectPhoneAsync(Unique("rp-other-"), Unique("phone-"));

        foreach (var phone in new[] { strangerOfTheSamePerson, anotherPersons })
        {
            var refused = await Assert.ThrowsAsync<HubException>(() => phone.InvokeAsync<JoinReply>("JoinAsRemote", tvSid));
            Assert.Contains("not paired", refused.Message);
        }
    }

    [Fact]
    public async Task A_phone_paired_with_one_screen_cannot_take_another_of_the_same_person()
    {
        var (_, _, _, phone, _, person) = await Setup();
        var otherSid = Unique("tv-");
        await fixture.ConnectScreenAsync(person, otherSid, "Bedroom");

        await Assert.ThrowsAsync<HubException>(() => phone.InvokeAsync<JoinReply>("JoinAsRemote", otherSid));
    }

    [Fact]
    public async Task A_television_an_activity_and_a_connection_with_no_account_cannot_become_a_remote()
    {
        var (_, tvSid, _, _, _, person) = await Setup();
        var anotherTv = await fixture.ConnectScreenAsync(person, Unique("tv-"), "Bedroom");
        var activity = await fixture.ConnectScreenAsync(person, "", clientId: "cinema-activity");
        var noSession = await fixture.ConnectOutsideAsync(person, "Alice");

        foreach (var connection in new[] { anotherTv, activity, noSession })
            await Assert.ThrowsAsync<HubException>(() => connection.InvokeAsync<JoinReply>("JoinAsRemote", tvSid));
    }

    [Fact]
    public async Task A_guest_cannot_become_a_remote()
    {
        var (_, tvSid, room, _, _, person) = await Setup();
        using var link = await fixture.CreatePersonClient(person).PostAsync($"/api/rooms/{room}/guests", null);
        link.EnsureSuccessStatusCode();
        var code = (await link.Content.ReadFromJsonAsync<GuestLink>())!.Path["/g/".Length..];
        var guest = new HubConnectionBuilder()
            .WithUrl(new Uri(fixture.Server.BaseAddress, "/hub/session"), o =>
            {
                o.HttpMessageHandlerFactory = _ => fixture.Server.CreateHandler();
                o.Transports = Microsoft.AspNetCore.Http.Connections.HttpTransportType.LongPolling;
                o.AccessTokenProvider = () => Task.FromResult<string?>(GuestCodes.CredentialPrefix + code);
            })
            .AddJsonProtocol(o => o.PayloadSerializerOptions.Converters.Add(
                new System.Text.Json.Serialization.JsonStringEnumConverter()))
            .Build();
        await guest.StartAsync();

        var refused = await Assert.ThrowsAsync<HubException>(() => guest.InvokeAsync<JoinReply>("JoinAsRemote", tvSid));
        Assert.Contains("A guest watches", refused.Message);
    }

    [Fact]
    public async Task A_remotes_play_pause_and_seek_move_the_room_and_reach_the_screen_naming_the_person_alone()
    {
        var (tv, tvSid, _, phone, _, person) = await Setup();
        await phone.InvokeAsync<JoinReply>("JoinAsRemote", tvSid);
        var onTv = new Heard<SessionStatePush>(tv, "StateChanged");
        var onPhone = new Heard<SessionStatePush>(phone, "StateChanged");

        await phone.InvokeAsync<SessionStatePush>("Play", 12.0);
        var played = await onTv.Until(p => !p.State.Paused);
        Assert.Equal(person, played.State.UpdatedBy!.UserId);
        Assert.Null(played.State.UpdatedBy.Via);
        Assert.False((await onPhone.Until(p => !p.State.Paused)).State.Paused);

        await phone.InvokeAsync<SessionStatePush>("Seek", 300.0);
        Assert.InRange((await onTv.Until(p => p.State.PositionSeconds >= 299)).State.PositionSeconds, 299, 301);

        await phone.InvokeAsync<SessionStatePush>("Pause", 301.0);
        Assert.True((await onTv.Until(p => p.State.Paused)).State.Paused);

        await phone.InvokeAsync<SessionStatePush>("LoadTitle", SessionFixture.Collateral, null);
        Assert.Equal(SessionFixture.Collateral, (await onTv.Until(p => p.State.TitleId == SessionFixture.Collateral)).State.TitleId);
    }

    [Fact]
    public async Task A_remote_is_in_no_roster_head_count_hold_plan_or_watch_record()
    {
        var (tv, tvSid, room, phone, _, person) = await Setup();
        var roster = new Heard<List<Participant>>(tv, "ParticipantsChanged");
        var spans = new List<WatchedSpan>();
        Sessions.Watched += spans.Add;
        var health = await fixture.CreateClient().GetFromJsonAsync<HealthCounts>("/health");

        var reply = await phone.InvokeAsync<JoinReply>("JoinAsRemote", tvSid);
        Assert.True(reply.Admitted);
        await tv.InvokeAsync<SessionStatePush>("Play", 5.0);

        // Nothing announced the remote, and the room holds one participant: the screen's player.
        Assert.Single(Sessions.Participants(room));
        Assert.DoesNotContain(roster.Silent ? [] : await Drain(roster), list => list.Count != 1);
        var after = await fixture.CreateClient().GetFromJsonAsync<HealthCounts>("/health");
        Assert.Equal(health!.Watching, after!.Watching);
        Assert.Single(Quality.WantedHeights(room));

        // The only player waiting holds the room, though a remote is attached; the remote adds nobody to wait for.
        await tv.InvokeAsync("Buffering", true, 5.0);
        Assert.True(Sessions.Get(room)!.Waiting);
        await tv.InvokeAsync("Buffering", false, 5.0);

        // A remote driving the room credits nobody but the screen's person, once.
        await phone.InvokeAsync<SessionStatePush>("Pause", 20.0);
        Assert.NotEmpty(spans);
        Assert.All(spans, span => Assert.Equal(person, Assert.Single(span.Viewers).UserId));
        Sessions.Watched -= spans.Add;
    }

    private static async Task<List<T>> Drain<T>(Heard<T> heard)
    {
        var all = new List<T>();
        while (!heard.Silent) all.Add(await heard.Next());
        return all;
    }

    private sealed record HealthCounts(string Status, int Rooms, int Watching);

    [Fact]
    public async Task A_remote_that_is_not_anyones_viewer_leaves_a_room_with_only_a_remote_unwatched()
    {
        var (tv, tvSid, room, phone, _, _) = await Setup();
        await phone.InvokeAsync<JoinReply>("JoinAsRemote", tvSid);

        await tv.StopAsync();

        Assert.Empty(Sessions.Participants(room));
        Assert.DoesNotContain(Sessions.Occupied(), r => r.SessionId == room);
    }

    [Fact]
    public async Task A_remote_reports_neither_buffering_nor_quality_and_opens_no_door()
    {
        var (_, tvSid, room, phone, _, _) = await Setup();
        await phone.InvokeAsync<JoinReply>("JoinAsRemote", tvSid);
        var heightsBefore = Quality.WantedHeights(room);

        foreach (var (method, args) in new (string, object[])[]
        {
            ("Buffering", [true, 3.0]),
            ("ReportQuality", [480, 480]),
            ("Join", [room]),
            ("JoinAsGuest", ["Sam"]),
            ("Knock", [room]),
            ("SetOpen", [false]),
            ("Answer", ["someone", true]),
        })
        {
            var refused = await Assert.ThrowsAsync<HubException>(() => phone.InvokeCoreAsync(method, args));
            Assert.Contains("A remote moves the film", refused.Message);
        }

        Assert.Equal(heightsBefore, Quality.WantedHeights(room));
        Assert.Single(Sessions.Participants(room));
        Assert.False(Sessions.Get(room)!.Waiting);
    }

    [Fact]
    public async Task A_remote_follows_its_screen_to_another_room_and_acts_on_that_one()
    {
        var (tv, tvSid, first, phone, _, _) = await Setup();
        await phone.InvokeAsync<JoinReply>("JoinAsRemote", tvSid);
        var moved = new Heard<JoinReply>(phone, "RemoteMoved");
        var second = Unique("remote-room-");

        await tv.InvokeAsync<JoinReply>("Join", second);
        await tv.InvokeAsync<SessionStatePush>("LoadTitle", SessionFixture.Collateral, null);
        var told = await moved.Until(r => r.Push?.State.SessionId == second);
        Assert.True(told.Admitted);

        // The second room's own pushes from loading the film can still be on their way, so the phone
        // waits for the room playing rather than taking the next push it hears.
        var heardOnPhone = new Heard<SessionStatePush>(phone, "StateChanged");
        var played = await phone.InvokeAsync<SessionStatePush>("Play", 7.0);
        Assert.Equal(second, played.State.SessionId);
        Assert.False(played.State.Paused);
        await heardOnPhone.Until(s => s.State.SessionId == second && !s.State.Paused);
        Assert.True(Sessions.Get(first)!.Paused);

        // Nothing said in the room it left reaches it any more.
        Sessions.Play(first, 1.0, new Actor("nobody", "Nobody"));
        await Task.Delay(300);
        Assert.DoesNotContain(await Drain(heardOnPhone), s => s.State.SessionId == first);
    }

    [Fact]
    public async Task A_remote_leaves_when_its_screen_leaves_its_rooms_and_comes_back_with_it()
    {
        var (tv, tvSid, room, phone, _, person) = await Setup();
        var reply = await phone.InvokeAsync<JoinReply>("JoinAsRemote", tvSid);
        Assert.NotNull(reply.Push);
        var moved = new Heard<JoinReply>(phone, "RemoteMoved");

        // The screen's player closes its room connection; the connection that is the screen stays.
        var person2 = await fixture.ConnectScreenAsync(person, tvSid, "Living room");
        await tv.StopAsync();
        await moved.Until(r => r.Push is null);
        var refused = await Assert.ThrowsAsync<HubException>(() => phone.InvokeAsync<SessionStatePush>("Pause", 1.0));
        Assert.Contains("in no room", refused.Message);

        await person2.InvokeAsync<JoinReply>("Join", room);
        Assert.Equal(room, (await moved.Until(r => r.Push is not null)).Push!.State.SessionId);
        Assert.False((await phone.InvokeAsync<SessionStatePush>("Play", 2.0)).State.Paused);
    }

    [Fact]
    public async Task A_remote_joining_while_its_screen_is_in_no_room_is_in_none_until_the_screen_is()
    {
        var person = Unique("rp-");
        var tvSid = Unique("tv-");
        var phoneSid = Unique("phone-");
        var tv = await fixture.ConnectScreenAsync(person, tvSid, "Living room");
        var phone = await fixture.ConnectPhoneAsync(person, phoneSid);
        using (var paired = await fixture.PairAsync(tv, person, phoneSid)) paired.EnsureSuccessStatusCode();

        var reply = await phone.InvokeAsync<JoinReply>("JoinAsRemote", tvSid);
        Assert.True(reply.Admitted);
        Assert.Null(reply.Push);
        await Assert.ThrowsAsync<HubException>(() => phone.InvokeAsync<SessionStatePush>("Play", 1.0));

        var moved = new Heard<JoinReply>(phone, "RemoteMoved");
        var room = Unique("remote-room-");
        await tv.InvokeAsync<JoinReply>("Join", room);
        Assert.Equal(room, (await moved.Next()).Push!.State.SessionId);
    }

    [Fact]
    public async Task Unpairing_drops_the_remote_from_the_room()
    {
        var (_, tvSid, room, phone, _, _) = await Setup();
        await phone.InvokeAsync<JoinReply>("JoinAsRemote", tvSid);
        var heard = new Heard<SessionStatePush>(phone, "StateChanged");
        var left = new Heard<string>(phone, "ScreenLeft");

        await phone.InvokeAsync("Unpair", tvSid);
        Assert.Equal(tvSid, await left.Next());

        var refused = await Assert.ThrowsAsync<HubException>(() => phone.InvokeAsync<SessionStatePush>("Play", 1.0));
        Assert.Contains("Join a screen's room first", refused.Message);
        Sessions.Play(room, 1.0, new Actor("nobody", "Nobody"));
        await Task.Delay(300);
        Assert.True(heard.Silent);
    }

    [Fact]
    public async Task A_screen_unpairing_the_phone_drops_the_remote_too()
    {
        var (tv, tvSid, _, phone, phoneSid, _) = await Setup();
        await phone.InvokeAsync<JoinReply>("JoinAsRemote", tvSid);

        await tv.InvokeAsync("Unpair", phoneSid);

        await Assert.ThrowsAsync<HubException>(() => phone.InvokeAsync<SessionStatePush>("Pause", 1.0));
    }

    /// <summary>A screen that holds a hub connection and no room, a phone paired with it, and the phone a remote of it.</summary>
    private async Task<(HubConnection Tv, string TvSid, HubConnection Phone, string Person)> Idle()
    {
        var person = Unique("rp-");
        var tvSid = Unique("tv-");
        var phoneSid = Unique("phone-");
        var tv = await fixture.ConnectScreenAsync(person, tvSid, "Living room");
        var phone = await fixture.ConnectPhoneAsync(person, phoneSid);
        using (var paired = await fixture.PairAsync(tv, person, phoneSid)) paired.EnsureSuccessStatusCode();
        await phone.InvokeAsync<JoinReply>("JoinAsRemote", tvSid);
        return (tv, tvSid, phone, person);
    }

    [Fact]
    public async Task Watch_on_screen_tells_a_screen_in_no_room_where_to_go_and_what_to_put_on()
    {
        var (tv, _, phone, _) = await Idle();
        var told = new Heard<ScreenWatch>(tv, "Watch");
        var room = Unique("watch-room-");

        await phone.InvokeAsync("WatchOnScreen", room, SessionFixture.Heat, 90.0);

        var watch = await told.Next();
        Assert.Equal(room, watch.Room);
        Assert.Equal(SessionFixture.Heat, watch.TitleId);
        Assert.Equal(90.0, watch.StartSeconds);
    }

    [Fact]
    public async Task Watch_on_screen_without_a_film_names_a_room_that_is_waiting_on_one()
    {
        var (tv, _, phone, _) = await Idle();
        var told = new Heard<ScreenWatch>(tv, "Watch");
        var room = Unique("watch-room-");

        await phone.InvokeAsync("WatchOnScreen", room, null, null);

        var watch = await told.Next();
        Assert.Equal(room, watch.Room);
        Assert.Null(watch.TitleId);
        Assert.Null(watch.StartSeconds);
    }

    [Fact]
    public async Task Watch_on_screen_reaches_the_screen_of_the_remote_alone()
    {
        var (_, _, phone, person) = await Idle();
        var otherTv = await fixture.ConnectScreenAsync(person, Unique("tv-"), "Bedroom");
        var another = await fixture.ConnectScreenAsync(Unique("rp-other-"), Unique("tv-"), "Elsewhere");
        var heardByOther = new Heard<ScreenWatch>(otherTv, "Watch");
        var heardByAnother = new Heard<ScreenWatch>(another, "Watch");

        await phone.InvokeAsync("WatchOnScreen", Unique("watch-room-"), SessionFixture.Heat, null);
        await Task.Delay(300);

        Assert.True(heardByOther.Silent);
        Assert.True(heardByAnother.Silent);
    }

    [Fact]
    public async Task Watch_on_screen_is_refused_for_a_screen_in_a_room_one_not_connected_and_a_pairing_that_ended()
    {
        var (tv, tvSid, _, phone, phoneSid, person) = await Setup();
        await phone.InvokeAsync<JoinReply>("JoinAsRemote", tvSid);
        var inARoom = await Assert.ThrowsAsync<HubException>(() => phone.InvokeAsync("WatchOnScreen", Unique("watch-room-"), SessionFixture.Heat, null));
        Assert.Contains("already in a room", inARoom.Message);

        var (idleTv, _, idlePhone, _) = await Idle();
        await idleTv.StopAsync();
        var gone = await Assert.ThrowsAsync<HubException>(() => idlePhone.InvokeAsync("WatchOnScreen", Unique("watch-room-"), SessionFixture.Heat, null));
        Assert.Contains("not connected", gone.Message);

        var (_, idleSid, secondPhone, _) = await Idle();
        await secondPhone.InvokeAsync("Unpair", idleSid);
        var unpaired = await Assert.ThrowsAsync<HubException>(() => secondPhone.InvokeAsync("WatchOnScreen", Unique("watch-room-"), SessionFixture.Heat, null));
        Assert.Contains("Join a screen's room first", unpaired.Message);
    }

    [Fact]
    public async Task Watch_on_screen_is_refused_for_a_bad_room_an_unknown_film_and_a_start_that_is_not_a_place()
    {
        var (_, _, phone, _) = await Idle();

        foreach (var (room, title, start) in new (string, string?, double?)[]
        {
            ("not a room!", SessionFixture.Heat, null),
            (Unique("watch-room-"), "no-such-film-1999", null),
            (Unique("watch-room-"), SessionFixture.Heat, -5.0),
        })
            await Assert.ThrowsAsync<HubException>(() => phone.InvokeAsync("WatchOnScreen", room, title, start));
    }

    [Fact]
    public async Task Watch_on_screen_is_refused_a_television_a_phone_that_is_no_remote_a_guest_and_a_connection_with_no_account()
    {
        var (tv, _, _, person) = await Idle();
        var notRemote = await fixture.ConnectPhoneAsync(person, Unique("phone-"));
        var noSession = await fixture.ConnectOutsideAsync(person, "Alice");

        foreach (var connection in new[] { tv, notRemote, noSession })
            await Assert.ThrowsAsync<HubException>(() => connection.InvokeAsync("WatchOnScreen", Unique("watch-room-"), SessionFixture.Heat, null));
    }

    [Fact]
    public async Task A_screen_is_told_how_many_remotes_drive_it_and_the_room_is_not()
    {
        var (tv, tvSid, _, phone, _, _) = await Setup();
        var told = new Heard<int>(tv, "RemotesChanged");
        var roster = new Heard<List<Participant>>(tv, "ParticipantsChanged");
        Assert.Equal(0, await tv.InvokeAsync<int>("Remotes"));

        await phone.InvokeAsync<JoinReply>("JoinAsRemote", tvSid);
        Assert.Equal(1, await told.Next());
        Assert.Equal(1, await tv.InvokeAsync<int>("Remotes"));

        await phone.InvokeAsync("Unpair", tvSid);
        Assert.Equal(0, await told.Until(n => n == 0));
        Assert.Equal(0, await tv.InvokeAsync<int>("Remotes"));
        Assert.True(roster.Silent);
    }

    [Fact]
    public async Task A_screen_is_told_when_a_remote_disconnects_and_only_a_screen_may_ask()
    {
        var (tv, tvSid, _, phone, _, person) = await Setup();
        var told = new Heard<int>(tv, "RemotesChanged");
        await phone.InvokeAsync<JoinReply>("JoinAsRemote", tvSid);
        Assert.Equal(1, await told.Next());

        await phone.StopAsync();
        Assert.Equal(0, await told.Next());

        var another = await fixture.ConnectPhoneAsync(person, Unique("phone-"));
        await Assert.ThrowsAsync<HubException>(() => another.InvokeAsync<int>("Remotes"));
    }

    [Fact]
    public void Only_the_methods_marked_for_remotes_are_open_to_them()
    {
        var methods = typeof(SessionHub).GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(m => !m.IsSpecialName && m.Name != nameof(SessionHub.OnConnectedAsync) && m.Name != nameof(SessionHub.OnDisconnectedAsync));

        Assert.Equal(
            ["JoinAsRemote", "LoadTitle", "Next", "Pause", "Play", "PlayEpisode", "Previous", "Screens", "Seek", "ServerTime", "Unpair", "WatchOnScreen"],
            methods.Where(m => m.IsDefined(typeof(RemoteAllowedAttribute))).Select(m => m.Name).Order(StringComparer.Ordinal));
    }
}
