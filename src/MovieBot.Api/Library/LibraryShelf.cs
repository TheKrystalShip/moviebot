using TheKrystalShip.MovieBot.Api.People;
using TheKrystalShip.MovieBot.Core;

namespace TheKrystalShip.MovieBot.Api.Library;

/// <summary>
/// What the library holds, by IMDb id: the title that plays each film, the shows with an episode
/// here, and the films the viewer watched through. What a list, a search or a discovery joins a film
/// the database named with, so each says whether the library has it and whether this person has seen it.
/// </summary>
public sealed record LibraryShelf(Dictionary<string, string> Films, HashSet<string> Shows, HashSet<string> Watched)
{
    /// <summary>
    /// The shelf as the library was last listed. <paramref name="viewerId"/> is null for the service
    /// key, which names nobody and so has watched nothing.
    /// </summary>
    public static LibraryShelf Of(IReadOnlyList<TitleSummary> titles, WatchRecord record, string? viewerId)
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

        var watched = viewerId is null
            ? new HashSet<string>(StringComparer.Ordinal)
            : record.Entries(viewerId)
                .Where(e => e.Entry.Finished && filmOf.ContainsKey(e.TitleId))
                .Select(e => filmOf[e.TitleId])
                .ToHashSet(StringComparer.Ordinal);
        return new LibraryShelf(films, shows, watched);
    }

    /// <summary>The library's title that plays the film, or null when it does not hold it. A show has none: it is a set of episodes.</summary>
    public string? LibraryIdOf(string kind, string? imdbId) =>
        kind == "film" && imdbId is not null ? Films.GetValueOrDefault(imdbId) : null;

    /// <summary>Whether the library holds the film, or any episode of the show.</summary>
    public bool Holds(string kind, string? imdbId) =>
        imdbId is not null && (kind == "film" ? Films.ContainsKey(imdbId) : Shows.Contains(imdbId));

    /// <summary>Whether the viewer watched the film through. Never true for a show.</summary>
    public bool Saw(string kind, string? imdbId) => kind == "film" && imdbId is not null && Watched.Contains(imdbId);
}
