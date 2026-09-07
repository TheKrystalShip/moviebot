using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using TheKrystalShip.MovieBot.Api.Library;
using TheKrystalShip.MovieBot.Api.Sessions;
using TheKrystalShip.MovieBot.Api.Subtitles;
using TheKrystalShip.MovieBot.Core;
using Xunit;

namespace TheKrystalShip.MovieBot.Tests;

/// <summary>
/// A room is the only thing in the system that exists nowhere else. The films are on disk and the
/// subtitles are on disk; what a room is watching and where it has got to lived in memory alone,
/// so restarting the server took the film out from under everybody watching it.
/// </summary>
public sealed class SessionJournalTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "moviebot-journal", Guid.NewGuid().ToString("n"));

    private readonly FakeTimeProvider _clock = new(DateTimeOffset.Parse("2026-09-02T20:00:00Z"));

    private static readonly Actor Someone = new("u1", "Someone");
    private const string TitleId = "a-film";

    private string JournalPath => Path.Combine(_root, "rooms.json");

    public SessionJournalTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    /// <summary>
    /// The whole point: the same room, not a new one wearing its name. A restored room keeping its
    /// epoch and revision is what lets a client that was connected across the restart go on
    /// applying pushes rather than discarding every one of them.
    /// </summary>
    [Fact]
    public async Task A_room_comes_back_as_the_same_run_of_itself()
    {
        var before = NewStore();
        before.LoadTitle("room", TitleId, Someone);
        var played = before.Play("room", 4210, Someone).State;

        await Journal().WriteAsync(before.Snapshot(), CancellationToken.None);

        var after = NewStore();
        after.Restore(Journal().Read(_clock.GetUtcNow(), TimeSpan.FromMinutes(30)));
        var restored = after.Get("room")!;

        Assert.Equal(played.Epoch, restored.Epoch);
        Assert.Equal(played.Revision, restored.Revision);
        Assert.Equal(TitleId, restored.TitleId);
        Assert.False(restored.Paused);
        Assert.Equal(4210, restored.PositionSeconds, 3);
        Assert.Equal(played.AnchorUtc, restored.AnchorUtc);
        Assert.Equal("Someone", restored.UpdatedBy?.DisplayName);
    }

    /// <summary>
    /// Position is held as a place at an instant rather than as a number that ticks, so a room
    /// that was playing when the server stopped is at the right place when it starts again —
    /// however long that took.
    /// </summary>
    [Fact]
    public async Task A_room_that_was_playing_is_still_playing_where_it_should_be()
    {
        var before = NewStore();
        before.LoadTitle("room", TitleId, Someone);
        before.Play("room", 600, Someone);

        await Journal().WriteAsync(before.Snapshot(), CancellationToken.None);

        // The film ran on for a minute while nothing was serving it.
        _clock.Advance(TimeSpan.FromMinutes(1));

        var after = NewStore();
        after.Restore(Journal().Read(_clock.GetUtcNow(), TimeSpan.FromMinutes(30)));

        Assert.Equal(660, after.Get("room")!.PositionAt(_clock.GetUtcNow()), 1);
    }

    /// <summary>
    /// The journal must not resurrect what the reaper would have forgotten. A link into a room
    /// stops working after the idle window, and a restart is not a way around that.
    /// </summary>
    [Fact]
    public async Task A_room_idle_past_the_window_is_not_restored()
    {
        var before = NewStore();
        before.LoadTitle("room", TitleId, Someone);
        await Journal().WriteAsync(before.Snapshot(), CancellationToken.None);

        _clock.Advance(TimeSpan.FromMinutes(31));

        var after = NewStore();
        after.Restore(Journal().Read(_clock.GetUtcNow(), TimeSpan.FromMinutes(30)));

        Assert.Null(after.Get("room"));
    }

    /// <summary>
    /// Membership is a live connection and every one of them died with the server. Restoring the
    /// names would leave a room that reports people in it who are not connected to anything, and
    /// which the reaper would then refuse to ever forget.
    /// </summary>
    [Fact]
    public async Task Nobody_is_restored_into_a_room()
    {
        var before = NewStore();
        before.LoadTitle("room", TitleId, Someone);
        before.Join("room", "conn-1", new Participant { UserId = "u1", DisplayName = "Someone" });

        await Journal().WriteAsync(before.Snapshot(), CancellationToken.None);

        var after = NewStore();
        after.Restore(Journal().Read(_clock.GetUtcNow(), TimeSpan.FromMinutes(30)));

        Assert.Empty(after.Participants("room"));
    }

    [Fact]
    public void A_journal_that_is_not_there_is_no_rooms_rather_than_a_failure()
    {
        Assert.Empty(Journal().Read(_clock.GetUtcNow(), TimeSpan.FromMinutes(30)));
    }

    [Fact]
    public async Task A_journal_that_cannot_be_read_is_no_rooms_rather_than_a_failure()
    {
        await File.WriteAllTextAsync(JournalPath, "{ this is not a journal");

        Assert.Empty(Journal().Read(_clock.GetUtcNow(), TimeSpan.FromMinutes(30)));
    }

    /// <summary>Written whole and moved into place: a half-written journal is worse than none.</summary>
    [Fact]
    public async Task Writing_leaves_nothing_half_written_behind()
    {
        var store = NewStore();
        store.LoadTitle("room", TitleId, Someone);
        await Journal().WriteAsync(store.Snapshot(), CancellationToken.None);

        Assert.True(File.Exists(JournalPath));
        Assert.Empty(Directory.GetFiles(_root, "*.tmp"));
    }

    [Fact]
    public async Task Every_room_is_kept_not_only_the_last_one()
    {
        var store = NewStore();
        store.LoadTitle("one", TitleId, Someone);
        store.LoadTitle("two", TitleId, Someone);
        store.Play("two", 90, Someone);

        await Journal().WriteAsync(store.Snapshot(), CancellationToken.None);

        var after = NewStore();
        after.Restore(Journal().Read(_clock.GetUtcNow(), TimeSpan.FromMinutes(30)));

        Assert.True(after.Get("one")!.Paused);
        Assert.Equal(90, after.Get("two")!.PositionSeconds, 3);
    }

    private SessionJournal Journal() => new(JournalPath, NullLogger<SessionJournal>.Instance);

    private SessionStore NewStore()
    {
        var media = Path.Combine(_root, "media");
        Directory.CreateDirectory(media);

        var library = new TitleLibrary(
            new MediaRoots(media),
            new SubtitleStore(Path.Combine(_root, "subtitles"), NullLogger<SubtitleStore>.Instance),
            new PinStore(Path.Combine(_root, "subtitles"), NullLogger<PinStore>.Instance),
            NullLogger<TitleLibrary>.Instance);

        return new SessionStore(library, _clock);
    }
}
