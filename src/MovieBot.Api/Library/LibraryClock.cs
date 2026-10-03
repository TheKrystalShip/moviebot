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

    /// <summary>Every settled film, soonest to leave first, with the kept ones, which never leave, after them.</summary>
    public IReadOnlyList<TitleStay> Stays()
    {
        var now = clock.GetUtcNow();
        return [.. roots.Ids()
            .Where(IsSettled)
            .Select(id => Describe(id, now))
            .OrderBy(s => s.LeavesAt is null)
            .ThenBy(s => s.LeavesAt)
            .ThenBy(s => s.TitleId, StringComparer.Ordinal)];
    }

    /// <summary>
    /// The ids of the settled films whose time is up: not kept and not held by any room. A film a
    /// room holds is never due, whether or not anybody is in the room this second.
    /// </summary>
    public IReadOnlyList<string> Due()
    {
        var now = clock.GetUtcNow();
        var held = sessions.Summaries().Select(r => r.TitleId).OfType<string>().ToHashSet(StringComparer.Ordinal);

        return [.. roots.Ids()
            .Where(IsSettled)
            .Where(id => !held.Contains(id))
            .Where(id => Describe(id, now) is { KeeperId: null, LeavesAt: { } leaves } && leaves <= now)];
    }

    /// <summary>Whether the film is on the cold disk alone: moved, and not being made again.</summary>
    public bool IsSettled(string id)
    {
        if (!TitleLibrary.IsSafeId(id) || roots.Cold is null || !roots.ColdReady) return false;

        return Directory.Exists(Path.Combine(roots.Cold, id))
               && !Directory.Exists(Path.Combine(roots.Hot, id));
    }

    private TitleStay Describe(string id, DateTimeOffset now)
    {
        var keep = keeps.Get(id);
        var played = history.LastPlayedAt(id);

        var stay = new TitleStay
        {
            TitleId = id,
            Settled = IsSettled(id),
            KeeperId = keep?.KeeperId,
            KeeperName = keep is null ? null : names(keep.KeeperId) ?? keep.KeeperId,
            KeptAt = keep?.KeptAt,
            LastPlayedAt = played,
        };

        if (!stay.Settled || keep is not null) return stay;

        var since = Latest(Latest(played, SettledAt(id)), history.RecordingSince);
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
