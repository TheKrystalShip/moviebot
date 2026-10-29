using TheKrystalShip.MovieBot.Api.Data;

namespace TheKrystalShip.MovieBot.Api.Votes;

/// <summary>The subjects a person can like or dislike.</summary>
public static class VoteKinds
{
    public const string Title = "title";
    public const string Person = "person";
    public const string Franchise = "franchise";
    public const string Company = "company";

    public static bool IsKnown(string? kind) => kind is Title or Person or Franchise or Company;
}

/// <summary>One person's vote on one subject, under the id the store keeps it by (<see cref="VoteStore"/>).</summary>
public sealed record StoredVote(string UserId, string Kind, string Id, int Vote, DateTimeOffset VotedAt);

/// <summary>
/// What each person likes and dislikes. A vote is keyed by the person and an id that outlives a rebuild
/// of the catalogue: a title's IMDb id, a person's film database id, a franchise's collection or
/// Wikidata id and a company's film database id. Casting again replaces the vote and its time.
/// </summary>
public sealed class VoteStore(CinemaDatabase database)
{
    private const string Columns = "user_id, subject_kind, subject_id, vote, voted_at";

    /// <summary>One person's votes, newest first.</summary>
    public List<StoredVote> For(string userId) => database.Read(c => c.Query(
        $"SELECT {Columns} FROM taste_votes WHERE user_id = $user ORDER BY voted_at DESC, subject_kind, subject_id", Read, ("$user", userId)));

    public StoredVote Set(string userId, string kind, string id, int vote, DateTimeOffset now) => database.Write(c =>
    {
        c.Execute(
            "INSERT INTO taste_votes (user_id, subject_kind, subject_id, vote, voted_at) VALUES ($user, $kind, $id, $vote, $at) "
            + "ON CONFLICT (user_id, subject_kind, subject_id) DO UPDATE SET vote = excluded.vote, voted_at = excluded.voted_at",
            ("$user", userId), ("$kind", kind), ("$id", id), ("$vote", vote), ("$at", now));
        return new StoredVote(userId, kind, id, vote, now);
    });

    /// <summary>Ends a person's vote. False when they had none.</summary>
    public bool Remove(string userId, string kind, string id) => database.Write(c => c.Execute(
        "DELETE FROM taste_votes WHERE user_id = $user AND subject_kind = $kind AND subject_id = $id",
        ("$user", userId), ("$kind", kind), ("$id", id)) > 0);

    private static StoredVote Read(Microsoft.Data.Sqlite.SqliteDataReader r) =>
        new(r.GetString(0), r.GetString(1), r.GetString(2), r.GetInt32(3), r.GetInstant(4));
}
