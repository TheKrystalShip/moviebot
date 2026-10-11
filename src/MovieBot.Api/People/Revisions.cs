using System.Collections.Concurrent;

namespace TheKrystalShip.MovieBot.Api.People;

/// <summary>
/// The order a person's writes were kept in, so a device can tell a push that is older than what it
/// already holds. A write is stamped inside the transaction that keeps it, so the numbers follow the
/// order the database kept them in whatever order the pushes then travel. Each number is at least the
/// clock in milliseconds and always one past the last for its key: they only ever rise, across a
/// restart too, and stay below the largest integer a browser holds exactly. A record is stamped as one
/// stream per person for preferences and one per person and film for tracks; the numbers of different
/// streams are not comparable.
/// </summary>
internal static class Revisions
{
    private static readonly ConcurrentDictionary<string, long> Last = new();

    public static long Next(string key) =>
        Last.AddOrUpdate(key, _ => Math.Max(1, Now()), (_, last) => Math.Max(last + 1, Now()));

    private static long Now() => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
}
