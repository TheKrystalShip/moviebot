using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using TheKrystalShip.MovieBot.Api.Data;
using TheKrystalShip.MovieBot.Api.Library;
using TheKrystalShip.MovieBot.Api.Subtitles;
using TheKrystalShip.MovieBot.Api.Sessions;
using TheKrystalShip.MovieBot.Core;
using Xunit;

namespace TheKrystalShip.MovieBot.Tests;

/// <summary>
/// A viewer on a phone loses the connection, plays on from what the phone fetched, and comes back. A
/// room nobody is in waits where it was, and the returning player's account of what it played is the
/// room's when nobody else could have seen the room differently.
/// </summary>
public sealed class RoomResumeTests : IDisposable
{
    // Each test's library, database and subtitles sit in a directory of its own.
    private readonly string _root = Path.Combine(Path.GetTempPath(), "moviebot-resume-" + Guid.NewGuid().ToString("n"));
    private string MediaRoot => Path.Combine(_root, "media");

    private readonly FakeTimeProvider _clock = new(DateTimeOffset.Parse("2026-09-01T20:00:00Z"));
    private readonly SessionStore _sessions;
    private readonly List<WatchedSpan> _watched = [];

    private static readonly Actor Ada = new("u1", "Ada");
    private static readonly Participant AdaViewer = new() { UserId = "u1", DisplayName = "Ada" };
    private static readonly Participant BoViewer = new() { UserId = "u2", DisplayName = "Bo" };
    private const string TitleId = "a-film";

    public RoomResumeTests()
    {
        var database = new CinemaDatabase(Path.Combine(_root, "moviebot.db"));
        WriteManifest(TitleId);
        var library = new TitleLibrary(
            new MediaRoots(MediaRoot),
            new SubtitleStore(Path.Combine(_root, "subtitles"), database),
            new PinStore(database),
            NullLogger<TitleLibrary>.Instance);
        _sessions = new SessionStore(library, _clock);
        _sessions.Watched += _watched.Add;
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    /// <summary>Ada watching alone, playing from 100s; answers the state her player last applied.</summary>
    private SessionState AdaWatching()
    {
        _sessions.Join("room", "phone-1", AdaViewer);
        _sessions.LoadTitle("room", TitleId, Ada);
        return _sessions.Play("room", 100, Ada).State;
    }

    private static PlayerAccount Account(SessionState applied, double at, bool paused = false) => new()
    {
        Epoch = applied.Epoch, Revision = applied.Revision, TitleId = TitleId, PositionSeconds = at, Paused = paused
    };

    [Fact]
    public void A_room_whose_last_viewer_drops_waits_where_it_was()
    {
        AdaWatching();
        _clock.Advance(TimeSpan.FromSeconds(60));
        _sessions.Leave("room", "phone-1");

        var held = _sessions.Hold("room")!;
        Assert.True(held.Waiting);
        Assert.Null(held.UpdatedBy);

        _clock.Advance(TimeSpan.FromMinutes(10));
        Assert.Equal(160, _sessions.Get("room")!.PositionAt(_clock.GetUtcNow()), 1);
    }

    [Fact]
    public void A_viewer_back_alone_carries_the_room_on_from_their_own_playhead()
    {
        var applied = AdaWatching();
        _clock.Advance(TimeSpan.FromSeconds(60));
        _sessions.Leave("room", "phone-1");
        _sessions.Hold("room");

        // The phone played its buffer for four more minutes, then the connection came back.
        _clock.Advance(TimeSpan.FromMinutes(5));
        _sessions.Join("room", "phone-2", AdaViewer);
        var resumed = _sessions.Resume("room", "phone-2", AdaViewer, Account(applied, 400))!;

        Assert.Equal(400, resumed.PositionSeconds, 1);
        Assert.False(resumed.Paused);
        Assert.False(resumed.Waiting);
        Assert.Null(resumed.UpdatedBy);
        Assert.Null(_sessions.Hold("room"));

        var credited = Assert.Single(_watched, w => w.PositionSeconds > 399);
        Assert.Equal("u1", Assert.Single(credited.Viewers).UserId);
    }

    [Fact]
    public void A_viewer_who_paused_while_away_comes_back_paused()
    {
        var applied = AdaWatching();
        _sessions.Leave("room", "phone-1");
        _sessions.Hold("room");
        _sessions.Join("room", "phone-2", AdaViewer);

        var resumed = _sessions.Resume("room", "phone-2", AdaViewer, Account(applied, 250, paused: true))!;
        Assert.True(resumed.Paused);
        Assert.Equal(250, resumed.PositionSeconds, 1);
    }

    [Fact]
    public void A_room_somebody_changed_meanwhile_stands()
    {
        var applied = AdaWatching();
        _sessions.Leave("room", "phone-1");
        _sessions.Hold("room");
        _sessions.Seek("room", 2000, new Actor("u2", "Bo", Via: "bot"));

        _sessions.Join("room", "phone-2", AdaViewer);
        Assert.Null(_sessions.Resume("room", "phone-2", AdaViewer, Account(applied, 400)));
        Assert.Equal(2000, _sessions.Get("room")!.PositionSeconds, 1);
    }

    [Fact]
    public void A_room_somebody_else_is_watching_stands()
    {
        var applied = AdaWatching();
        _sessions.Join("room", "laptop-1", BoViewer);
        _sessions.Leave("room", "phone-1");

        _sessions.Join("room", "phone-2", AdaViewer);
        Assert.Null(_sessions.Resume("room", "phone-2", AdaViewer, Account(applied, 40)));
    }

    [Fact]
    public void An_account_from_another_run_of_the_room_is_not_taken()
    {
        var applied = AdaWatching();
        _sessions.Leave("room", "phone-1");
        _sessions.Hold("room");
        _sessions.Join("room", "phone-2", AdaViewer);

        Assert.Null(_sessions.Resume("room", "phone-2", AdaViewer, Account(applied, 400) with { Epoch = "another" }));
        Assert.Null(_sessions.Resume("room", "phone-2", AdaViewer, Account(applied, 400) with { TitleId = "other-film" }));
    }

    [Fact]
    public void A_room_forgotten_while_the_viewer_was_away_gets_their_film_back()
    {
        var applied = AdaWatching();
        _sessions.Leave("room", "phone-1");
        _sessions.Hold("room");
        _clock.Advance(TimeSpan.FromMinutes(31));
        Assert.Equal(["room"], _sessions.Sweep(TimeSpan.FromMinutes(30)));

        _sessions.Enter("room", "u1");
        _sessions.Join("room", "phone-2", AdaViewer);
        var resumed = _sessions.Resume("room", "phone-2", AdaViewer, Account(applied, 1900))!;

        Assert.Equal(TitleId, resumed.TitleId);
        Assert.Equal(1900, resumed.PositionSeconds, 1);
        Assert.False(resumed.Paused);
    }

    [Fact]
    public void A_playhead_that_is_no_place_in_the_film_is_refused()
    {
        var applied = AdaWatching();
        _sessions.Leave("room", "phone-1");
        _sessions.Hold("room");
        _sessions.Join("room", "phone-2", AdaViewer);

        Assert.Null(_sessions.Resume("room", "phone-2", AdaViewer, Account(applied, -5)));
        Assert.Null(_sessions.Resume("room", "phone-2", AdaViewer, Account(applied, double.NaN)));
    }

    private void WriteManifest(string id)
    {
        var manifest = new Manifest
        {
            Id = id,
            Title = id,
            DurationSeconds = 6000,
            Status = TitleStatus.Ready,
            Video = new VideoInfo
            {
                Width = 1920,
                Height = 800,
                SourceCodec = "hevc",
                SourceHdr = "sdr",
                Renditions = [new Rendition { Name = "800p", BitrateKbps = 9000, Width = 1920, Height = 800, Uri = "v0/index.m3u8" }]
            },
            Audio = [],
            Subtitles = []
        };

        ManifestJson.WriteAtomic(Path.Combine(MediaRoot, id, "manifest.json"), manifest);
    }
}
