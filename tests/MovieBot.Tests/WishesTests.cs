using System.Net;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using TheKrystalShip.MovieBot.Acquire.Configuration;
using TheKrystalShip.MovieBot.Acquire.Download;
using TheKrystalShip.MovieBot.Acquire.Tracker;
using TheKrystalShip.MovieBot.Acquire.Imdb;
using TheKrystalShip.MovieBot.Acquire.Search;
using TheKrystalShip.MovieBot.Api.Data;
using TheKrystalShip.MovieBot.Api.Acquisition;
using TheKrystalShip.MovieBot.Api.Library;
using TheKrystalShip.MovieBot.Api.Subtitles;
using TheKrystalShip.MovieBot.Api.Wants;
using TheKrystalShip.MovieBot.Core;
using Xunit;

namespace TheKrystalShip.MovieBot.Tests;

/// <summary>
/// What a film somebody wants already is, against a catalogue, a library, a torrent client and a
/// tracker that each answer what the test says. What makes it wrong is watching for the wrong film,
/// promising to watch for one that is already here, or counting people who did not want it in the open.
/// </summary>
public sealed class WishesTests : IDisposable
{
    private const string Link = "https://www.imdb.com/title/tt33612209/?ref_=nv_sr_srsg_0";

    private const string Catalogue = """
        {"d":[
          {"id":"tt33612209","l":"The Devil Wears Prada 2","y":2026,"qid":"movie",
           "s":"Meryl Streep, Anne Hathaway",
           "i":{"imageUrl":"https://m.media-amazon.com/images/M/MV5BZmM3._V1_.jpg","width":2100,"height":3156}},
          {"id":"tt0903747","l":"Breaking Bad","y":2008,"qid":"tvSeries","s":"Bryan Cranston"}
        ],"q":"x","v":1}
        """;

    private readonly string _dir = Path.Combine(
        Path.GetTempPath(), "moviebot-tests", "wishes-" + Guid.NewGuid().ToString("N"));

    private CinemaDatabase Database => field ??= new CinemaDatabase(Path.Combine(_dir, "moviebot.db"));

    /// <summary>Answers every service by the path asked, so each test states what the world holds.</summary>
    private sealed class StubHandler(Func<HttpRequestMessage, string?> answer) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var body = answer(request);
            return Task.FromResult(body is null
                ? new HttpResponseMessage(HttpStatusCode.NotFound)
                : new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json"),
                });
        }
    }

    private sealed class StubSearch(IReadOnlyList<Release> offered) : IReleaseSearch
    {
        public Task<RankedReleases> ByTextAsync(string typed, CancellationToken ct) =>
            Task.FromResult(new RankedReleases(offered, []));

        public Task<RankedReleases> ByTitleAsync(string query, CancellationToken ct) =>
            Task.FromResult(new RankedReleases(offered, []));

        public Task<RankedReleases> ByImdbAsync(string imdbId, CancellationToken ct) =>
            Task.FromResult(new RankedReleases(offered, []));
    }

    private static Release Offered(string name, Source source) => new()
    {
        TorrentId = 1, ReleaseName = name, Title = "The Devil Wears Prada 2", Year = 2026, Source = source,
    };

    private WishList _wishes = null!;
    private WantStore _wants = null!;

    private Wishes Desk(
        FilmIdentity? inLibrary = null,
        string downloads = "[]",
        IReadOnlyList<Release>? offered = null,
        string catalogue = Catalogue)
    {
        static HttpClient Http(string baseAddress, Func<HttpRequestMessage, string?> answer) =>
            new(new StubHandler(answer)) { BaseAddress = new Uri(baseAddress) };

        var imdb = new ImdbClient(Http("https://v3.sg.media-imdb.com/", _ => catalogue), NullLogger<ImdbClient>.Instance);

        var media = Path.Combine(_dir, "media");
        Directory.CreateDirectory(media);
        if (inLibrary is not null) WriteTitle(media, "prada-2", inLibrary);
        var subtitles = Path.Combine(_dir, "subtitles");
        var library = new TitleLibrary(
            new MediaRoots(media),
            new SubtitleStore(subtitles, Database),
            new PinStore(Database),
            NullLogger<TitleLibrary>.Instance);

        var torrents = new QBittorrentClient(
            Http("http://qb.invalid/", r => r.RequestUri!.AbsolutePath.Contains("torrents/info") ? downloads : null),
            Options.Create(new QBittorrentOptions()),
            NullLogger<QBittorrentClient>.Instance);

        var tracker = new TrackerClient(
            Http("https://tracker.invalid/", _ => null),
            Options.Create(new TrackerOptions()),
            NullLogger<TrackerClient>.Instance);

        // No reserve: the test directory sits on whatever small volume the runner has.
        var download = Options.Create(new DownloadOptions
        {
            Root = Path.Combine(_dir, "downloads"), ReservedGiB = 0,
        });
        var acquisition = new AcquisitionService(
            tracker, torrents, new DiskBudget(download, new TranscodeLedger(Options.Create(new TranscodeLedgerOptions()), NullLogger<TranscodeLedger>.Instance), NullLogger<DiskBudget>.Instance),
            download, NullLogger<AcquisitionService>.Instance);

        var search = new StubSearch(offered ?? []);
        var suggestions = new AutocompleteSearch(search, new AutocompleteOptions(), NullLogger<AutocompleteSearch>.Instance);

        _wishes = new WishList(Database);
        _wants = new WantStore(Database);

        return new Wishes(
            imdb, new FilmOfferCache(search, new FakeTimeProvider()), library, acquisition, new OfferedReleases(suggestions), _wishes,
            Options.Create(new NotifyOptions()), new FakeTimeProvider(), NullLogger<Wishes>.Instance);
    }

    private static void WriteTitle(string root, string id, FilmIdentity film) =>
        ManifestJson.WriteAtomic(Path.Combine(root, id, "manifest.json"), new Manifest
        {
            Id = id,
            Title = id,
            Film = film,
            DurationSeconds = 6000,
            Status = TitleStatus.Ready,
            Video = new VideoInfo
            {
                Width = 1920, Height = 800, SourceCodec = "hevc", SourceHdr = "sdr",
                Renditions = [new Rendition { Name = "800p", BitrateKbps = 9000, Width = 1920, Height = 800, Uri = "v0/index.m3u8" }],
            },
            Audio = [],
            Subtitles = [],
        });

    private static readonly Actor Alice = new("10", "Alice");

    private static Task<WishCheck> Check(Wishes desk, string imdbId = "tt33612209", bool keep = true) =>
        desk.CheckAsync(imdbId, keep, Alice, CancellationToken.None);

    [Fact]
    public async Task A_film_nothing_has_gets_a_wish_kept_from_the_catalogue()
    {
        var check = await Check(Desk());

        Assert.Equal(WantOutcomes.Waiting, check.Outcome);

        var kept = Assert.Single(_wishes.All());
        Assert.Equal("tt33612209", kept.ImdbId);
        Assert.Equal("The Devil Wears Prada 2 (2026)", kept.Display);
        Assert.Equal("Meryl Streep, Anne Hathaway", kept.Starring);
        Assert.Empty(kept.Subscribers);
    }

    [Fact]
    public async Task Checking_again_keeps_one_wish()
    {
        var desk = Desk();
        await Check(desk);

        Assert.Equal(WantOutcomes.Waiting, (await Check(desk)).Outcome);
        Assert.Single(_wishes.All());
    }

    [Fact]
    public async Task Somebody_who_may_not_wish_keeps_no_wish()
    {
        var check = await Check(Desk(), keep: false);

        Assert.Equal(WantOutcomes.Wanted, check.Outcome);
        Assert.Empty(_wishes.All());
    }

    [Fact]
    public async Task A_series_is_not_a_film_to_wish_for()
    {
        var check = await Check(Desk(), "tt0903747");

        Assert.Equal(WantOutcomes.Wanted, check.Outcome);
        Assert.Empty(_wishes.All());
    }

    [Fact]
    public async Task An_id_the_catalogue_does_not_know_keeps_no_wish()
    {
        var check = await Check(Desk(), "tt0000001");

        Assert.Equal(WantOutcomes.Wanted, check.Outcome);
        Assert.Empty(_wishes.All());
    }

    [Fact]
    public async Task A_film_already_in_the_library_is_pointed_at_the_player()
    {
        var check = await Check(Desk(inLibrary: new FilmIdentity { ImdbId = "tt33612209", Name = "The Devil Wears Prada 2", Year = 2026 }));

        Assert.Equal(WantOutcomes.InLibrary, check.Outcome);
        Assert.Equal("prada-2", check.LibraryId);
        Assert.Equal("The Devil Wears Prada 2 (2026)", check.LibraryName);
        Assert.Empty(_wishes.All());
    }

    [Fact]
    public async Task A_film_already_downloading_is_not_waited_for()
    {
        const string downloads = """
            [{"hash":"abc","name":"The.Devil.Wears.Prada.2.2026.1080p.WEB-DL-GRP","state":"downloading",
              "progress":0.4,"size":8000000000,"dlspeed":1000000,"eta":3000,"content_path":"/x",
              "seq_dl":true,"num_seeds":4,"tags":"notify:1, imdb:tt33612209, ingest"}]
            """;

        var check = await Check(Desk(downloads: downloads));

        Assert.Equal(WantOutcomes.Downloading, check.Outcome);
        Assert.Equal("The.Devil.Wears.Prada.2.2026.1080p.WEB-DL-GRP", check.DownloadName);
        Assert.Empty(_wishes.All());
    }

    [Fact]
    public async Task A_film_the_tracker_already_has_needs_no_waiting()
    {
        var offered = new[] { Offered("The.Devil.Wears.Prada.2.2026.1080p.WEB-DL-GRP", Source.Web) };

        var check = await Check(Desk(offered: offered));

        Assert.Equal(WantOutcomes.Available, check.Outcome);
        Assert.Equal("The.Devil.Wears.Prada.2.2026.1080p.WEB-DL-GRP", check.Release?.ReleaseName);
        Assert.Empty(_wishes.All());
    }

    /// <summary>A camcorder recording on the tracker is the ordinary state of a film in cinemas.</summary>
    [Fact]
    public async Task A_camcorder_recording_on_the_tracker_still_means_waiting()
    {
        var offered = new[] { Offered("The.Devil.Wears.Prada.2.2026.HDCAM-GRP", Source.Cam) };

        var check = await Check(Desk(offered: offered));

        Assert.Equal(WantOutcomes.Waiting, check.Outcome);
        Assert.Single(_wishes.All());
    }

    private static ImdbTitle Film(string id, string title, int year) => new() { ImdbId = id, Title = title, Year = year };

    private void Want(string user, string imdbId, int minute, bool visible = true, bool wish = true) =>
        _wants.Set(user, imdbId, WantKinds.Film, visible, false, wish, DateTimeOffset.UnixEpoch.AddMinutes(minute));

    private void Keep(string imdbId, string title) => _wishes.Keep(Film(imdbId, title, 2026), DateTimeOffset.UnixEpoch);

    [Fact]
    public void The_anticipated_are_the_most_wanted_first_then_the_newest_want()
    {
        var desk = Desk();
        Keep("tt0000001", "Lone");
        Keep("tt0000002", "Older Pair");
        Keep("tt0000003", "Newer Pair");
        Keep("tt0000004", "Trio");
        Want("10", "tt0000001", 50);
        Want("10", "tt0000002", 1);
        Want("20", "tt0000002", 2);
        Want("30", "tt0000003", 3);
        Want("40", "tt0000003", 4);
        Want("10", "tt0000004", 0);
        Want("20", "tt0000004", 0);
        Want("30", "tt0000004", 0);

        var listed = desk.Anticipated("10");

        Assert.Equal(["tt0000004", "tt0000003", "tt0000002", "tt0000001"], listed.Select(a => a.ImdbId));
        Assert.Equal([3, 2, 2, 1], listed.Select(a => a.Waiters));
        Assert.Equal([true, false, true, true], listed.Select(a => a.Waiting));
        Assert.All(listed, a => Assert.Equal("film", a.Kind));
        Assert.Equal("Trio (2026)", listed[0].Display);
    }

    /// <summary>A just-me want is counted by nobody else, and by its own person.</summary>
    [Fact]
    public void A_want_kept_to_oneself_is_counted_only_for_its_person()
    {
        var desk = Desk();
        Keep("tt0000001", "Quiet");
        Want("10", "tt0000001", 1, visible: false);
        Want("20", "tt0000001", 2);

        var own = Assert.Single(desk.Anticipated("10"));
        Assert.Equal(2, own.Waiters);
        Assert.True(own.Waiting);

        var other = Assert.Single(desk.Anticipated("20"));
        Assert.Equal(1, other.Waiters);

        Assert.Equal(1, Assert.Single(desk.Anticipated("30")).Waiters);
    }

    [Fact]
    public void A_wish_that_is_out_or_already_here_is_not_anticipated()
    {
        var desk = Desk(inLibrary: new FilmIdentity { ImdbId = "tt0000002", Name = "Here", Year = 2026 });
        Keep("tt0000001", "Out");
        Keep("tt0000002", "Here");
        Keep("tt0000003", "Pending");
        Want("10", "tt0000001", 1);
        Want("10", "tt0000002", 2);
        Want("10", "tt0000003", 3);
        _wishes.MarkOut("tt0000001", Offered("Out.2026.1080p.WEB-GRP", Source.Web));

        Assert.Equal("tt0000003", Assert.Single(desk.Anticipated("20")).ImdbId);
    }

    [Fact]
    public void The_anticipated_name_nobody()
    {
        var desk = Desk();
        Keep("tt0000001", "Lone");
        Want("local:usr_secret", "tt0000001", 1);

        var json = System.Text.Json.JsonSerializer.Serialize(desk.Anticipated("local:usr_secret"), ManifestJsonContext.Default.ListAnticipatedView);

        Assert.DoesNotContain("usr_secret", json);
        Assert.DoesNotContain("userId", json, StringComparison.OrdinalIgnoreCase);
    }

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }
}
