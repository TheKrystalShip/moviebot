using System.Net;
using System.Net.Http.Json;
using System.Threading.Channels;
using Microsoft.AspNetCore.SignalR.Client;
using TheKrystalShip.MovieBot.Api.People;
using TheKrystalShip.MovieBot.Api.Sessions;
using TheKrystalShip.MovieBot.Core;
using Xunit;

namespace TheKrystalShip.MovieBot.Tests;

/// <summary>
/// The subtitle and audio track a person watches a film with is kept by the API, the same on every
/// device: the manifest decides what the film has, only the token's person is reached, and a change is
/// pushed to that person's connections and to nobody else's.
/// </summary>
public sealed class TrackTests(SessionFixture fixture) : IClassFixture<SessionFixture>
{
    private static string Unique(string prefix) => prefix + Guid.NewGuid().ToString("n")[..8];

    private HttpClient As(string userId) => fixture.CreatePersonClient(userId, "Cy");

    private static string Route(string title = SessionFixture.Dubbed) => $"/api/me/tracks/{title}";

    private static async Task<FilmTracks?> Read(HttpClient client, string title = SessionFixture.Dubbed) =>
        await client.GetFromJsonAsync<FilmTracks>(Route(title));

    private sealed class Heard<T>
    {
        private readonly Channel<T> _messages = Channel.CreateUnbounded<T>();

        public Heard(HubConnection connection, string method) =>
            connection.On<T>(method, value => _messages.Writer.TryWrite(value));

        public async Task<T> Next() =>
            await _messages.Reader.ReadAsync(new CancellationTokenSource(TimeSpan.FromSeconds(10)).Token);

        public async Task<bool> Quiet()
        {
            await Task.Delay(300);
            return !_messages.Reader.TryPeek(out _);
        }
    }

    [Fact]
    public async Task Somebody_who_chose_nothing_has_no_choice_for_a_film()
    {
        var tracks = await Read(As(Unique("tr-")));

        Assert.Equal(SessionFixture.Dubbed, tracks?.TitleId);
        Assert.Null(tracks?.SubtitleId);
        Assert.False(tracks?.SubtitleOff);
        Assert.Null(tracks?.AudioId);
    }

    [Fact]
    public async Task A_subtitle_and_an_audio_track_are_kept_and_read_back_on_another_device()
    {
        var person = Unique("tr-");
        using var put = await As(person).PutAsJsonAsync(Route(), new TrackChoice { SubtitleId = "s1", AudioId = "a1" });
        put.EnsureSuccessStatusCode();

        var seen = await Read(fixture.CreatePersonClient(person, "Cy", via: "cinema-tv"));
        Assert.Equal("s1", seen?.SubtitleId);
        Assert.Equal("a1", seen?.AudioId);
        Assert.False(seen?.SubtitleOff);
    }

    [Fact]
    public async Task What_a_request_leaves_out_is_kept_and_off_is_a_choice_of_its_own()
    {
        var client = As(Unique("tr-"));
        (await client.PutAsJsonAsync(Route(), new TrackChoice { SubtitleId = "s0", AudioId = "a1" })).EnsureSuccessStatusCode();

        (await client.PutAsJsonAsync(Route(), new TrackChoice { AudioId = "a0" })).EnsureSuccessStatusCode();
        var audioOnly = await Read(client);
        Assert.Equal("s0", audioOnly?.SubtitleId);
        Assert.Equal("a0", audioOnly?.AudioId);

        (await client.PutAsJsonAsync(Route(), new TrackChoice { SubtitleOff = true })).EnsureSuccessStatusCode();
        var off = await Read(client);
        Assert.Null(off?.SubtitleId);
        Assert.True(off?.SubtitleOff);
        Assert.Equal("a0", off?.AudioId);

        (await client.PutAsJsonAsync(Route(), new TrackChoice { SubtitleId = "s1" })).EnsureSuccessStatusCode();
        var back = await Read(client);
        Assert.Equal("s1", back?.SubtitleId);
        Assert.False(back?.SubtitleOff);
    }

    [Theory]
    [InlineData("nope", null)]
    [InlineData("s2", null)]
    [InlineData(null, "a9")]
    [InlineData("s0", "a9")]
    public async Task A_track_the_film_does_not_have_is_refused_and_keeps_what_was_there(string? subtitle, string? audio)
    {
        var client = As(Unique("tr-"));
        (await client.PutAsJsonAsync(Route(), new TrackChoice { SubtitleId = "s1", AudioId = "a1" })).EnsureSuccessStatusCode();

        using var put = await client.PutAsJsonAsync(Route(), new TrackChoice { SubtitleId = subtitle, AudioId = audio });

        Assert.Equal(HttpStatusCode.BadRequest, put.StatusCode);
        var kept = await Read(client);
        Assert.Equal("s1", kept?.SubtitleId);
        Assert.Equal("a1", kept?.AudioId);
    }

    [Fact]
    public async Task A_request_that_chooses_nothing_or_two_things_for_the_subtitle_is_refused()
    {
        var client = As(Unique("tr-"));

        using var nothing = await client.PutAsJsonAsync(Route(), new TrackChoice());
        using var both = await client.PutAsJsonAsync(Route(), new TrackChoice { SubtitleId = "s0", SubtitleOff = true });

        Assert.Equal(HttpStatusCode.BadRequest, nothing.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, both.StatusCode);
    }

    [Fact]
    public async Task A_film_the_library_does_not_hold_is_not_found()
    {
        var client = As(Unique("tr-"));

        using var read = await client.GetAsync(Route("no-such-film"));
        using var put = await client.PutAsJsonAsync(Route("no-such-film"), new TrackChoice { SubtitleOff = true });

        Assert.Equal(HttpStatusCode.NotFound, read.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, put.StatusCode);
    }

    [Fact]
    public async Task Another_person_s_choice_is_never_reached()
    {
        var ann = Unique("tr-ann-");
        (await As(ann).PutAsJsonAsync(Route(), new TrackChoice { SubtitleId = "s1", AudioId = "a1" })).EnsureSuccessStatusCode();

        var bob = await Read(As(Unique("tr-bob-")));

        Assert.Null(bob?.SubtitleId);
        Assert.Null(bob?.AudioId);
    }

    [Fact]
    public async Task The_service_key_and_a_guest_name_nobody_and_reach_no_choice()
    {
        var service = fixture.CreateServiceClient();

        using var read = await service.GetAsync(Route());
        using var put = await service.PutAsJsonAsync(Route(), new TrackChoice { SubtitleOff = true });

        Assert.Equal(HttpStatusCode.Unauthorized, read.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, put.StatusCode);
    }

    [Fact]
    public async Task A_change_reaches_the_person_s_every_connection_and_nobody_else_s_and_the_room_not_at_all()
    {
        var ann = Unique("tr-ann-");
        var tv = await fixture.ConnectScreenAsync(ann, Unique("tv-"), "Living room");
        var room = Unique("tracks-room-");
        Assert.True((await tv.InvokeAsync<JoinReply>("Join", room)).Admitted);
        await tv.InvokeAsync<SessionStatePush>("LoadTitle", SessionFixture.Dubbed, null);
        var phone = await fixture.ConnectPhoneAsync(ann, Unique("phone-"));
        var otherDevice = await fixture.ConnectOutsideAsync(ann, "Cy", via: "cinema-activity");
        var bob = Unique("tr-bob-");
        var bobsPhone = await fixture.ConnectPhoneAsync(bob, Unique("phone-"));
        var bobsRoom = await fixture.ConnectAsync(room, bob, "Bob");

        var tvHears = new Heard<FilmTracks>(tv, "TracksChanged");
        var phoneHears = new Heard<FilmTracks>(phone, "TracksChanged");
        var deviceHears = new Heard<FilmTracks>(otherDevice, "TracksChanged");
        var bobHears = new Heard<FilmTracks>(bobsPhone, "TracksChanged");
        var bobInRoomHears = new Heard<FilmTracks>(bobsRoom, "TracksChanged");
        var tvState = new Heard<SessionStatePush>(tv, "StateChanged");
        var tvRoster = new Heard<object>(tv, "ParticipantsChanged");
        var bobState = new Heard<SessionStatePush>(bobsRoom, "StateChanged");

        using var put = await As(ann).PutAsJsonAsync(Route(), new TrackChoice { SubtitleId = "s1", AudioId = "a1" });
        put.EnsureSuccessStatusCode();

        foreach (var heard in new[] { tvHears, phoneHears, deviceHears })
        {
            var tracks = await heard.Next();
            Assert.Equal(SessionFixture.Dubbed, tracks.TitleId);
            Assert.Equal("s1", tracks.SubtitleId);
            Assert.Equal("a1", tracks.AudioId);
        }
        Assert.True(await bobHears.Quiet());
        Assert.True(await bobInRoomHears.Quiet());
        Assert.True(await tvState.Quiet());
        Assert.True(await tvRoster.Quiet());
        Assert.True(await bobState.Quiet());
    }

    [Fact]
    public async Task A_refused_change_pushes_nothing()
    {
        var ann = Unique("tr-ann-");
        var phone = await fixture.ConnectPhoneAsync(ann, Unique("phone-"));
        var tracksHeard = new Heard<FilmTracks>(phone, "TracksChanged");
        var preferencesHeard = new Heard<ViewerPreferences>(phone, "PreferencesChanged");

        using var tracks = await As(ann).PutAsJsonAsync(Route(), new TrackChoice { SubtitleId = "s2" });
        using var preferences = await As(ann).PutAsJsonAsync("/api/me/preferences", new ViewerPreferences { QualityHeight = -1 });

        Assert.Equal(HttpStatusCode.BadRequest, tracks.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, preferences.StatusCode);
        Assert.True(await tracksHeard.Quiet());
        Assert.True(await preferencesHeard.Quiet());
    }

    [Fact]
    public async Task A_change_to_how_the_person_watches_reaches_their_connections_and_nobody_else_s()
    {
        var ann = Unique("tr-ann-");
        var tv = await fixture.ConnectScreenAsync(ann, Unique("tv-"), "Living room");
        var phone = await fixture.ConnectPhoneAsync(ann, Unique("phone-"));
        var bobsPhone = await fixture.ConnectPhoneAsync(Unique("tr-bob-"), Unique("phone-"));
        var tvHears = new Heard<ViewerPreferences>(tv, "PreferencesChanged");
        var phoneHears = new Heard<ViewerPreferences>(phone, "PreferencesChanged");
        var bobHears = new Heard<ViewerPreferences>(bobsPhone, "PreferencesChanged");

        using var put = await As(ann).PutAsJsonAsync("/api/me/preferences", new ViewerPreferences
        {
            QualityHeight = 720, SubtitleStyle = new SubtitleStyle { Size = 150, TextColor = "#FFE14D" },
        });
        put.EnsureSuccessStatusCode();

        foreach (var heard in new[] { tvHears, phoneHears })
        {
            var preferences = await heard.Next();
            Assert.Equal(720, preferences.QualityHeight);
            Assert.Equal(150, preferences.SubtitleStyle?.Size);
            Assert.Equal("#ffe14d", preferences.SubtitleStyle?.TextColor);
        }
        Assert.True(await bobHears.Quiet());
    }
}
