using System.Net;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TheKrystalShip.MovieBot.Acquire.Download;
using TheKrystalShip.MovieBot.Core;
using TheKrystalShip.MovieBot.Handoff;
using Xunit;

namespace TheKrystalShip.MovieBot.Tests;

/// <summary>
/// The two clocks as the hand-off acts on them. The tracker's clock removes a torrent and its files
/// and nothing else; the library's clock deletes a settled film's directory from the cold root, only
/// what the API names, and nothing at all when the API cannot answer.
/// </summary>
public sealed class LibraryRetentionTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "moviebot-retention-tests", Guid.NewGuid().ToString("n"));

    private string Hot => Path.Combine(_root, "media");
    private string Cold => Path.Combine(_root, "cold");

    public LibraryRetentionTests()
    {
        Directory.CreateDirectory(Hot);
        Directory.CreateDirectory(Cold);
        File.WriteAllText(Path.Combine(Cold, MediaRoots.ColdMarker), "");
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    private static string Title(string root, string id)
    {
        var directory = Path.Combine(root, id);
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "manifest.json"), "{}");
        return directory;
    }

    /// <summary>An API that answers each path with a status and a body, or with nothing at all.</summary>
    private sealed class Api : HttpMessageHandler
    {
        public string Due { get; set; } = "[]";
        public string Rooms { get; set; } = "[]";
        public string Saving { get; set; } = "[]";
        public HttpStatusCode DueStatus { get; set; } = HttpStatusCode.OK;
        public bool RoomsDown { get; set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path.EndsWith("api/sessions", StringComparison.Ordinal))
            {
                if (RoomsDown) throw new HttpRequestException("down");
                return Task.FromResult(Json(HttpStatusCode.OK, Rooms));
            }

            if (path.EndsWith("api/library/due", StringComparison.Ordinal))
                return Task.FromResult(Json(DueStatus, Due));

            if (path.EndsWith("api/library/saving", StringComparison.Ordinal))
                return Task.FromResult(Json(HttpStatusCode.OK, Saving));

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        }

        private static HttpResponseMessage Json(HttpStatusCode status, string body) =>
            new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
    }

    private static HttpClient Client(Api api) => new(api) { BaseAddress = new Uri("http://api.test/") };

    private LibraryWorker Library(Api api, MediaRoots? roots = null) => new(
        new DueTitles(Client(api), NullLogger<DueTitles>.Instance),
        new OccupiedRooms(Client(api), NullLogger<OccupiedRooms>.Instance),
        roots ?? new MediaRoots(Hot, Cold),
        NullLogger<LibraryWorker>.Instance);

    // ── the library's clock ─────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task A_due_film_is_deleted_from_the_cold_root_and_a_film_not_named_stays()
    {
        var due = Title(Cold, "heat-1995");
        var other = Title(Cold, "collateral-2004");

        await Library(new Api { Due = """["heat-1995"]""" }).SweepAsync(CancellationToken.None);

        Assert.False(Directory.Exists(due));
        Assert.True(Directory.Exists(other));
    }

    [Fact]
    public async Task A_due_film_a_save_holds_stays_and_a_saving_list_that_cannot_be_read_deletes_nothing()
    {
        var film = Title(Cold, "heat-1995");

        await Library(new Api { Due = """["heat-1995"]""", Saving = """["heat-1995"]""" }).SweepAsync(CancellationToken.None);
        Assert.True(Directory.Exists(film));

        await Library(new Api { Due = """["heat-1995"]""", Saving = "not json" }).SweepAsync(CancellationToken.None);
        Assert.True(Directory.Exists(film));
    }

    [Fact]
    public async Task Nothing_is_deleted_when_the_api_cannot_say_what_is_due()
    {
        var film = Title(Cold, "heat-1995");

        await Library(new Api { DueStatus = HttpStatusCode.BadGateway, Due = "" }).SweepAsync(CancellationToken.None);
        await Library(new Api { Due = "not json" }).SweepAsync(CancellationToken.None);

        Assert.True(Directory.Exists(film));
    }

    [Fact]
    public async Task Nothing_is_deleted_when_the_rooms_cannot_be_asked()
    {
        var film = Title(Cold, "heat-1995");

        await Library(new Api { Due = """["heat-1995"]""", RoomsDown = true }).SweepAsync(CancellationToken.None);

        Assert.True(Directory.Exists(film));
    }

    [Fact]
    public async Task A_film_a_room_holds_is_not_deleted_even_when_the_api_named_it()
    {
        var film = Title(Cold, "heat-1995");
        var rooms = """[{"sessionId":"r","titleId":"heat-1995","paused":false,"positionSeconds":0,"participants":0}]""";

        await Library(new Api { Due = """["heat-1995"]""", Rooms = rooms }).SweepAsync(CancellationToken.None);

        Assert.True(Directory.Exists(film));
    }

    [Fact]
    public async Task Only_the_cold_root_is_deleted_from_and_a_film_also_on_the_media_root_stays()
    {
        var hotOnly = Title(Hot, "making-1999");
        var both = Title(Cold, "heat-1995");
        var hotCopy = Title(Hot, "heat-1995");

        await Library(new Api { Due = """["making-1999","heat-1995"]""" }).SweepAsync(CancellationToken.None);

        Assert.True(Directory.Exists(hotOnly));
        Assert.True(Directory.Exists(both));
        Assert.True(Directory.Exists(hotCopy));
    }

    [Theory]
    [InlineData("../media/heat-1995")]
    [InlineData("..")]
    [InlineData(".")]
    [InlineData(".incoming")]
    [InlineData("a/b")]
    [InlineData("")]
    public async Task An_id_that_could_reach_outside_the_cold_root_is_not_followed(string id)
    {
        var outside = Title(Hot, "heat-1995");
        Directory.CreateDirectory(Path.Combine(Cold, ".incoming"));
        var due = $"[{System.Text.Json.JsonSerializer.Serialize(id)}]";

        await Library(new Api { Due = due }).SweepAsync(CancellationToken.None);

        Assert.True(Directory.Exists(outside));
        Assert.True(Directory.Exists(Path.Combine(Cold, ".incoming")));
        Assert.True(File.Exists(Path.Combine(Cold, MediaRoots.ColdMarker)));
    }

    [Fact]
    public async Task A_link_is_not_followed_into_wherever_it_points()
    {
        var elsewhere = Title(_root, "precious");
        Directory.CreateSymbolicLink(Path.Combine(Cold, "linked"), elsewhere);

        await Library(new Api { Due = """["linked"]""" }).SweepAsync(CancellationToken.None);

        Assert.True(Directory.Exists(elsewhere));
        Assert.True(File.Exists(Path.Combine(elsewhere, "manifest.json")));
    }

    [Fact]
    public async Task Nothing_is_deleted_while_the_cold_volume_is_out()
    {
        var film = Title(Cold, "heat-1995");
        File.Delete(Path.Combine(Cold, MediaRoots.ColdMarker));

        await Library(new Api { Due = """["heat-1995"]""" }).SweepAsync(CancellationToken.None);

        Assert.True(Directory.Exists(film));
    }

    [Fact]
    public async Task With_no_cold_root_there_is_nothing_to_delete()
    {
        var film = Title(Hot, "heat-1995");

        await Library(new Api { Due = """["heat-1995"]""" }, new MediaRoots(Hot)).SweepAsync(CancellationToken.None);

        Assert.True(Directory.Exists(film));
    }

    // ── the tracker's clock ─────────────────────────────────────────────────────────────────────

    private sealed class Seeding(params DownloadStatus[] downloads) : ISeedingDownloads
    {
        public List<string> Removed { get; } = [];

        public Task<IReadOnlyList<DownloadStatus>> ListAsync(CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<DownloadStatus>>(downloads);

        public Task RemoveAsync(string hash, CancellationToken ct)
        {
            Removed.Add(hash);
            return Task.CompletedTask;
        }
    }

    private static DownloadStatus Torrent(string hash, TimeSpan seeded, string name = "Heat.1995.1080p.BluRay.x264-GRP",
        DownloadState state = DownloadState.Complete, params string[] tags) => new()
    {
        Hash = hash,
        Name = name,
        State = state,
        Seeded = seeded,
        Tags = tags,
    };

    private RetentionWorker Tracker(Seeding downloads, Api api, MediaRoots? roots = null) => new(
        downloads,
        new Retention(Options.Create(new RetentionOptions { TrackerMinimumHours = 48, MarginHours = 12 })),
        new OccupiedRooms(Client(api), NullLogger<OccupiedRooms>.Instance),
        roots ?? new MediaRoots(Hot, Cold),
        NullLogger<RetentionWorker>.Instance);

    [Fact]
    public async Task A_source_past_its_floor_loses_its_torrent_and_the_film_stays_in_the_library()
    {
        var settled = Title(Cold, "heat-1995");
        var making = Title(Hot, "collateral-2004");
        var downloads = new Seeding(
            Torrent("old", TimeSpan.FromDays(8), tags: "library:heat-1995"),
            Torrent("fresh", TimeSpan.FromDays(2), name: "Collateral.2004.1080p.BluRay.x264-GRP", tags: "library:collateral-2004"));

        await Tracker(downloads, new Api()).SweepAsync(CancellationToken.None);

        Assert.Equal(["old"], downloads.Removed);
        Assert.True(Directory.Exists(settled));
        Assert.True(File.Exists(Path.Combine(settled, "manifest.json")));
        Assert.True(Directory.Exists(making));
    }

    [Fact]
    public async Task A_title_directory_on_either_root_survives_the_tracker_s_clock()
    {
        var hot = Title(Hot, "heat-1995");
        var downloads = new Seeding(Torrent("old", TimeSpan.FromDays(30), tags: "library:heat-1995"));

        await Tracker(downloads, new Api()).SweepAsync(CancellationToken.None);

        Assert.Equal(["old"], downloads.Removed);
        Assert.True(Directory.Exists(hot));
    }

    [Fact]
    public async Task A_kept_film_s_source_goes_like_any_other_because_a_keep_is_the_library_s()
    {
        var downloads = new Seeding(Torrent("old", TimeSpan.FromDays(8), tags: ["library:heat-1995", "keep", "keeper:1"]));

        await Tracker(downloads, new Api()).SweepAsync(CancellationToken.None);

        Assert.Equal(["old"], downloads.Removed);
    }

    [Fact]
    public async Task The_seeding_floor_and_a_transcode_still_owed_hold_a_source_back()
    {
        var downloads = new Seeding(
            Torrent("under-floor", TimeSpan.FromHours(30)),
            Torrent("owes-ingest", TimeSpan.FromDays(30), tags: "ingest"),
            Torrent("downloading", TimeSpan.FromDays(30), state: DownloadState.Downloading));

        await Tracker(downloads, new Api()).SweepAsync(CancellationToken.None);

        Assert.Empty(downloads.Removed);
    }

    [Fact]
    public async Task A_source_a_room_holds_stays_and_nothing_is_removed_when_the_rooms_cannot_be_asked()
    {
        var held = new Seeding(Torrent("old", TimeSpan.FromDays(8), tags: "library:heat-1995"));
        var rooms = """[{"sessionId":"r","titleId":"heat-1995","paused":false,"positionSeconds":0,"participants":0}]""";
        await Tracker(held, new Api { Rooms = rooms }).SweepAsync(CancellationToken.None);
        Assert.Empty(held.Removed);

        var unknown = new Seeding(Torrent("old", TimeSpan.FromDays(8), tags: "library:heat-1995"));
        await Tracker(unknown, new Api { RoomsDown = true }).SweepAsync(CancellationToken.None);
        Assert.Empty(unknown.Removed);
    }

    [Fact]
    public async Task Nothing_is_removed_while_the_cold_volume_is_out()
    {
        File.Delete(Path.Combine(Cold, MediaRoots.ColdMarker));
        var downloads = new Seeding(Torrent("old", TimeSpan.FromDays(8)));

        await Tracker(downloads, new Api()).SweepAsync(CancellationToken.None);

        Assert.Empty(downloads.Removed);
    }
}
