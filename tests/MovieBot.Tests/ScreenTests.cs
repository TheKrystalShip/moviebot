using System.Net;
using System.Net.Http.Json;
using System.Threading.Channels;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using TheKrystalShip.MovieBot.Core;
using TheKrystalShip.MovieBot.Api.Auth;
using TheKrystalShip.MovieBot.Api.Data;
using TheKrystalShip.MovieBot.Api.Sessions;
using Xunit;

namespace TheKrystalShip.MovieBot.Tests;

/// <summary>
/// A person's screens and the phones paired with them: a connection signed in through the television
/// client is a screen, it shows a code, a phone of the same person that sends the code is paired with
/// it for good, and from then on that phone, and no other connection, hears the screen arrive, change
/// room and leave.
/// </summary>
public sealed class ScreenTests(SessionFixture fixture) : IClassFixture<SessionFixture>
{
    private static string Unique(string prefix) => prefix + Guid.NewGuid().ToString("n")[..8];

    private const string ChromeOnAndroid =
        "Mozilla/5.0 (Linux; Android 14; Pixel 8) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/129.0.0.0 Mobile Safari/537.36";

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

    /// <summary>A television of the person's, and a phone of theirs connected and paired with it.</summary>
    private async Task<(HubConnection Tv, string TvSid, HubConnection Phone, string PhoneSid)> Paired(
        string person, string? tvName = "Living room")
    {
        var tvSid = Unique("tv-");
        var phoneSid = Unique("phone-");
        var tv = await fixture.ConnectScreenAsync(person, tvSid, tvName);
        var phone = await fixture.ConnectPhoneAsync(person, phoneSid);
        using var paired = await fixture.PairAsync(tv, person, phoneSid);
        paired.EnsureSuccessStatusCode();
        return (tv, tvSid, phone, phoneSid);
    }

    [Fact]
    public async Task A_phone_that_scans_a_code_is_paired_and_hears_the_screen_with_its_name()
    {
        var person = Unique("p-");
        var tvSid = Unique("tv-");
        var phoneSid = Unique("phone-");
        var tv = await fixture.ConnectScreenAsync(person, tvSid, "Living room");
        var phone = await fixture.ConnectPhoneAsync(person, phoneSid);
        var arrived = new Heard<ScreenView>(phone, "ScreenArrived");

        using var response = await fixture.PairAsync(tv, person, phoneSid);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var answered = (await response.Content.ReadFromJsonAsync<ScreenView>())!;
        Assert.Equal(tvSid, answered.Id);
        Assert.Equal("Living room", answered.Name);
        Assert.True(answered.Connected);
        var heard = await arrived.Next();
        Assert.Equal(tvSid, heard.Id);
        Assert.Null(heard.Room);
    }

    [Fact]
    public async Task A_code_is_a_short_address_that_runs_out_and_is_replaced_before_it_does()
    {
        var tv = await fixture.ConnectScreenAsync(Unique("p-"), Unique("tv-"), "Living room");

        var first = await tv.InvokeAsync<PairingCodeView>("PairingCode");
        var second = await tv.InvokeAsync<PairingCodeView>("PairingCode");

        Assert.Matches("^/p/[A-Za-z0-9_-]{22}$", first.Path);
        Assert.InRange(first.ExpiresAt - DateTimeOffset.UtcNow, TimeSpan.FromMinutes(4), PairingCodes.Lifetime);
        Assert.Equal(60, first.RenewSeconds);
        Assert.StartsWith("/p/", second.Path);
    }

    [Theory]
    [InlineData("cinema-site")]
    [InlineData("cinema-activity")]
    public async Task A_code_is_issued_to_a_screen_alone(string client)
    {
        var person = Unique("p-");
        // The token even carries a session and a device name; the client is what decides.
        var notTv = await fixture.ConnectScreenAsync(person, Unique("sid-"), "Pretending", clientId: client);
        var refused = await Assert.ThrowsAsync<HubException>(() => notTv.InvokeAsync<PairingCodeView>("PairingCode"));
        Assert.Contains("Only a television", refused.Message);
        await Assert.ThrowsAsync<HubException>(() => notTv.InvokeAsync<List<PairedPhone>>("Phones"));
    }

    [Fact]
    public async Task A_television_token_with_no_session_is_no_screen_and_is_given_no_code()
    {
        var tv = await fixture.ConnectScreenAsync(Unique("p-"), sid: "", clientId: "cinema-tv");
        await Assert.ThrowsAsync<HubException>(() => tv.InvokeAsync<PairingCodeView>("PairingCode"));
    }

    [Fact]
    public async Task A_code_that_ran_out_pairs_nothing()
    {
        var person = Unique("p-");
        var tvSid = Unique("tv-");
        await fixture.ConnectScreenAsync(person, tvSid, "Living room");

        // The server's own key, at a time ten minutes ago: a code the screen was shown and that has run out.
        var past = new FakeTimeProvider(DateTimeOffset.UtcNow - TimeSpan.FromMinutes(10));
        var old = new PairingCodes(fixture.Services.GetRequiredService<IOptions<AuthOptions>>(), past).Issue(person, tvSid);

        using var response = await fixture.CreatePhoneClient(person, Unique("phone-"))
            .PostAsJsonAsync("/api/me/screens", new PairRequest(old.Path["/p/".Length..]));

        Assert.Equal(HttpStatusCode.Gone, response.StatusCode);
    }

    [Fact]
    public async Task A_code_changed_by_one_character_pairs_nothing()
    {
        var person = Unique("p-");
        var tv = await fixture.ConnectScreenAsync(person, Unique("tv-"), "Living room");
        var code = (await tv.InvokeAsync<PairingCodeView>("PairingCode")).Path["/p/".Length..];
        // Every character but the last carries six bits of the code; the last carries two and four of padding.
        var changed = Enumerable.Range(0, code.Length - 1)
            .Select(i => code[..i] + (code[i] == 'A' ? 'B' : 'A') + code[(i + 1)..]);
        var phone = fixture.CreatePhoneClient(person, Unique("phone-"));

        foreach (var tampered in changed.Concat([code[..^1], "not-a-code", ""]))
        {
            using var response = await phone.PostAsJsonAsync("/api/me/screens", new PairRequest(tampered));
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }
    }

    [Fact]
    public async Task A_phone_of_another_person_is_refused()
    {
        var alice = Unique("alice-");
        var bob = Unique("bob-");
        var tv = await fixture.ConnectScreenAsync(alice, Unique("tv-"), "Living room");
        var bobsSid = Unique("phone-");
        var bobsPhone = await fixture.ConnectPhoneAsync(bob, bobsSid);
        var heard = new Heard<ScreenView>(bobsPhone, "ScreenArrived");

        using var response = await fixture.PairAsync(tv, bob, bobsSid);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Empty(await tv.InvokeAsync<List<PairedPhone>>("Phones"));
        Assert.Empty(await bobsPhone.InvokeAsync<List<ScreenView>>("Screens"));
        await Task.Delay(300);
        Assert.True(heard.Silent);
    }

    [Theory]
    [InlineData("cinema-tv")]
    [InlineData(null)]
    public async Task Only_a_session_that_is_no_screen_pairs(string? kind)
    {
        // A television pairs with nothing, and a token that belongs to no session (the Activity's, from a
        // token exchange) has nothing to pair.
        var person = Unique("p-");
        var tv = await fixture.ConnectScreenAsync(person, Unique("tv-"), "Living room");
        var code = (await tv.InvokeAsync<PairingCodeView>("PairingCode")).Path["/p/".Length..];
        var client = kind is null
            ? fixture.CreatePersonClient(person)
            : fixture.CreatePhoneClient(person, Unique("other-tv-"), clientId: kind);

        using var response = await client.PostAsJsonAsync("/api/me/screens", new PairRequest(code));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task The_screen_lists_its_phones_by_the_browser_they_paired_with()
    {
        var person = Unique("p-");
        var tv = await fixture.ConnectScreenAsync(person, Unique("tv-"), "Living room");
        var phonesChanged = new Heard<List<PairedPhone>>(tv, "PhonesChanged");
        var phoneSid = Unique("phone-");

        using var _ = await fixture.PairAsync(tv, person, phoneSid, ChromeOnAndroid);

        var told = Assert.Single(await phonesChanged.Next());
        Assert.Equal(phoneSid, told.Id);
        Assert.Equal("Chrome on Android", told.Name);
        var listed = Assert.Single(await tv.InvokeAsync<List<PairedPhone>>("Phones"));
        Assert.Equal(phoneSid, listed.Id);
        Assert.InRange(listed.PairedAt, DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow);
    }

    [Fact]
    public async Task Scanning_again_is_no_second_pairing()
    {
        var person = Unique("p-");
        var (tv, _, _, phoneSid) = await Paired(person);
        var first = Assert.Single(await tv.InvokeAsync<List<PairedPhone>>("Phones"));

        using var again = await fixture.PairAsync(tv, person, phoneSid);
        again.EnsureSuccessStatusCode();

        var still = Assert.Single(await tv.InvokeAsync<List<PairedPhone>>("Phones"));
        Assert.Equal(first.PairedAt, still.PairedAt);
    }

    [Fact]
    public async Task A_phone_lists_the_screens_it_is_paired_with_and_no_others()
    {
        var person = Unique("p-");
        var (_, tvSid, phone, phoneSid) = await Paired(person, "Bedroom");
        await fixture.ConnectScreenAsync(person, Unique("tv-"), "Kitchen");

        var screens = Assert.Single(await phone.InvokeAsync<List<ScreenView>>("Screens"));
        Assert.Equal(tvSid, screens.Id);
        Assert.Equal("Bedroom", screens.Name);
        Assert.True(screens.Connected);

        var overHttp = Assert.Single((await fixture.CreatePhoneClient(person, phoneSid)
            .GetFromJsonAsync<List<ScreenView>>("/api/me/screens"))!);
        Assert.Equal(tvSid, overHttp.Id);
    }

    [Fact]
    public async Task A_phone_of_the_same_person_that_never_paired_hears_nothing()
    {
        var person = Unique("p-");
        var unpaired = await fixture.ConnectPhoneAsync(person, Unique("phone-"));
        var arrived = new Heard<ScreenView>(unpaired, "ScreenArrived");
        var changed = new Heard<ScreenView>(unpaired, "ScreenChanged");
        var noSession = await fixture.ConnectOutsideAsync(person, "Alice");
        var heardWithoutSession = new Heard<ScreenView>(noSession, "ScreenChanged");

        var (tv, _, phone, _) = await Paired(person);
        var pairedChanges = new Heard<ScreenView>(phone, "ScreenChanged");
        await tv.InvokeAsync<JoinReply>("Join", Unique("screen-room-"));
        await pairedChanges.Until(s => s.Room is not null);

        // The paired phone has heard the change, so the server has finished telling anyone.
        await Task.Delay(300);
        Assert.True(arrived.Silent);
        Assert.True(changed.Silent);
        Assert.True(heardWithoutSession.Silent);
        Assert.Empty(await unpaired.InvokeAsync<List<ScreenView>>("Screens"));
        Assert.Empty(await noSession.InvokeAsync<List<ScreenView>>("Screens"));
    }

    [Fact]
    public async Task A_screen_reports_the_room_it_joins_and_what_the_room_holds()
    {
        var person = Unique("p-");
        var (tv, _, phone, _) = await Paired(person);
        var changed = new Heard<ScreenView>(phone, "ScreenChanged");

        var room = Unique("screen-room-");
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
        var (tv, _, phone, _) = await Paired(person);
        var changed = new Heard<ScreenView>(phone, "ScreenChanged");
        var first = Unique("screen-room-");
        var second = Unique("screen-room-");

        await tv.InvokeAsync<JoinReply>("Join", first);
        await changed.Until(s => s.Room == first);
        await tv.InvokeAsync<JoinReply>("Join", second);

        Assert.Equal(second, (await changed.Until(s => s.Room == second)).Room);
    }

    [Fact]
    public async Task A_screen_whose_connection_closes_is_not_connected_and_stays_paired()
    {
        var person = Unique("p-");
        var (tv, tvSid, phone, _) = await Paired(person);
        var changed = new Heard<ScreenView>(phone, "ScreenChanged");
        await tv.InvokeAsync<JoinReply>("Join", Unique("screen-room-"));
        await changed.Until(s => s.Room is not null);

        await tv.StopAsync();

        var gone = await changed.Until(s => !s.Connected);
        Assert.Null(gone.Room);
        Assert.Equal("Living room", gone.Name);
        var listed = Assert.Single(await phone.InvokeAsync<List<ScreenView>>("Screens"));
        Assert.Equal(tvSid, listed.Id);
        Assert.False(listed.Connected);
    }

    [Fact]
    public async Task A_screen_connecting_again_arrives_again_at_its_phones()
    {
        var person = Unique("p-");
        var (tv, tvSid, phone, _) = await Paired(person);
        var arrived = new Heard<ScreenView>(phone, "ScreenArrived");
        var changed = new Heard<ScreenView>(phone, "ScreenChanged");

        // The server hears the screen go after StopAsync returns; one that comes back before then never left.
        await tv.StopAsync();
        await changed.Until(s => !s.Connected);
        await fixture.ConnectScreenAsync(person, tvSid, "Living room");

        var again = await arrived.Next();
        Assert.Equal(tvSid, again.Id);
        Assert.True(again.Connected);
        Assert.Single(await phone.InvokeAsync<List<ScreenView>>("Screens"));
    }

    [Fact]
    public async Task A_television_signed_in_again_ends_the_old_screens_pairings()
    {
        var person = Unique("p-");
        var (old, oldSid, phone, phoneSid) = await Paired(person);
        var left = new Heard<string>(phone, "ScreenLeft");

        await old.StopAsync();
        var fresh = await fixture.ConnectScreenAsync(person, Unique("new-"), "Living room");

        Assert.Equal(oldSid, await left.Next());
        Assert.Empty(await phone.InvokeAsync<List<ScreenView>>("Screens"));
        Assert.Empty(await fresh.InvokeAsync<List<PairedPhone>>("Phones"));

        // The same television signed in again is paired by scanning it again, as any other is.
        using var paired = await fixture.PairAsync(fresh, person, phoneSid);
        paired.EnsureSuccessStatusCode();
        Assert.Single(await phone.InvokeAsync<List<ScreenView>>("Screens"));
    }

    [Fact]
    public async Task Another_screen_of_the_same_name_that_is_connected_keeps_its_pairings()
    {
        var person = Unique("p-");
        var (_, tvSid, phone, _) = await Paired(person);

        await fixture.ConnectScreenAsync(person, Unique("b-"), "Living room");

        Assert.Equal(tvSid, Assert.Single(await phone.InvokeAsync<List<ScreenView>>("Screens")).Id);
    }

    [Fact]
    public async Task A_phone_unpairs_and_the_screen_hears_it()
    {
        var person = Unique("p-");
        var (tv, tvSid, phone, phoneSid) = await Paired(person);
        var left = new Heard<string>(phone, "ScreenLeft");
        var phonesChanged = new Heard<List<PairedPhone>>(tv, "PhonesChanged");

        using var response = await fixture.CreatePhoneClient(person, phoneSid).DeleteAsync($"/api/me/screens/{tvSid}");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(tvSid, await left.Next());
        // Pairing's own push, listing the phone, can still reach the screen after the test listens.
        await phonesChanged.Until(phones => phones.Count == 0);
        Assert.Empty(await phone.InvokeAsync<List<ScreenView>>("Screens"));
    }

    [Fact]
    public async Task A_screen_unpairs_a_phone_and_the_phone_hears_it()
    {
        var person = Unique("p-");
        var (tv, tvSid, phone, phoneSid) = await Paired(person);
        var left = new Heard<string>(phone, "ScreenLeft");
        var changed = new Heard<ScreenView>(phone, "ScreenChanged");

        await tv.InvokeAsync("Unpair", phoneSid);

        Assert.Equal(tvSid, await left.Next());
        Assert.Empty(await tv.InvokeAsync<List<PairedPhone>>("Phones"));

        // Unpaired, the phone hears nothing more of the screen.
        await tv.InvokeAsync<JoinReply>("Join", Unique("screen-room-"));
        await Task.Delay(300);
        Assert.True(changed.Silent);
    }

    [Fact]
    public async Task A_phone_unpairs_from_its_hub_connection_too()
    {
        var person = Unique("p-");
        var (tv, tvSid, phone, _) = await Paired(person);

        await phone.InvokeAsync("Unpair", tvSid);

        Assert.Empty(await tv.InvokeAsync<List<PairedPhone>>("Phones"));
    }

    [Fact]
    public async Task Nobody_unpairs_anybody_elses()
    {
        var alice = Unique("alice-");
        var (tv, tvSid, _, phoneSid) = await Paired(alice);
        var bobsTv = await fixture.ConnectScreenAsync(Unique("bob-"), Unique("tv-"));

        // Bob names Alice's phone from his screen, and Alice's screen from a phone of his on her phone's session.
        await bobsTv.InvokeAsync("Unpair", phoneSid);
        using var _ = await fixture.CreatePhoneClient(Unique("bob-"), phoneSid).DeleteAsync($"/api/me/screens/{tvSid}");

        Assert.Single(await tv.InvokeAsync<List<PairedPhone>>("Phones"));
    }

    [Fact]
    public async Task A_pairing_is_kept_in_the_database()
    {
        var person = Unique("p-");
        var (_, tvSid, _, phoneSid) = await Paired(person);

        // A store opened afresh on the same file, as a restart opens it, with no screen known in memory.
        var database = new CinemaDatabase(fixture.Services.GetRequiredService<CinemaDatabase>().Path);
        var pairings = new ScreenPairings(database, Options.Create(new OidcOptions()), TimeProvider.System);

        var kept = Assert.Single(pairings.OfPhone(person, phoneSid, new HashSet<string>()));
        Assert.Equal(tvSid, kept.ScreenId);
        Assert.Equal("Living room", kept.ScreenName);
    }

    [Fact]
    public async Task A_pairing_whose_session_went_unseen_for_longer_than_a_session_lasts_is_forgotten()
    {
        var person = Unique("p-");
        var (_, tvSid, _, phoneSid) = await Paired(person);
        var database = fixture.Services.GetRequiredService<CinemaDatabase>();
        var lifetime = new OidcOptions().SessionLifetime;
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow + lifetime - TimeSpan.FromHours(1));
        var pairings = new ScreenPairings(database, Options.Create(new OidcOptions()), clock);
        Assert.Single(pairings.OfPhone(person, phoneSid, new HashSet<string>()));

        // Past the window the phone is seen again; the screen, unseen as long, is kept only while it is connected.
        clock.Advance(TimeSpan.FromHours(2));
        pairings.PhoneSeen(person, phoneSid);
        Assert.Single(pairings.OfPhone(person, phoneSid, new HashSet<string> { tvSid }));
        Assert.Empty(pairings.OfPhone(person, phoneSid, new HashSet<string>()));

        // Forgotten for good, not hidden.
        Assert.Empty(pairings.OfScreen(person, tvSid, new HashSet<string> { tvSid }));
    }

    [Fact]
    public async Task Two_televisions_paired_with_one_phone_are_two_screens()
    {
        var person = Unique("p-");
        var phoneSid = Unique("phone-");
        var phone = await fixture.ConnectPhoneAsync(person, phoneSid);
        var first = await fixture.ConnectScreenAsync(person, Unique("a-"), "Living room");
        var second = await fixture.ConnectScreenAsync(person, Unique("b-"), "Bedroom");
        (await fixture.PairAsync(first, person, phoneSid)).EnsureSuccessStatusCode();
        (await fixture.PairAsync(second, person, phoneSid)).EnsureSuccessStatusCode();

        Assert.Equal(2, (await phone.InvokeAsync<List<ScreenView>>("Screens")).Count);
    }

    [Fact]
    public async Task Another_person_neither_hears_a_screen_nor_is_handed_it()
    {
        var alice = Unique("alice-");
        var bob = Unique("bob-");
        var bobsPhone = await fixture.ConnectPhoneAsync(bob, Unique("phone-"));
        var heardByBob = new Heard<ScreenView>(bobsPhone, "ScreenArrived");
        var bobsChanges = new Heard<ScreenView>(bobsPhone, "ScreenChanged");

        var (tv, _, alicesPhone, _) = await Paired(alice);
        var heardByAlice = new Heard<ScreenView>(alicesPhone, "ScreenChanged");
        await tv.InvokeAsync<JoinReply>("Join", Unique("screen-room-"));
        await heardByAlice.Next();

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
        Assert.Empty(await first.InvokeAsync<List<ScreenView>>("Screens"));
    }

    [Fact]
    public async Task A_guest_joins_no_persons_group_and_asks_for_no_screens()
    {
        var alice = Unique("alice-");
        var room = Unique("screen-room-");
        await fixture.ConnectAsync(room, alice, "Alice");
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

        var (_, tvSid, phone, _) = await Paired(alice);
        await Task.Delay(300);

        Assert.True(heardByGuest.Silent);
        await Assert.ThrowsAsync<HubException>(() => guest.InvokeAsync<List<ScreenView>>("Screens"));
        await Assert.ThrowsAsync<HubException>(() => guest.InvokeAsync<PairingCodeView>("PairingCode"));
        await Assert.ThrowsAsync<HubException>(() => guest.InvokeAsync("Unpair", tvSid));
        Assert.Single(await phone.InvokeAsync<List<ScreenView>>("Screens"));
    }
}
