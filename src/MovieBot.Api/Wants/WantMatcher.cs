using TheKrystalShip.MovieBot.Api.Catalogue;
using TheKrystalShip.MovieBot.Api.Library;
using TheKrystalShip.MovieBot.Api.People;
using TheKrystalShip.MovieBot.Api.Shows;
using TheKrystalShip.MovieBot.Core;

namespace TheKrystalShip.MovieBot.Api.Wants;

/// <summary>A title a person wants that others visibly want too, and whether the library holds it.</summary>
public sealed record WantMatch(StoredWant Mine, List<StoredWant> With, bool Ready);

/// <summary>
/// Finds the titles a person could watch with somebody, computed when asked and never stored. Only
/// visible wants of other people are ever matched. A film matches whoever else wants it; a show
/// matches those whose next unwatched episode is the same, since two people part-way through
/// different seasons have nothing to watch together.
/// </summary>
public sealed class WantMatcher(WantStore wants, LibraryListing library, WatchRecord record, CatalogueTitles shows)
{
    /// <summary>The person's matches, ready ones first and then the newest want. A film they have watched is not one.</summary>
    public List<WantMatch> Of(string userId)
    {
        var mine = wants.For(userId);
        var others = wants.OthersOnMine(userId);
        if (mine.Count == 0 || others.Count == 0) return [];

        var titles = library.Titles();
        var shelf = LibraryShelf.Of(titles, record, userId);
        var finished = new Dictionary<string, Dictionary<string, HashSet<(int, int)>>>(StringComparer.Ordinal);

        (int, int)? Next(StoredWant w)
        {
            if (!finished.TryGetValue(w.UserId, out var byShow))
                finished[w.UserId] = byShow = ShowProgress.Finished(titles, record, w.UserId);
            return ShowProgress.Next(shows.Show(w.ImdbId), byShow.GetValueOrDefault(w.ImdbId)) is { } e ? (e.Season, e.Episode) : null;
        }

        return [.. Match(mine.Where(w => w.Kind == WantKinds.Show || !shelf.Watched.Contains(w.ImdbId)).ToList(), others, Next)
            .Select(m => new WantMatch(m.Mine, m.With, m.Mine.Kind == WantKinds.Show
                ? shelf.Shows.Contains(m.Mine.ImdbId)
                : shelf.Films.ContainsKey(m.Mine.ImdbId)))
            .OrderByDescending(m => m.Ready)
            .ThenByDescending(m => m.Mine.Since)
            .ThenBy(m => m.Mine.ImdbId, StringComparer.Ordinal)];
    }

    /// <summary>
    /// The matching rule: for each of the person's wants, the others' wants on the same title that are
    /// at the same point. <paramref name="next"/> is a person's next unwatched episode of a show, null
    /// when there is none to name, and a show with none for the person matches nobody.
    /// </summary>
    public static List<(StoredWant Mine, List<StoredWant> With)> Match(
        IReadOnlyList<StoredWant> mine, IReadOnlyList<StoredWant> others, Func<StoredWant, (int Season, int Episode)?> next)
    {
        var matches = new List<(StoredWant, List<StoredWant>)>();
        foreach (var want in mine)
        {
            var same = others.Where(o => o.Visible && o.UserId != want.UserId
                && string.Equals(o.ImdbId, want.ImdbId, StringComparison.Ordinal)).ToList();
            if (want.Kind == WantKinds.Show)
            {
                if (next(want) is not { } at) continue;
                same = [.. same.Where(o => next(o) == at)];
            }
            if (same.Count > 0) matches.Add((want, same));
        }
        return matches;
    }
}
