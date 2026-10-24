using TheKrystalShip.MovieBot.Acquire.Tmdb;

namespace TheKrystalShip.MovieBot.Api.Films;

/// <summary>
/// Orders the titles of a question asked by rating on IMDb's votes, which are deep enough at any
/// breadth for the order to mean something where the film database's, narrowed to a year and a
/// genre, are not.
///
/// The vote floor scales with how broad the question is. It is the votes of the
/// <see cref="Breadth"/>th most voted IMDb title the question names, kept between
/// <see cref="Narrowest"/> and <see cref="Broadest"/>: a question about one year and one genre has few
/// titles, so its hundredth sits low, while "the best films of all time" has thousands above the
/// ceiling, which is the vote count IMDb's own top list asks. A question whose filters IMDb cannot
/// express takes the floor from the least voted of the titles the film database listed for it.
///
/// A title at or above the floor is ordered by its rating weighted by its votes toward the average
/// the titles above the floor were voted, with the floor as the weight of that average
/// (<c>v / (v + m) * R + m / (v + m) * C</c>), so a title just over the floor does not outrank one
/// everybody has seen on a tenth of a point. A title without a rating from IMDb, or under the floor,
/// follows them in the order of the film database's own weighted rating.
/// </summary>
public static class ImdbRanking
{
    /// <summary>How many of a question's most voted titles the floor is read from.</summary>
    public const int Breadth = 100;

    /// <summary>The fewest votes a floor is, so a rating built on a handful of voters never ranks.</summary>
    public const int Narrowest = 500;

    /// <summary>The most votes a floor is.</summary>
    public const int Broadest = 25_000;

    /// <summary>The most titles IMDb adds to what the film database listed.</summary>
    public const int Widened = 40;

    /// <summary>A title the film database listed, with what IMDb says of it where it says anything.</summary>
    public readonly record struct Entry(ListedTitle Title, ImdbRatings.Rating? Imdb);

    /// <summary>The floor for a question whose most voted titles have these votes, the most voted first.</summary>
    public static int FloorFor(IReadOnlyList<int> mostVoted) =>
        mostVoted.Count == 0 ? Narrowest : Math.Clamp(mostVoted[Math.Min(Breadth, mostVoted.Count) - 1], Narrowest, Broadest);

    /// <summary>The titles best first: those IMDb rated with at least <paramref name="floor"/> votes, then the rest.</summary>
    public static List<ListedTitle> Order(IReadOnlyList<Entry> entries, int floor)
    {
        var above = entries.Where(e => e.Imdb is { } r && r.Votes >= floor).ToList();
        if (above.Count == 0) return [.. WeightedRating.Order([.. entries.Select(e => e.Title)])];

        var mean = above.Sum(e => e.Imdb!.Value.Value * e.Imdb.Value.Votes) / above.Sum(e => (double)e.Imdb!.Value.Votes);
        double Score(Entry e)
        {
            var (rating, votes) = (e.Imdb!.Value.Value, (double)e.Imdb.Value.Votes);
            return votes / (votes + floor) * rating + floor / (votes + floor) * mean;
        }

        var rest = entries.Except(above).Select(e => e.Title).ToList();
        return
        [
            .. above.OrderByDescending(Score).ThenByDescending(e => e.Imdb!.Value.Votes).Select(e => e.Title),
            .. WeightedRating.Order(rest),
        ];
    }
}

/// <summary>
/// The film database's genre names and the IMDb genre names that stand for them, for the genres the two
/// agree on. A genre with no entry here, such as a film database's TV Movie or a soap, has no IMDb
/// counterpart, and a question that names one is not widened with IMDb's candidates.
/// </summary>
public static class ImdbGenres
{
    private static readonly Dictionary<string, string[]> Names = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Action"] = ["Action"],
        ["Adventure"] = ["Adventure"],
        ["Animation"] = ["Animation"],
        ["Comedy"] = ["Comedy"],
        ["Crime"] = ["Crime"],
        ["Documentary"] = ["Documentary"],
        ["Drama"] = ["Drama"],
        ["Family"] = ["Family"],
        ["Fantasy"] = ["Fantasy"],
        ["History"] = ["History"],
        ["Horror"] = ["Horror"],
        ["Music"] = ["Music", "Musical"],
        ["Mystery"] = ["Mystery"],
        ["Romance"] = ["Romance"],
        ["Science Fiction"] = ["Sci-Fi"],
        ["Thriller"] = ["Thriller"],
        ["War"] = ["War"],
        ["Western"] = ["Western"],

        // The names the film database gives its shows.
        ["Action & Adventure"] = ["Action", "Adventure"],
        ["Sci-Fi & Fantasy"] = ["Sci-Fi", "Fantasy"],
        ["War & Politics"] = ["War"],
        ["Kids"] = ["Family"],
        ["Reality"] = ["Reality-TV"],
        ["Talk"] = ["Talk-Show"],
        ["News"] = ["News"],
    };

    /// <summary>The genres IMDb files talk, news and reality shows under, which a show question that names no genre leaves out.</summary>
    public static readonly IReadOnlyList<string> Unscripted = ["Reality-TV", "Talk-Show", "News"];

    /// <summary>The IMDb names a film database genre stands for, or null when IMDb has no such genre.</summary>
    public static IReadOnlyList<string>? Of(string name) => Names.GetValueOrDefault(name.Trim());
}
