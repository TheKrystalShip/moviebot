using TheKrystalShip.MovieBot.Acquire.Tmdb;

namespace TheKrystalShip.MovieBot.Api.Films;

/// <summary>The lengths a film is picked by, in minutes either side.</summary>
public static class FilmLengths
{
    public const string Short = "short";
    public const string Standard = "standard";
    public const string Long = "long";

    /// <summary>Every length, shortest first: each starts the minute after the one before ends.</summary>
    public static readonly IReadOnlyList<string> All = [Short, Standard, Long];

    /// <summary>The minutes a length spans, either end open; null for a word that is no length.</summary>
    public static (int? Min, int? Max)? Of(string name) => name switch
    {
        Short => (null, 89),
        Standard => (90, 150),
        Long => (151, null),
        _ => null,
    };
}

/// <summary>The decades a discovery is picked by, each named by its first year.</summary>
public static class Decades
{
    /// <summary>The first decade a discovery's facets are measured over.</summary>
    public const int First = 1920;

    /// <summary>The earliest decade a discovery takes.</summary>
    public const int Earliest = 1870;

    public static bool IsDecade(int year, DateOnly today) => year % 10 == 0 && year >= Earliest && year <= today.Year;

    /// <summary>The decades a facet is measured over, newest first: from this one back to <see cref="First"/>.</summary>
    public static IReadOnlyList<int> Offered(DateOnly today)
    {
        var decades = new List<int>();
        for (var decade = today.Year / 10 * 10; decade >= First; decade -= 10) decades.Add(decade);
        return decades;
    }
}

/// <summary>
/// The picks a discovery is narrowed by, in the database's terms: each is answered as any of its
/// values, and they combine as all of them. Null or empty is no pick.
/// </summary>
public sealed record Narrowing
{
    public IReadOnlyList<int> AnyGenreIds { get; init; } = [];

    /// <summary>The release windows, each end open; null for no pick.</summary>
    public IReadOnlyList<(DateOnly? From, DateOnly? To)>? Windows { get; init; }

    /// <summary>The runtimes in minutes, each end open; null for no pick.</summary>
    public IReadOnlyList<(int? Min, int? Max)>? Runtimes { get; init; }

    /// <summary>Original languages as ISO 639-1 codes.</summary>
    public IReadOnlyList<string> Languages { get; init; } = [];
}

/// <summary>
/// A discovery narrowed by picks the database cannot take in one question. It reads a comma as all
/// of a list and a bar as any of it, never both in one parameter, and it takes one release window
/// and one runtime window; so a pick of genres on a question that already names genres, two decades
/// apart or the shortest and the longest films are several questions, whose answers together are the
/// answer.
/// </summary>
public static class DiscoverNarrowing
{
    /// <summary>The most questions one discovery is asked as.</summary>
    public const int MostQuestions = 24;

    /// <summary>The questions whose answers together are <paramref name="query"/> narrowed by <paramref name="by"/>; none when nothing can match.</summary>
    public static List<DiscoverQuery> Narrow(DiscoverQuery query, Narrowing by)
    {
        IEnumerable<DiscoverQuery> asked = [query];
        asked = asked.SelectMany(q => ByGenres(q, by.AnyGenreIds));
        if (by.Windows is { } windows) asked = asked.SelectMany(q => ByWindows(q, windows));
        if (by.Runtimes is { } runtimes) asked = asked.SelectMany(q => ByRuntimes(q, runtimes));
        if (by.Languages.Count > 0) asked = asked.SelectMany(q => ByLanguages(q, by.Languages));
        return asked.ToList();
    }

    /// <summary>
    /// The decades as release windows, those next to each other joined into one; null for none.
    /// </summary>
    public static List<(DateOnly? From, DateOnly? To)>? Windows(IReadOnlyList<int> decades)
    {
        if (decades.Count == 0) return null;
        var windows = new List<(DateOnly? From, DateOnly? To)>();
        foreach (var decade in decades.Distinct().Order())
        {
            var from = new DateOnly(decade, 1, 1);
            var to = new DateOnly(decade + 9, 12, 31);
            if (windows.Count > 0 && windows[^1].To is { } end && end.AddDays(1) == from) windows[^1] = (windows[^1].From, to);
            else windows.Add((from, to));
        }
        return windows;
    }

    /// <summary>The lengths as runtime windows, those next to each other joined into one; null for none.</summary>
    public static List<(int? Min, int? Max)>? Runtimes(IReadOnlyList<string> lengths)
    {
        if (lengths.Count == 0) return null;
        var runtimes = new List<(int? Min, int? Max)>();
        foreach (var length in FilmLengths.All.Where(l => lengths.Contains(l)))
        {
            var span = FilmLengths.Of(length)!.Value;
            if (runtimes.Count > 0 && runtimes[^1].Max is { } end && span.Min == end + 1) runtimes[^1] = (runtimes[^1].Min, span.Max);
            else runtimes.Add(span);
        }
        return runtimes;
    }

    private static IEnumerable<DiscoverQuery> ByGenres(DiscoverQuery q, IReadOnlyList<int> picked)
    {
        var wanted = picked.Where(g => !q.WithoutGenreIds.Contains(g)).Distinct().ToList();
        if (picked.Count == 0) return [q];
        if (wanted.Count == 0) return [];

        // Every title of the question is filed under all its genres, so one of them picked is all of it.
        if (q.GenreIds.Count > 0)
            return wanted.Any(q.GenreIds.Contains) ? [q] : wanted.Select(g => q with { GenreIds = [.. q.GenreIds, g] });

        if (q.AnyGenreIds.Count > 0)
        {
            if (q.AnyGenreIds.All(wanted.Contains)) return [q];
            return wanted.SelectMany(g => q.AnyGenreIds.Contains(g)
                ? [q with { AnyGenreIds = [], GenreIds = [g] }]
                : q.AnyGenreIds.Where(a => !wanted.Contains(a)).Select(a => q with { AnyGenreIds = [], GenreIds = [a, g] }));
        }
        return [q with { AnyGenreIds = wanted }];
    }

    private static IEnumerable<DiscoverQuery> ByWindows(DiscoverQuery q, IReadOnlyList<(DateOnly? From, DateOnly? To)> windows)
    {
        foreach (var (from, to) in windows)
        {
            var start = Later(q.From, from);
            var end = Earlier(q.To, to);
            if (start is { } s && end is { } e && s > e) continue;
            yield return q with { From = start, To = end };
        }
    }

    private static IEnumerable<DiscoverQuery> ByRuntimes(DiscoverQuery q, IReadOnlyList<(int? Min, int? Max)> runtimes)
    {
        foreach (var (min, max) in runtimes)
        {
            var shortest = q.MinRuntime is { } a && min is { } b ? Math.Max(a, b) : q.MinRuntime ?? min;
            var longest = q.MaxRuntime is { } c && max is { } d ? Math.Min(c, d) : q.MaxRuntime ?? max;
            if (shortest > longest) continue;
            yield return q with { MinRuntime = shortest, MaxRuntime = longest };
        }
    }

    private static IEnumerable<DiscoverQuery> ByLanguages(DiscoverQuery q, IReadOnlyList<string> picked)
    {
        var wanted = picked.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (!string.IsNullOrWhiteSpace(q.Language))
            return wanted.Contains(q.Language.Trim(), StringComparer.OrdinalIgnoreCase) ? [q] : [];
        if (q.AnyLanguages.Count > 0)
        {
            var both = q.AnyLanguages.Where(l => wanted.Contains(l, StringComparer.OrdinalIgnoreCase)).ToList();
            return both.Count == 0 ? [] : [q with { AnyLanguages = both }];
        }
        return wanted.Count == 1 ? [q with { Language = wanted[0] }] : [q with { AnyLanguages = wanted }];
    }

    private static DateOnly? Later(DateOnly? a, DateOnly? b) => a is { } x && b is { } y ? (x > y ? x : y) : a ?? b;

    private static DateOnly? Earlier(DateOnly? a, DateOnly? b) => a is { } x && b is { } y ? (x < y ? x : y) : a ?? b;

    /// <summary>Whether a title the database listed answers a question, by what its list entry carries: everything but people, keywords, companies and runtime.</summary>
    public static bool Holds(ListedTitle t, DiscoverQuery q) =>
        t.Kind == q.Kind
        && q.GenreIds.All(id => t.GenreIds.Contains(id))
        && (q.AnyGenreIds.Count == 0 || q.AnyGenreIds.Any(id => t.GenreIds.Contains(id)))
        && q.WithoutGenreIds.All(id => !t.GenreIds.Contains(id))
        && (q.From is null || t.Released >= q.From)
        && (q.To is null || t.Released <= q.To)
        && (q.Language is null || string.Equals(t.OriginalLanguage, q.Language, StringComparison.OrdinalIgnoreCase))
        && (q.AnyLanguages.Count == 0 || q.AnyLanguages.Contains(t.OriginalLanguage ?? "", StringComparer.OrdinalIgnoreCase))
        && (q.MinRating is null || t.Rating >= q.MinRating)
        && (q.MinVotes is null || t.Votes >= q.MinVotes);
}
