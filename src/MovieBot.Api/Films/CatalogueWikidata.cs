using TheKrystalShip.MovieBot.Acquire.Tmdb;
using TheKrystalShip.MovieBot.Api.Library;

namespace TheKrystalShip.MovieBot.Api.Films;

/// <summary>What narrows a Wikidata set once it is listed: the film database's genres and language and the years of release.</summary>
internal sealed record WikidataNarrowing(IReadOnlyList<string> Genres, IReadOnlyList<string> Excluded, string? Language, int? From, int? To)
{
    public static readonly WikidataNarrowing None = new([], [], null, null, null);

    /// <summary>Whether a film's card has to be read to apply it.</summary>
    public bool NeedsCards => Genres.Count + Excluded.Count > 0 || Language is not null;
}

public sealed partial class CatalogueSearch
{
    /// <summary>The most films of a set whose cards are read to apply a genre or a language.</summary>
    private const int MostExamined = 400;

    /// <summary>
    /// Answers a question that names a set of films in Wikidata (<see cref="Question.Wikidata"/>): an
    /// award, an author's adaptations, a place, a subgenre. A name is resolved to its item through
    /// Wikidata's entity search by its exact label. The set is the one the weekly cycle kept or else
    /// asked now and kept the same time, ordered by the question's sort (an award's and an adaptation's
    /// newest first, the rest by IMDb's votes) and narrowed by the question's genres, language and years.
    /// A question Wikidata cannot narrow (people, subjects, runtime, shows) is answered with the reason.
    /// </summary>
    private async Task<Outcome<QuestionAnswer>> AskWikidataAsync(Question question, int page, DiscoverPicks picks, CancellationToken ct)
    {
        var planned = await PlanWikidataAsync(question, page, ct);
        if (planned.Settled is { } settled) return settled;
        var plan = planned.Plan!;
        var pageAsk = new PageAsk(plan.Limit, Math.Min(plan.Limit, ResultsListed), page);

        PagedHits paged;
        if (!picks.Any)
        {
            var listed = await WikidataTitlesAsync(plan.Ordered, plan.Narrowing, pageAsk, ct);
            if (listed.Value is not { } found) return listed.Carry<QuestionAnswer>();
            paged = found;
        }
        else
        {
            if (WholeFacets.Refusal(picks, Today()) is { } refusal) return Invalid<QuestionAnswer>(refusal);
            if (picks.Sections().FirstOrDefault(WikidataFixed(plan.Parts).Contains) is { } section)
                return Invalid<QuestionAnswer>($"The question names its own {section}, so a filter does not pick one.");
            var kept = WholeFacets.Filter(await WikidataWholeAsync(plan, ct), Traits, picks).Take(plan.Limit).ToList();
            paged = new PagedHits([.. kept.Skip(pageAsk.Skip).Take(pageAsk.Take)], pageAsk.Next(kept.Count > pageAsk.Skip + pageAsk.Size));
        }
        return Outcome<QuestionAnswer>.Ok(new QuestionAnswer(true, plan.Parts, new DiscoverAnswer("film", plan.Resolved, paged.Hits, paged.Next), null));
    }

    /// <summary>
    /// Which options each section of a Wikidata question's films holds with a filter's picks on it,
    /// counted over every film of the set the question narrows to (<see cref="WholeFacets"/>). The set
    /// is of films, so it has no kind section; a question Wikidata cannot answer has no sections.
    /// </summary>
    private async Task<Outcome<CatalogueFacetsView>> WikidataFacetsAsync(Question question, DiscoverPicks picks, CancellationToken ct)
    {
        var planned = await PlanWikidataAsync(question, 1, ct);
        if (planned.Plan is not { } plan)
            return planned.Settled!.Value is not null ? Outcome<CatalogueFacetsView>.Ok(new CatalogueFacetsView { Sections = [] }) : planned.Settled.Carry<CatalogueFacetsView>();
        if (WholeFacets.Refusal(picks, Today()) is { } refusal) return Invalid<CatalogueFacetsView>(refusal);
        var fixedBy = WikidataFixed(plan.Parts);
        if (picks.Sections().FirstOrDefault(fixedBy.Contains) is { } section)
            return Invalid<CatalogueFacetsView>($"The question names its own {section}, so a filter does not pick one.");
        var whole = await WikidataWholeAsync(plan, ct);
        return Outcome<CatalogueFacetsView>.Ok(WholeFacets.Facets([.. whole.Select(Traits)], picks, fixedBy, await LanguageNamesAsync(ct)));
    }

    /// <summary>The sections a Wikidata question fixes: its own parts', and its kind, since the set is of films.</summary>
    private static HashSet<string> WikidataFixed(IEnumerable<QuestionPart> parts) => [.. FixedBy(parts), FacetSections.Kind];

    /// <summary>How long a filter over a whole set waits for the film database to name its films.</summary>
    private static readonly TimeSpan WholeWait = TimeSpan.FromMinutes(2);

    /// <summary>Every film of the set the question narrows to, up to the most a shelf holds, each named by the film database.</summary>
    private async Task<List<TitleHit>> WikidataWholeAsync(WikidataPlan plan, CancellationToken ct)
    {
        var dated = plan.Narrowing with { Genres = [], Excluded = [], Language = null };
        var listed = await WikidataTitlesAsync([.. plan.Ordered.Take(MostOnShelf)], dated, new PageAsk(MostOnShelf, MostOnShelf, 1), ct, WholeWait);
        var n = plan.Narrowing;
        return [.. (listed.Value?.Hits ?? []).Where(h => h.Card is { } card
                ? n.Genres.All(g => card.Genres.Contains(g, StringComparer.OrdinalIgnoreCase))
                  && !n.Excluded.Any(g => card.Genres.Contains(g, StringComparer.OrdinalIgnoreCase))
                  && (n.Language is not { } code || string.Equals(card.OriginalLanguage, code, StringComparison.OrdinalIgnoreCase))
                : !n.NeedsCards)];
    }

    /// <summary>A Wikidata question resolved: its parts, the set in the question's order, what narrows it, and the most titles it lists.</summary>
    private sealed record WikidataPlan(List<QuestionPart> Parts, IReadOnlyList<WikidataFilm> Ordered, WikidataNarrowing Narrowing, DiscoverResolved Resolved, int Limit);

    /// <summary>A Wikidata question resolved, or the answer it settles on: a reason it cannot be answered, or a set Wikidata could not be asked for.</summary>
    private async Task<(WikidataPlan? Plan, Outcome<QuestionAnswer>? Settled)> PlanWikidataAsync(Question question, int page, CancellationToken ct)
    {
        var ask = question.Wikidata!;
        var request = question.Request;
        var parts = question.Parts.ToList();
        (WikidataPlan?, Outcome<QuestionAnswer>?) Said(string message) => (null, Outcome<QuestionAnswer>.Ok(new QuestionAnswer(true, parts, null, message)));
        (WikidataPlan?, Outcome<QuestionAnswer>?) Unavailable(string message) => (null, Unavailable<QuestionAnswer>(message));

        if (!wikidata.Enabled) return Said("This server does not ask Wikidata, which awards, adaptations, places and subgenres come from.");
        if (page < 1 || page > PagesListed) return Said($"Page is 1 to {PagesListed}.");
        if (request.Kind == "show") return Said("Wikidata lists films, so there are no shows to answer this with.");
        if (question.Named is not null || question.WithEither.Count > 0 || request.With.Count > 0 || request.About.Count > 0 || !string.IsNullOrWhiteSpace(request.Like))
            return Said("A question about awards, adaptations, places or subgenres cannot also name people, subjects or another title.");
        if (request.MinRuntime is not null || request.MaxRuntime is not null)
            return Said("A question about awards, adaptations, places or subgenres cannot be narrowed by runtime.");

        var item = ask.Item;
        var name = ask.Name;
        if (item is null)
        {
            var found = await wikidata.EntitiesAsync(ask.Name, ct);
            if (found is null) return Unavailable("Wikidata could not be asked about this name.");
            var entity = found.FirstOrDefault(e => WikidataClient.IsItemId(e.Id) && string.Equals(e.Label, ask.Name, StringComparison.OrdinalIgnoreCase));
            if (entity is null) return Said($"Wikidata has nothing called \"{ask.Name}\".");
            (item, name) = (entity.Id, entity.Label);
            var at = parts.FindIndex(p => p.Part is "adaptation" or "place");
            if (at >= 0)
                parts[at] = parts[at] with
                {
                    Text = ask.Relation switch
                    {
                        WikidataRelation.Adaptation => "Adaptations of " + name,
                        WikidataRelation.Filmed => "Filmed in " + name,
                        _ => "Set in " + name,
                    },
                };
        }

        var genres = new List<CatalogueGenre>();
        var excluded = new List<CatalogueGenre>();
        string? languageCode = null;
        string? languageName = null;
        var genreNames = request.Genres.Where(g => !string.IsNullOrWhiteSpace(g)).ToList();
        if (request.Category is not null)
        {
            if (TitleCategories.Parse(request.Category) != TitleCategory.Anime) return Said("A question is narrowed to anime alone; leave the category out for everything.");
            genreNames.Add(TitleCategories.AnimeGenre);
        }
        if (genreNames.Count > 0 || request.Exclude.Count > 0)
        {
            var all = await catalogue.GenresAsync("film", ct);
            if (all is null) return Unavailable("The film database could not be asked about its genres.");
            foreach (var genreName in genreNames)
            {
                if (MatchGenre(all, genreName) is not { } genre) return Said($"The film database has no film genre called \"{genreName.Trim()}\".");
                if (genres.All(g => g.Id != genre.Id)) genres.Add(genre);
            }
            foreach (var genreName in request.Exclude.Where(g => !string.IsNullOrWhiteSpace(g)))
            {
                if (MatchGenre(all, genreName) is not { } genre) return Said($"The film database has no film genre called \"{genreName.Trim()}\".");
                if (genres.Any(g => g.Id == genre.Id)) return Said($"{genre.Name} is both asked for and left out.");
                if (excluded.All(g => g.Id != genre.Id)) excluded.Add(genre);
            }
        }
        var wantedLanguage = request.Category is not null ? TitleCategories.AnimeLanguage : request.Language;
        if (!string.IsNullOrWhiteSpace(wantedLanguage))
        {
            var language = await LanguageAsync(wantedLanguage, ct);
            if (language.Value is not { } resolvedLanguage) return language.Problem == Problem.NotFound ? Said(language.Message!) : (null, language.Carry<QuestionAnswer>());
            (languageCode, languageName) = (resolvedLanguage.Code, resolvedLanguage.Name);
        }

        var films = await wikidata.AskedAsync(ask.Relation, item, ct);
        if (films is null) return Unavailable("Wikidata could not be asked, and nothing is kept for this.");
        if (films.Count == 0) return Said($"Wikidata lists no films for {name}.");

        var order = request.Sort switch
        {
            DiscoverSorts.Rating => WikidataSets.Orders.Rating,
            DiscoverSorts.Popular => WikidataSets.Orders.Votes,
            DiscoverSorts.Newest => WikidataSets.Orders.Newest,
            DiscoverSorts.Oldest => WikidataSets.Orders.Oldest,
            _ => ask.Relation is WikidataRelation.Won or WikidataRelation.Nominated or WikidataRelation.Adaptation
                ? WikidataSets.Orders.Newest : WikidataSets.Orders.Votes,
        };
        var limit = Math.Clamp(request.Limit, 1, MostListed);
        var narrowing = new WikidataNarrowing([.. genres.Select(g => g.Name)], [.. excluded.Select(g => g.Name)], languageCode, request.From, request.To);

        var resolved = new DiscoverResolved
        {
            Genres = [.. genres.Select(g => g.Name)],
            Excluded = [.. excluded.Select(g => g.Name)],
            People = [],
            Keywords = [],
            Language = languageName,
            Sort = order == WikidataSets.Orders.Votes ? DiscoverSorts.Popular : order,
        };
        return (new WikidataPlan(parts, wikidata.Ordered(films, order), narrowing, resolved, limit), null);
    }

    /// <summary>
    /// A page of one of the fixed Wikidata sets a Discover shelf shows, from what the weekly cycle
    /// kept, in the set's own order or the one a <paramref name="sort"/> names: nothing is asked of
    /// Wikidata here, so a shelf the cycle has not reached is empty.
    /// </summary>
    public async Task<Outcome<PagedHits>> WikidataShelfAsync(WikidataSet set, PageAsk ask, CancellationToken ct, string? sort = null)
    {
        if (wikidata.Kept(set) is not { Count: > 0 } films) return Outcome<PagedHits>.Ok(new PagedHits([], null));
        var ordered = wikidata.Ordered(films, sort switch
        {
            DiscoverSorts.Rating => WikidataSets.Orders.Rating,
            DiscoverSorts.Newest => WikidataSets.Orders.Newest,
            DiscoverSorts.Oldest => WikidataSets.Orders.Oldest,
            DiscoverSorts.Popular => WikidataSets.Orders.Votes,
            _ => WikidataSets.Orders.Of(set.Order),
        });
        return await WikidataTitlesAsync(ordered.Take(MostOnShelf).ToList(), WikidataNarrowing.None, ask, ct);
    }

    /// <summary>
    /// A page of the films of an ordered Wikidata set that pass the narrowing, each named by the film
    /// database: its card, and its glance by IMDb id for the poster and rating. Years are read from
    /// Wikidata's release date; genres and language from the card, which a film Wikidata holds no
    /// database id for cannot supply, so such a film is left out of a narrowing that needs one.
    /// </summary>
    private async Task<Outcome<PagedHits>> WikidataTitlesAsync(IReadOnlyList<WikidataFilm> ordered, WikidataNarrowing narrowing, PageAsk ask, CancellationToken ct, TimeSpan? wait = null)
    {
        if (ask.Take <= 0) return Outcome<PagedHits>.Ok(new PagedHits([], null));

        var candidates = ordered
            .Where(f => (narrowing.From is null && narrowing.To is null)
                        || (f.Released is { } day && (narrowing.From is null || day.Year >= narrowing.From) && (narrowing.To is null || day.Year <= narrowing.To)))
            .ToList();

        List<WikidataFilm> page;
        bool more;
        if (!narrowing.NeedsCards)
        {
            page = [.. candidates.Skip(ask.Skip).Take(ask.Take)];
            more = candidates.Count > ask.Skip + ask.Size;
        }
        else
        {
            var matched = new List<WikidataFilm>();
            var examined = 0;
            foreach (var chunk in candidates.Where(f => f.TmdbId is not null).Chunk(CardsAtOnce))
            {
                if (examined >= MostExamined) break;
                examined += chunk.Length;
                var cards = await CardsAsync(chunk.Select(f => ("film", f.TmdbId!.Value)), ct);
                foreach (var film in chunk)
                {
                    if (cards.GetValueOrDefault(("film", film.TmdbId!.Value)) is not { } card) continue;
                    if (!narrowing.Genres.All(g => card.Genres.Contains(g, StringComparer.OrdinalIgnoreCase))) continue;
                    if (narrowing.Excluded.Any(g => card.Genres.Contains(g, StringComparer.OrdinalIgnoreCase))) continue;
                    if (narrowing.Language is { } code && !string.Equals(card.OriginalLanguage, code, StringComparison.OrdinalIgnoreCase)) continue;
                    matched.Add(film);
                }
                if (matched.Count > ask.Skip + ask.Size) break;
            }
            page = [.. matched.Skip(ask.Skip).Take(ask.Take)];
            more = matched.Count > ask.Skip + ask.Size;
        }

        var glanced = await glances.ForAsync([.. page.Select(f => f.ImdbId)], ct, wait ?? WideningWait);
        var listed = new List<ListedTitle>();
        foreach (var film in page)
        {
            if (!glanced.TryGetValue(film.ImdbId, out var glance) || glance.Kind != "film") continue;
            listed.Add(new ListedTitle
            {
                TmdbId = glance.TmdbId,
                Kind = "film",
                Title = film.Title ?? film.ImdbId,
                Released = glance.Released ?? film.Released,
                Rating = glance.Rating,
                Votes = glance.Votes,
                Overview = glance.Overview,
                PosterPath = glance.PosterPath,
            });
        }

        // The film database names a title the way the rest of Cinema shows it.
        var hits = (await HitsAsync(listed, ask.Take, ct))
            .Select(h => h.Card?.Title is { } title ? h with { Listed = h.Listed with { Title = title } } : h)
            .ToList();
        return Outcome<PagedHits>.Ok(new PagedHits(hits, ask.Next(more)));
    }
}
