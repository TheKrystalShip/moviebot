using System.Globalization;
using System.Text.RegularExpressions;

namespace TheKrystalShip.MovieBot.Catalogue;

/// <summary>
/// The one list query: every list of titles is these filters over the catalogue's entries, combined
/// (authority: <c>docs/api-contract.md</c>, Catalogue). A list filter holds several values; how they
/// combine is each filter's (all of for <see cref="Genres"/>, any of for <see cref="AnyGenres"/>).
/// </summary>
public sealed partial record TitleQuery
{
    public const int PageSize = 20;
    public const int MostPerPage = 100;

    public string? Words { get; init; }
    public string? Kind { get; init; }
    public string? Category { get; init; }
    public List<string> Genres { get; init; } = [];
    public List<string> AnyGenres { get; init; } = [];
    public List<string> NotGenres { get; init; } = [];
    public int? From { get; init; }
    public int? To { get; init; }
    public List<int> Decades { get; init; } = [];
    public string? Released { get; init; }
    public string? ReleasedBefore { get; init; }
    public List<string> Lengths { get; init; } = [];
    public int? MinRuntime { get; init; }
    public int? MaxRuntime { get; init; }
    public List<string> Languages { get; init; } = [];
    public List<string> Countries { get; init; } = [];
    public List<long> Keywords { get; init; } = [];
    public List<long> Companies { get; init; } = [];
    public List<long> People { get; init; } = [];
    public string Role { get; init; } = "any";
    public double? MinRating { get; init; }
    public int? MinVotes { get; init; }
    public List<(string Property, string Item)> Facts { get; init; } = [];
    public string? Franchise { get; init; }
    public string? Ranking { get; init; }
    public string? SimilarTo { get; init; }
    public string? Sort { get; init; }
    public int Page { get; init; } = 1;
    public int Size { get; init; } = PageSize;

    /// <summary>The asking person's: only these IMDb ids, or none of these (the API's joins).</summary>
    public IReadOnlyCollection<string>? Only { get; init; }
    public IReadOnlyCollection<string>? Except { get; init; }

    public static readonly string[] Kinds = ["film", "show"];
    public static readonly string[] CategoryNames = ["general", "anime"];
    public static readonly string[] LengthNames = ["short", "standard", "long"];
    public static readonly string[] RoleNames = ["any", "cast", "directing", "writing"];
    public static readonly string[] Sorts = ["popular", "rating", "newest", "oldest", "title", "votes", "order"];

    /// <summary>The query parameters this reads, so a caller can tell its own from an unknown one.</summary>
    public static readonly string[] Parameters =
    [
        "q", "kind", "category", "genre", "anyGenre", "notGenre", "from", "to", "decade", "released", "releasedBefore",
        "length", "minRuntime", "maxRuntime", "language", "country", "keyword", "company", "person", "role",
        "minRating", "minVotes", "fact", "franchise", "ranking", "similarTo", "sort", "page", "size",
    ];

    [GeneratedRegex("^P[0-9]{1,6}(/P[0-9]{1,6})?:Q[0-9]{1,12}$", RegexOptions.CultureInvariant)]
    private static partial Regex FactPattern();

    [GeneratedRegex("^(c[0-9]{1,9}|Q[0-9]{1,12})$", RegexOptions.CultureInvariant)]
    private static partial Regex FranchisePattern();

    [GeneratedRegex("^tt[0-9]{1,10}$", RegexOptions.CultureInvariant)]
    private static partial Regex ImdbPattern();

    [GeneratedRegex("^[0-9]{4}-[0-9]{2}-[0-9]{2}$", RegexOptions.CultureInvariant)]
    private static partial Regex DatePattern();

    [GeneratedRegex("^[a-z]{2,3}$", RegexOptions.CultureInvariant)]
    private static partial Regex LanguagePattern();

    [GeneratedRegex("^[A-Z]{2}$", RegexOptions.CultureInvariant)]
    private static partial Regex CountryPattern();

    [GeneratedRegex("^[a-z0-9-]{1,40}$", RegexOptions.CultureInvariant)]
    private static partial Regex RankingPattern();

    public static bool IsImdbId(string? id) => id is not null && ImdbPattern().IsMatch(id);

    /// <summary>
    /// Reads a query from request parameters, each parameter's values as given (a list parameter may be
    /// repeated or comma-separated). A value that is not one the filter takes is refused, never dropped,
    /// since a list that ignores a filter answers a question nobody asked.
    /// </summary>
    public static (TitleQuery? Query, string? Refusal) Parse(Func<string, IReadOnlyList<string>> values)
    {
        string? error = null;
        List<string> List(string name) => values(name)
            .SelectMany(v => v.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .Distinct(StringComparer.Ordinal)
            .ToList();
        string? One(string name) => values(name) is { Count: > 0 } v && !string.IsNullOrWhiteSpace(v[^1]) ? v[^1].Trim() : null;
        void Refuse(string message) => error ??= message;
        string? Choice(string name, string[] allowed)
        {
            var value = One(name);
            if (value is not null && !allowed.Contains(value)) Refuse($"{name} is one of {string.Join(", ", allowed)}.");
            return value;
        }
        int? Int(string name, int min, int max)
        {
            var value = One(name);
            if (value is null) return null;
            if (int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var n) && n >= min && n <= max) return n;
            Refuse($"{name} is a whole number from {min} to {max}.");
            return null;
        }
        List<long> Ids(string name, char prefix)
        {
            var ids = new List<long>();
            foreach (var value in List(name))
                if (value.Length > 1 && value[0] == prefix && long.TryParse(value.AsSpan(1), NumberStyles.None, CultureInfo.InvariantCulture, out var id))
                    ids.Add(id);
                else
                    Refuse($"{name} takes ids of the form {prefix}123.");
            return ids;
        }
        List<string> Matching(string name, Regex pattern, string what)
        {
            var list = List(name);
            foreach (var value in list)
                if (!pattern.IsMatch(value)) Refuse($"{name} takes {what}.");
            return list;
        }

        var lengths = List("length");
        if (lengths.Any(l => !LengthNames.Contains(l))) Refuse($"length is one of {string.Join(", ", LengthNames)}.");
        var decades = new List<int>();
        foreach (var value in List("decade"))
            if (int.TryParse(value.TrimEnd('s'), NumberStyles.None, CultureInfo.InvariantCulture, out var decade) && decade % 10 == 0 && decade is >= 1870 and <= 2100)
                decades.Add(decade);
            else
                Refuse("decade is a decade, as 1990 or 1990s.");
        var facts = new List<(string, string)>();
        foreach (var value in Matching("fact", FactPattern(), "a property and an item, as P840:Q90"))
            if (value.Split(':') is [var property, var item]) facts.Add((property, item));
        double? minRating = null;
        if (One("minRating") is { } rating)
        {
            if (double.TryParse(rating, NumberStyles.Float, CultureInfo.InvariantCulture, out var r) && r is >= 0 and <= 10) minRating = r;
            else Refuse("minRating is a rating from 0 to 10.");
        }
        var franchise = One("franchise");
        if (franchise is not null && !FranchisePattern().IsMatch(franchise)) Refuse("franchise is a franchise id, as c9485 or Q642878.");
        var similar = One("similarTo");
        if (similar is not null && !IsImdbId(similar)) Refuse("similarTo is an IMDb id.");
        var ranking = One("ranking");
        if (ranking is not null && !RankingPattern().IsMatch(ranking)) Refuse("ranking is a ranking's name.");
        var released = One("released");
        var before = One("releasedBefore");
        if (released is not null && !DatePattern().IsMatch(released) || before is not null && !DatePattern().IsMatch(before))
            Refuse("released and releasedBefore are dates, as 2026-10-25.");

        var query = new TitleQuery
        {
            Words = One("q") is { Length: > 0 } words ? words[..Math.Min(words.Length, 200)] : null,
            Kind = Choice("kind", Kinds),
            Category = Choice("category", CategoryNames),
            Genres = List("genre"),
            AnyGenres = List("anyGenre"),
            NotGenres = List("notGenre"),
            From = Int("from", 1870, 2100),
            To = Int("to", 1870, 2100),
            Decades = decades,
            Released = released,
            ReleasedBefore = before,
            Lengths = lengths,
            MinRuntime = Int("minRuntime", 0, 1000),
            MaxRuntime = Int("maxRuntime", 0, 1000),
            Languages = Matching("language", LanguagePattern(), "ISO 639 language codes, as ja"),
            Countries = Matching("country", CountryPattern(), "ISO 3166 country codes, as JP"),
            Keywords = Ids("keyword", 'k'),
            Companies = Ids("company", 's'),
            People = List("person").Select(p => long.TryParse(p, NumberStyles.None, CultureInfo.InvariantCulture, out var id) ? id : -1).ToList(),
            Role = Choice("role", RoleNames) ?? "any",
            MinRating = minRating,
            MinVotes = Int("minVotes", 0, int.MaxValue),
            Facts = facts,
            Franchise = franchise,
            Ranking = ranking,
            SimilarTo = similar,
            Sort = Choice("sort", Sorts),
            Page = Int("page", 1, 10_000) ?? 1,
            Size = Int("size", 1, MostPerPage) ?? PageSize,
        };
        if (query.People.Contains(-1)) Refuse("person takes person ids.");
        if (query.Sort == "order" && query.Ranking is null && query.Franchise is null && query.SimilarTo is null)
            Refuse("sort=order is a ranking's, a franchise's or the similar titles' own order, and the query names none.");
        return error is null ? (query, null) : (null, error);
    }

    /// <summary>The order a list is in when none is asked for: its own where it has one, the closest match for words, the most popular otherwise.</summary>
    public string Order => Sort ?? (Ranking is not null || Franchise is not null || SimilarTo is not null ? "order" : Words is not null ? "match" : "popular");
}
