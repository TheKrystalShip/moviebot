using TheKrystalShip.MovieBot.Api.Library;
using TheKrystalShip.MovieBot.Api.People;
using TheKrystalShip.MovieBot.Api.Wants;
using TheKrystalShip.MovieBot.Catalogue;
using TheKrystalShip.MovieBot.Core;

namespace TheKrystalShip.MovieBot.Api.Catalogue;

/// <summary>
/// What the asking person has to do with the catalogue's titles: what the library holds, as last listed,
/// what they watched through, and what they want. Joined when a card is served, never kept with it.
/// </summary>
public sealed class CatalogueViewers(LibraryListing library, WatchRecord record, WantStore wants)
{
    /// <summary>The joins for one person; <paramref name="userId"/> is null for the service key, which names nobody.</summary>
    public ViewerJoin For(string? userId)
    {
        var titles = library.Titles();
        var shelf = LibraryShelf.Of(titles, record, userId);
        var wanted = userId is null
            ? new HashSet<string>(StringComparer.Ordinal)
            : wants.For(userId).Select(w => w.ImdbId).ToHashSet(StringComparer.Ordinal);
        return new ViewerJoin(shelf, wanted, titles, record);
    }
}

/// <summary>One person's joins: each card's <see cref="Viewer"/>, and the lists only the API can make.</summary>
public sealed class ViewerJoin(LibraryShelf shelf, HashSet<string> wanted, IReadOnlyList<TitleSummary> titles, WatchRecord record)
{
    /// <summary>How far back what the people here watched is read for what is popular here.</summary>
    public static readonly TimeSpan PopularHereWindow = TimeSpan.FromDays(30);

    public LibraryShelf Shelf => shelf;

    public Viewer Of(string kind, string imdbId) =>
        new(shelf.Holds(kind, imdbId), shelf.LibraryIdOf(kind, imdbId), shelf.Saw(kind, imdbId), wanted.Contains(imdbId));

    public TitleCard Join(TitleCard card) => card with { You = Of(card.Kind, card.Id) };

    public TitlePage Join(TitlePage page) => page with
    {
        You = Of(page.Kind, page.Id),
        Similar = page.Similar.Select(Join).ToList(),
        Follows = page.Follows.Select(Join).ToList(),
        FollowedBy = page.FollowedBy.Select(Join).ToList(),
    };

    public CardPage Join(CardPage page) => page with { Titles = page.Titles.Select(Join).ToList() };

    /// <summary>Every title the library holds an episode or the film of, by IMDb id.</summary>
    public HashSet<string> Held() => [.. shelf.Films.Keys, .. shelf.Shows];

    /// <summary>The films the person watched through.</summary>
    public HashSet<string> Watched() => shelf.Watched;

    /// <summary>The titles the people here watched in the last <see cref="PopularHereWindow"/>, the most watched first.</summary>
    public List<string> PopularHere(DateTimeOffset now)
    {
        var imdbOf = titles.Where(t => t.Film?.ImdbId is not null).ToDictionary(t => t.Id, t => t.Film!.ImdbId!, StringComparer.Ordinal);
        return record.WatchedSince(now - PopularHereWindow)
            .Where(w => imdbOf.ContainsKey(w.TitleId))
            .GroupBy(w => imdbOf[w.TitleId])
            .OrderByDescending(g => g.Select(w => w.UserId).Distinct().Count())
            .ThenByDescending(g => g.Max(w => w.At))
            .Select(g => g.Key)
            .ToList();
    }

    /// <summary>What the library holds ready to play, the newest added first: each film once, each show once.</summary>
    public List<string> ReadyHere() => titles
        .Where(t => t.Status == TitleStatus.Ready && t.Film?.ImdbId is not null)
        .OrderByDescending(t => t.AddedAt)
        .Select(t => t.Film!.ImdbId!)
        .Distinct(StringComparer.Ordinal)
        .ToList();

    /// <summary>The titles the person last watched through, newest first: what the shelves of their likes are made from.</summary>
    public List<(string Kind, string ImdbId, string Name)> LastWatched(string? userId, int most)
    {
        if (userId is null) return [];
        var byId = titles.ToDictionary(t => t.Id, StringComparer.Ordinal);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var last = new List<(string, string, string)>();
        foreach (var (titleId, entry) in record.Entries(userId))
        {
            if (last.Count == most) break;
            if (!entry.Finished || !byId.TryGetValue(titleId, out var title) || title.Film?.ImdbId is not { } imdb || !seen.Add(imdb)) continue;
            last.Add((title.Kind == TitleKind.Show ? "show" : "film", imdb, title.Film.Name ?? title.Title));
        }
        return last;
    }
}
