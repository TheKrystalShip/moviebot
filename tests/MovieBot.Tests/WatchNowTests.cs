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
using TheKrystalShip.MovieBot.Acquire.Tmdb;
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

    /// <summary>A show, and its first season as one pack of three episode files.</summary>
    public const string Show = "tt5875444";
    public const string ShowRelease = "Show.S01.720p.WEB-DL-GRP";
    private const long ShowTorrentId = 71;

    private const string ShowTorrentFile =
        "d4:infod5:filesl"
        + "d6:lengthi10e4:pathl20:Show.S01E01.720p.mkvee"
        + "d6:lengthi10e4:pathl20:Show.S01E02.720p.mkvee"
        + "d6:lengthi10e4:pathl20:Show.S01E03.720p.mkvee"
        + "e4:name24:Show.S01.720p.WEB-DL-GRP12:piece lengthi16384e6:pieces20:AAAAAAAAAAAAAAAAAAAAee";

    public static readonly string ShowHash = Bencode.InfoHash(Encoding.ASCII.GetBytes(ShowTorrentFile));

    public static readonly IReadOnlyList<string> ShowFiles =
        [.. Bencode.Files(Encoding.ASCII.GetBytes(ShowTorrentFile)).Select(f => f.Path)];

    public TorrentClientStub Torrents { get; } = new();

    public WatchNowFixture()
    {
        // The catalogue lists the show: three episodes in its first season.
        KeepShow(Show, 5875444, "Show", [.. Enumerable.Range(1, 3).Select(n => new ShowEpisode(1, n, $"Episode {n}", null))]);
    }

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
        services.RemoveAll<IShowSearch>();
        services.AddSingleton<IShowSearch>(new OneSeasonPack());
        services.AddHttpClient<QBittorrentClient>().ConfigurePrimaryHttpMessageHandler(() => Torrents);
        services.AddHttpClient<TrackerClient>().ConfigurePrimaryHttpMessageHandler(() => new Answer(r =>
            !r.RequestUri!.AbsolutePath.EndsWith("download.php", StringComparison.Ordinal) ? null
            : r.RequestUri.Query.Contains($"={ShowTorrentId}", StringComparison.Ordinal) ? ShowTorrentFile
            : TorrentFile));
    }

    /// <summary>The tracker offers the show's first season as a 720p pack.</summary>
    private sealed class OneSeasonPack : IShowSearch
    {
        private static readonly Release Pack = new()
        {
            TorrentId = ShowTorrentId, ReleaseName = ShowRelease, Title = "Show", ImdbId = Show, SizeBytes = 30,
            Resolution = Resolution.Hd720, Source = Source.Web, Episodic = EpisodeMarker.Read(ShowRelease),
        };

        public Task<ShowReleases> ByShowAsync(string imdbId, CancellationToken ct) => Task.FromResult(
            new ShowReleases(imdbId == Show ? [new SeasonOffer(1, [Pack], [])] : [], []));

        public Task<ShowReleases> ShowByTextAsync(string typed, CancellationToken ct) => ByShowAsync(Show, ct);
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

/// <summary>
/// A torrent client that keeps the torrents it is given, with their tags and their files' priorities,
/// and lists them back. It knows the fixture's two torrents by what is in the upload.
/// </summary>
public sealed partial class TorrentClientStub : HttpMessageHandler
{
    public ConcurrentDictionary<string, (string Name, double Progress, HashSet<string> Tags)> Held { get; } = new();

    /// <summary>Each held torrent's files, as path and priority, in the torrent's order.</summary>
    public ConcurrentDictionary<string, List<(string Path, int Priority)>> Files { get; } = new();

    public void Tag(string hash, params string[] tags)
    {
        lock (Held) foreach (var t in tags) Held[hash].Tags.Add(t);
    }

    public void Progress(string hash, double progress)
    {
        lock (Held) Held[hash] = Held[hash] with { Progress = progress };
    }

    public int PriorityOf(string hash, string fileEnding)
    {
        lock (Held) return Files[hash].First(f => f.Path.EndsWith(fileEnding, StringComparison.Ordinal)).Priority;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        var path = request.RequestUri!.AbsolutePath;
        var query = HttpUtility.ParseQueryString(request.RequestUri.Query);
        var body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(ct);

        if (path.EndsWith("app/version", StringComparison.Ordinal)) return Ok("v5.0.0");
        if (path.EndsWith("torrents/add", StringComparison.Ordinal))
        {
            var tags = TagsPart().Match(body) is { Success: true } m ? m.Groups[1].Value : "";
            var show = body.Contains(WatchNowFixture.ShowRelease, StringComparison.Ordinal);
            var hash = show ? WatchNowFixture.ShowHash : WatchNowFixture.Hash;
            lock (Held)
            {
                Held[hash] = (show ? WatchNowFixture.ShowRelease : "Inception.2010.1080p.WEB-DL-GRP", 0,
                    [.. tags.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)]);
                Files[hash] = show
                    ? [.. WatchNowFixture.ShowFiles.Select(f => (f, 1))]
                    : [("Inception.2010.1080p.WEB-DL-GRP.mkv", 1)];
            }
            return Ok("Ok.");
        }
        if (path.EndsWith("torrents/files", StringComparison.Ordinal))
        {
            lock (Held)
                return Ok(Files.TryGetValue(query["hash"] ?? "", out var files)
                    ? "[" + string.Join(",", files.Select((f, i) =>
                        $$"""{"name":"{{f.Path}}","size":1048576,"index":{{i}},"priority":{{f.Priority}},"progress":0}""")) + "]"
                    : "[]");
        }
        if (path.EndsWith("torrents/filePrio", StringComparison.Ordinal))
        {
            var form = HttpUtility.ParseQueryString(body);
            lock (Held)
            {
                var files = Files[form["hash"]!];
                var priority = int.Parse(form["priority"]!, System.Globalization.CultureInfo.InvariantCulture);
                foreach (var id in (form["id"] ?? "").Split('|', StringSplitOptions.RemoveEmptyEntries))
                {
                    var i = int.Parse(id, System.Globalization.CultureInfo.InvariantCulture);
                    files[i] = (files[i].Path, priority);
                }
            }
            return Ok("");
        }
        if (path.EndsWith("torrents/start", StringComparison.Ordinal)) return Ok("");
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
            var only = query["hashes"];
            lock (Held)
                return Ok("[" + string.Join(",", Held.Where(h => only is null || h.Key == only).Select(h =>
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
    private HttpClient Member(string userId = "6001", string name = "Sam") => fixture.CreatePersonClient(userId, name);

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
        Assert.Contains(TorrentTags.Requester("6001"), tags);

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
            // Other tests' downloads may be arriving too; the two here are enough to fill both slots.
            Assert.Equal(2, reply.Streams!.Limit);
            Assert.True(reply.Streams.Used >= 2);
        }
        finally
        {
            fixture.Torrents.Held.TryRemove("aaa", out _);
            fixture.Torrents.Held.TryRemove("bbb", out _);
        }
    }

    [Fact]
    public async Task An_episode_is_fetched_out_of_its_season_and_the_room_waits_on_that_episode()
    {
        // The first episode is here and its season's torrent says so; the room asks for the second.
        fixture.AddEpisode("show-s01e01", WatchNowFixture.Show, "Show", 1, 1);
        using var pressed = await Member().PostAsJsonAsync("/api/watch", new WatchNowRequest
        {
            ImdbId = WatchNowFixture.Show, Episodes = [new EpisodePick(1, 2)], Room = "show-night",
        });
        Assert.Equal("preparing", (await pressed.Content.ReadFromJsonAsync<WatchNowReply>())!.Outcome);

        await Until(() => fixture.Torrents.Held.TryGetValue(WatchNowFixture.ShowHash, out var held)
            && held.Tags.Contains("watch:show-night:s01e02"));
        // The tag goes on before the files are prioritised, so the priority is waited for too.
        await Until(() => fixture.Torrents.PriorityOf(WatchNowFixture.ShowHash, "E02.720p.mkv") == 7);
        Assert.Equal(0, fixture.Torrents.PriorityOf(WatchNowFixture.ShowHash, "E03.720p.mkv"));
        // The room's own pick is the room's alone.
        Assert.DoesNotContain(TorrentTags.Keep(new EpisodeNumber(1, 2)), fixture.Torrents.Held[WatchNowFixture.ShowHash].Tags);
        await Until(() => Room("show-night").Preparing?.Stage == PreparingStages.Downloading);
        Assert.Equal("Show S01E02", Room("show-night").Preparing!.Name);

        // The torrent being watchable for the first episode does not put the first in the room.
        fixture.Torrents.Tag(WatchNowFixture.ShowHash, TorrentTags.Watchable, TorrentTags.Library("show-s01e01"));
        await Task.Delay(TimeSpan.FromSeconds(4));
        Assert.Null(Room("show-night").TitleId);

        // The second arriving does.
        fixture.AddEpisode("show-s01e02", WatchNowFixture.Show, "Show", 1, 2);
        await Until(() => Room("show-night").TitleId == "show-s01e02");
    }

    [Fact]
    public async Task A_room_holds_what_it_was_last_given_and_stops_waiting_on_what_it_held_before()
    {
        var sessions = fixture.Services.GetRequiredService<SessionStore>();
        sessions.Enter("one-thing", "6001");
        sessions.LoadTitle("one-thing", SessionFixture.Heat, new Actor("6001", "Sam"));

        // Asking for an episode that is not here puts it in the room at once, in place of the film.
        using var episode = await Member().PostAsJsonAsync("/api/watch", new WatchNowRequest
        {
            ImdbId = WatchNowFixture.Show, Episodes = [new EpisodePick(1, 3)], Room = "one-thing",
        });
        Assert.Equal("preparing", (await episode.Content.ReadFromJsonAsync<WatchNowReply>())!.Outcome);
        var holding = Room("one-thing");
        Assert.Null(holding.TitleId);
        Assert.Equal((1, 3), (holding.Preparing!.Season, holding.Preparing.Episode));
        Assert.Equal("6001", holding.UpdatedBy?.UserId);
        await Until(() => fixture.Torrents.Held.TryGetValue(WatchNowFixture.ShowHash, out var held)
            && held.Tags.Contains("watch:one-thing:s01e03"));

        // Given something else, the room holds that, and the season's torrent stops naming the room
        // for the episode it no longer waits on.
        sessions.LoadTitle("one-thing", SessionFixture.Heat, new Actor("6001", "Sam"));
        Assert.Null(Room("one-thing").Preparing);
        await Until(() => !fixture.Torrents.Held[WatchNowFixture.ShowHash].Tags.Contains("watch:one-thing:s01e03"));

        // The room moved past the episode, which is said for the hand-off to let go of.
        Assert.Contains(TorrentTags.Passed(new EpisodeNumber(1, 3)), fixture.Torrents.Held[WatchNowFixture.ShowHash].Tags);
    }

    [Fact]
    public async Task An_episode_here_already_is_loaded_into_the_room_at_once()
    {
        fixture.AddEpisode("show-s01e01", WatchNowFixture.Show, "Show", 1, 1);
        using var pressed = await Member().PostAsJsonAsync("/api/watch", new WatchNowRequest
        {
            ImdbId = WatchNowFixture.Show, Episodes = [new EpisodePick(1, 1)], Room = "show-here",
        });
        var reply = await pressed.Content.ReadFromJsonAsync<WatchNowReply>();

        Assert.Equal("inLibrary", reply!.Outcome);
        Assert.Equal("show-s01e01", Room("show-here").TitleId);
    }

    [Fact]
    public async Task The_episode_after_the_one_a_room_watches_is_fetched_ahead_and_only_once()
    {
        fixture.AddEpisode("show-s01e02", WatchNowFixture.Show, "Show", 1, 2);
        var sessions = fixture.Services.GetRequiredService<SessionStore>();
        sessions.Enter("show-watching", "6002");
        sessions.Join("show-watching", "conn-1", new Participant { UserId = "6002", DisplayName = "Jo" });
        sessions.LoadTitle("show-watching", "show-s01e02", new Actor("6002", "Jo"));
        var ahead = fixture.Services.GetServices<Microsoft.Extensions.Hosting.IHostedService>().OfType<Api.Shows.NextEpisodes>().Single();

        await ahead.PassAsync(CancellationToken.None);

        Assert.True(fixture.Torrents.PriorityOf(WatchNowFixture.ShowHash, "E03.720p.mkv") > 0);
        var tags = fixture.Torrents.Held[WatchNowFixture.ShowHash].Tags;
        Assert.DoesNotContain(tags, t => t.StartsWith(TorrentTags.RequesterPrefix, StringComparison.Ordinal) && t.EndsWith("6002", StringComparison.Ordinal));

        // The room names the next as one to fetch rather than one here, to somebody who may fetch it.
        var around = await Member("6002", "Jo").GetFromJsonAsync<System.Text.Json.Nodes.JsonObject>("/api/sessions/show-watching/around");
        Assert.Equal(3, (int)around!["next"]!["number"]!);
        Assert.Null(around["next"]!["titleId"]);
    }

    /// <summary>
    /// Runs <paramref name="test"/> against the show's season torrent held by the client, and puts
    /// its files and tags back as they were afterwards, since the other tests read them too.
    /// </summary>
    private async Task OnTheShowTorrentAsync(Func<Task> test)
    {
        if (!fixture.Torrents.Held.ContainsKey(WatchNowFixture.ShowHash))
        {
            using var first = await Member().PostAsJsonAsync("/api/watch", new WatchNowRequest
            {
                ImdbId = WatchNowFixture.Show, Episodes = [new EpisodePick(1, 3)], Room = "show-held",
            });
            await Until(() => fixture.Torrents.Held.TryGetValue(WatchNowFixture.ShowHash, out var held)
                && held.Tags.Contains("watch:show-held:s01e03"));
            await Until(() => fixture.Torrents.PriorityOf(WatchNowFixture.ShowHash, "E03.720p.mkv") == 7);
        }

        List<(string Path, int Priority)> files;
        HashSet<string> tags;
        lock (fixture.Torrents.Held)
        {
            files = [.. fixture.Torrents.Files[WatchNowFixture.ShowHash]];
            tags = [.. fixture.Torrents.Held[WatchNowFixture.ShowHash].Tags];
        }
        try
        {
            await test();
        }
        finally
        {
            lock (fixture.Torrents.Held)
            {
                fixture.Torrents.Files[WatchNowFixture.ShowHash] = files;
                var held = fixture.Torrents.Held[WatchNowFixture.ShowHash].Tags;
                held.Clear();
                held.UnionWith(tags);
            }
        }
    }

    [Fact]
    public Task Asking_again_withdraws_the_verdict_and_puts_the_episode_asked_for_first() => OnTheShowTorrentAsync(async () =>
    {
        lock (fixture.Torrents.Held)
        {
            var files = fixture.Torrents.Files[WatchNowFixture.ShowHash];
            for (var i = 0; i < files.Count; i++)
                files[i] = (files[i].Path, files[i].Path.EndsWith("E01.720p.mkv", StringComparison.Ordinal) ? 7 : 0);
        }
        fixture.Torrents.Tag(WatchNowFixture.ShowHash, TorrentTags.IngestFailed);

        using var pressed = await Member().PostAsJsonAsync("/api/watch", new WatchNowRequest
        {
            ImdbId = WatchNowFixture.Show, Episodes = [new EpisodePick(1, 3)], Room = "show-again",
        });

        // The hand-off is to try the torrent afresh, so the room is not told it failed first.
        await Until(() => fixture.Torrents.PriorityOf(WatchNowFixture.ShowHash, "E03.720p.mkv") == 7);
        Assert.DoesNotContain(TorrentTags.IngestFailed, fixture.Torrents.Held[WatchNowFixture.ShowHash].Tags);

        // What was at the top and is still arriving goes back to normal: one file is wanted soonest.
        Assert.Equal(1, fixture.Torrents.PriorityOf(WatchNowFixture.ShowHash, "E01.720p.mkv"));
        Assert.Equal(0, fixture.Torrents.PriorityOf(WatchNowFixture.ShowHash, "E02.720p.mkv"));
    });

    [Fact]
    public Task An_episode_that_can_play_is_loaded_whatever_the_torrent_says() => OnTheShowTorrentAsync(async () =>
    {
        var sessions = fixture.Services.GetRequiredService<SessionStore>();
        sessions.Enter("failed-but-here", "6001");
        sessions.Prepare("failed-but-here", new RoomPreparing
        {
            ImdbId = WatchNowFixture.Show, Season = 1, Episode = 2, Name = "Show S01E02", Stage = PreparingStages.Downloading,
        }, new Actor("6001", "Sam"));
        fixture.AddEpisode("show-s01e02", WatchNowFixture.Show, "Show", 1, 2);
        fixture.Torrents.Tag(WatchNowFixture.ShowHash, TorrentTags.Watch("failed-but-here", new EpisodeNumber(1, 2)), TorrentTags.IngestFailed);

        await Until(() => Room("failed-but-here").TitleId == "show-s01e02");
    });

    [Fact]
    public Task An_episode_asked_for_outright_is_kept_whoever_moves_past_it() => OnTheShowTorrentAsync(async () =>
    {
        // The offers are what a pick is resolved against.
        using var offers = await Member().GetAsync($"/api/shows/{WatchNowFixture.Show}/releases");
        offers.EnsureSuccessStatusCode();

        using var started = await Member().PostAsJsonAsync("/api/downloads", new StartDownloadRequest
        {
            TorrentId = 71, Episodes = [new EpisodePick(1, 1)],
        });
        Assert.Equal("started", (await started.Content.ReadFromJsonAsync<StartDownloadReply>())!.Outcome);

        Assert.Contains(TorrentTags.Keep(new EpisodeNumber(1, 1)), fixture.Torrents.Held[WatchNowFixture.ShowHash].Tags);
    });

    [Fact]
    public Task A_room_left_holding_nothing_moved_past_nothing() => OnTheShowTorrentAsync(async () =>
    {
        // The room's tag names an episode it is not waiting on, and the room holds nothing at all.
        fixture.Torrents.Tag(WatchNowFixture.ShowHash, TorrentTags.Watch("left-empty", new EpisodeNumber(1, 1)));

        await Until(() => !fixture.Torrents.Held[WatchNowFixture.ShowHash].Tags.Contains("watch:left-empty:s01e01"));
        Assert.DoesNotContain(TorrentTags.Passed(new EpisodeNumber(1, 1)), fixture.Torrents.Held[WatchNowFixture.ShowHash].Tags);
    });

    [Fact]
    public async Task A_room_id_that_cannot_ride_on_a_tag_is_refused()
    {
        using var pressed = await Member().PostAsJsonAsync("/api/watch", new WatchNowRequest { ImdbId = WatchNowFixture.Inception, Room = "a,b" });

        Assert.Equal("badRoom", (await pressed.Content.ReadFromJsonAsync<WatchNowReply>())!.Outcome);
    }

    [Fact]
    public async Task Somebody_who_may_not_download_watches_nothing_now()
    {
        var client = fixture.CreatePersonClient("6009", actions: [Api.Auth.CinemaActions.Watch]);

        using var pressed = await client.PostAsJsonAsync("/api/watch", new WatchNowRequest { ImdbId = WatchNowFixture.Inception });

        Assert.Equal(HttpStatusCode.Forbidden, pressed.StatusCode);
    }
}
