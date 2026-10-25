using TheKrystalShip.MovieBot.Acquire.Tmdb;

namespace TheKrystalShip.MovieBot.Api.Films;

// A shelf that is a question of the film database is read whole when it lists no more than a shelf
// serves, and a filter's picks and the options each section holds are then answered over the titles
// held, as an answer the API holds whole is (WholeFacets): in memory, with exact counts, asking the
// database nothing. A shelf that lists more is narrowed by the database and measured by its probes.
/// <summary>A shelf's question read whole: every title it lists, or null when it lists more than a shelf serves.</summary>
public sealed record ShelfWhole(List<TitleHit>? Titles);

public sealed partial class CatalogueSearch
{
    /// <summary>
    /// Every title a shelf's question lists, in the database's own order, each with its card and the
    /// shelf's floor held for the rating shown; none when it lists more than <see cref="MostOnShelf"/>.
    /// Each page read is the question's own kept answer and each card the title's, so the shelf's
    /// pages and the whole share what they ask. A question ordered by rating is read most looked at
    /// first, since its ranked pool is the most voted of it and not the whole.
    /// </summary>
    public async Task<Outcome<ShelfWhole>> ShelfWholeAsync(DiscoverQuery query, CancellationToken ct)
    {
        var asked = query.Sort == DiscoverSorts.Rating ? query with { Sort = DiscoverSorts.Popular } : query;
        var listed = new List<ListedTitle>();
        for (var at = 1; ; at++)
        {
            if (await KeptPageAsync("discover", DiscoverKept, asked, new PageAsk(MostOnShelf, ShelfListed, at), ct) is not { } page)
                return Unavailable<ShelfWhole>("The film database could not be asked to discover.");
            listed.AddRange(page.Titles);
            if (at >= page.Pages || page.Titles.Count == 0) break;
            if (at == PagesListed) return Outcome<ShelfWhole>.Ok(new ShelfWhole(null));
        }

        var titles = listed.DistinctBy(t => (t.Kind, t.TmdbId)).ToList();
        var cards = await CardsAsync(titles.Select(t => (t.Kind, t.TmdbId)), ct);
        List<TitleHit> hits = [.. titles.Select(t => new TitleHit(t, cards.GetValueOrDefault((t.Kind, t.TmdbId))))];
        return Outcome<ShelfWhole>.Ok(new ShelfWhole(RatedAtLeast(hits, query.MinRating)));
    }

    /// <summary>
    /// A page of a shelf held whole (<see cref="ShelfWholeAsync"/>) narrowed by picks
    /// <see cref="ShelfPicksAsync"/> resolved, in the order <paramref name="sort"/> names, else the
    /// shelf's own (<see cref="OrderedAsync"/>).
    /// </summary>
    public async Task<PagedHits> WholeShelfPageAsync(DiscoverQuery query, IReadOnlyList<TitleHit> whole, DiscoverPicks picks, string? sort, PageAsk ask, CancellationToken ct)
    {
        var kept = WholeFacets.Filter(whole, Traits, picks);
        var byKey = kept.ToDictionary(h => (h.Listed.Kind, h.Listed.TmdbId));
        var ordered = await OrderedAsync([.. kept.Select(h => h.Listed)], sort ?? query.Sort, ct);
        List<TitleHit> page = [.. ordered.Skip(ask.Skip).Take(ask.Take).Select(t => byKey[(t.Kind, t.TmdbId)])];
        foreach (var hit in page)
            if (hit.Card?.ImdbId is { } imdb) glances.Offer(imdb, Glance(hit.Listed));
        return new PagedHits(page, ask.Next(ordered.Count > ask.Skip + ask.Size));
    }

    /// <summary>
    /// Which options each section of a shelf held whole holds with picks <see cref="ShelfPicksAsync"/>
    /// resolved on it, counted over every title (<see cref="WholeFacets"/>). The shelf's kind is its
    /// own, and so are a language it names, a length when it is of shows or bounded by runtime, and the
    /// genres every title of it is filed under; an unscripted show genre is no option.
    /// </summary>
    public async Task<CatalogueFacetsView> WholeShelfFacetsAsync(DiscoverQuery query, IReadOnlyList<TitleHit> whole, DiscoverPicks picks, CancellationToken ct)
    {
        HashSet<string> fixedBy = [FacetSections.Kind];
        if (!string.IsNullOrWhiteSpace(query.Language)) fixedBy.Add(FacetSections.Language);
        if (query.Kind != "film" || query.MinRuntime is not null || query.MaxRuntime is not null) fixedBy.Add(FacetSections.Length);
        var facets = WholeFacets.Facets([.. whole.Select(Traits)], picks, fixedBy, await LanguageNamesAsync(ct));

        var genres = await catalogue.GenresAsync(query.Kind, ct) ?? [];
        var own = CinemaGenres.Of(genres
            .Where(g => query.GenreIds.Contains(g.Id) || (query.Kind == "show" && DiscoverShelves.UnscriptedShowGenres.Contains(g.Id)))
            .Select(g => g.Name));
        return facets with
        {
            Sections = [.. facets.Sections.Select(s => s.Id == FacetSections.Genre
                ? s with { Options = [.. s.Options.Where(o => !own.Contains(o.Value, StringComparer.OrdinalIgnoreCase))] }
                : s)],
        };
    }
}
