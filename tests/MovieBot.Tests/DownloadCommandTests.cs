using System.Net;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TheKrystalShip.MovieBot.Acquire.Configuration;
using TheKrystalShip.MovieBot.Acquire.Download;
using TheKrystalShip.MovieBot.Acquire.Tracker;
using TheKrystalShip.MovieBot.Acquire.Search;
using TheKrystalShip.MovieBot.Bot.Download;
using TheKrystalShip.MovieBot.Bot.Watch;
using Xunit;

namespace TheKrystalShip.MovieBot.Tests;

/// <summary>
/// The command, against a tracker and a torrent client that each answer what the test says.
///
/// What makes it wrong is the notes left on the torrent: they are the only thing that tells a
/// later pass, in whichever process is running by then, where to announce the film and which room
/// to start it in. A note missing here is a film that arrives and starts nowhere.
/// </summary>
public sealed class DownloadCommandTests : IDisposable
{
    /// <summary>The smallest thing the tracker could hand over that is still a torrent.</summary>
    private const string TorrentFile =
        "d4:infod6:lengthi1e4:name1:x12:piece lengthi1e6:pieces20:AAAAAAAAAAAAAAAAAAAAee";

    private readonly string _dir = Path.Combine(
        Path.GetTempPath(), "moviebot-tests", "download-" + Guid.NewGuid().ToString("N"));

    /// <summary>What the torrent client was asked to add, as the form it was sent.</summary>
    private readonly List<string> _added = [];

    private sealed class StubHandler(Func<HttpRequestMessage, Task<string?>> answer) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var body = await answer(request);
            return body is null
                ? new HttpResponseMessage(HttpStatusCode.NotFound)
                : new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body) };
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

    private static readonly Release Offered = new()
    {
        TorrentId = 41, ReleaseName = "Heat.1995.1080p.BluRay-GRP", Title = "Heat", Year = 1995,
        ImdbId = "tt0113277", SizeBytes = 1 << 20,
    };

    private async Task<DownloadCommand> CommandAsync()
    {
        static HttpClient Http(string baseAddress, Func<HttpRequestMessage, Task<string?>> answer) =>
            new(new StubHandler(answer)) { BaseAddress = new Uri(baseAddress) };

        var torrents = new QBittorrentClient(
            Http("http://qb.invalid/", async r =>
            {
                var path = r.RequestUri!.AbsolutePath;
                if (path.EndsWith("app/version")) return "v5.0.0";
                if (path.EndsWith("torrents/add"))
                {
                    _added.Add(await r.Content!.ReadAsStringAsync());
                    return "Ok.";
                }
                if (path.EndsWith("torrents/addTags")) return "";
                return null;
            }),
            Options.Create(new QBittorrentOptions()),
            NullLogger<QBittorrentClient>.Instance);

        var tracker = new TrackerClient(
            Http("https://tracker.invalid/", r =>
                Task.FromResult(r.RequestUri!.AbsolutePath.EndsWith("download.php") ? TorrentFile : null)),
            Options.Create(new TrackerOptions
            {
                BaseUrl = "https://tracker.invalid/", Username = "u", Passkey = "p",
            }),
            NullLogger<TrackerClient>.Instance);

        var root = Path.Combine(_dir, "downloads");
        Directory.CreateDirectory(root);
        var download = Options.Create(new DownloadOptions { Root = root });
        var acquisition = new AcquisitionService(
            tracker, torrents, new DiskBudget(download, NullLogger<DiskBudget>.Instance),
            download, NullLogger<AcquisitionService>.Instance);

        // A row has to have been offered before it can be picked, which is how the surface works
        // too: the value behind a row is only meaningful to the process that showed it.
        var suggestions = new AutocompleteSearch(
            new StubSearch([Offered]),
            new AutocompleteOptions { DebounceMs = 0 },
            NullLogger<AutocompleteSearch>.Instance);
        await suggestions.SuggestAsync("heat", "tester", CancellationToken.None);

        return new DownloadCommand(suggestions, acquisition, NullLogger<DownloadCommand>.Instance);
    }

    private static DownloadRequest Pick(long torrentId, ulong? room) => new()
    {
        TorrentId = torrentId, ChannelId = 1, RequesterId = 10, RequestedBy = "Alice", RoomId = room,
    };

    [Fact]
    public async Task A_pick_from_a_voice_channel_carries_the_room_to_start_in()
    {
        var result = await (await CommandAsync()).ExecuteAsync(Pick(41, room: 918273645), CancellationToken.None);

        Assert.Equal(DownloadOutcome.Started, result.Outcome);
        Assert.NotNull(result.Hash);
        Assert.Contains("voice channel", result.Message);

        var form = Assert.Single(_added);
        Assert.Contains(TorrentTags.Room(918273645), form);
        Assert.Contains(TorrentTags.Notify(1), form);
        Assert.Contains(TorrentTags.Requester(10), form);
        Assert.Contains(TorrentTags.NeedsIngest, form);
        Assert.Contains(TorrentTags.Imdb("tt0113277"), form);
    }

    [Fact]
    public async Task A_pick_from_outside_a_voice_channel_is_a_download_and_nothing_more()
    {
        var result = await (await CommandAsync()).ExecuteAsync(Pick(41, room: null), CancellationToken.None);

        Assert.Equal(DownloadOutcome.Started, result.Outcome);
        Assert.Contains("announced here", result.Message);

        var form = Assert.Single(_added);
        Assert.DoesNotContain(TorrentTags.RoomPrefix, form);
        Assert.Contains(TorrentTags.Notify(1), form);
    }

    [Fact]
    public async Task A_row_this_process_never_offered_is_refused_rather_than_guessed()
    {
        var result = await (await CommandAsync()).ExecuteAsync(Pick(99, room: 918273645), CancellationToken.None);

        Assert.Equal(DownloadOutcome.NoLongerOffered, result.Outcome);
        Assert.Empty(_added);
    }

    [Fact]
    public void A_tracker_row_and_a_library_id_are_told_apart_by_the_value_alone()
    {
        Assert.Equal(41, TrackerPick.Parse(TrackerPick.Value(41)));

        // A library id can be nothing but digits, so a bare number is a film that is here.
        Assert.Null(TrackerPick.Parse("41"));
        Assert.Null(TrackerPick.Parse("1917-2019"));
        Assert.Null(TrackerPick.Parse("torrent:"));
        Assert.Null(TrackerPick.Parse("torrent:-1"));
        Assert.Null(TrackerPick.Parse(""));
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }
}
