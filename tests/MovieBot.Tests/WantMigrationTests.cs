using Microsoft.Data.Sqlite;
using TheKrystalShip.MovieBot.Api.Data;
using TheKrystalShip.MovieBot.Api.Wants;
using TheKrystalShip.MovieBot.Core;
using Xunit;

namespace TheKrystalShip.MovieBot.Tests;

/// <summary>
/// A file at the schema version before wants holds saved titles and wishes; running the step brings
/// each across as a just-me want and drops the tables they lived in.
/// </summary>
public sealed class WantMigrationTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "moviebot-tests", "wants-" + Guid.NewGuid().ToString("n"));

    private string Path_ => System.IO.Path.Combine(_dir, "moviebot.db");

    /// <summary>The steps before the one that adds wants, with a few people's records in them.</summary>
    private void BuildPreviousFile() => PreviousSchema.Build(Path_, ["Tables", "Sorts"], """
            INSERT INTO lists (id, name, owner, watch_later, created_at, updated_at) VALUES
              ('l1', 'Watch later', 'ana', 1, '2026-01-01T00:00:00.0000000Z', '2026-01-01T00:00:00.0000000Z'),
              ('l2', 'Watch later', 'bo',  1, '2026-01-01T00:00:00.0000000Z', '2026-01-01T00:00:00.0000000Z'),
              ('l3', 'Friday',      'ana', 0, '2026-01-01T00:00:00.0000000Z', '2026-01-01T00:00:00.0000000Z');
            INSERT INTO list_members (list_id, user_id, role, since) VALUES ('l3', 'bo', 'editor', '2026-01-02T00:00:00.0000000Z');
            INSERT INTO list_entries (list_id, imdb_id, kind, added_at, added_by, note) VALUES
              ('l1', 'tt0000001', 'film', '2026-02-01T00:00:00.0000000Z', 'ana', 'a note'),
              ('l1', 'tt0000002', 'show', '2026-02-02T00:00:00.0000000Z', 'ana', NULL),
              ('l3', 'tt0000001', 'film', '2026-03-01T00:00:00.0000000Z', 'ana', NULL),
              ('l3', 'tt0000003', 'film', '2026-03-02T00:00:00.0000000Z', 'bo',  NULL),
              ('l2', 'tt0000004', 'film', '2026-02-05T00:00:00.0000000Z', 'bo',  NULL);
            INSERT INTO wishes (imdb_id, title, year, starring, poster_url, made_at) VALUES
              ('tt0000004', 'Waited On', 2026, 'Somebody', NULL, '2026-01-10T00:00:00.0000000Z'),
              ('tt0000005', 'Only Waited', 2026, NULL, NULL, '2026-01-11T00:00:00.0000000Z');
            INSERT INTO wish_subscribers (imdb_id, user_id, asked_at) VALUES
              ('tt0000004', 'bo',  '2026-01-12T00:00:00.0000000Z'),
              ('tt0000004', 'cy',  '2026-01-13T00:00:00.0000000Z'),
              ('tt0000005', 'ana', '2026-01-14T00:00:00.0000000Z');
            INSERT INTO notification_preferences (user_id, available, ready, episode, shared) VALUES ('ana', 1, 0, 1, 0);
            INSERT INTO notifications (id, user_id, kind, key, text, created_at, pushed) VALUES
              ('n1', 'bo', 'shared', 'shared:l3:bo', 'Ana shared Friday with you.', '2026-01-02T00:00:00.0000000Z', 0),
              ('n2', 'bo', 'available', 'available:tt0000004', 'Waited On is out.', '2026-01-02T00:00:00.0000000Z', 0);
            """);

    private static List<string> Tables(string path)
    {
        using var connection = new SqliteConnection($"Data Source={path};Pooling=false");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT name FROM sqlite_master WHERE type = 'table'";
        using var reader = command.ExecuteReader();
        var names = new List<string>();
        while (reader.Read()) names.Add(reader.GetString(0));
        return names;
    }

    [Fact]
    public void Saved_titles_and_wishes_arrive_as_just_me_wants_and_the_old_tables_go()
    {
        BuildPreviousFile();

        var database = new CinemaDatabase(Path_);
        var wants = new WantStore(database);

        var ana = wants.For("ana").ToDictionary(w => w.ImdbId);
        var bo = wants.For("bo").ToDictionary(w => w.ImdbId);
        var cy = wants.For("cy").ToDictionary(w => w.ImdbId);

        // Every entry is its adder's want, once, from the earlier moment, kept to oneself.
        Assert.Equal(["tt0000001", "tt0000002", "tt0000005"], ana.Keys.Order());
        Assert.Equal(["tt0000003", "tt0000004"], bo.Keys.Order());
        Assert.Equal(["tt0000004"], cy.Keys);
        Assert.All(wants.All(), w => Assert.False(w.Visible));
        Assert.All(wants.All(), w => Assert.False(w.MayFetch));
        Assert.Equal(6, wants.All().Count);

        Assert.Equal("film", ana["tt0000001"].Kind);
        Assert.Equal("show", ana["tt0000002"].Kind);
        Assert.Equal(DateTimeOffset.Parse("2026-02-01T00:00:00Z"), ana["tt0000001"].Since);

        // A title both saved and waited on keeps the earlier moment and the right to wish; one only saved has none.
        Assert.Equal(DateTimeOffset.Parse("2026-01-12T00:00:00Z"), bo["tt0000004"].Since);
        Assert.True(bo["tt0000004"].MayWish);
        Assert.False(bo["tt0000003"].MayWish);
        Assert.False(ana["tt0000001"].MayWish);
        Assert.True(cy["tt0000004"].MayWish);
        Assert.True(ana["tt0000005"].MayWish);
        Assert.Equal("film", ana["tt0000005"].Kind);

        // The wishes stay, now with their people read from the wants.
        var wishes = new Api.Acquisition.WishList(database);
        Assert.Equal(["tt0000004", "tt0000005"], wishes.All().Select(w => w.ImdbId));
        Assert.Equal(["bo", "cy"], wishes.Find("tt0000004")!.Subscribers.Select(s => s.UserId));

        Assert.DoesNotContain(Tables(Path_), t => t is "lists" or "list_members" or "list_entries" or "wish_subscribers");

        // The notification kind that went is gone from the bell, and from the switches.
        var preferences = new Api.Notifications.NotificationPreferenceStore(database).For("ana");
        Assert.False(preferences.Ready);
        Assert.True(preferences.Available && preferences.Episode);
        using var connection = new SqliteConnection($"Data Source={Path_};Pooling=false");
        connection.Open();
        using var count = connection.CreateCommand();
        count.CommandText = "SELECT group_concat(id) FROM notifications";
        Assert.Equal("n2", count.ExecuteScalar());
    }

    [Fact]
    public void Running_the_step_twice_changes_nothing()
    {
        BuildPreviousFile();
        var first = new WantStore(new CinemaDatabase(Path_)).All();

        var second = new WantStore(new CinemaDatabase(Path_)).All();

        Assert.Equal(first, second);
    }

    [Fact]
    public void A_new_file_has_the_wants_and_none_of_what_they_replace()
    {
        var database = new CinemaDatabase(Path_);

        Assert.Empty(new WantStore(database).All());
        Assert.DoesNotContain(Tables(Path_), t => t is "lists" or "list_members" or "list_entries" or "wish_subscribers");
        Assert.Contains("title_wants", Tables(Path_));
        Assert.Contains("wishes", Tables(Path_));
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }
}
