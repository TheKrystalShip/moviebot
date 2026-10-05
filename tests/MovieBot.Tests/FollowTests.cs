using System.Buffers.Text;
using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using TheKrystalShip.MovieBot.Acquire.Download;
using TheKrystalShip.MovieBot.Acquire.Search;
using TheKrystalShip.MovieBot.Acquire.Tmdb;
using TheKrystalShip.MovieBot.Acquire.Tracker;
using TheKrystalShip.MovieBot.Api.Acquisition;
using TheKrystalShip.MovieBot.Api.Library;
using TheKrystalShip.MovieBot.Api.Notifications;
using TheKrystalShip.MovieBot.Api.People;
using TheKrystalShip.MovieBot.Api.Shows;
using TheKrystalShip.MovieBot.Core;
using Xunit;

namespace TheKrystalShip.MovieBot.Tests;

/// <summary>
/// The film database for the follow tests: shows that are set up by the test, each with the episodes
/// and air dates it names. It counts nothing and keeps no images.
/// </summary>
public sealed class ShowDatabaseStub : HttpMessageHandler
{
    public sealed class FakeShow
    {
        public required int TmdbId { get; init; }
        public string Status { get; set; } = "Returning Series";
        public List<(int Season, int Number, string AirDate)> Episodes { get; } = [];
        public (int Season, int Number, string AirDate)? Next { get; set; }
    }

    public ConcurrentDictionary<string, FakeShow> Shows { get; } = new();

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        var path = request.RequestUri!.AbsolutePath;
        if (path.Contains("/find/", StringComparison.Ordinal))
        {
            var imdb = path[(path.LastIndexOf('/') + 1)..];
            return Json(Shows.TryGetValue(imdb, out var found)
                ? $$"""{ "movie_results": [], "tv_results": [ { "id": {{found.TmdbId}} } ] }"""
                : """{ "movie_results": [], "tv_results": [] }""");
        }

        var parts = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var at = Array.IndexOf(parts, "tv");
        if (at < 0 || !int.TryParse(parts[at + 1], CultureInfo.InvariantCulture, out var id)
            || Shows.Values.FirstOrDefault(s => s.TmdbId == id) is not { } show)
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));

        if (parts.Length > at + 3 && parts[at + 2] == "season" && int.TryParse(parts[at + 3], CultureInfo.InvariantCulture, out var season))
        {
            var episodes = show.Episodes.Where(e => e.Season == season).OrderBy(e => e.Number).Select(e =>
                $$"""{ "season_number": {{e.Season}}, "episode_number": {{e.Number}}, "name": "Episode {{e.Number}}", "air_date": "{{e.AirDate}}" }""");
            return Json($$"""{ "season_number": {{season}}, "name": "Season {{season}}", "episodes": [ {{string.Join(",", episodes)}} ] }""");
        }

        var seasons = show.Episodes.GroupBy(e => e.Season).OrderBy(g => g.Key).Select(g =>
            $$"""{ "season_number": {{g.Key}}, "name": "Season {{g.Key}}", "episode_count": {{g.Count()}}, "air_date": "{{g.Min(e => e.AirDate)}}" }""");
        var next = show.Next is { } n
            ? $$""", "next_episode_to_air": { "season_number": {{n.Season}}, "episode_number": {{n.Number}}, "name": "Episode {{n.Number}}", "air_date": "{{n.AirDate}}" }"""
            : "";
        return Json($$"""
            { "id": {{show.TmdbId}}, "name": "Show {{show.TmdbId}}", "status": "{{show.Status}}", "in_production": true,
              "seasons": [ {{string.Join(",", seasons)}} ]{{next}} }
            """);
    }

    private static Task<HttpResponseMessage> Json(string body) =>
        Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") });
}

/// <summary>
/// The API with a clock the test moves, a film database and a tracker the test sets up, and a
/// torrent client that keeps what it is given. Episodes are offered as the season's packs, at 720p
/// and 1080p, so which quality was picked is which torrent the tracker was asked for.
/// </summary>
public class FollowFixture : SessionFixture
{
    public const long Pack720 = 71;
    public const long Pack1080 = 72;

    /// <summary>The smallest torrent holding the episodes of a season: three files named for them.</summary>
    private const string PackFile =
        "d4:infod5:filesl"
        + "d6:lengthi10e4:pathl20:Show.S01E01.720p.mkvee"
        + "d6:lengthi10e4:pathl20:Show.S01E02.720p.mkvee"
        + "d6:lengthi10e4:pathl20:Show.S01E03.720p.mkvee"
        + "e4:name24:Show.S01.720p.WEB-DL-GRP12:piece lengthi16384e6:pieces20:AAAAAAAAAAAAAAAAAAAAee";

    /// <summary>The clock every service in the API reads, set to now and moved by the test.</summary>
    public FakeTimeProvider Clock { get; } = new(DateTimeOffset.UtcNow);

    public ShowDatabaseStub Database { get; } = new();

    public TorrentClientStub Torrents { get; } = new();

    /// <summary>What the tracker is asked for, by show: how often the show's releases were searched.</summary>
    public ConcurrentDictionary<string, int> Searches { get; } = new();

    /// <summary>The query of every torrent file the tracker was asked to hand over.</summary>
    public ConcurrentQueue<string> Fetched { get; } = new();

    /// <summary>What the tracker offers, by show; a show that is not here is offered nothing.</summary>
    public ConcurrentDictionary<string, ShowReleases> Offered { get; } = new();

    /// <summary>The tracker offers the show's first season as a pack at each quality.</summary>
    public void Offer(string imdbId, bool hd720 = true, bool hd1080 = true)
    {
        List<Release> packs = [];
        if (hd1080) packs.Add(Pack(Pack1080, "Show.S01.1080p.WEB-DL-GRP", Resolution.Hd1080, imdbId));
        if (hd720) packs.Add(Pack(Pack720, WatchNowFixture.ShowRelease, Resolution.Hd720, imdbId));
        Offered[imdbId] = new ShowReleases([new SeasonOffer(1, packs, [])], []);
    }

    private static Release Pack(long id, string name, Resolution resolution, string imdbId) => new()
    {
        TorrentId = id, ReleaseName = name, Title = "Show", ImdbId = imdbId, SizeBytes = 30,
        Resolution = resolution, Source = Source.Web, Episodic = EpisodeMarker.Read(name),
    };

    protected override Dictionary<string, string?> MoreSettings() => new()
    {
        ["Tracker:Username"] = "u",
        ["Tracker:Passkey"] = "p",
        ["Download:ReservedGiB"] = "0",
    };

    protected override void MoreServices(IServiceCollection services)
    {
        services.RemoveAll<TimeProvider>();
        services.AddSingleton<TimeProvider>(Clock);
        services.AddHttpClient<TmdbClient>().ConfigurePrimaryHttpMessageHandler(() => Database);
        services.RemoveAll<IShowSearch>();
        services.AddSingleton<IShowSearch>(new Search(this));
        services.AddHttpClient<QBittorrentClient>().ConfigurePrimaryHttpMessageHandler(() => Torrents);
        services.AddHttpClient<TrackerClient>().ConfigurePrimaryHttpMessageHandler(() => new Answer(this));
    }

    private sealed class Search(FollowFixture fixture) : IShowSearch
    {
        public Task<ShowReleases> ByShowAsync(string imdbId, CancellationToken ct)
        {
            fixture.Searches.AddOrUpdate(imdbId, 1, (_, n) => n + 1);
            return Task.FromResult(fixture.Offered.GetValueOrDefault(imdbId) ?? new ShowReleases([], []));
        }

        public Task<ShowReleases> ShowByTextAsync(string typed, CancellationToken ct) =>
            Task.FromResult(new ShowReleases([], []));
    }

    private sealed class Answer(FollowFixture fixture) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            if (!request.RequestUri!.AbsolutePath.EndsWith("download.php", StringComparison.Ordinal))
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
            fixture.Fetched.Enqueue(request.RequestUri.Query);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(PackFile) });
        }
    }
}

/// <summary>A disk with less room than the reserve it must keep, so every download is refused.</summary>
public sealed class FullDiskFixture : FollowFixture
{
    protected override Dictionary<string, string?> MoreSettings()
    {
        var settings = base.MoreSettings();
        settings["Download:ReservedGiB"] = "100000000";
        return settings;
    }
}

/// <summary>
/// Following a show: the routes, the fields the show's page gains, and the watcher that tells the
/// followers of a show when an episode is out and fetches it for those who asked.
/// </summary>
public sealed class FollowTests : IClassFixture<FollowFixture>
{
    private readonly FollowFixture _fixture;
    private static int s_next = 100;

    public FollowTests(FollowFixture fixture)
    {
        _fixture = fixture;
        fixture.Torrents.Held.Clear();
        fixture.Torrents.Files.Clear();
        while (fixture.Fetched.TryDequeue(out _)) { }
    }

    private FakeTimeProvider Clock => _fixture.Clock;

    private string Day(int fromToday) =>
        DateOnly.FromDateTime(Clock.GetUtcNow().UtcDateTime).AddDays(fromToday).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    /// <summary>A show the film database knows, with its episodes at days from today, and a number nobody else uses.</summary>
    private string Show(params (int Number, int Days)[] episodes)
    {
        var n = Interlocked.Increment(ref s_next);
        var imdb = $"tt91{n:00000}";
        var show = new ShowDatabaseStub.FakeShow { TmdbId = 9000 + n };
        foreach (var (number, days) in episodes) show.Episodes.Add((1, number, Day(days)));
        _fixture.Database.Shows[imdb] = show;
        return imdb;
    }

    private HttpClient As(string userId, string name = "Cy")
    {
        _fixture.Services.GetRequiredService<PersonDirectory>().Remember(userId, name);
        var client = _fixture.CreateClient();
        // Lives as long as the clock the tests move days at a time.
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer",
            _fixture.Issuer.Mint(userId, name, lifetime: TimeSpan.FromDays(400)));
        return client;
    }

    private static async Task<FollowView> Follow(HttpClient client, string imdb, bool autoDownload = false)
    {
        using var response = await client.PutAsJsonAsync($"/api/me/follows/{imdb}", new FollowRequest { AutoDownload = autoDownload });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<FollowView>())!;
    }

    private Task Pass() => _fixture.Services.GetRequiredService<FollowWatcher>().PassAsync(CancellationToken.None);

    private IReadOnlyList<StoredNotification> Told(string userId, string imdb) =>
        [.. _fixture.Services.GetRequiredService<NotificationStore>().For(userId)
            .Where(n => n.Kind == NotificationKinds.Episode && n.Key.StartsWith($"episode:{imdb}:", StringComparison.Ordinal))];

    private int Asked(string imdb) => _fixture.Searches.GetValueOrDefault(imdb);

    /// <summary>A show with two episodes out and a third that airs the day after the follows begin, and has aired by the time they are looked at.</summary>
    private string AiringShow() => Show((1, -30), (2, -10), (3, 1));

    /// <summary>An episode of the show in the library, with a picture of the width given.</summary>
    private void InLibrary(string id, string imdb, int season, int number, int width)
    {
        var manifest = new Manifest
        {
            Id = id, Title = id, Kind = TitleKind.Show, DurationSeconds = 3000, Status = TitleStatus.Ready,
            Film = new FilmIdentity { ImdbId = imdb, Name = "Show" },
            Episode = new EpisodeIdentity { Season = season, Number = number },
            Video = new VideoInfo
            {
                Width = width, Height = width * 9 / 16, SourceCodec = "h264", SourceHdr = "sdr",
                Renditions = [new Rendition { Name = "r", BitrateKbps = 4000, Width = width, Height = width * 9 / 16, Uri = "v0/index.m3u8" }],
            },
            Audio = [], Subtitles = [],
        };
        ManifestJson.WriteAtomic(Path.Combine(_fixture.MediaRoot, id, "manifest.json"), manifest);
        _fixture.Services.GetRequiredService<LibraryListing>().Refresh();
    }

    private void Watched(string userId, string titleId) =>
        _fixture.Services.GetRequiredService<WatchRecord>().Record(userId, titleId, 3000, 3000, Clock.GetUtcNow());

    [Fact]
    public async Task A_person_follows_a_show_and_stops()
    {
        var imdb = Show((1, -30), (2, -3));
        _fixture.Database.Shows[imdb].Next = (1, 3, Day(5));
        var sam = As("7001", "Sam");

        var followed = await Follow(sam, imdb, autoDownload: true);
        Assert.Equal(imdb, followed.ImdbId);
        Assert.Equal($"Show {_fixture.Database.Shows[imdb].TmdbId}", followed.Title);
        Assert.True(followed.AutoDownload);
        Assert.Equal(new AiringView(1, 3, "Episode 3", Day(5)), followed.NextEpisode);

        var listed = await sam.GetFromJsonAsync<List<FollowView>>("/api/me/follows");
        Assert.Equal(imdb, Assert.Single(listed!).ImdbId);

        using var first = await sam.DeleteAsync($"/api/me/follows/{imdb}");
        using var again = await sam.DeleteAsync($"/api/me/follows/{imdb}");
        Assert.Equal(HttpStatusCode.NoContent, first.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, again.StatusCode);
        Assert.Empty((await sam.GetFromJsonAsync<List<FollowView>>("/api/me/follows"))!);
    }

    [Fact]
    public async Task Follows_are_listed_newest_first_and_following_again_changes_only_the_download()
    {
        var older = Show((1, -3));
        var newer = Show((1, -3));
        var sam = As("7002");

        var first = await Follow(sam, older);
        Clock.Advance(TimeSpan.FromMinutes(5));
        await Follow(sam, newer);
        Clock.Advance(TimeSpan.FromMinutes(5));
        var changed = await Follow(sam, older, autoDownload: true);

        Assert.Equal(first.Since, changed.Since);
        Assert.True(changed.AutoDownload);
        var listed = await sam.GetFromJsonAsync<List<FollowView>>("/api/me/follows");
        Assert.Equal([newer, older], listed!.Select(f => f.ImdbId));
    }

    [Fact]
    public async Task A_show_that_cannot_be_followed_is_refused_with_the_reason()
    {
        var sam = As("7003");
        var ended = Show((1, -3));
        _fixture.Database.Shows[ended].Status = "Ended";
        var canceled = Show((1, -3));
        _fixture.Database.Shows[canceled].Status = "Canceled";

        async Task<HttpStatusCode> Put(string id)
        {
            using var response = await sam.PutAsJsonAsync($"/api/me/follows/{id}", new FollowRequest());
            return response.StatusCode;
        }

        Assert.Equal(HttpStatusCode.BadRequest, await Put("not-an-id"));
        Assert.Equal(HttpStatusCode.NotFound, await Put("tt9999999"));
        Assert.Equal(HttpStatusCode.Conflict, await Put(ended));
        Assert.Equal(HttpStatusCode.Conflict, await Put(canceled));
        Assert.Empty((await sam.GetFromJsonAsync<List<FollowView>>("/api/me/follows"))!);
    }

    [Fact]
    public async Task Follows_answer_for_a_person_and_never_for_the_service_key()
    {
        var bot = _fixture.CreateServiceClient();
        using var list = await bot.GetAsync("/api/me/follows");
        using var put = await bot.PutAsJsonAsync("/api/me/follows/tt9100001", new FollowRequest());
        using var delete = await bot.DeleteAsync("/api/me/follows/tt9100001");

        Assert.Equal(HttpStatusCode.Unauthorized, list.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, put.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, delete.StatusCode);
    }

    [Fact]
    public async Task What_somebody_follows_is_theirs_alone()
    {
        var imdb = Show((1, -3));
        await Follow(As("7004"), imdb);

        Assert.Empty((await As("7005").GetFromJsonAsync<List<FollowView>>("/api/me/follows"))!);
    }

    [Fact]
    public async Task A_shows_page_says_whether_it_can_be_followed_and_whether_the_asker_does()
    {
        var airing = Show((1, -30), (2, -3));
        _fixture.Database.Shows[airing].Next = (1, 3, Day(4));
        var ended = Show((1, -30));
        _fixture.Database.Shows[ended].Status = "Ended";
        var sam = As("7006");
        var jo = As("7007");
        await Follow(sam, airing, autoDownload: true);

        async Task<JsonElement> Page(HttpClient who, string id)
        {
            using var response = await who.GetAsync($"/api/shows/{id}");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            return JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.Clone();
        }

        var mine = await Page(sam, airing);
        Assert.True(mine.GetProperty("followable").GetBoolean());
        Assert.True(mine.GetProperty("follow").GetProperty("autoDownload").GetBoolean());
        Assert.True(mine.GetProperty("follow").TryGetProperty("since", out _));
        Assert.Equal(3, mine.GetProperty("nextEpisode").GetProperty("episode").GetInt32());
        Assert.Equal(Day(4), mine.GetProperty("nextEpisode").GetProperty("airDate").GetString());

        var theirs = await Page(jo, airing);
        Assert.True(theirs.GetProperty("followable").GetBoolean());
        Assert.False(theirs.TryGetProperty("follow", out _));

        var over = await Page(sam, ended);
        Assert.False(over.GetProperty("followable").GetBoolean());
        Assert.False(over.TryGetProperty("nextEpisode", out _));
    }

    [Fact]
    public async Task Following_a_show_with_episodes_out_announces_none_of_them_and_asks_the_tracker_nothing()
    {
        var imdb = Show((1, -30), (2, -20), (3, -10), (4, -1), (5, 6));
        _fixture.Offer(imdb);
        await Follow(As("7010"), imdb);

        await Pass();
        Clock.Advance(TimeSpan.FromDays(2));
        await Pass();

        Assert.Empty(Told("7010", imdb));
        Assert.Equal(0, Asked(imdb));
    }

    [Fact]
    public async Task An_episode_after_the_follow_is_announced_once_it_is_due_and_offered()
    {
        var imdb = AiringShow();
        _fixture.Offer(imdb);
        await Follow(As("7011"), imdb);

        // It airs tomorrow: not due, and the tracker is not asked.
        await Pass();
        Assert.Equal(0, Asked(imdb));

        // The day it airs is not due either.
        Clock.Advance(TimeSpan.FromDays(1));
        await Pass();
        Assert.Equal(0, Asked(imdb));
        Assert.Empty(Told("7011", imdb));

        // The day after is.
        Clock.Advance(TimeSpan.FromDays(1));
        await Pass();
        var told = Assert.Single(Told("7011", imdb));
        Assert.Equal("Show " + _fixture.Database.Shows[imdb].TmdbId + " S1 · E3 is out.", told.Text);
        Assert.Equal($"/tv/{imdb}/season/1", told.Url);
        Assert.Equal(1, Asked(imdb));
    }

    [Fact]
    public async Task An_episode_the_tracker_does_not_offer_yet_is_asked_for_hourly()
    {
        var imdb = AiringShow();
        await Follow(As("7012"), imdb);
        Clock.Advance(TimeSpan.FromDays(2));

        await Pass();
        Clock.Advance(TimeSpan.FromMinutes(30));
        await Pass();
        Assert.Equal(1, Asked(imdb));
        Assert.Empty(Told("7012", imdb));

        Clock.Advance(TimeSpan.FromMinutes(31));
        await Pass();
        Assert.Equal(2, Asked(imdb));

        _fixture.Offer(imdb);
        Clock.Advance(TimeSpan.FromMinutes(61));
        await Pass();
        Assert.Equal(3, Asked(imdb));
        Assert.Single(Told("7012", imdb));
    }

    [Fact]
    public async Task An_episode_is_looked_for_until_fourteen_days_past_its_air_date_and_no_longer()
    {
        // Airs the day after the follow; a pass at 14 days past the air date still asks, one at 15 does not.
        var within = AiringShow();
        await Follow(As("7013"), within);

        Clock.Advance(TimeSpan.FromDays(15));
        await Pass();
        Assert.Equal(1, Asked(within));

        Clock.Advance(TimeSpan.FromDays(1));
        await Pass();
        Assert.Equal(1, Asked(within));

        // Offered after the window, it is never announced.
        _fixture.Offer(within);
        Clock.Advance(TimeSpan.FromDays(2));
        await Pass();
        Assert.Empty(Told("7013", within));
    }

    [Fact]
    public async Task What_was_announced_stays_announced_across_a_restart()
    {
        var imdb = AiringShow();
        _fixture.Offer(imdb);
        var sam = As("7014");
        await Follow(sam, imdb);
        Clock.Advance(TimeSpan.FromDays(2));
        await Pass();
        Assert.Single(Told("7014", imdb));
        Assert.Equal(1, Asked(imdb));

        // A new process reads the file the first one wrote.
        var path = Path.Combine(_fixture.StateRoot, "follows.json");
        var restarted = new FollowStore(path, NullLogger<FollowStore>.Instance);
        Assert.True(restarted.IsAnnounced(imdb, 1, 3));
        var services = _fixture.Services;
        var watcher = new FollowWatcher(
            restarted, services.GetRequiredService<ShowStore>(), services.GetRequiredService<ShowOfferCache>(),
            services.GetRequiredService<TitleLibrary>(), services.GetRequiredService<Downloads>(),
            services.GetRequiredService<AcquisitionService>(), services.GetRequiredService<WatchRecord>(),
            services.GetRequiredService<PersonDirectory>(), services.GetRequiredService<Notifier>(),
            Options.Create(new FollowOptions()), Clock, NullLogger<FollowWatcher>.Instance);
        Clock.Advance(TimeSpan.FromHours(3));
        await watcher.PassAsync(CancellationToken.None);

        Assert.Equal(1, Asked(imdb));
        Assert.Single(Told("7014", imdb));
    }

    [Fact]
    public async Task A_refreshed_show_with_a_new_episode_is_noticed()
    {
        var imdb = Show((1, -30), (2, -10));
        _fixture.Offer(imdb);
        await Follow(As("7015"), imdb);
        await Pass();

        // The database lists the next episode a day or two after the follow.
        _fixture.Database.Shows[imdb].Episodes.Add((1, 3, Day(0)));
        Clock.Advance(TimeSpan.FromDays(2));
        await Pass();

        Assert.Single(Told("7015", imdb));
    }

    [Fact]
    public async Task An_unfollowed_show_tells_nobody()
    {
        var imdb = AiringShow();
        _fixture.Offer(imdb);
        var sam = As("7016");
        await Follow(sam, imdb);
        using var _ = await sam.DeleteAsync($"/api/me/follows/{imdb}");

        Clock.Advance(TimeSpan.FromDays(2));
        await Pass();

        Assert.Empty(Told("7016", imdb));
        Assert.Equal(0, Asked(imdb));
    }

    [Fact]
    public async Task One_download_serves_every_follower_who_wants_one_at_the_best_quality_any_of_them_watches_at()
    {
        var imdb = AiringShow();
        _fixture.Offer(imdb);
        InLibrary("hd-watched", imdb, 1, 2, 1920);
        Watched("7022", "hd-watched");

        await Follow(As("7021"), imdb, autoDownload: true);
        Clock.Advance(TimeSpan.FromMinutes(1));
        await Follow(As("7022"), imdb, autoDownload: true);
        await Follow(As("7023"), imdb);
        Clock.Advance(TimeSpan.FromDays(2));
        await Pass();

        // One torrent file asked for, the 1080p pack, started as whoever followed first.
        var fetched = Assert.Single(_fixture.Fetched);
        Assert.Contains($"={FollowFixture.Pack1080}", fetched);
        var tags = _fixture.Torrents.Held[WatchNowFixture.ShowHash].Tags;
        Assert.Equal([TorrentTags.Requester("7021")], tags.Where(t => TorrentTags.ReadRequester(t) is not null));
        Assert.Equal(0, _fixture.Torrents.PriorityOf(WatchNowFixture.ShowHash, "S01E01.720p.mkv"));
        Assert.Equal(7, _fixture.Torrents.PriorityOf(WatchNowFixture.ShowHash, "S01E03.720p.mkv"));

        // The one it was started as is told by the download when it can be watched; the others, now.
        Assert.Empty(Told("7021", imdb));
        Assert.Single(Told("7022", imdb));
        Assert.Single(Told("7023", imdb));
    }

    [Fact]
    public async Task A_follower_who_has_watched_nothing_of_it_is_fetched_for_at_720p()
    {
        var imdb = AiringShow();
        _fixture.Offer(imdb);
        await Follow(As("7031"), imdb, autoDownload: true);
        Clock.Advance(TimeSpan.FromDays(2));
        await Pass();

        Assert.Contains($"={FollowFixture.Pack720}", Assert.Single(_fixture.Fetched));
    }

    [Fact]
    public async Task The_latest_episode_a_follower_watched_sets_their_quality()
    {
        var imdb = AiringShow();
        _fixture.Offer(imdb);
        InLibrary("early-hd", imdb, 1, 1, 1920);
        InLibrary("late-sd", imdb, 1, 2, 1280);
        Watched("7041", "early-hd");
        Clock.Advance(TimeSpan.FromMinutes(5));
        Watched("7041", "late-sd");

        await Follow(As("7041"), imdb, autoDownload: true);
        Clock.Advance(TimeSpan.FromDays(2));
        await Pass();

        Assert.Contains($"={FollowFixture.Pack720}", Assert.Single(_fixture.Fetched));
    }

    [Fact]
    public async Task An_episode_already_in_the_library_is_announced_without_fetching_or_asking_the_tracker()
    {
        var imdb = AiringShow();
        _fixture.Offer(imdb);
        await Follow(As("7051"), imdb, autoDownload: true);
        await Follow(As("7052"), imdb);
        InLibrary("already-here", imdb, 1, 3, 1920);
        Clock.Advance(TimeSpan.FromDays(2));
        await Pass();

        Assert.Empty(_fixture.Fetched);
        Assert.Equal(0, Asked(imdb));
        Assert.Single(Told("7051", imdb));
        Assert.Single(Told("7052", imdb));
    }

    [Fact]
    public async Task An_episode_somebody_else_is_already_downloading_is_announced_and_not_fetched_again()
    {
        var imdb = AiringShow();
        _fixture.Offer(imdb);
        _fixture.Torrents.Held[WatchNowFixture.ShowHash] = (WatchNowFixture.ShowRelease, 0.1, [TorrentTags.Imdb(imdb)]);
        _fixture.Torrents.Files[WatchNowFixture.ShowHash] = [.. WatchNowFixture.ShowFiles.Select(f => (f, 1))];
        await Follow(As("7061"), imdb, autoDownload: true);
        Clock.Advance(TimeSpan.FromDays(2));
        await Pass();

        Assert.Empty(_fixture.Fetched);
        Assert.Single(Told("7061", imdb));
    }

    [Fact]
    public async Task A_follower_who_switched_the_episode_kind_off_has_it_in_the_bell_and_no_push()
    {
        var imdb = AiringShow();
        _fixture.Offer(imdb);
        var loud = As("7071");
        var quiet = As("7072");
        foreach (var (client, id) in new[] { (loud, "7071"), (quiet, "7072") })
        {
            using var subscribed = await client.PostAsJsonAsync("/api/me/push", Browser($"https://push.invalid/send/{id}"));
            Assert.Equal(HttpStatusCode.NoContent, subscribed.StatusCode);
        }
        using var off = await quiet.PutAsJsonAsync("/api/me/notifications/preferences", new NotificationPreferences { Episode = false });
        await Follow(loud, imdb);
        await Follow(quiet, imdb);

        Clock.Advance(TimeSpan.FromDays(2));
        await Pass();

        Assert.Single(Told("7071", imdb));
        Assert.Single(Told("7072", imdb));
        Assert.Equal(1, _fixture.PushService.SentTo("https://push.invalid/send/7071"));
        Assert.Equal(0, _fixture.PushService.SentTo("https://push.invalid/send/7072"));
    }

    [Fact]
    public void The_episode_kind_is_pushed_unless_switched_off()
    {
        Assert.True(new NotificationPreferences().Allows(NotificationKinds.Episode));
        Assert.False(new NotificationPreferences { Episode = false }.Allows(NotificationKinds.Episode));
    }

    /// <summary>A subscription as a browser makes one: a P-256 public key and an auth secret, base64url.</summary>
    private static PushSubscribeRequest Browser(string endpoint)
    {
        using var ecdh = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);
        var point = ecdh.ExportParameters(false).Q;
        return new PushSubscribeRequest
        {
            Endpoint = endpoint,
            Keys = new PushSubscriptionKeys
            {
                P256dh = Base64Url.EncodeToString([0x04, .. point.X!, .. point.Y!]),
                Auth = Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(16)),
            },
        };
    }
}

/// <summary>A download the disk has no room for leaves the follower with the plain notice that the episode is out.</summary>
public sealed class FollowRefusalTests(FullDiskFixture fixture) : IClassFixture<FullDiskFixture>
{
    [Fact]
    public async Task A_refused_download_leaves_the_follower_told_the_episode_is_out()
    {
        var imdb = "tt9200001";
        var show = new ShowDatabaseStub.FakeShow { TmdbId = 9201 };
        var today = () => DateOnly.FromDateTime(fixture.Clock.GetUtcNow().UtcDateTime);
        show.Episodes.Add((1, 1, today().AddDays(-30).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)));
        show.Episodes.Add((1, 3, today().AddDays(1).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)));
        fixture.Database.Shows[imdb] = show;
        fixture.Offer(imdb);

        fixture.Services.GetRequiredService<PersonDirectory>().Remember("7081", "Sam");
        var sam = fixture.CreateClient();
        sam.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer",
            fixture.Issuer.Mint("7081", "Sam", lifetime: TimeSpan.FromDays(400)));
        using var put = await sam.PutAsJsonAsync($"/api/me/follows/{imdb}", new FollowRequest { AutoDownload = true });
        Assert.Equal(HttpStatusCode.OK, put.StatusCode);

        fixture.Clock.Advance(TimeSpan.FromDays(2));
        await fixture.Services.GetRequiredService<FollowWatcher>().PassAsync(CancellationToken.None);

        Assert.Empty(fixture.Torrents.Held);
        var told = fixture.Services.GetRequiredService<NotificationStore>().For("7081")
            .Single(n => n.Kind == NotificationKinds.Episode);
        Assert.Contains("S1 · E3 is out", told.Text);
    }
}
