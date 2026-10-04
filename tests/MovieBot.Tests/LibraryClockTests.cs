using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using TheKrystalShip.MovieBot.Api.Acquisition;
using TheKrystalShip.MovieBot.Api.Library;
using TheKrystalShip.MovieBot.Api.People;
using TheKrystalShip.MovieBot.Api.Sessions;
using TheKrystalShip.MovieBot.Api.Subtitles;
using TheKrystalShip.MovieBot.Core;
using Xunit;

namespace TheKrystalShip.MovieBot.Tests;

/// <summary>
/// The library's clock: when a settled film is due, and what stops it. The one place the rule is
/// written, so these are the tests of what the hand-off will delete.
/// </summary>
public sealed class LibraryClockTests : IDisposable
{
    private static readonly Actor Somebody = new("1", "Somebody");

    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "moviebot-clock-tests", Guid.NewGuid().ToString("n"));

    private readonly FakeTimeProvider _time = new();
    private readonly Plays _plays = new();
    private readonly MediaRoots _roots;
    private readonly SessionStore _sessions;
    private readonly KeepStore _keeps;

    private string Hot => Path.Combine(_root, "media");
    private string Cold => Path.Combine(_root, "cold");

    public LibraryClockTests()
    {
        Directory.CreateDirectory(Hot);
        Directory.CreateDirectory(Cold);
        File.WriteAllText(Path.Combine(Cold, MediaRoots.ColdMarker), "");

        _roots = new MediaRoots(Hot, Cold, _time);
        _keeps = new KeepStore(Path.Combine(_root, "keeps.json"), NullLogger<KeepStore>.Instance);
        _library = LibraryOn(_roots);
        _sessions = new SessionStore(_library, _time);
    }

    private readonly TitleLibrary _library;

    private TitleLibrary LibraryOn(MediaRoots roots) => new(
        roots,
        new SubtitleStore(Path.Combine(_root, "subtitles"), NullLogger<SubtitleStore>.Instance),
        new PinStore(Path.Combine(_root, "subtitles"), NullLogger<PinStore>.Instance),
        NullLogger<TitleLibrary>.Instance);

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    private sealed class Plays : IPlayHistory
    {
        public Dictionary<string, DateTimeOffset> At { get; } = [];

        public DateTimeOffset? LastPlayedAt(string titleId) =>
            At.TryGetValue(titleId, out var at) ? at : null;

        public DateTimeOffset RecordingSince { get; set; } = DateTimeOffset.MinValue;
    }

    private LibraryClock Clock(KeeperNames? names = null, double days = 7) => new(
        _roots, _library, _keeps, _plays, _sessions, names ?? (_ => null),
        Options.Create(new LibraryOptions { LeaveAfterDays = days }), _time);

    private string Settle(string id)
    {
        var directory = Path.Combine(Cold, id);
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "manifest.json"), ManifestOf(id, "ready"));
        return directory;
    }

    /// <summary>A manifest the library can read, in the given status.</summary>
    private static string ManifestOf(string id, string status) => $$"""
        {
          "id": "{{id}}", "title": "{{id}}", "durationSeconds": 600.0,
          "status": "{{status}}", "master": "master.m3u8",
          "video": { "width": 1280, "height": 720, "sourceCodec": "h264", "sourceHdr": "sdr", "renditions": [] },
          "audio": [], "subtitles": []
        }
        """;

    /// <summary>The film's creation time on the cold disk, which is when it settled, and the clock set a span after it.</summary>
    private DateTimeOffset SettledAt(string id, TimeSpan after)
    {
        var settled = new DateTimeOffset(Directory.GetCreationTimeUtc(Path.Combine(Cold, id)), TimeSpan.Zero);
        _time.SetUtcNow(settled + after);
        return settled;
    }

    [Fact]
    public void A_film_nobody_has_played_counts_from_when_it_settled()
    {
        Settle("heat-1995");
        var clock = Clock();

        var settled = SettledAt("heat-1995", TimeSpan.FromDays(3));
        var stay = clock.StayOf("heat-1995")!;

        Assert.True(stay.Settled);
        Assert.Null(stay.LastPlayedAt);
        Assert.Equal(settled.AddDays(7), stay.LeavesAt);
        Assert.Equal(4, stay.DaysLeft);
        Assert.Empty(clock.Due());

        SettledAt("heat-1995", TimeSpan.FromDays(7));
        Assert.Equal(["heat-1995"], clock.Due());
        Assert.Equal(0, clock.StayOf("heat-1995")!.DaysLeft);
    }

    /// <summary>
    /// A library that has only just started recording plays knows nothing about the weeks before, so
    /// a film that settled long ago is not due the moment the record begins.
    /// </summary>
    [Fact]
    public void Nothing_counts_from_before_plays_began_to_be_recorded()
    {
        Settle("heat-1995");
        var clock = Clock();
        var settled = SettledAt("heat-1995", TimeSpan.FromDays(30));
        _plays.RecordingSince = settled.AddDays(29);

        var stay = clock.StayOf("heat-1995")!;

        Assert.Equal(settled.AddDays(36), stay.LeavesAt);
        Assert.Equal(6, stay.DaysLeft);
        Assert.Empty(clock.Due());
    }

    [Fact]
    public void A_play_moves_the_film_s_time_to_the_play()
    {
        Settle("heat-1995");
        var clock = Clock();
        var settled = SettledAt("heat-1995", TimeSpan.FromDays(8));
        _plays.At["heat-1995"] = settled.AddDays(5);

        Assert.Empty(clock.Due());
        Assert.Equal(settled.AddDays(12), clock.StayOf("heat-1995")!.LeavesAt);
        Assert.Equal(settled.AddDays(5), clock.StayOf("heat-1995")!.LastPlayedAt);

        SettledAt("heat-1995", TimeSpan.FromDays(12));
        Assert.Equal(["heat-1995"], clock.Due());
    }

    [Fact]
    public void A_play_from_before_the_film_settled_does_not_make_it_due_the_moment_it_arrives()
    {
        Settle("heat-1995");
        var clock = Clock();
        var settled = SettledAt("heat-1995", TimeSpan.FromHours(1));
        _plays.At["heat-1995"] = settled.AddDays(-30);

        Assert.Empty(clock.Due());
        Assert.Equal(settled.AddDays(7), clock.StayOf("heat-1995")!.LeavesAt);
    }

    [Fact]
    public void A_kept_film_is_never_due_and_has_no_date()
    {
        Settle("heat-1995");
        var clock = Clock();
        _keeps.Add("heat-1995", "42", _time.GetUtcNow());

        SettledAt("heat-1995", TimeSpan.FromDays(400));
        var stay = clock.StayOf("heat-1995")!;

        Assert.Empty(clock.Due());
        Assert.Equal("42", stay.KeeperId);
        Assert.Null(stay.LeavesAt);
        Assert.Null(stay.DaysLeft);

        _keeps.Remove("heat-1995");
        Assert.Equal(["heat-1995"], clock.Due());
    }

    [Fact]
    public void A_film_a_room_holds_is_never_due_however_long_it_has_waited()
    {
        Settle("heat-1995");
        var clock = Clock();
        SettledAt("heat-1995", TimeSpan.FromDays(30));

        _sessions.LoadTitle("room", "heat-1995", Somebody);
        Assert.Empty(clock.Due());

        // The room moves on, and the film's time has been up the whole while.
        _sessions.LoadTitle("room", "collateral-2004", Somebody);
        Assert.Equal(["heat-1995"], clock.Due());
    }

    [Fact]
    public void A_film_still_transcoding_on_the_cold_disk_is_on_no_clock()
    {
        // Written straight to the cold root by the hand-off, so it is there alone and is not yet a
        // film that has settled.
        var directory = Settle("heat-1995");
        File.WriteAllText(Path.Combine(directory, "manifest.json"), ManifestOf("heat-1995", "transcoding"));
        var clock = Clock();
        _time.SetUtcNow(DateTimeOffset.UtcNow.AddDays(365));

        var stay = clock.StayOf("heat-1995")!;

        Assert.False(stay.Settled);
        Assert.Null(stay.LeavesAt);
        Assert.Empty(clock.Due());
        Assert.Empty(clock.Stays());
    }

    [Fact]
    public void A_film_that_has_not_settled_is_on_no_clock()
    {
        Directory.CreateDirectory(Path.Combine(Hot, "making"));
        File.WriteAllText(Path.Combine(Hot, "making", "manifest.json"), "{}");
        var clock = Clock();
        _time.SetUtcNow(DateTimeOffset.UtcNow.AddDays(365));

        var stay = clock.StayOf("making")!;

        Assert.False(stay.Settled);
        Assert.Null(stay.LeavesAt);
        Assert.Null(stay.DaysLeft);
        Assert.Empty(clock.Due());
        Assert.Empty(clock.Stays());
    }

    [Fact]
    public void A_film_in_both_roots_is_mid_move_or_being_made_again_and_is_not_settled()
    {
        Settle("heat-1995");
        Directory.CreateDirectory(Path.Combine(Hot, "heat-1995"));
        var clock = Clock();
        _time.SetUtcNow(DateTimeOffset.UtcNow.AddDays(365));

        Assert.False(clock.StayOf("heat-1995")!.Settled);
        Assert.Empty(clock.Due());
    }

    [Fact]
    public void Nothing_is_settled_while_the_cold_volume_is_out()
    {
        Settle("heat-1995");
        var clock = Clock();
        _time.SetUtcNow(DateTimeOffset.UtcNow.AddDays(365));
        Assert.Equal(["heat-1995"], clock.Due());

        File.Delete(Path.Combine(Cold, MediaRoots.ColdMarker));
        _time.Advance(MediaRoots.Recheck + TimeSpan.FromSeconds(1));

        Assert.Empty(clock.Due());
        Assert.Empty(clock.Stays());
    }

    [Fact]
    public void Nothing_is_settled_with_no_cold_root()
    {
        var hotOnly = new MediaRoots(Hot);
        var clock = new LibraryClock(
            hotOnly, LibraryOn(hotOnly), _keeps, _plays, _sessions, _ => null,
            Options.Create(new LibraryOptions()), _time);
        Directory.CreateDirectory(Path.Combine(Hot, "heat-1995"));
        _time.SetUtcNow(DateTimeOffset.UtcNow.AddDays(365));

        Assert.Empty(clock.Due());
        Assert.False(clock.StayOf("heat-1995")!.Settled);
    }

    [Fact]
    public void Days_left_round_up_so_a_film_with_an_hour_left_still_says_one_day()
    {
        Settle("heat-1995");
        var clock = Clock();

        SettledAt("heat-1995", TimeSpan.FromDays(7) - TimeSpan.FromHours(1));
        Assert.Equal(1, clock.StayOf("heat-1995")!.DaysLeft);
        Assert.Empty(clock.Due());

        SettledAt("heat-1995", TimeSpan.FromDays(100));
        Assert.Equal(0, clock.StayOf("heat-1995")!.DaysLeft);
    }

    [Fact]
    public void Stays_list_the_settled_films_soonest_to_leave_first_and_the_kept_ones_last()
    {
        Settle("a-kept");
        Settle("b-old");
        Settle("c-newer");
        Directory.CreateDirectory(Path.Combine(Hot, "d-making"));
        var clock = Clock();
        var settled = DateTimeOffset.UtcNow;
        _time.SetUtcNow(settled.AddDays(20));

        _keeps.Add("a-kept", "1", _time.GetUtcNow());
        _plays.At["b-old"] = settled.AddDays(10);
        _plays.At["c-newer"] = settled.AddDays(19);

        var stays = clock.Stays();

        Assert.Equal(["b-old", "c-newer", "a-kept"], stays.Select(s => s.TitleId));
    }

    [Fact]
    public void A_keeper_is_shown_by_the_name_the_directory_gives_and_by_id_otherwise()
    {
        Settle("heat-1995");
        _keeps.Add("heat-1995", "42", _time.GetUtcNow());

        Assert.Equal("42", Clock().StayOf("heat-1995")!.KeeperName);
        Assert.Equal("Bea", Clock(id => id == "42" ? "Bea" : null).StayOf("heat-1995")!.KeeperName);
    }

    [Theory]
    [InlineData("../escape")]
    [InlineData("a/b")]
    [InlineData(".hidden")]
    [InlineData("")]
    public void An_id_that_could_leave_the_roots_is_not_a_film(string id)
    {
        Assert.Null(Clock().StayOf(id));
        Assert.False(Clock().IsSettled(id));
    }

    [Fact]
    public void A_film_the_library_does_not_hold_has_no_stay()
    {
        Assert.Null(Clock().StayOf("nothing-here"));
    }

    [Fact]
    public void A_keep_outlives_a_restart_and_the_first_keeper_keeps_it()
    {
        var path = Path.Combine(_root, "persist", "keeps.json");
        var store = new KeepStore(path, NullLogger<KeepStore>.Instance);
        var now = DateTimeOffset.UtcNow;

        var first = store.Add("heat-1995", "1", now);
        var second = store.Add("heat-1995", "2", now.AddMinutes(1));

        Assert.Equal("1", second.KeeperId);
        Assert.Equal(first.KeptAt, second.KeptAt);

        var reopened = new KeepStore(path, NullLogger<KeepStore>.Instance);
        Assert.Equal("1", reopened.Get("heat-1995")?.KeeperId);

        Assert.True(reopened.Remove("heat-1995"));
        Assert.False(reopened.Remove("heat-1995"));
        Assert.Null(new KeepStore(path, NullLogger<KeepStore>.Instance).Get("heat-1995"));
    }

    /// <summary>Settles an episode of a show, with the manifest that says which.</summary>
    private void SettleEpisode(string id, int season, int number)
    {
        var directory = Settle(id);
        File.WriteAllText(Path.Combine(directory, "manifest.json"), $$"""
            {
              "id": "{{id}}", "title": "Severance S{{season:00}}E{{number:00}}", "durationSeconds": 3000.0,
              "status": "ready", "master": "master.m3u8", "kind": "show",
              "episode": { "season": {{season}}, "number": {{number}} },
              "film": { "imdbId": "tt11280740", "name": "Severance" },
              "video": { "width": 1280, "height": 720, "sourceCodec": "h264", "sourceHdr": "sdr", "renditions": [] },
              "audio": [], "subtitles": []
            }
            """);
    }

    [Fact]
    public void A_season_stays_and_leaves_as_one()
    {
        SettleEpisode("severance-s02e01", 2, 1);
        SettleEpisode("severance-s02e02", 2, 2);
        SettleEpisode("severance-s01e01", 1, 1);
        var clock = Clock();
        var settled = SettledAt("severance-s02e02", TimeSpan.FromDays(3));

        // Watching one episode keeps its season, and only its season.
        _plays.At["severance-s02e02"] = settled.AddDays(2);
        Assert.Equal(settled.AddDays(9), clock.StayOf("severance-s02e01")!.LeavesAt);
        Assert.Equal("tt11280740:s02", clock.StayOf("severance-s02e01")!.Unit);
        Assert.Equal(2, clock.StayOf("severance-s02e01")!.Season);

        // A season is listed once.
        Assert.Equal(2, clock.Stays().Count);

        _time.SetUtcNow(settled.AddDays(8));
        Assert.Equal(["severance-s01e01"], clock.Due());

        _time.SetUtcNow(settled.AddDays(10));
        Assert.Equal(["severance-s01e01", "severance-s02e01", "severance-s02e02"], clock.Due().Order(StringComparer.Ordinal));
    }

    [Fact]
    public void A_keep_on_a_season_keeps_every_episode_in_it()
    {
        SettleEpisode("severance-s02e01", 2, 1);
        SettleEpisode("severance-s02e02", 2, 2);
        var clock = Clock();
        _keeps.Add("tt11280740:s02", "1", DateTimeOffset.UtcNow);
        SettledAt("severance-s02e01", TimeSpan.FromDays(100));

        Assert.Empty(clock.Due());
        Assert.Equal("1", clock.StayOf("severance-s02e02")!.KeeperId);
    }

    [Fact]
    public void A_room_on_one_episode_holds_its_whole_season()
    {
        SettleEpisode("severance-s02e01", 2, 1);
        SettleEpisode("severance-s02e02", 2, 2);
        var clock = Clock();
        _sessions.LoadTitle("room", "severance-s02e01", Somebody);
        SettledAt("severance-s02e01", TimeSpan.FromDays(100));

        Assert.Empty(clock.Due());
    }

    [Fact]
    public void A_keep_file_that_cannot_be_read_is_not_read_as_nobody_keeping_anything()
    {
        var path = Path.Combine(_root, "broken", "keeps.json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "{ not json");

        Assert.ThrowsAny<Exception>(() => new KeepStore(path, NullLogger<KeepStore>.Instance));
    }
}
