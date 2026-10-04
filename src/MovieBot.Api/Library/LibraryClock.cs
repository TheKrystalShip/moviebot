using Microsoft.Extensions.Options;
using TheKrystalShip.MovieBot.Api.Acquisition;
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
    /// Somebody keeping it, or a room holding it, stops the count.
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
/// </summary>
public sealed class LibraryClock(
    MediaRoots roots,
    TitleLibrary library,
    KeepStore keeps,
    IPlayHistory history,
    SessionStore sessions,
    KeeperNames names,
    IOptions<LibraryOptions> options,
    TimeProvider clock)
{
    /// <summary>
    /// Where a library film stands, or null when the library holds no film under the id. An id that
    /// could name a path outside a root is not a film.
    /// </summary>
    public TitleStay? StayOf(string id)
    {
        if (!TitleLibrary.IsSafeId(id) || roots.DirectoryOf(id) is null) return null;
        return Describe(id, clock.GetUtcNow());
    }

    /// <summary>
    /// Every settled unit — a film, or a season of a show — soonest to leave first, with the kept
    /// ones, which never leave, after them. A season is listed once, by the first of its episodes.
    /// </summary>
    public IReadOnlyList<TitleStay> Stays()
    {
        var now = clock.GetUtcNow();
        return [.. roots.Ids()
            .Where(IsSettled)
            .Order(StringComparer.Ordinal)
            .Select(id => Describe(id, now))
            .GroupBy(s => s.Unit, StringComparer.Ordinal)
            .Select(g => g.First())
            .OrderBy(s => s.LeavesAt is null)
            .ThenBy(s => s.LeavesAt)
            .ThenBy(s => s.TitleId, StringComparer.Ordinal)];
    }

    /// <summary>
    /// The ids of the settled titles whose time is up: not kept and not held by any room. A title a
    /// room holds is never due, whether or not anybody is in the room this second, and neither is
    /// anything that leaves with it: a room on one episode holds its whole season.
    /// </summary>
    public IReadOnlyList<string> Due()
    {
        var now = clock.GetUtcNow();
        var held = sessions.Summaries().Select(r => r.TitleId).OfType<string>()
            .Select(id => library.UnitOf(id) ?? id)
            .ToHashSet(StringComparer.Ordinal);

        return [.. roots.Ids()
            .Where(IsSettled)
            .Select(id => Describe(id, now))
            .Where(s => !held.Contains(s.Unit))
            .Where(s => s is { KeeperId: null, LeavesAt: { } leaves } && leaves <= now)
            .Select(s => s.TitleId)];
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

    /// <summary>
    /// A title's stay is its unit's. The keep is on the unit; the count runs from the latest play of
    /// anything in it and from the latest of its titles to settle, so an episode watched or newly
    /// arrived keeps its whole season, and every settled title in the unit falls due at once.
    /// </summary>
    private TitleStay Describe(string id, DateTimeOffset now)
    {
        var manifest = library.Get(id);
        var unit = manifest is null ? id : TitleLibrary.UnitOf(manifest);
        IReadOnlyList<string> members = unit == id ? [id] : library.IdsIn(unit);

        var keep = keeps.Get(unit);
        var played = members.Select(history.LastPlayedAt).Max();

        var stay = new TitleStay
        {
            TitleId = id,
            Unit = unit,
            Season = manifest?.Episode?.Season,
            Settled = IsSettled(id),
            KeeperId = keep?.KeeperId,
            KeeperName = keep is null ? null : names(keep.KeeperId) ?? keep.KeeperId,
            KeptAt = keep?.KeptAt,
            LastPlayedAt = played,
            LeaveAfterDays = options.Value.LeaveAfterDays,
        };

        if (!stay.Settled || keep is not null) return stay;

        var settled = members.Where(IsSettled).Select(SettledAt).DefaultIfEmpty(SettledAt(id)).Max();
        var since = Latest(Latest(played, settled), history.RecordingSince);
        var leaves = since + options.Value.LeaveAfter;
        var days = (int)Math.Ceiling((leaves - now).TotalDays);

        return stay with { LeavesAt = leaves, DaysLeft = Math.Max(0, days) };
    }

    /// <summary>
    /// When the film came onto the cold disk: its directory's creation time there, which is what the
    /// library's listing calls when a film was added.
    /// </summary>
    private DateTimeOffset SettledAt(string id) =>
        new(Directory.GetCreationTimeUtc(Path.Combine(roots.Cold!, id)), TimeSpan.Zero);

    private static DateTimeOffset Latest(DateTimeOffset? played, DateTimeOffset settled) =>
        played is { } at && at > settled ? at : settled;
}
