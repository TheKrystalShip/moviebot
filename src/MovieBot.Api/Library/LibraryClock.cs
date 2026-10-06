using Microsoft.Extensions.Options;
using TheKrystalShip.MovieBot.Api.People;
using TheKrystalShip.MovieBot.Api.Sessions;
using TheKrystalShip.MovieBot.Core;

namespace TheKrystalShip.MovieBot.Api.Library;

/// <summary>How long a settled film stays in the library without a play.</summary>
public sealed class LibraryOptions
{
    public const string Section = "Library";

    /// <summary>
    /// Days a settled film stays after its last play, or after it settled when nobody has played it.
    /// A room holding it stops the count.
    /// </summary>
    public double LeaveAfterDays { get; set; } = 7;

    public TimeSpan LeaveAfter => TimeSpan.FromDays(LeaveAfterDays);
}

/// <summary>
/// The library's clock: when each settled film leaves, and which are due now.
///
/// This is the only place the rule is written. The hand-off is the one process allowed to write the
/// media roots, so it deletes, but it deletes what this names and decides nothing of its own.
///
/// Only a film on the cold disk is on the clock. A film still on the media root is being made, or
/// is waiting to move, and a film in both roots is mid-move or being made again; none of them is a
/// film that has settled. The clock starts from whichever is later, the last play or the moment the
/// film settled, so a film that settles long after it was last watched is not due the instant it
/// arrives. It never starts before plays began to be recorded: a film with no recorded play is one
/// nobody has played since then, which is all that can be known.
///
/// A film is its own unit and an episode's is its season (<see cref="TitleLibrary.UnitOf(Manifest)"/>),
/// and a unit stays and leaves as one: its count runs from the latest play of anything in it and the
/// latest of its titles to settle, so every settled title in it falls due at the same moment.
/// </summary>
public sealed class LibraryClock(
    MediaRoots roots,
    TitleLibrary library,
    IPlayHistory history,
    SessionStore sessions,
    IOptions<LibraryOptions> options,
    TimeProvider clock)
{
    /// <summary>
    /// When a settled title leaves unless something in its unit is played first, or null when it is
    /// not settled: a date for a film that is not on the clock would be invented.
    /// </summary>
    public DateTimeOffset? LeavesAt(string id) => IsSettled(id) ? SettledLeavesAt(id) : null;

    /// <summary>
    /// The ids of the settled titles whose time is up and that no room holds. A title a room holds is
    /// never due, whether or not anybody is in the room this second, and neither is anything that
    /// leaves with it: a room on one episode holds its whole season.
    /// </summary>
    public IReadOnlyList<string> Due()
    {
        var now = clock.GetUtcNow();
        var held = sessions.Summaries().Select(r => r.TitleId).OfType<string>()
            .Select(id => library.UnitOf(id) ?? id)
            .ToHashSet(StringComparer.Ordinal);

        return [.. roots.Ids()
            .Where(IsSettled)
            .Where(id => !held.Contains(UnitOf(id)))
            .Where(id => SettledLeavesAt(id) <= now)];
    }

    /// <summary>
    /// Whether the film is on the cold disk alone and finished: moved, or made there, and not being
    /// made again. A transcode written straight to the cold disk is on it but is not settled until
    /// it is ready, so it is not on the clock while it runs or if it failed.
    /// </summary>
    public bool IsSettled(string id)
    {
        if (!TitleLibrary.IsSafeId(id) || roots.Cold is null || !roots.ColdReady) return false;

        return Directory.Exists(Path.Combine(roots.Cold, id))
               && !Directory.Exists(Path.Combine(roots.Hot, id))
               && library.Get(id)?.Status == TitleStatus.Ready;
    }

    private DateTimeOffset SettledLeavesAt(string id)
    {
        var unit = UnitOf(id);
        IReadOnlyList<string> members = unit == id ? [id] : library.IdsIn(unit);

        var played = members.Select(history.LastPlayedAt).Max();
        var settled = members.Where(IsSettled).Select(SettledAt).DefaultIfEmpty(SettledAt(id)).Max();
        return Latest(Latest(played, settled), history.RecordingSince) + options.Value.LeaveAfter;
    }

    private string UnitOf(string id) => library.Get(id) is { } manifest ? TitleLibrary.UnitOf(manifest) : id;

    /// <summary>
    /// When the film came onto the cold disk: its directory's creation time there, which is what the
    /// library's listing calls when a film was added.
    /// </summary>
    private DateTimeOffset SettledAt(string id) =>
        new(Directory.GetCreationTimeUtc(Path.Combine(roots.Cold!, id)), TimeSpan.Zero);

    private static DateTimeOffset Latest(DateTimeOffset? played, DateTimeOffset settled) =>
        played is { } at && at > settled ? at : settled;
}
