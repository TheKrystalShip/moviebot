using System.Globalization;
using TheKrystalShip.MovieBot.Api.Library;

namespace TheKrystalShip.MovieBot.Api.Films;

/// <summary>
/// What a filter reads off a title the API holds: its kind, whether it is anime, its genres in Cinema's
/// vocabulary (<see cref="CinemaGenres"/>), the year it came out, a film's runtime, its rating and the
/// language it was made in. A trait the answer does not carry is null, and the title is in none of
/// that section's options.
/// </summary>
public sealed record FacetTraits(string Kind, bool Anime, IReadOnlyList<string> Genres, int? Year, int? RuntimeMinutes, double? Rating, string? Language);

/// <summary>
/// A filter over an answer the API holds whole: a shelf's titles, a set Wikidata lists, a person's or
/// a genre's titles. A section is answered as any of its picks and the sections as all of them. A
/// section's options are the values the whole answer holds, whatever is picked, so they stay the same
/// while the picks change; each is counted with every other section's picks applied and without its
/// own, so the counts are exact, and one no title holds at those picks is empty. A section no title of
/// the answer carries the trait of is not measured and not reported.
/// </summary>
public static class WholeFacets
{
    /// <summary>
    /// Why the picks cannot narrow an answer of this kind, or null when they can: a kind or a category
    /// that is none, a decade or a length that is none, or a length on titles that carry no runtime.
    /// </summary>
    public static string? Refusal(DiscoverPicks picks, DateOnly today)
    {
        if (picks.Kind is not null and not ("film" or "show" or CatalogueSearch.AllKinds)) return "Kind is film, show or all.";
        if (picks.Category is not null && TitleCategories.Parse(picks.Category) != TitleCategory.Anime) return "A filter's category is anime.";
        foreach (var decade in picks.Decades)
            if (!Decades.IsDecade(decade, today))
                return $"{decade} is no decade: a decade is named by its first year, from {Decades.Earliest} to this one.";
        if (picks.Lengths.FirstOrDefault(l => FilmLengths.Of(l) is null) is { } length)
            return $"\"{length}\" is no length; a length is {string.Join(", ", FilmLengths.All)}.";
        return null;
    }

    /// <summary>The titles that answer every section's picks, in the order given.</summary>
    public static List<T> Filter<T>(IEnumerable<T> titles, Func<T, FacetTraits> traits, DiscoverPicks picks) =>
        [.. titles.Where(t => Answers(traits(t), picks, null))];

    /// <summary>
    /// Each section's options and how many titles hold each, in the API's order for every facet. The
    /// options are the values <paramref name="offeredBy"/> holds (by default the titles counted), so a
    /// page that counts a part of what it lists still offers what the whole holds. A section in
    /// <paramref name="fixedBy"/> is the answer's own and is not reported.
    /// </summary>
    public static CatalogueFacetsView Facets(IReadOnlyList<FacetTraits> titles, DiscoverPicks picks, IReadOnlySet<string> fixedBy, Func<string, string?> languageName,
        IReadOnlyList<FacetTraits>? offeredBy = null)
    {
        var whole = offeredBy ?? titles;
        var sections = new List<FacetSectionView>();
        void Section(string id, IEnumerable<FacetOptionView> offered, Func<FacetTraits, bool> carries)
        {
            if (fixedBy.Contains(id) || !whole.Any(carries)) return;
            var counted = titles.Where(t => Answers(t, picks, id)).ToList();
            var options = new List<FacetOptionView>();
            foreach (var option in offered.Where(o => whole.Any(t => Holds(t, id, o.Value))))
                options.Add(counted.Count(t => Holds(t, id, option.Value)) is var count and > 0 ? option with { Count = count } : option with { Count = 0, Empty = true });
            sections.Add(new FacetSectionView(id, options));
        }

        Section(FacetSections.Kind, [new(FacetSections.Film), new(FacetSections.Show), new(FacetSections.Anime)], _ => true);
        Section(FacetSections.Genre, whole.SelectMany(t => t.Genres).Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase).Select(g => new FacetOptionView(g)), t => t.Genres.Count > 0);
        Section(FacetSections.Decade, whole.Select(t => DecadeOf(t.Year)).OfType<int>().Distinct().OrderDescending()
            .Select(d => new FacetOptionView(d.ToString(CultureInfo.InvariantCulture))), t => t.Year is not null);
        Section(FacetSections.Length, FilmLengths.All.Select(l => new FacetOptionView(l)), t => t.RuntimeMinutes is not null);
        Section(FacetSections.Rating, FacetSections.Ratings.Select(r => new FacetOptionView(r.ToString(CultureInfo.InvariantCulture))), t => t.Rating is not null);
        Section(FacetSections.Language, whole.Select(t => t.Language).OfType<string>().Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.Ordinal).Select(l => new FacetOptionView(l, languageName(l))), t => t.Language is not null);
        return new CatalogueFacetsView { Sections = sections };
    }

    private static int? DecadeOf(int? year) => year is { } y ? y / 10 * 10 : null;

    private static string? LengthOf(int? minutes) => minutes switch
    {
        null or <= 0 => null,
        < 90 => FilmLengths.Short,
        <= 150 => FilmLengths.Standard,
        _ => FilmLengths.Long,
    };

    private static bool Answers(FacetTraits t, DiscoverPicks picks, string? except) =>
        (except == FacetSections.Kind || ((picks.Kind is null or CatalogueSearch.AllKinds || t.Kind == picks.Kind) && (picks.Category is null || t.Anime)))
        && (except == FacetSections.Genre || picks.AnyGenres.Count == 0 || CinemaGenres.Of(picks.AnyGenres).Any(g => Holds(t, FacetSections.Genre, g)))
        && (except == FacetSections.Decade || picks.Decades.Count == 0 || picks.Decades.Any(d => DecadeOf(t.Year) == d))
        && (except == FacetSections.Length || picks.Lengths.Count == 0 || picks.Lengths.Contains(LengthOf(t.RuntimeMinutes) ?? ""))
        && (except == FacetSections.Rating || picks.MinRating is not { } floor || t.Rating >= floor)
        && (except == FacetSections.Language || picks.Languages.Count == 0 || picks.Languages.Contains(t.Language ?? "", StringComparer.OrdinalIgnoreCase));

    private static bool Holds(FacetTraits t, string section, string value) => section switch
    {
        FacetSections.Kind => value == FacetSections.Anime ? t.Anime : t.Kind == value,
        FacetSections.Genre => t.Genres.Contains(value, StringComparer.OrdinalIgnoreCase),
        FacetSections.Decade => DecadeOf(t.Year)?.ToString(CultureInfo.InvariantCulture) == value,
        FacetSections.Length => LengthOf(t.RuntimeMinutes) == value,
        FacetSections.Rating => t.Rating >= double.Parse(value, CultureInfo.InvariantCulture),
        FacetSections.Language => string.Equals(t.Language, value, StringComparison.OrdinalIgnoreCase),
        _ => false,
    };
}
