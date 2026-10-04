using System.Net;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using TheKrystalShip.MovieBot.Acquire.Configuration;
using TheKrystalShip.MovieBot.Acquire.Download;
using TheKrystalShip.MovieBot.Acquire.Tracker;
using TheKrystalShip.MovieBot.Acquire.Imdb;
using TheKrystalShip.MovieBot.Acquire.Search;
using TheKrystalShip.MovieBot.Api.Acquisition;
using TheKrystalShip.MovieBot.Api.Library;
using TheKrystalShip.MovieBot.Api.Subtitles;
using TheKrystalShip.MovieBot.Core;
using Xunit;

namespace TheKrystalShip.MovieBot.Tests;

/// <summary>
/// Waiting on a film, against a catalogue, a library, a torrent client and a tracker that each answer
/// what the test says. What makes it wrong is watching for the wrong film, promising to watch for one
/// that is already here, or losing the person who asked.
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
            new SubtitleStore(subtitles, NullLogger<SubtitleStore>.Instance),
            new PinStore(subtitles, NullLogger<PinStore>.Instance),
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

        _wishes = new WishList(Path.Combine(_dir, "wishes.json"), NullLogger<WishList>.Instance);

        return new Wishes(
            imdb, search, library, acquisition, new OfferedReleases(suggestions), _wishes,
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

    private static AddWishRequest Ask(string chosen, string user = "10", string? channel = "1") => new()
    {
        Chosen = chosen, RequestedBy = new Actor(user, "Alice"), ChannelId = channel,
    };

    [Fact]
    public async Task A_pasted_link_puts_the_person_on_the_list_for_the_film_it_names()
    {
        var reply = await Desk().SubscribeAsync(Ask(Link), CancellationToken.None);

        Assert.Equal("subscribed", reply.Outcome);
        Assert.Equal("The Devil Wears Prada 2 (2026)", reply.Wish?.Display);
        Assert.Equal("Meryl Streep, Anne Hathaway", reply.Wish?.Starring);

        var kept = Assert.Single(_wishes.All());
        Assert.Equal("tt33612209", kept.ImdbId);
        var person = Assert.Single(kept.Subscribers);
        Assert.Equal(10UL, person.UserId);
        Assert.Equal(1UL, person.ChannelId);
    }

    [Fact]
    public async Task A_picked_suggestion_arrives_as_the_id_and_works_the_same_way()
    {
        var reply = await Desk().SubscribeAsync(Ask("tt33612209"), CancellationToken.None);

        Assert.Equal("subscribed", reply.Outcome);
    }

    /// <summary>Somebody asking from a page has no Discord channel, and is waiting all the same.</summary>
    [Fact]
    public async Task A_person_with_no_channel_is_kept_without_one()
    {
        await Desk().SubscribeAsync(Ask(Link, channel: null), CancellationToken.None);

        Assert.Null(Assert.Single(Assert.Single(_wishes.All()).Subscribers).ChannelId);
    }

    [Fact]
    public async Task Asking_twice_says_so_and_adds_nobody()
    {
        var desk = Desk();
        await desk.SubscribeAsync(Ask(Link), CancellationToken.None);

        var again = await desk.SubscribeAsync(Ask(Link), CancellationToken.None);

        Assert.Equal("alreadySubscribed", again.Outcome);
        Assert.Single(Assert.Single(_wishes.All()).Subscribers);
    }

    [Fact]
    public async Task Text_that_is_neither_a_suggestion_nor_a_link_is_refused()
    {
        var reply = await Desk().SubscribeAsync(Ask("the devil wears prada"), CancellationToken.None);

        Assert.Equal("nothingPicked", reply.Outcome);
        Assert.Empty(_wishes.All());
    }

    [Fact]
    public async Task A_series_is_not_a_film()
    {
        var reply = await Desk().SubscribeAsync(
            Ask("https://www.imdb.com/title/tt0903747/"), CancellationToken.None);

        Assert.Equal("notAFilm", reply.Outcome);
        Assert.Equal("Breaking Bad", reply.FilmTitle);
        Assert.Empty(_wishes.All());
    }

    [Fact]
    public async Task An_id_the_catalogue_does_not_know_is_refused()
    {
        var reply = await Desk().SubscribeAsync(Ask("tt0000001"), CancellationToken.None);

        Assert.Equal("notFound", reply.Outcome);
    }

    [Fact]
    public async Task A_film_already_in_the_library_is_pointed_at_the_player()
    {
        var reply = await Desk(inLibrary: new FilmIdentity { ImdbId = "tt33612209", Name = "The Devil Wears Prada 2", Year = 2026 })
            .SubscribeAsync(Ask(Link), CancellationToken.None);

        Assert.Equal("inLibrary", reply.Outcome);
        Assert.Equal("prada-2", reply.LibraryId);
        Assert.Equal("The Devil Wears Prada 2 (2026)", reply.LibraryName);
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

        var reply = await Desk(downloads: downloads).SubscribeAsync(Ask(Link), CancellationToken.None);

        Assert.Equal("downloading", reply.Outcome);
        Assert.Equal("The.Devil.Wears.Prada.2.2026.1080p.WEB-DL-GRP", reply.DownloadName);
        Assert.Empty(_wishes.All());
    }

    [Fact]
    public async Task A_film_the_tracker_already_has_needs_no_waiting()
    {
        var offered = new[] { Offered("The.Devil.Wears.Prada.2.2026.1080p.WEB-DL-GRP", Source.Web) };

        var reply = await Desk(offered: offered).SubscribeAsync(Ask(Link), CancellationToken.None);

        Assert.Equal("alreadyAvailable", reply.Outcome);
        Assert.Equal("The.Devil.Wears.Prada.2.2026.1080p.WEB-DL-GRP", reply.Release?.ReleaseName);
        Assert.Empty(_wishes.All());
    }

    /// <summary>A camcorder recording on the tracker is the ordinary state of a film in cinemas.</summary>
    [Fact]
    public async Task A_camcorder_recording_on_the_tracker_still_means_waiting()
    {
        var offered = new[] { Offered("The.Devil.Wears.Prada.2.2026.HDCAM-GRP", Source.Cam) };

        var reply = await Desk(offered: offered).SubscribeAsync(Ask(Link), CancellationToken.None);

        Assert.Equal("subscribed", reply.Outcome);
        Assert.Single(_wishes.All());
    }

    [Fact]
    public async Task Cancelling_takes_the_person_off_the_list()
    {
        var desk = Desk();
        await desk.SubscribeAsync(Ask(Link, user: "10"), CancellationToken.None);
        await desk.SubscribeAsync(Ask(Link, user: "20"), CancellationToken.None);

        var cancelled = desk.Cancel("10", "tt33612209");
        var again = desk.Cancel("10", "tt33612209");

        Assert.Equal("cancelled", cancelled.Outcome);
        Assert.Equal("notWaiting", again.Outcome);
        Assert.Empty(desk.List("10"));
        Assert.Single(desk.List("20"));
    }

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }
}
