using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.RegularExpressions;
using System.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using TheKrystalShip.MovieBot.Acquire.Download;
using TheKrystalShip.MovieBot.Acquire.Search;
using TheKrystalShip.MovieBot.Acquire.Tracker;
using TheKrystalShip.MovieBot.Api.Sessions;
using TheKrystalShip.MovieBot.Core;
using Xunit;

namespace TheKrystalShip.MovieBot.Tests;

/// <summary>
/// The API with a tracker that offers Inception at 1080p and 720p, and a torrent client that keeps
/// what it is given and answers with it, so a Watch now can be followed from the press to the film
/// playing in the room.
/// </summary>
public sealed class WatchNowFixture : SessionFixture
{
    public const string Inception = "tt1375666";

    /// <summary>The smallest thing the tracker could hand over that is still a torrent.</summary>
    private const string TorrentFile =
        "d4:infod6:lengthi1e4:name1:x12:piece lengthi1e6:pieces20:AAAAAAAAAAAAAAAAAAAAee";

    public static readonly string Hash = Bencode.InfoHash(Encoding.ASCII.GetBytes(TorrentFile));

    public TorrentClientStub Torrents { get; } = new();

    protected override Dictionary<string, string?> MoreSettings() => new()
    {
        ["Tracker:Username"] = "u",
        ["Tracker:Passkey"] = "p",
        ["Download:ReservedGiB"] = "0",
        ["Watch:MaxStreams"] = "2",
    };

    protected override void MoreServices(IServiceCollection services)
    {
        services.RemoveAll<IReleaseSearch>();
        services.AddSingleton<IReleaseSearch>(new InceptionOnly());
        services.AddHttpClient<QBittorrentClient>().ConfigurePrimaryHttpMessageHandler(() => Torrents);
        services.AddHttpClient<TrackerClient>().ConfigurePrimaryHttpMessageHandler(() => new Answer(r =>
            r.RequestUri!.AbsolutePath.EndsWith("download.php", StringComparison.Ordinal) ? TorrentFile : null));
    }

    public sealed class InceptionOnly : IReleaseSearch
    {
        private static readonly Release[] Offered =
        [
            new() { TorrentId = 51, ReleaseName = "Inception.2010.1080p.WEB-DL-GRP", Title = "Inception", Year = 2010,
                ImdbId = Inception, SizeBytes = 1 << 20, Resolution = Resolution.Hd1080, Source = Source.Web },
            new() { TorrentId = 52, ReleaseName = "Inception.2010.720p.WEB-DL-GRP", Title = "Inception", Year = 2010,
                ImdbId = Inception, SizeBytes = 1 << 19, Resolution = Resolution.Hd720, Source = Source.Web },
        ];

        /// <summary>Out digitally, and turned down here: a web release filed outside the film categories.</summary>
        public const string Elsewhere = "tt0245429";

        /// <summary>Not out digitally: a camcorder recording, and nothing else.</summary>
        public const string InCinemas = "tt0000003";

        private static Task<RankedReleases> Find(string said) => Task.FromResult(said switch
        {
            Elsewhere => new RankedReleases([], [new RejectedRelease(new Release
            {
                TorrentId = 61, ReleaseName = "Spirited.Away.2001.1080p.WEB-DL-GRP", Title = "Spirited Away", Year = 2001,
                Resolution = Resolution.Hd1080, Source = Source.Web,
            }, RejectionReason.NotAFilm)]),
            InCinemas => new RankedReleases([new Release
            {
                TorrentId = 62, ReleaseName = "New.Film.2026.CAM-GRP", Title = "New Film", Year = 2026, Source = Source.Cam,
            }], []),
            _ => new RankedReleases(said == Inception || said.Contains("Inception") ? Offered : [], []),
        });

        public Task<RankedReleases> ByTextAsync(string typed, CancellationToken ct) => Find(typed);
        public Task<RankedReleases> ByTitleAsync(string query, CancellationToken ct) => Find(query);
        public Task<RankedReleases> ByImdbAsync(string imdbId, CancellationToken ct) => Find(imdbId);
    }

    private sealed class Answer(Func<HttpRequestMessage, string?> answer) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            Task.FromResult(answer(request) is { } body
                ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body) }
                : new HttpResponseMessage(HttpStatusCode.NotFound));
    }
}

/// <summary>A torrent client that keeps the torrents it is given, with their tags, and lists them back.</summary>
public sealed partial class TorrentClientStub : HttpMessageHandler
{
    public ConcurrentDictionary<string, (string Name, double Progress, HashSet<string> Tags)> Held { get; } = new();

    public void Tag(string hash, params string[] tags)
    {
        lock (Held) foreach (var t in tags) Held[hash].Tags.Add(t);
    }

    public void Progress(string hash, double progress)
    {
        lock (Held) Held[hash] = Held[hash] with { Progress = progress };
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        var path = request.RequestUri!.AbsolutePath;
        var body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(ct);

        if (path.EndsWith("app/version", StringComparison.Ordinal)) return Ok("v5.0.0");
        if (path.EndsWith("torrents/add", StringComparison.Ordinal))
        {
            var tags = TagsPart().Match(body) is { Success: true } m ? m.Groups[1].Value : "";
            lock (Held)
                Held[WatchNowFixture.Hash] = ("Inception.2010.1080p.WEB-DL-GRP", 0,
                    [.. tags.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)]);
            return Ok("Ok.");
        }
        if (path.EndsWith("torrents/addTags", StringComparison.Ordinal) || path.EndsWith("torrents/removeTags", StringComparison.Ordinal))
        {
            var form = HttpUtility.ParseQueryString(body);
            var adding = path.EndsWith("addTags", StringComparison.Ordinal);
            lock (Held)
                foreach (var hash in (form["hashes"] ?? "").Split('|'))
                    if (Held.TryGetValue(hash, out var t))
                        foreach (var tag in (form["tags"] ?? "").Split(','))
                            if (adding) t.Tags.Add(tag); else t.Tags.Remove(tag);
            return Ok("");
        }
        if (path.EndsWith("torrents/info", StringComparison.Ordinal))
        {
            lock (Held)
                return Ok("[" + string.Join(",", Held.Select(h =>
                    $$"""{"hash":"{{h.Key}}","name":"{{h.Value.Name}}","state":"downloading","progress":{{h.Value.Progress.ToString(System.Globalization.CultureInfo.InvariantCulture)}},"size":1048576,"content_path":"/x","seq_dl":true,"tags":"{{string.Join(", ", h.Value.Tags)}}"}""")) + "]");
        }
        return new HttpResponseMessage(HttpStatusCode.NotFound);
    }

    private static HttpResponseMessage Ok(string body) => new(HttpStatusCode.OK) { Content = new StringContent(body) };

    [GeneratedRegex("name=\"?tags\"?\\r\\n(?:[^\\r\\n]+\\r\\n)*\\r\\n([^\\r]*)")]
    private static partial Regex TagsPart();
}

/// <summary>
/// Watch now: a film that is not here is fetched for a room, the room says what it waits on, and the
/// film is loaded and played there the moment it can be opened.
/// </summary>
public sealed class WatchNowTests(WatchNowFixture fixture) : IClassFixture<WatchNowFixture>
{
    private HttpClient Member(string userId = "6001", string name = "Sam")
    {
        var client = fixture.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", RoomToken.Issue(new RoomTokenPayload
        {
            UserId = userId, DisplayName = name, RoomId = "", Member = true,
            ExpiresAtUnix = DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeSeconds(),
        }, SessionFixture.SigningKey));
        return client;
    }

    private SessionState Room(string room) => fixture.Services.GetRequiredService<SessionStore>().GetOrCreate(room);

    private static async Task Until(Func<bool> done, int seconds = 15)
    {
        var deadline = DateTime.UtcNow.AddSeconds(seconds);
        while (!done())
        {
            if (DateTime.UtcNow > deadline) throw new TimeoutException("It never happened.");
            await Task.Delay(100);
        }
    }

    [Fact]
    public async Task A_film_here_already_is_in_the_library_and_offered_nothing()
    {
        var availability = await Member().GetFromJsonAsync<FilmAvailability>("/api/films/tt0113277/availability");

        Assert.Equal(SessionFixture.Heat, availability!.LibraryId);
        Assert.Null(availability.Hd1080);
    }

    [Fact]
    public async Task Out_digitally_but_turned_down_here_is_not_offered_rather_than_not_out()
    {
        var turnedDown = await Member().GetFromJsonAsync<FilmAvailability>($"/api/films/{WatchNowFixture.InceptionOnly.Elsewhere}/availability");
        Assert.True(turnedDown!.NotOffered);
        Assert.False(turnedDown.NotOut);
        Assert.Contains("outside the film categories", turnedDown.EmptyExplanation);

        var inCinemas = await Member().GetFromJsonAsync<FilmAvailability>($"/api/films/{WatchNowFixture.InceptionOnly.InCinemas}/availability");
        Assert.True(inCinemas!.NotOut);
        Assert.False(inCinemas.NotOffered);
    }

    [Fact]
    public async Task A_film_not_here_is_pressed_waited_on_and_played()
    {
        var sam = Member();
        var availability = await sam.GetFromJsonAsync<FilmAvailability>($"/api/films/{WatchNowFixture.Inception}/availability");
        Assert.Equal("1080p", availability!.Hd1080?.Resolution);
        Assert.Equal("720p", availability.Hd720?.Resolution);
        Assert.False(availability.NotOut);

        using var pressed = await sam.PostAsJsonAsync("/api/watch", new WatchNowRequest
        {
            ImdbId = WatchNowFixture.Inception, TorrentId = availability.Hd1080!.TorrentId, Room = "inception-night",
        });
        var reply = await pressed.Content.ReadFromJsonAsync<WatchNowReply>();
        Assert.Equal("preparing", reply!.Outcome);
        Assert.Equal("inception-night", reply.Room);

        // Found, started, and the room is told it is arriving, with nobody named as having moved it.
        await Until(() => Room("inception-night").Preparing?.Stage == PreparingStages.Downloading);
        var waiting = Room("inception-night");
        Assert.Equal("Inception (2010)", waiting.Preparing!.Name);
        Assert.Equal("1080p WEB", waiting.Preparing.Release);
        Assert.Null(waiting.UpdatedBy);
        var tags = fixture.Torrents.Held[WatchNowFixture.Hash].Tags;
        Assert.Contains(TorrentTags.Watch("inception-night"), tags);
        Assert.Contains(TorrentTags.Requester(6001), tags);

        // Arriving: the room hears how far it has got.
        fixture.Torrents.Progress(WatchNowFixture.Hash, 0.25);
        await Until(() => Room("inception-night").Preparing?.Progress == 0.25);

        // The hand-off makes it watchable: it is loaded into the room and plays, and the room waits on nothing.
        fixture.AddTitle("inception-2010", new FilmIdentity { ImdbId = WatchNowFixture.Inception, Name = "Inception", Year = 2010 });
        fixture.Torrents.Tag(WatchNowFixture.Hash, TorrentTags.Watchable, TorrentTags.Library("inception-2010"));
        await Until(() => Room("inception-night").TitleId == "inception-2010");

        var playing = Room("inception-night");
        Assert.Null(playing.Preparing);
        Assert.False(playing.Paused);
        Assert.Equal("6001", playing.UpdatedBy?.UserId);
        await Until(() => !fixture.Torrents.Held[WatchNowFixture.Hash].Tags.Contains(TorrentTags.Watch("inception-night")));
    }

    [Fact]
    public async Task A_film_here_already_is_loaded_into_the_room_at_once()
    {
        using var pressed = await Member().PostAsJsonAsync("/api/watch", new WatchNowRequest { ImdbId = "tt0113277", Room = "heat-now" });
        var reply = await pressed.Content.ReadFromJsonAsync<WatchNowReply>();

        Assert.Equal("inLibrary", reply!.Outcome);
        Assert.Equal(SessionFixture.Heat, Room("heat-now").TitleId);
    }

    [Fact]
    public async Task A_film_nothing_offers_says_so_in_the_room()
    {
        using var pressed = await Member().PostAsJsonAsync("/api/watch", new WatchNowRequest { ImdbId = "tt0000001", Room = "nothing-here" });
        Assert.Equal("preparing", (await pressed.Content.ReadFromJsonAsync<WatchNowReply>())!.Outcome);

        await Until(() => Room("nothing-here").Preparing?.Stage == PreparingStages.Failed);
        Assert.Equal("notOffered", Room("nothing-here").Preparing!.Problem);
    }

    [Fact]
    public async Task Past_the_cap_on_films_watched_while_they_arrive_it_is_refused()
    {
        // Two films already being watched while they arrive fill the fixture's two slots.
        foreach (var (hash, room) in new[] { ("aaa", "busy-1"), ("bbb", "busy-2") })
            fixture.Torrents.Held[hash] = ("Something.2024.1080p.WEB-DL-GRP", 0.1, [TorrentTags.Watch(room)]);
        try
        {
            using var pressed = await Member().PostAsJsonAsync("/api/watch", new WatchNowRequest { ImdbId = "tt0000002" });
            var reply = await pressed.Content.ReadFromJsonAsync<WatchNowReply>();

            Assert.Equal(HttpStatusCode.Conflict, pressed.StatusCode);
            Assert.Equal("full", reply!.Outcome);
            Assert.Equal(new StreamSlots(2, 2), reply.Streams);
        }
        finally
        {
            fixture.Torrents.Held.TryRemove("aaa", out _);
            fixture.Torrents.Held.TryRemove("bbb", out _);
        }
    }

    [Fact]
    public async Task A_room_id_that_cannot_ride_on_a_tag_is_refused()
    {
        using var pressed = await Member().PostAsJsonAsync("/api/watch", new WatchNowRequest { ImdbId = WatchNowFixture.Inception, Room = "a,b" });

        Assert.Equal("badRoom", (await pressed.Content.ReadFromJsonAsync<WatchNowReply>())!.Outcome);
    }

    [Fact]
    public async Task An_activity_token_is_not_a_member_and_spends_nothing()
    {
        var client = fixture.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", fixture.TokenFor("room-1", "6009"));

        using var pressed = await client.PostAsJsonAsync("/api/watch", new WatchNowRequest { ImdbId = WatchNowFixture.Inception });

        Assert.Equal(HttpStatusCode.Forbidden, pressed.StatusCode);
    }
}
