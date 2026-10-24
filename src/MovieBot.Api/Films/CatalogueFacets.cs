using System.Globalization;
using TheKrystalShip.MovieBot.Acquire.Tmdb;
using TheKrystalShip.MovieBot.Api.Library;

namespace TheKrystalShip.MovieBot.Api.Films;

/// <summary>The sections a filter picks in, by the ids the facets name them with.</summary>
public static class FacetSections
{
    public const string Kind = "kind";
    public const string Genre = "genre";
    public const string Decade = "decade";
    public const string Length = "length";
    public const string Rating = "rating";
    public const string Language = "language";

    /// <summary>The kinds a filter offers: films, shows, and anime of either.</summary>
    public const string Film = "film";
    public const string Show = "show";
    public const string Anime = "anime";

    /// <summary>The rating floors a filter offers, highest first.</summary>
    public static readonly IReadOnlyList<int> Ratings = [8, 7, 6];
}

/// <summary>
/// The picks a filter adds to a question somebody asked: each is answered as any of its values,
/// and they combine as all of them. <see cref="Kind"/> and <see cref="Category"/> take the
/// question's own place when it names no kind.
/// </summary>
public sealed record DiscoverPicks
{
    public string? Kind { get; init; }
    public string? Category { get; init; }
    public IReadOnlyList<string> AnyGenres { get; init; } = [];
    public IReadOnlyList<string> Languages { get; init; } = [];
    public IReadOnlyList<int> Decades { get; init; } = [];
    public IReadOnlyList<string> Lengths { get; init; } = [];
    public double? MinRating { get; init; }

    public static readonly DiscoverPicks None = new();

    public bool Any => Kind is not null || Category is not null || AnyGenres.Count + Languages.Count + Decades.Count + Lengths.Count > 0 || MinRating is not null;

    /// <summary>The sections these picks are in.</summary>
    public IEnumerable<string> Sections()
    {
        if (Kind is not null || Category is not null) yield return FacetSections.Kind;
        if (AnyGenres.Count > 0) yield return FacetSections.Genre;
        if (Decades.Count > 0) yield return FacetSections.Decade;
        if (Lengths.Count > 0) yield return FacetSections.Length;
        if (MinRating is not null) yield return FacetSections.Rating;
        if (Languages.Count > 0) yield return FacetSections.Language;
    }

    /// <summary>A question's discovery with the picks added.</summary>
    public DiscoverRequest On(DiscoverRequest request) => request with
    {
        Kind = Kind ?? request.Kind,
        Category = Category ?? request.Category,
        AnyGenres = AnyGenres,
        Languages = Languages,
        Decades = Decades,
        Lengths = Lengths,
        MinRating = MinRating ?? request.MinRating,
    };
}

/// <summary>
/// One option a facet reports: its value as a pick names it, for a language its English name, and for
/// an answer the API holds whole how many of its titles hold it.
/// </summary>
public sealed record FacetOptionView(string Value, string? Label = null, int? Count = null);

/// <summary>A section and the options its answers hold, in the section's own order.</summary>
public sealed record FacetSectionView(string Id, List<FacetOptionView> Options);

/// <summary>What a facets route answers: the sections a filter can pick in, each with the options the answers hold.</summary>
public sealed record CatalogueFacetsView
{
    public required List<FacetSectionView> Sections { get; init; }
}

public sealed partial class CatalogueSearch
{
    /// <summary>The English name of a language by its code: the filter's own names, else the database's; null for a code neither knows.</summary>
    public async Task<Func<string, string?>> LanguageNamesAsync(CancellationToken ct)
    {
        var known = (await LanguagesAsync(ct)).Value ?? [];
        var names = known.GroupBy(l => l.Code, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.First().Name, StringComparer.OrdinalIgnoreCase);
        foreach (var language in DiscoverShelves.FilterLanguages) names[language.Code] = language.Name;
        return code => names.GetValueOrDefault(code);
    }

    /// <summary>What a filter reads off a title the film database listed, by its card and its listing; a listing carries no runtime.</summary>
    internal FacetTraits Traits(TitleHit hit)
    {
        var (listed, card) = (hit.Listed, hit.Card);
        var language = listed.OriginalLanguage ?? card?.OriginalLanguage;
        return new FacetTraits(listed.Kind, categories.Of(listed.Kind, card?.ImdbId, card?.Genres, language) == TitleCategory.Anime,
            [.. card?.Genres ?? []], (listed.Released ?? card?.Released)?.Year, null,
            scores.Serve(card?.ImdbId, listed.TmdbId, listed.Kind, listed.Rating, listed.Votes).Rating, language);
    }

    /// <summary>How many options are asked of the database at once.</summary>
    private const int ProbesAtOnce = 8;

    /// <summary>
    /// Which options each section's answers hold, for a discovery and the picks on it. A section's
    /// options are measured with every other section's picks applied and without its own, so a
    /// second option of the same section can still be offered. An option is reported when the
    /// database lists at least one title for the discovery with that option picked in its section:
    /// one question per option, its first page, which is also that answer's first page and is kept as
    /// that answer is. A section in <paramref name="fixedBy"/> is the question's own and is not
    /// reported, and neither is a section whose options could not all be measured.
    /// </summary>
    public async Task<Outcome<CatalogueFacetsView>> DiscoverFacetsAsync(DiscoverRequest request, IReadOnlySet<string> fixedBy, CancellationToken ct)
    {
        var planned = await PlanAsync(request with { Page = 1 }, ct);
        if (planned.Value is not { } plan) return planned.Carry<CatalogueFacetsView>();

        var anime = TitleCategories.Parse(request.Category) == TitleCategory.Anime;
        var kinds = plan.Kind == AllKinds ? (string[])["film", "show"] : [plan.Kind];
        var probes = new List<(string Section, FacetOptionView Option, DiscoverRequest Asked)>();
        void Offer(string section, FacetOptionView option, DiscoverRequest asked) => probes.Add((section, option, asked with { Page = 1 }));

        if (!fixedBy.Contains(FacetSections.Kind) && plan.Seed is null)
        {
            Offer(FacetSections.Kind, new(FacetSections.Film), request with { Kind = "film", Category = null });
            Offer(FacetSections.Kind, new(FacetSections.Show), request with { Kind = "show", Category = null });
            Offer(FacetSections.Kind, new(FacetSections.Anime), request with { Kind = AllKinds, Category = FacetSections.Anime });
        }

        if (!fixedBy.Contains(FacetSections.Genre))
        {
            var named = new List<string>();
            foreach (var kind in kinds)
            {
                var genres = await catalogue.GenresAsync(kind, ct);
                if (genres is null) return Unavailable<CatalogueFacetsView>("The film database could not be asked about its genres.");
                named.AddRange(genres
                    .Where(g => kind != "show" || !DiscoverShelves.UnscriptedShowGenres.Contains(g.Id))
                    .Select(g => g.Name)
                    .Where(n => !named.Contains(n, StringComparer.OrdinalIgnoreCase)
                        && !plan.Resolved.Genres.Contains(n, StringComparer.OrdinalIgnoreCase)
                        && !(anime && string.Equals(n, TitleCategories.AnimeGenre, StringComparison.OrdinalIgnoreCase))));
            }
            foreach (var name in named.Order(StringComparer.OrdinalIgnoreCase)) Offer(FacetSections.Genre, new(name), request with { AnyGenres = [name] });
        }

        if (!fixedBy.Contains(FacetSections.Decade))
            foreach (var decade in Decades.Offered(Today()))
                Offer(FacetSections.Decade, new(decade.ToString(CultureInfo.InvariantCulture)), request with { Decades = [decade] });

        if (!fixedBy.Contains(FacetSections.Length) && kinds.Contains("film") && plan.Seed is null && request.MinRuntime is null && request.MaxRuntime is null)
            foreach (var length in FilmLengths.All) Offer(FacetSections.Length, new(length), request with { Lengths = [length] });

        if (!fixedBy.Contains(FacetSections.Rating))
            foreach (var floor in FacetSections.Ratings)
                Offer(FacetSections.Rating, new(floor.ToString(CultureInfo.InvariantCulture)), request with { MinRating = floor });

        if (!fixedBy.Contains(FacetSections.Language) && string.IsNullOrWhiteSpace(request.Language) && !anime)
            foreach (var language in DiscoverShelves.FilterLanguages)
                Offer(FacetSections.Language, new(language.Code, language.Name), request with { Languages = [language.Code] });

        return Outcome<CatalogueFacetsView>.Ok(await MeasuredAsync([.. probes.Select(p =>
            (p.Section, p.Option, (Func<CancellationToken, Task<bool?>>)(token => HoldsAsync(p.Asked, token))))], ct));
    }

    /// <summary>
    /// The sections and the options found to hold, each section in the order its options were
    /// offered; a section with an option that could not be measured is left out.
    /// </summary>
    private static async Task<CatalogueFacetsView> MeasuredAsync(
        List<(string Section, FacetOptionView Option, Func<CancellationToken, Task<bool?>> Holds)> probes, CancellationToken ct)
    {
        var held = new bool?[probes.Count];
        await Parallel.ForEachAsync(Enumerable.Range(0, probes.Count), new ParallelOptions { MaxDegreeOfParallelism = ProbesAtOnce, CancellationToken = ct },
            async (i, token) => held[i] = await probes[i].Holds(token));

        var sections = new List<FacetSectionView>();
        foreach (var section in probes.Select(p => p.Section).Distinct())
        {
            var asked = Enumerable.Range(0, probes.Count).Where(i => probes[i].Section == section).ToList();
            if (asked.Any(i => held[i] is null)) continue;
            sections.Add(new FacetSectionView(section, [.. asked.Where(i => held[i] == true).Select(i => probes[i].Option)]));
        }
        return new CatalogueFacetsView { Sections = sections };
    }

    /// <summary>
    /// The questions a shelf's question is asked as with a filter's picks on it, the picks matched to
    /// the database's ids for the shelf's kind. A shelf is of its own kind, so a pick of a kind is
    /// refused, as is a length on a shelf of shows; a genre or a language the database does not have
    /// is a refusal as it is for a discovery.
    /// </summary>
    public async Task<Outcome<List<DiscoverQuery>>> NarrowedAsync(DiscoverQuery query, DiscoverPicks picks, CancellationToken ct)
    {
        if (picks.Kind is not null || picks.Category is not null) return Invalid<List<DiscoverQuery>>("A shelf is of its own kind; a filter does not pick one.");
        if (picks.Lengths.Count > 0 && query.Kind != "film") return Invalid<List<DiscoverQuery>>("A length is a film's; a show is not picked by one.");
        var today = Today();
        foreach (var decade in picks.Decades)
            if (!Decades.IsDecade(decade, today))
                return Invalid<List<DiscoverQuery>>($"{decade} is no decade: a decade is named by its first year, from {Decades.Earliest} to this one.");
        if (picks.Lengths.FirstOrDefault(l => FilmLengths.Of(l) is null) is { } length)
            return Invalid<List<DiscoverQuery>>($"\"{length}\" is no length; a length is {string.Join(", ", FilmLengths.All)}.");

        var genreIds = new List<int>();
        if (picks.AnyGenres.Count > 0)
        {
            var all = await catalogue.GenresAsync(query.Kind, ct);
            if (all is null) return Unavailable<List<DiscoverQuery>>("The film database could not be asked about its genres.");
            foreach (var name in picks.AnyGenres)
            {
                if (MatchGenre(all, name) is not { } genre)
                    return NotFound<List<DiscoverQuery>>($"The film database has no {query.Kind} genre called \"{name.Trim()}\".");
                if (!genreIds.Contains(genre.Id)) genreIds.Add(genre.Id);
            }
        }
        var languages = new List<string>();
        foreach (var text in picks.Languages)
        {
            var named = await LanguageAsync(text, ct);
            if (named.Value is not { } language) return named.Carry<List<DiscoverQuery>>();
            if (!languages.Contains(language.Code)) languages.Add(language.Code);
        }

        var floor = picks.MinRating is { } least && (query.MinRating is null || least > query.MinRating) ? query with { MinRating = least } : query;
        var queries = DiscoverNarrowing.Narrow(floor, new Narrowing
        {
            AnyGenreIds = genreIds,
            Windows = DiscoverNarrowing.Windows(picks.Decades),
            Runtimes = DiscoverNarrowing.Runtimes(picks.Lengths),
            Languages = languages,
        });
        return queries.Count > DiscoverNarrowing.MostQuestions
            ? Invalid<List<DiscoverQuery>>($"These picks ask the film database {queries.Count} questions at once; at most {DiscoverNarrowing.MostQuestions} are asked.")
            : Outcome<List<DiscoverQuery>>.Ok(queries);
    }

    /// <summary>
    /// Which options each section of a shelf's question holds with a filter's picks on it, measured as
    /// a discovery's are (<see cref="DiscoverFacetsAsync"/>). The shelf's kind is its own, and so is a
    /// language it names or a genre every title of it is filed under. An option in
    /// <paramref name="known"/> is already known to hold elsewhere and is reported without being asked.
    /// </summary>
    public async Task<Outcome<CatalogueFacetsView>> ShelfFacetsAsync(DiscoverQuery query, DiscoverPicks picks, CancellationToken ct, IReadOnlySet<(string Section, string Value)>? known = null)
    {
        var planned = await NarrowedAsync(query, picks, ct);
        if (planned.Value is null) return planned.Carry<CatalogueFacetsView>();

        var probes = new List<(string Section, FacetOptionView Option, Func<CancellationToken, Task<bool?>> Holds)>();
        void Offer(string section, FacetOptionView option, DiscoverPicks asked) =>
            probes.Add((section, option, known is not null && known.Contains((section, option.Value))
                ? _ => Task.FromResult<bool?>(true)
                : token => ShelfHoldsAsync(query, asked, token)));

        var genres = await catalogue.GenresAsync(query.Kind, ct);
        if (genres is null) return Unavailable<CatalogueFacetsView>("The film database could not be asked about its genres.");
        foreach (var genre in genres.Where(g => !query.GenreIds.Contains(g.Id) && !query.WithoutGenreIds.Contains(g.Id)
                     && (query.Kind != "show" || !DiscoverShelves.UnscriptedShowGenres.Contains(g.Id))).OrderBy(g => g.Name, StringComparer.OrdinalIgnoreCase))
            Offer(FacetSections.Genre, new(genre.Name), picks with { AnyGenres = [genre.Name] });
        foreach (var decade in Decades.Offered(Today()))
            Offer(FacetSections.Decade, new(decade.ToString(CultureInfo.InvariantCulture)), picks with { Decades = [decade] });
        if (query.Kind == "film" && query.MinRuntime is null && query.MaxRuntime is null)
            foreach (var length in FilmLengths.All) Offer(FacetSections.Length, new(length), picks with { Lengths = [length] });
        foreach (var floor in FacetSections.Ratings)
            Offer(FacetSections.Rating, new(floor.ToString(CultureInfo.InvariantCulture)), picks with { MinRating = floor });
        if (string.IsNullOrWhiteSpace(query.Language))
            foreach (var language in DiscoverShelves.FilterLanguages)
                Offer(FacetSections.Language, new(language.Code, language.Name), picks with { Languages = [language.Code] });

        return Outcome<CatalogueFacetsView>.Ok(await MeasuredAsync(probes, ct));
    }

    private async Task<bool?> ShelfHoldsAsync(DiscoverQuery query, DiscoverPicks picks, CancellationToken ct)
    {
        var planned = await NarrowedAsync(query, picks, ct);
        if (planned.Value is not { } queries) return planned.Problem is Problem.NotFound or Problem.Invalid ? false : null;
        return await AnyListedAsync(queries, new PageAsk(MostListed, ResultsListed, 1), ct);
    }

    /// <summary>Whether any of the questions has a title on its first page, in the database's own order; null when the database could not answer.</summary>
    private async Task<bool?> AnyListedAsync(IEnumerable<DiscoverQuery> queries, PageAsk ask, CancellationToken ct)
    {
        foreach (var query in queries)
        {
            var asked = query.Sort == DiscoverSorts.Rating ? query with { Sort = DiscoverSorts.Popular } : query;
            var listed = await DiscoveredAsync("discover", DiscoverKept, asked, ask with { Page = 1 }, ct);
            if (listed.Value is not { } page) return null;
            if (page.Titles.Count > 0) return true;
        }
        return false;
    }

    /// <summary>
    /// Whether a discovery has any title: false when the database lists none or the discovery cannot
    /// be asked (a genre the kind lacks, a pick it refuses), null when the database could not answer.
    /// A sort by rating is measured in the database's own order, which lists the same titles.
    /// </summary>
    private async Task<bool?> HoldsAsync(DiscoverRequest request, CancellationToken ct)
    {
        var planned = await PlanAsync(request, ct);
        if (planned.Value is not { } plan) return planned.Problem is Problem.NotFound or Problem.Invalid ? false : null;

        if (plan.Seed is { } seed)
        {
            var similar = await SimilarToAsync(seed.Key, plan.Queries, plan.Sort, new PageAsk(1, 1, 1), ct);
            return similar.Value is { } found ? found.Titles.Count > 0 : null;
        }
        return await AnyListedAsync(plan.Queries, plan.Ask, ct);
    }
}
