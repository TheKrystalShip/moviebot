using System.Net;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using TheKrystalShip.MovieBot.Acquire.Configuration;
using TheKrystalShip.MovieBot.Acquire.Download;
using TheKrystalShip.MovieBot.Acquire.Tracker;
using TheKrystalShip.MovieBot.Acquire.Imdb;
using TheKrystalShip.MovieBot.Acquire.Search;
using TheKrystalShip.MovieBot.Bot.Api;
using TheKrystalShip.MovieBot.Bot.Notify;
using Xunit;

namespace TheKrystalShip.MovieBot.Tests;

/// <summary>
/// The command, against a catalogue, a library, a torrent client and a tracker that each answer
/// what the test says. What makes it wrong is watching for the wrong film, promising to watch
/// for one that is already here, or losing the person who asked.
/// </summary>
public sealed class NotifyCommandTests : IDisposable
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
        Path.GetTempPath(), "moviebot-tests", "notify-" + Guid.NewGuid().ToString("N"));

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

    private NotifyCommand Command(
        string library = "[]",
        string downloads = "[]",
        IReadOnlyList<Release>? offered = null,
        string catalogue = Catalogue)
    {
        static HttpClient Http(string baseAddress, Func<HttpRequestMessage, string?> answer) =>
            new(new StubHandler(answer)) { BaseAddress = new Uri(baseAddress) };

        var imdb = new ImdbClient(Http("https://v3.sg.media-imdb.com/", _ => catalogue), NullLogger<ImdbClient>.Instance);

        var api = new MovieBotApiClient(Http("http://api.invalid/", r =>
            r.RequestUri!.AbsolutePath.EndsWith("/api/titles") ? library : null));

        var torrents = new QBittorrentClient(
            Http("http://qb.invalid/", r => r.RequestUri!.AbsolutePath.Contains("torrents/info") ? downloads : null),
            Options.Create(new QBittorrentOptions()),
            NullLogger<QBittorrentClient>.Instance);

        var tracker = new TrackerClient(
            Http("https://tracker.invalid/", _ => null),
            Options.Create(new TrackerOptions()),
            NullLogger<TrackerClient>.Instance);

        var download = Options.Create(new DownloadOptions { Root = Path.Combine(_dir, "downloads") });
        var acquisition = new AcquisitionService(
            tracker, torrents, new DiskBudget(download, NullLogger<DiskBudget>.Instance),
            download, NullLogger<AcquisitionService>.Instance);

        _wishes = new WishList(Path.Combine(_dir, "wishes.json"), NullLogger<WishList>.Instance);

        return new NotifyCommand(
            imdb, new StubSearch(offered ?? []), api, acquisition, _wishes,
            Options.Create(new NotifyOptions()), new FakeTimeProvider(), NullLogger<NotifyCommand>.Instance);
    }

    private static NotifyRequest Ask(string chosen, ulong user = 10, ulong channel = 1) => new()
    {
        Chosen = chosen, ChannelId = channel, UserId = user, RequestedBy = "Alice",
    };

    [Fact]
    public async Task A_pasted_link_puts_the_person_on_the_list_for_the_film_it_names()
    {
        var result = await Command().SubscribeAsync(Ask(Link), CancellationToken.None);

        Assert.Equal(NotifyStatus.Subscribed, result.Status);
        Assert.Equal("The Devil Wears Prada 2 (2026)", result.Wish?.Display);
        Assert.Equal("Meryl Streep, Anne Hathaway", result.Wish?.Starring);

        var kept = Assert.Single(_wishes.All());
        Assert.Equal("tt33612209", kept.ImdbId);
        var person = Assert.Single(kept.Subscribers);
        Assert.Equal(10UL, person.UserId);
        Assert.Equal(1UL, person.ChannelId);
    }

    [Fact]
    public async Task A_picked_suggestion_arrives_as_the_id_and_works_the_same_way()
    {
        var result = await Command().SubscribeAsync(Ask("tt33612209"), CancellationToken.None);

        Assert.Equal(NotifyStatus.Subscribed, result.Status);
    }

    [Fact]
    public async Task Asking_twice_says_so_and_adds_nobody()
    {
        var command = Command();
        await command.SubscribeAsync(Ask(Link), CancellationToken.None);

        var again = await command.SubscribeAsync(Ask(Link), CancellationToken.None);

        Assert.Equal(NotifyStatus.AlreadySubscribed, again.Status);
        Assert.Single(Assert.Single(_wishes.All()).Subscribers);
    }

    [Fact]
    public async Task Text_that_is_neither_a_suggestion_nor_a_link_is_refused()
    {
        var result = await Command().SubscribeAsync(Ask("the devil wears prada"), CancellationToken.None);

        Assert.Equal(NotifyStatus.NothingPicked, result.Status);
        Assert.Empty(_wishes.All());
    }

    [Fact]
    public async Task A_series_is_not_a_film()
    {
        var result = await Command().SubscribeAsync(
            Ask("https://www.imdb.com/title/tt0903747/"), CancellationToken.None);

        Assert.Equal(NotifyStatus.NotAFilm, result.Status);
        Assert.Contains("Breaking Bad", result.Message);
        Assert.Empty(_wishes.All());
    }

    [Fact]
    public async Task An_id_the_catalogue_does_not_know_is_refused()
    {
        var result = await Command().SubscribeAsync(Ask("tt0000001"), CancellationToken.None);

        Assert.Equal(NotifyStatus.NotFound, result.Status);
    }

    [Fact]
    public async Task A_film_already_in_the_library_is_pointed_at_the_player()
    {
        const string library = """
            [{"id":"prada-2","title":"The.Devil.Wears.Prada.2","durationSeconds":6000,"status":"ready",
              "film":{"imdbId":"tt33612209","name":"The Devil Wears Prada 2","year":2026}}]
            """;

        var result = await Command(library: library).SubscribeAsync(Ask(Link), CancellationToken.None);

        Assert.Equal(NotifyStatus.InLibrary, result.Status);
        Assert.Equal("prada-2", result.Title?.Id);
        Assert.Contains("/watch", result.Message);
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

        var result = await Command(downloads: downloads).SubscribeAsync(Ask(Link), CancellationToken.None);

        Assert.Equal(NotifyStatus.Downloading, result.Status);
        Assert.Empty(_wishes.All());
    }

    [Fact]
    public async Task A_film_the_tracker_already_has_needs_no_waiting()
    {
        var offered = new[] { Offered("The.Devil.Wears.Prada.2.2026.1080p.WEB-DL-GRP", Source.Web) };

        var result = await Command(offered: offered).SubscribeAsync(Ask(Link), CancellationToken.None);

        Assert.Equal(NotifyStatus.AlreadyAvailable, result.Status);
        Assert.Equal("The.Devil.Wears.Prada.2.2026.1080p.WEB-DL-GRP", result.Release?.ReleaseName);
        Assert.Contains("/watch", result.Message);
        Assert.Empty(_wishes.All());
    }

    /// <summary>A camcorder recording on the tracker is the ordinary state of a film in cinemas.</summary>
    [Fact]
    public async Task A_camcorder_recording_on_the_tracker_still_means_waiting()
    {
        var offered = new[] { Offered("The.Devil.Wears.Prada.2.2026.HDCAM-GRP", Source.Cam) };

        var result = await Command(offered: offered).SubscribeAsync(Ask(Link), CancellationToken.None);

        Assert.Equal(NotifyStatus.Subscribed, result.Status);
        Assert.Single(_wishes.All());
    }

    [Fact]
    public async Task Cancelling_takes_the_person_off_the_list()
    {
        var command = Command();
        await command.SubscribeAsync(Ask(Link, user: 10), CancellationToken.None);
        await command.SubscribeAsync(Ask(Link, user: 20), CancellationToken.None);

        var cancelled = command.Cancel(10, "tt33612209");
        var again = command.Cancel(10, "tt33612209");

        Assert.Equal(CancelStatus.Cancelled, cancelled.Status);
        Assert.Equal(CancelStatus.NotWaiting, again.Status);
        Assert.Empty(command.List(10));
        Assert.Single(command.List(20));
    }

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }
}
