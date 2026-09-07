using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using TheKrystalShip.MovieBot.Api.Library;
using TheKrystalShip.MovieBot.Api.Subtitles;
using TheKrystalShip.MovieBot.Api.Sessions;
using TheKrystalShip.MovieBot.Core;
using Xunit;

namespace TheKrystalShip.MovieBot.Tests;

/// <summary>
/// A launch hands out a link, and the promise is that the link stops being a way in once nobody
/// is watching. That is a security claim, so it is tested rather than asserted in a comment.
/// </summary>
public sealed class RoomExpiryTests : IDisposable
{
    private readonly string _mediaRoot = Path.Combine(
        Path.GetTempPath(), "moviebot-expiry", Guid.NewGuid().ToString("n"));

    private readonly FakeTimeProvider _clock = new(DateTimeOffset.Parse("2026-09-01T20:00:00Z"));
    private readonly SessionStore _sessions;

    private static readonly Actor Someone = new("u1", "Someone");
    private const string TitleId = "a-film";

    public RoomExpiryTests()
    {
        WriteManifest(TitleId);
        var library = new TitleLibrary(
            new MediaRoots(_mediaRoot),
            new SubtitleStore(Path.Combine(_mediaRoot, "..", "subtitles"), NullLogger<SubtitleStore>.Instance),
            new PinStore(Path.Combine(_mediaRoot, "..", "subtitles"), NullLogger<PinStore>.Instance),
            NullLogger<TitleLibrary>.Instance);
        _sessions = new SessionStore(library, _clock);
    }

    public void Dispose()
    {
        if (Directory.Exists(_mediaRoot)) Directory.Delete(_mediaRoot, recursive: true);
    }

    [Fact]
    public void An_empty_room_is_forgotten_once_the_idle_window_passes()
    {
        _sessions.LoadTitle("room", TitleId, Someone);
        Assert.Equal(TitleId, _sessions.Get("room")?.TitleId);

        _clock.Advance(TimeSpan.FromMinutes(29));
        Assert.Empty(_sessions.Sweep(TimeSpan.FromMinutes(30)));
        Assert.Equal(TitleId, _sessions.Get("room")?.TitleId);

        _clock.Advance(TimeSpan.FromMinutes(2));
        Assert.Equal(["room"], _sessions.Sweep(TimeSpan.FromMinutes(30)));
        Assert.Null(_sessions.Get("room"));
    }

    [Fact]
    public void Reopening_a_forgotten_room_finds_nothing_playing()
    {
        _sessions.LoadTitle("room", TitleId, Someone);
        _sessions.Play("room", 42, Someone);

        _clock.Advance(TimeSpan.FromHours(1));
        _sessions.Sweep(TimeSpan.FromMinutes(30));

        // The same link still resolves — it names a room, and rooms are made on demand. What it
        // must not do is resume the film that was playing in it.
        var reopened = _sessions.GetOrCreate("room");
        Assert.Null(reopened.TitleId);
        Assert.True(reopened.Paused);
        Assert.Equal(0, reopened.PositionSeconds);
    }

    /// <summary>
    /// Revisions count within one run of a room and are meaningless across two.
    ///
    /// A client discards any push whose revision is not greater than the last it applied. A room
    /// built again — swept for being empty, or lost with the process — counts from zero, so a
    /// client still holding the old run's numbers would discard everything the new run ever says,
    /// for as long as its page stays open: the film then runs on a state nothing can correct, and
    /// no seek it makes is ever applied. The epoch is what tells the two runs apart.
    /// </summary>
    [Fact]
    public void A_room_built_again_is_a_different_run_of_itself()
    {
        _sessions.LoadTitle("room", TitleId, Someone);
        var before = _sessions.Play("room", 42, Someone).State;
        Assert.Equal(2, before.Revision);

        _clock.Advance(TimeSpan.FromHours(1));
        _sessions.Sweep(TimeSpan.FromMinutes(30));

        var after = _sessions.GetOrCreate("room");

        Assert.Equal(0, after.Revision);
        Assert.NotEqual(before.Epoch, after.Epoch);
    }

    [Fact]
    public void Every_state_a_room_produces_carries_the_epoch_it_was_built_with()
    {
        var epoch = _sessions.GetOrCreate("room").Epoch;
        Assert.NotEqual("", epoch);

        Assert.Equal(epoch, _sessions.LoadTitle("room", TitleId, Someone).State.Epoch);
        Assert.Equal(epoch, _sessions.Play("room", 10, Someone).State.Epoch);
        Assert.Equal(epoch, _sessions.Pause("room", 20, Someone).State.Epoch);
        Assert.Equal(epoch, _sessions.Seek("room", 30, Someone).State.Epoch);
        Assert.Equal(epoch, _sessions.Get("room")?.Epoch);
    }

    [Fact]
    public void A_room_with_somebody_in_it_is_never_forgotten()
    {
        _sessions.LoadTitle("room", TitleId, Someone);
        _sessions.Join("room", "conn-1", new Participant { UserId = "u1", DisplayName = "Someone" });

        // A film runs for hours without anybody touching a control. Presence is the activity.
        _clock.Advance(TimeSpan.FromHours(3));

        Assert.Empty(_sessions.Sweep(TimeSpan.FromMinutes(30)));
        Assert.Equal(TitleId, _sessions.Get("room")?.TitleId);
    }

    [Fact]
    public void The_window_starts_when_the_last_person_leaves()
    {
        _sessions.LoadTitle("room", TitleId, Someone);
        _sessions.Join("room", "conn-1", new Participant { UserId = "u1", DisplayName = "Someone" });

        _clock.Advance(TimeSpan.FromHours(2));
        _sessions.Leave("room", "conn-1");

        _clock.Advance(TimeSpan.FromMinutes(29));
        Assert.Empty(_sessions.Sweep(TimeSpan.FromMinutes(30)));

        _clock.Advance(TimeSpan.FromMinutes(2));
        Assert.Equal(["room"], _sessions.Sweep(TimeSpan.FromMinutes(30)));
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
                Renditions = [new Rendition { Name = "800p", BitrateKbps = 9000, Uri = "v0/index.m3u8" }]
            },
            Audio = [],
            Subtitles = []
        };

        ManifestJson.WriteAtomic(Path.Combine(_mediaRoot, id, "manifest.json"), manifest);
    }
}
