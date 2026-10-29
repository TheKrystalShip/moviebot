using Microsoft.Data.Sqlite;
using TheKrystalShip.MovieBot.Api.Data;
using TheKrystalShip.MovieBot.Api.Votes;
using Xunit;

namespace TheKrystalShip.MovieBot.Tests;

/// <summary>A file at the schema version before votes is brought forward by the step that creates them, keeping what it held.</summary>
public sealed class VoteMigrationTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "moviebot-tests", "votes-" + Guid.NewGuid().ToString("n"));

    private string DbPath => Path.Combine(_dir, "moviebot.db");

    private static readonly string[] Before =
        ["Tables", "Sorts", "Wants", "Together", "Plans", "TitleScores", "PlanSuggestions", "ImdbTitles", "Franchises", "Catalogued", "TabBar", "WatchedMarks", "ContinueHidden"];

    [Fact]
    public void A_file_one_version_behind_gains_the_votes_table_and_keeps_its_records()
    {
        PreviousSchema.Build(DbPath, Before, "INSERT INTO watched_marks (user_id, imdb_id, season, number, marked_at) VALUES ('ana', 'tt0000001', 0, 0, '2026-01-01T00:00:00.0000000Z');");

        var database = new CinemaDatabase(DbPath);
        var store = new VoteStore(database);
        var at = DateTimeOffset.Parse("2026-03-01T10:00:00Z");
        store.Set("ana", "title", "tt0000001", 1, at);

        Assert.Equal([new StoredVote("ana", "title", "tt0000001", 1, at)], store.For("ana"));
        Assert.Equal(1, database.Read(c => c.Count("SELECT count(*) FROM watched_marks")));
    }

    [Fact]
    public void The_table_refuses_a_kind_or_a_vote_it_does_not_hold()
    {
        var store = new VoteStore(new CinemaDatabase(DbPath));

        Assert.Throws<SqliteException>(() => store.Set("ana", "genre", "Drama", 1, DateTimeOffset.UnixEpoch));
        Assert.Throws<SqliteException>(() => store.Set("ana", "title", "tt0000001", 0, DateTimeOffset.UnixEpoch));
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }
}
