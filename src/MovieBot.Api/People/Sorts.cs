using Microsoft.Data.Sqlite;
using TheKrystalShip.MovieBot.Api.Data;

namespace TheKrystalShip.MovieBot.Api.People;

/// <summary>
/// One page's order as a person last picked it: the key the page sorts by and its direction. The
/// site names the pages and their keys and the API keeps them as given, so a page that gains a sort
/// needs nothing here; a key the page no longer offers is the site's to treat as its default.
/// </summary>
public sealed record SortOrder(string Key, string Direction)
{
    /// <summary>Whether this is an order the store keeps: a well-formed key, ascending or descending.</summary>
    public bool Valid() => SortStore.IsName(Key) && Direction is "asc" or "desc";
}

/// <summary>
/// Everybody's orders, one row per person and page. Somebody who never picked one on a page has no
/// row for it, and the page opens in its own order.
/// </summary>
public sealed class SortStore(CinemaDatabase database)
{
    /// <summary>The longest name a page or a key has.</summary>
    public const int NameLength = 40;

    /// <summary>The most pages one person keeps an order for, far more than the site has.</summary>
    public const int MaxSurfaces = 32;

    /// <summary>
    /// A page's or a key's name: lowercase letters, digits, dots and hyphens, starting with a letter,
    /// at most <see cref="NameLength"/> long.
    /// </summary>
    public static bool IsName(string? name) =>
        name is { Length: > 0 and <= NameLength } && name[0] is >= 'a' and <= 'z'
        && !name.AsSpan().ContainsAnyExcept("abcdefghijklmnopqrstuvwxyz0123456789.-");

    public Dictionary<string, SortOrder> For(string userId) =>
        database.Read(c => All(c, userId));

    /// <summary>
    /// Keeps one page's order and answers every order the person keeps, or null when the page would be
    /// one more than <see cref="MaxSurfaces"/>.
    /// </summary>
    public Dictionary<string, SortOrder>? Set(string userId, string surface, SortOrder order) =>
        database.Write(c =>
        {
            var known = c.Exists("SELECT 1 FROM viewer_sorts WHERE user_id = $user AND surface = $surface",
                ("$user", userId), ("$surface", surface));
            if (!known && c.Count("SELECT COUNT(*) FROM viewer_sorts WHERE user_id = $user", ("$user", userId)) >= MaxSurfaces)
                return null;

            c.Execute("INSERT OR REPLACE INTO viewer_sorts (user_id, surface, key, direction) VALUES ($user, $surface, $key, $direction)",
                ("$user", userId), ("$surface", surface), ("$key", order.Key), ("$direction", order.Direction));
            return All(c, userId);
        });

    private static Dictionary<string, SortOrder> All(SqliteConnection c, string userId) =>
        c.Query("SELECT surface, key, direction FROM viewer_sorts WHERE user_id = $user",
                r => (Surface: r.GetString(0), Order: new SortOrder(r.GetString(1), r.GetString(2))), ("$user", userId))
            .ToDictionary(row => row.Surface, row => row.Order, StringComparer.Ordinal);
}
