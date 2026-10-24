using TheKrystalShip.MovieBot.Acquire.Tmdb;

namespace TheKrystalShip.MovieBot.Api.Films;

/// <summary>
/// Cinema's one genre vocabulary across films and shows. The film database keeps a list for each kind,
/// and its shows' list joins genres its films' list keeps apart (<c>Sci-Fi &amp; Fantasy</c>) or names
/// one otherwise (<c>War &amp; Politics</c>); here each of those is the films' genres it joins, so a
/// genre is picked once and answers films and shows that fit it. Every other genre is the database's
/// own name, and a genre only one kind has (<c>Horror</c>, <c>Kids</c>) is one only that kind holds.
/// The site's <c>web/site/src/facets.js</c> carries the same table, and a test holds the two equal.
/// </summary>
public static class CinemaGenres
{
    /// <summary>The database's genres that are other genres here, by the names they are here.</summary>
    public static readonly IReadOnlyDictionary<string, string[]> Joined = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
    {
        ["Action & Adventure"] = ["Action", "Adventure"],
        ["Sci-Fi & Fantasy"] = ["Science Fiction", "Fantasy"],
        ["War & Politics"] = ["War"],
    };

    /// <summary>The genres a database genre is here: the ones it joins, else its own name.</summary>
    public static IReadOnlyList<string> Of(string name) => Joined.TryGetValue(name.Trim(), out var joined) ? joined : [name.Trim()];

    /// <summary>The genres here of a title's database genres, each once, in the order they come.</summary>
    public static List<string> Of(IEnumerable<string> names) => [.. names.SelectMany(Of).Distinct(StringComparer.OrdinalIgnoreCase)];

    /// <summary>
    /// The database genre of one kind's list that a genre here is: the one called so, else the one that
    /// joins it (<c>Science Fiction</c> is a show's <c>Sci-Fi &amp; Fantasy</c>); null when the kind has none.
    /// </summary>
    public static CatalogueGenre? In(IReadOnlyList<CatalogueGenre> genres, string name)
    {
        var wanted = name.Trim();
        return genres.FirstOrDefault(g => string.Equals(g.Name, wanted, StringComparison.OrdinalIgnoreCase))
               ?? genres.FirstOrDefault(g => Of(g.Name).Contains(wanted, StringComparer.OrdinalIgnoreCase));
    }
}
