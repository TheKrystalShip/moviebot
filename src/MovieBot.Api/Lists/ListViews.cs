using TheKrystalShip.MovieBot.Api.Films;
using TheKrystalShip.MovieBot.Api.Library;
using TheKrystalShip.MovieBot.Api.People;
using TheKrystalShip.MovieBot.Api.Shows;
using TheKrystalShip.MovieBot.Core;

namespace TheKrystalShip.MovieBot.Api.Lists;

/// <summary>Somebody as a list names them: their id, and what they are called when the service has heard.</summary>
public sealed record ListPerson(string UserId, string? Name);

public sealed record ListMemberView(string UserId, string? Name, string Role, DateTimeOffset Since);

/// <summary>A list as the person's lists show it, without its entries.</summary>
public sealed record ListSummary
{
    /// <summary>What the list's route takes: <c>later</c> for the asking person's own Watch later.</summary>
    public required string Id { get; init; }
    public required string Name { get; init; }
    public bool WatchLater { get; init; }

    /// <summary>What the asking person may do with it, one of <see cref="ListRoles"/>.</summary>
    public required string Role { get; init; }
    public required ListPerson Owner { get; init; }
    public required List<ListMemberView> Members { get; init; }
    public int Count { get; init; }
    public required DateTimeOffset UpdatedAt { get; init; }

    /// <summary>The posters of the most recently added entries that have one, newest first, at most four.</summary>
    public required List<string> Posters { get; init; }

    /// <summary>Whether the list holds the title the listing was asked about; absent when it was asked about none.</summary>
    public bool? Holds { get; init; }
}

/// <summary>One title on a list, with what the film database and the library say about it.</summary>
public sealed record ListEntryView
{
    public required string ImdbId { get; init; }

    /// <summary><c>film</c> or <c>show</c>.</summary>
    public required string Kind { get; init; }

    /// <summary>The title's name, or its IMDb id while the film database has not described it.</summary>
    public required string Title { get; init; }
    public int? Year { get; init; }
    public string? PosterUrl { get; init; }
    public double? Rating { get; init; }
    public int? RuntimeMinutes { get; init; }
    public required List<string> Genres { get; init; }

    public required DateTimeOffset AddedAt { get; init; }
    public required ListPerson AddedBy { get; init; }
    public string? Note { get; init; }

    /// <summary>For a film the library holds, the title that plays it.</summary>
    public string? LibraryId { get; init; }

    /// <summary>Whether the library holds the film, or any episode of the show.</summary>
    public bool InLibrary { get; init; }

    /// <summary>Whether the asking person watched the film through; never set for a show.</summary>
    public bool Watched { get; init; }
}

public sealed record ListView
{
    public required ListSummary List { get; init; }
    public required List<ListEntryView> Entries { get; init; }
}

public sealed record ListNameRequest
{
    public string? Name { get; init; }
}

/// <summary>Save a title: what kind it is, and a note, which an absent field leaves as it was and an empty one clears.</summary>
public sealed record SaveEntryRequest
{
    public string? Kind { get; init; }
    public string? Note { get; init; }
}

public sealed record ListMemberRequest
{
    public string? Role { get; init; }
}

/// <summary>
/// Turns a stored list into what a page shows, reading only what is kept: the film and show
/// descriptions, the library as last listed, the watch record and the names the service has heard.
/// </summary>
public sealed class ListPresenter(
    FilmStore films,
    ShowStore shows,
    LibraryListing library,
    WatchRecord record,
    PersonDirectory people)
{
    public ListSummary Summary(StoredList list, string viewerId, string? holding = null) => new()
    {
        Id = list.WatchLater ? ListStore.WatchLaterId : list.Id,
        Name = list.Name,
        WatchLater = list.WatchLater,
        Role = list.RoleOf(viewerId) ?? ListRoles.Viewer,
        Owner = Person(list.Owner),
        Members = [.. list.Members.Select(m => new ListMemberView(m.UserId, people.NameOf(m.UserId), m.Role, m.Since))],
        Count = list.Entries.Count,
        UpdatedAt = list.UpdatedAt,
        Posters = [.. list.Entries.OrderByDescending(e => e.AddedAt).Select(PosterOf).OfType<string>().Take(4)],
        Holds = holding is null ? null : list.Entries.Any(e => e.ImdbId == holding),
    };

    public ListView View(StoredList list, string viewerId)
    {
        var shelf = Shelf.Of(library.Titles(), record, viewerId);
        return new ListView
        {
            List = Summary(list, viewerId),
            Entries = [.. list.Entries.OrderByDescending(e => e.AddedAt).Select(e => Entry(e, shelf))],
        };
    }

    public ListEntryView Entry(StoredEntry entry, string viewerId) =>
        Entry(entry, Shelf.Of(library.Titles(), record, viewerId));

    private ListEntryView Entry(StoredEntry entry, Shelf shelf)
    {
        var view = new ListEntryView
        {
            ImdbId = entry.ImdbId, Kind = entry.Kind, Title = entry.ImdbId, Genres = [],
            AddedAt = entry.AddedAt, AddedBy = Person(entry.AddedBy), Note = entry.Note,
            PosterUrl = PosterOf(entry),
        };

        if (entry.Kind == EntryKinds.Show)
        {
            var details = shows.Held(entry.ImdbId)?.Details;
            return view with
            {
                Title = details?.Title ?? view.Title, Year = details?.FirstYear, Rating = details?.Rating,
                Genres = [.. details?.Genres ?? []],
                InLibrary = shelf.Shows.Contains(entry.ImdbId),
            };
        }

        var film = films.Held(entry.ImdbId)?.Details;
        var libraryId = shelf.Films.GetValueOrDefault(entry.ImdbId);
        return view with
        {
            Title = film?.Title ?? view.Title, Year = film?.Year, Rating = film?.Rating,
            RuntimeMinutes = film?.RuntimeMinutes, Genres = [.. film?.Genres ?? []],
            LibraryId = libraryId, InLibrary = libraryId is not null,
            Watched = shelf.Watched.Contains(entry.ImdbId),
        };
    }

    private string? PosterOf(StoredEntry entry) => entry.Kind == EntryKinds.Show
        ? shows.HasImage(entry.ImdbId, ShowStore.Poster) ? ShowViews.ImageUrl(entry.ImdbId, ShowStore.Poster) : null
        : films.HasImage(entry.ImdbId, FilmStore.Poster) ? FilmViews.ImageUrl(entry.ImdbId, FilmStore.Poster) : null;

    private ListPerson Person(string userId) => new(userId, people.NameOf(userId));

    /// <summary>
    /// What the library holds, by IMDb id: the title that plays each film, the shows with an episode
    /// here, and the films the viewer watched through.
    /// </summary>
    private sealed record Shelf(Dictionary<string, string> Films, HashSet<string> Shows, HashSet<string> Watched)
    {
        public static Shelf Of(IReadOnlyList<TitleSummary> titles, WatchRecord record, string viewerId)
        {
            var films = new Dictionary<string, string>(StringComparer.Ordinal);
            var shows = new HashSet<string>(StringComparer.Ordinal);
            var filmOf = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var title in titles)
            {
                if (title.Film?.ImdbId is not { } imdbId) continue;
                if (title.Kind == TitleKind.Show)
                {
                    shows.Add(imdbId);
                    continue;
                }
                films.TryAdd(imdbId, title.Id);
                filmOf[title.Id] = imdbId;
            }

            var watched = record.Entries(viewerId)
                .Where(e => e.Entry.Finished && filmOf.ContainsKey(e.TitleId))
                .Select(e => filmOf[e.TitleId])
                .ToHashSet(StringComparer.Ordinal);
            return new Shelf(films, shows, watched);
        }
    }
}
