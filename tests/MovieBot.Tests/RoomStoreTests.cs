using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using TheKrystalShip.MovieBot.Api.Data;
using TheKrystalShip.MovieBot.Api.Library;
using TheKrystalShip.MovieBot.Api.Sessions;
using TheKrystalShip.MovieBot.Api.Subtitles;
using TheKrystalShip.MovieBot.Core;
using Xunit;

namespace TheKrystalShip.MovieBot.Tests;

/// <summary>
/// A room is the only thing in the system that exists nowhere else. The films are on disk and the
/// subtitles are kept; what a room is watching and where it has got to lives in memory, and what is
/// kept of it is all a restart can put back.
/// </summary>
public sealed class RoomStoreTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "moviebot-rooms", Guid.NewGuid().ToString("n"));

    private readonly FakeTimeProvider _clock = new(DateTimeOffset.Parse("2026-09-02T20:00:00Z"));

    private static readonly Actor Someone = new("u1", "Someone");
    private const string TitleId = "a-film";

    private readonly CinemaDatabase _database;

    public RoomStoreTests()
    {
        Directory.CreateDirectory(_root);
        _database = new CinemaDatabase(Path.Combine(_root, "moviebot.db"));
    }

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
    public void A_room_comes_back_as_the_same_run_of_itself()
    {
        var before = NewStore();
        before.LoadTitle("room", TitleId, Someone);
        var played = before.Play("room", 4210, Someone).State;

        Rooms().Write(before.Snapshot());

        var after = NewStore();
        after.Restore(Rooms().Read(_clock.GetUtcNow(), TimeSpan.FromMinutes(30)));
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
    /// A restart that opened every closed room is the worst way for one to stop being private, and
    /// one that forgot who was let in would shut everybody out of their own room.
    /// </summary>
    [Fact]
    public void A_room_comes_back_with_the_same_people_let_in()
    {
        var before = NewStore();
        before.Enter("private", "maker");
        before.SetOpen("private", false);
        before.Admit("private", "guest");
        before.Enter("open", "maker");

        Rooms().Write(before.Snapshot());

        var after = NewStore();
        after.Restore(Rooms().Read(_clock.GetUtcNow(), TimeSpan.FromMinutes(30)));

        Assert.False(after.Door("private")!.Open);
        Assert.True(after.Enter("private", "maker"));
        Assert.True(after.Enter("private", "guest"));
        Assert.False(after.Enter("private", "stranger"));
        Assert.True(after.Door("open")!.Open);
        Assert.True(after.Enter("open", "stranger"));
    }

    /// <summary>
    /// Position is held as a place at an instant rather than as a number that ticks, so a room
    /// that was playing when the server stopped is at the right place when it starts again —
    /// however long that took.
    /// </summary>
    [Fact]
    public void A_room_that_was_playing_is_still_playing_where_it_should_be()
    {
        var before = NewStore();
        before.LoadTitle("room", TitleId, Someone);
        before.Play("room", 600, Someone);

        Rooms().Write(before.Snapshot());

        // The film ran on for a minute while nothing was serving it.
        _clock.Advance(TimeSpan.FromMinutes(1));

        var after = NewStore();
        after.Restore(Rooms().Read(_clock.GetUtcNow(), TimeSpan.FromMinutes(30)));

        Assert.Equal(660, after.Get("room")!.PositionAt(_clock.GetUtcNow()), 1);
    }

    /// <summary>
    /// What is kept must not resurrect what the reaper would have forgotten. A link into a room
    /// stops working after the idle window, and a restart is not a way around that.
    /// </summary>
    [Fact]
    public void A_room_idle_past_the_window_is_not_restored()
    {
        var before = NewStore();
        before.LoadTitle("room", TitleId, Someone);
        Rooms().Write(before.Snapshot());

        _clock.Advance(TimeSpan.FromMinutes(31));

        var after = NewStore();
        after.Restore(Rooms().Read(_clock.GetUtcNow(), TimeSpan.FromMinutes(30)));

        Assert.Null(after.Get("room"));
    }

    /// <summary>
    /// Membership is a live connection and every one of them died with the server. Restoring the
    /// names would leave a room that reports people in it who are not connected to anything, and
    /// which the reaper would then refuse to ever forget.
    /// </summary>
    [Fact]
    public void Nobody_is_restored_into_a_room()
    {
        var before = NewStore();
        before.LoadTitle("room", TitleId, Someone);
        before.Join("room", "conn-1", new Participant { UserId = "u1", DisplayName = "Someone" });

        Rooms().Write(before.Snapshot());

        var after = NewStore();
        after.Restore(Rooms().Read(_clock.GetUtcNow(), TimeSpan.FromMinutes(30)));

        Assert.Empty(after.Participants("room"));
    }

    [Fact]
    public void Nothing_kept_is_no_rooms_rather_than_a_failure()
    {
        Assert.Empty(Rooms().Read(_clock.GetUtcNow(), TimeSpan.FromMinutes(30)));
    }

    /// <summary>A room whose kept state no longer reads as one starts again empty, and the others come back.</summary>
    [Fact]
    public void A_room_that_cannot_be_read_is_left_out_rather_than_a_failure()
    {
        var before = NewStore();
        before.LoadTitle("broken", TitleId, Someone);
        before.LoadTitle("fine", TitleId, Someone);
        Rooms().Write(before.Snapshot());
        _database.Write(c => c.Execute("UPDATE rooms SET state = '{ this is not a room' WHERE id = 'broken'"));

        var after = NewStore();
        after.Restore(Rooms().Read(_clock.GetUtcNow(), TimeSpan.FromMinutes(30)));

        Assert.Null(after.Get("broken"));
        Assert.NotNull(after.Get("fine"));
    }

    /// <summary>What is kept is the rooms as they stand: one swept since is not put back.</summary>
    [Fact]
    public void A_room_gone_since_is_not_kept()
    {
        var store = NewStore();
        store.LoadTitle("one", TitleId, Someone);
        store.LoadTitle("two", TitleId, Someone);
        Rooms().Write(store.Snapshot());

        var fewer = NewStore();
        fewer.LoadTitle("two", TitleId, Someone);
        Rooms().Write(fewer.Snapshot());

        var after = NewStore();
        after.Restore(Rooms().Read(_clock.GetUtcNow(), TimeSpan.FromMinutes(30)));

        Assert.Null(after.Get("one"));
        Assert.NotNull(after.Get("two"));
    }

    [Fact]
    public void Every_room_is_kept_not_only_the_last_one()
    {
        var store = NewStore();
        store.LoadTitle("one", TitleId, Someone);
        store.LoadTitle("two", TitleId, Someone);
        store.Play("two", 90, Someone);

        Rooms().Write(store.Snapshot());

        var after = NewStore();
        after.Restore(Rooms().Read(_clock.GetUtcNow(), TimeSpan.FromMinutes(30)));

        Assert.True(after.Get("one")!.Paused);
        Assert.Equal(90, after.Get("two")!.PositionSeconds, 3);
    }

    private RoomStore Rooms() => new(_database, NullLogger<RoomStore>.Instance);

    private SessionStore NewStore()
    {
        var media = Path.Combine(_root, "media");
        Directory.CreateDirectory(media);

        var library = new TitleLibrary(
            new MediaRoots(media),
            new SubtitleStore(Path.Combine(_root, "subtitles"), _database),
            new PinStore(_database),
            NullLogger<TitleLibrary>.Instance);

        return new SessionStore(library, _clock);
    }
}
