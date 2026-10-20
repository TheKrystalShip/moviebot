using TheKrystalShip.MovieBot.Api.Data;

namespace TheKrystalShip.MovieBot.Api.Wants;

/// <summary>What a want is for: a film or a show, never an episode.</summary>
public static class WantKinds
{
    public const string Film = "film";
    public const string Show = "show";

    public static bool IsKnown(string? kind) => kind is Film or Show;
}

/// <summary>One person's want of one title.</summary>
public sealed record StoredWant
{
    public required string UserId { get; init; }

    /// <summary>The canonical <c>tt0458352</c> form. A person wants a title once.</summary>
    public required string ImdbId { get; init; }

    /// <summary>One of <see cref="WantKinds"/>.</summary>
    public required string Kind { get; init; }

    /// <summary>Whether the want is named to everybody signed in.</summary>
    public required bool Visible { get; init; }

    /// <summary>Whether the token that wrote the want held <c>cinema:download</c>.</summary>
    public required bool MayFetch { get; init; }

    /// <summary>Whether the token that wrote the want held <c>cinema:wish</c>.</summary>
    public required bool MayWish { get; init; }

    public required DateTimeOffset Since { get; init; }
}

/// <summary>
/// What each person wants to watch, keyed by the title's IMDb id and not by anything the library
/// holds, so a want outlives the title leaving the library and is the same want when it comes back.
///
/// A want carries what the server may spend on it, captured from the token when the person writes it
/// (<see cref="StoredWant.MayFetch"/>, <see cref="StoredWant.MayWish"/>). The film a wish is kept for
/// lives while some want on it has <c>MayWish</c> set, and goes with the last one, in the same
/// transaction that ends the want.
/// </summary>
public sealed class WantStore(CinemaDatabase database)
{
    private const string Columns = "user_id, imdb_id, kind, visible, may_fetch, may_wish, since";

    /// <summary>Every want of every person, which is what keeps their titles described.</summary>
    public List<StoredWant> All() => database.Read(c => c.Query($"SELECT {Columns} FROM title_wants ORDER BY since, user_id, imdb_id", Read));

    /// <summary>One person's wants, newest first.</summary>
    public List<StoredWant> For(string userId) => database.Read(c => c.Query(
        $"SELECT {Columns} FROM title_wants WHERE user_id = $user ORDER BY since DESC, imdb_id", Read, ("$user", userId)));

    public StoredWant? Get(string userId, string imdbId) => database.Read(c => c.First(
        $"SELECT {Columns} FROM title_wants WHERE user_id = $user AND imdb_id = $imdb", Read, ("$user", userId), ("$imdb", imdbId)));

    /// <summary>The people with a visible want on a title, the one asking left out, newest first.</summary>
    public List<StoredWant> Wanters(string imdbId, string exceptUserId) => database.Read(c => c.Query(
        $"SELECT {Columns} FROM title_wants WHERE imdb_id = $imdb AND visible = 1 AND user_id <> $user ORDER BY since DESC, user_id",
        Read, ("$imdb", imdbId), ("$user", exceptUserId)));

    /// <summary>
    /// How many other people have a visible want on each title the person wants, by IMDb id. A title
    /// nobody else wants visibly is absent.
    /// </summary>
    public Dictionary<string, int> OthersOf(string userId) => database.Read(c => c.Query(
        "SELECT imdb_id, COUNT(*) FROM title_wants WHERE visible = 1 AND user_id <> $user "
        + "AND imdb_id IN (SELECT imdb_id FROM title_wants WHERE user_id = $user) GROUP BY imdb_id",
        r => (r.GetString(0), r.GetInt32(1)), ("$user", userId)).ToDictionary(p => p.Item1, p => p.Item2, StringComparer.Ordinal));

    /// <summary>The people whose want on a film lets the sweep ask the tracker for it, in the order they wanted it.</summary>
    public List<StoredWant> Wishers(string imdbId) => database.Read(c => c.Query(
        $"SELECT {Columns} FROM title_wants WHERE imdb_id = $imdb AND may_wish = 1 ORDER BY since, user_id",
        Read, ("$imdb", imdbId)));

    /// <summary>The people whose want on a title lets the server fetch it for them, in the order they wanted it.</summary>
    public List<StoredWant> Fetchers(string imdbId) => database.Read(c => c.Query(
        $"SELECT {Columns} FROM title_wants WHERE imdb_id = $imdb AND may_fetch = 1 ORDER BY since, user_id",
        Read, ("$imdb", imdbId)));

    /// <summary>
    /// How many people want a title as the person asking may count them: every visible want and their
    /// own, and whether the person asking is one.
    /// </summary>
    public (int Wanters, bool Wanting) Count(string imdbId, string? userId) => database.Read(c => c.First(
        "SELECT COUNT(*), COALESCE(MAX(user_id = $user), 0) FROM title_wants WHERE imdb_id = $imdb AND (visible = 1 OR user_id = $user)",
        r => (r.GetInt32(0), r.GetInt32(1) != 0), ("$imdb", imdbId), ("$user", userId)));

    /// <summary>
    /// Makes a person's want of a title, or refreshes the one they have: the flags come from the token
    /// that writes it, and <paramref name="visible"/> changes only when it is given (a new want with
    /// none given is visible). The want keeps the moment it was first made.
    /// </summary>
    public (StoredWant Want, bool Created) Set(
        string userId, string imdbId, string kind, bool? visible, bool mayFetch, bool mayWish, DateTimeOffset now) => database.Write(c =>
    {
        var existing = c.First($"SELECT {Columns} FROM title_wants WHERE user_id = $user AND imdb_id = $imdb", Read,
            ("$user", userId), ("$imdb", imdbId));
        var want = new StoredWant
        {
            UserId = userId, ImdbId = imdbId, Kind = kind, Visible = visible ?? existing?.Visible ?? true,
            MayFetch = mayFetch, MayWish = mayWish, Since = existing?.Since ?? now,
        };
        if (existing == want) return (want, false);

        c.Execute(
            "INSERT INTO title_wants (user_id, imdb_id, kind, visible, may_fetch, may_wish, since) "
            + "VALUES ($user, $imdb, $kind, $visible, $fetch, $wish, $since) "
            + "ON CONFLICT (user_id, imdb_id) DO UPDATE SET kind = excluded.kind, visible = excluded.visible, "
            + "may_fetch = excluded.may_fetch, may_wish = excluded.may_wish",
            ("$user", userId), ("$imdb", imdbId), ("$kind", kind), ("$visible", want.Visible), ("$fetch", mayFetch),
            ("$wish", mayWish), ("$since", want.Since));
        if (!mayWish) KeepWish(c, imdbId);
        return (want, existing is null);
    });

    /// <summary>Ends a person's want. False when they had none.</summary>
    public bool Remove(string userId, string imdbId) => database.Write(c =>
    {
        if (c.Execute("DELETE FROM title_wants WHERE user_id = $user AND imdb_id = $imdb", ("$user", userId), ("$imdb", imdbId)) == 0)
            return false;
        KeepWish(c, imdbId);
        return true;
    });

    /// <summary>A film's wish goes with the last want that could ask the tracker for it.</summary>
    private static void KeepWish(Microsoft.Data.Sqlite.SqliteConnection c, string imdbId) => c.Execute(
        "DELETE FROM wishes WHERE imdb_id = $imdb AND NOT EXISTS (SELECT 1 FROM title_wants WHERE imdb_id = $imdb AND may_wish = 1)",
        ("$imdb", imdbId));

    private static StoredWant Read(Microsoft.Data.Sqlite.SqliteDataReader r) => new()
    {
        UserId = r.GetString(0), ImdbId = r.GetString(1), Kind = r.GetString(2), Visible = r.GetBool(3),
        MayFetch = r.GetBool(4), MayWish = r.GetBool(5), Since = r.GetInstant(6),
    };
}
