using System.Diagnostics;
using Microsoft.Extensions.Options;
using TheKrystalShip.MovieBot.Acquire.Tmdb;

namespace TheKrystalShip.MovieBot.Api.Films;

/// <summary>How the films of a Wikidata set are ordered when nothing else orders them.</summary>
public enum WikidataOrder
{
    /// <summary>Newest release first: an award's or an adaptation's latest.</summary>
    Newest,

    /// <summary>Most voted on IMDb first: a place's or a subgenre's best known.</summary>
    Votes,
}

/// <summary>One question put to Wikidata whose answer is kept and shelved: a relation to an item, and the shelf it fills.</summary>
public sealed record WikidataSet(string Key, WikidataRelation Relation, string Item, string Title, string Icon, string Section, WikidataOrder Order);

/// <summary>
/// The sets of films Wikidata answers, kept in the database: the fixed ones the Discover shelves are
/// made of, asked again every <see cref="WikidataOptions.RefreshDays"/> by <see cref="WikidataWarmer"/>
/// and never while a page waits, and the ones a question names, asked when the question is and kept
/// the same time. A page reads what is kept; a host that never reached Wikidata has no sets and the
/// shelves they fill are absent.
/// </summary>
public sealed class WikidataSets(
    AnswerKeeper keeper,
    ImdbRatings imdb,
    IOptions<WikidataOptions> options,
    IServiceScopeFactory scopes,
    TimeProvider clock,
    ILogger<WikidataSets> logger)
{
    /// <summary>Every shelf Wikidata fills: a set is here only where Wikidata's count for it is as full as the subject should be.</summary>
    public static readonly IReadOnlyList<WikidataSet> Fixed =
    [
        new("best-picture-winners", WikidataRelation.Won, "Q102427", "Best Picture winners", "trophy", "awards", WikidataOrder.Newest),
        new("best-picture-nominees", WikidataRelation.Nominated, "Q102427", "Best Picture nominees", "award", "awards", WikidataOrder.Newest),
        new("palme-dor-winners", WikidataRelation.Won, "Q179808", "Palme d'Or winners", "medal", "awards", WikidataOrder.Newest),

        new("stephen-king", WikidataRelation.Adaptation, "Q39829", "Stephen King adaptations", "book-open", "adaptations", WikidataOrder.Newest),
        new("jane-austen", WikidataRelation.Adaptation, "Q36322", "Jane Austen adaptations", "book-open", "adaptations", WikidataOrder.Newest),
        new("agatha-christie", WikidataRelation.Adaptation, "Q35064", "Agatha Christie adaptations", "book-open", "adaptations", WikidataOrder.Newest),
        new("shakespeare", WikidataRelation.Adaptation, "Q692", "Shakespeare adaptations", "book-open", "adaptations", WikidataOrder.Newest),
        new("charles-dickens", WikidataRelation.Adaptation, "Q5686", "Charles Dickens adaptations", "book-open", "adaptations", WikidataOrder.Newest),
        new("arthur-conan-doyle", WikidataRelation.Adaptation, "Q35610", "Arthur Conan Doyle adaptations", "book-open", "adaptations", WikidataOrder.Newest),
        new("jules-verne", WikidataRelation.Adaptation, "Q33977", "Jules Verne adaptations", "book-open", "adaptations", WikidataOrder.Newest),

        new("set-in-paris", WikidataRelation.Set, "Q90", "Set in Paris", "map-pin", "places", WikidataOrder.Votes),
        new("set-in-new-york", WikidataRelation.Set, "Q60", "Set in New York", "map-pin", "places", WikidataOrder.Votes),
        new("set-in-london", WikidataRelation.Set, "Q84", "Set in London", "map-pin", "places", WikidataOrder.Votes),
        new("set-in-tokyo", WikidataRelation.Set, "Q1490", "Set in Tokyo", "map-pin", "places", WikidataOrder.Votes),

        new("neo-noir", WikidataRelation.Genre, "Q2421031", "Neo-noir", "moon", "themes", WikidataOrder.Votes),
        new("film-noir", WikidataRelation.Genre, "Q185867", "Film noir", "moon", "themes", WikidataOrder.Votes),
        new("spaghetti-westerns", WikidataRelation.Genre, "Q212781", "Spaghetti westerns", "mountain", "themes", WikidataOrder.Votes),
        new("disaster-films", WikidataRelation.Genre, "Q846544", "Disaster films", "siren", "themes", WikidataOrder.Votes),
        new("world-war-ii", WikidataRelation.Subject, "Q362", "World War II", "shield", "themes", WikidataOrder.Votes),
    ];

    private static readonly Dictionary<string, WikidataSet> ByKey = Fixed.ToDictionary(s => s.Key, StringComparer.Ordinal);

    public static WikidataSet? Find(string key) => ByKey.GetValueOrDefault(key);

    /// <summary>The fixed set that is this relation to this item, if there is one.</summary>
    public static WikidataSet? Find(WikidataRelation relation, string item) =>
        Fixed.FirstOrDefault(s => s.Relation == relation && s.Item == item);

    public bool Enabled => options.Value.Enabled;

    private TimeSpan Keep => TimeSpan.FromDays(Math.Max(1, options.Value.RefreshDays));

    private static string NameOf(WikidataSet set) => $"wikidata/{set.Key}";

    private static string NameOf(WikidataRelation relation, string item) =>
        Find(relation, item) is { } fixedSet ? NameOf(fixedSet) : AnswerKeeper.QueryName("wd-set", $"{relation}|{item}");

    /// <summary>The films a fixed set kept, never asking; null while nothing is kept for it.</summary>
    public IReadOnlyList<WikidataFilm>? Kept(WikidataSet set) =>
        Enabled ? keeper.Kept(NameOf(set), WikidataJson.Default.ListWikidataFilm)?.Value : null;

    /// <summary>
    /// The films of a relation to an item, from what is kept or else asked now and kept; null when
    /// Wikidata cannot be asked and nothing is kept.
    /// </summary>
    public async Task<IReadOnlyList<WikidataFilm>?> AskedAsync(WikidataRelation relation, string item, CancellationToken ct)
    {
        var kept = await keeper.KeptAsync(NameOf(relation, item), WikidataJson.Default.ListWikidataFilm, Keep,
            async (_, token) =>
            {
                using var scope = scopes.CreateScope();
                return await scope.ServiceProvider.GetRequiredService<WikidataClient>().FilmsAsync(relation, item, token);
            }, null, ct);
        return kept?.Value;
    }

    /// <summary>The items a name could mean, kept a month like the film database's vocabulary; null when Wikidata cannot be asked.</summary>
    public async Task<IReadOnlyList<WikidataEntity>?> EntitiesAsync(string name, CancellationToken ct)
    {
        var wanted = name.Trim();
        var kept = await keeper.KeptAsync(AnswerKeeper.QueryName("wd-entity", wanted.ToLowerInvariant()),
            WikidataJson.Default.ListWikidataEntity, CatalogueSearch.VocabularyKept,
            async (_, token) =>
            {
                using var scope = scopes.CreateScope();
                return await scope.ServiceProvider.GetRequiredService<WikidataClient>().SearchAsync(wanted, token);
            }, null, ct);
        return kept?.Value;
    }

    /// <summary>
    /// Asks every fixed set that is due, one after another, pausing after each question for twice as
    /// long as it took so the process stays well inside Wikidata's allowance of query time. The
    /// number of sets that could not be answered.
    /// </summary>
    public async Task<int> WarmAsync(CancellationToken ct)
    {
        if (!Enabled) return 0;
        var unanswered = 0;
        foreach (var set in Fixed)
        {
            ct.ThrowIfCancellationRequested();
            if (keeper.Kept(NameOf(set), WikidataJson.Default.ListWikidataFilm) is { Value: not null } held && clock.GetUtcNow() - held.FetchedAt <= Keep)
                continue;

            var took = Stopwatch.StartNew();
            var films = await AskedAsync(set.Relation, set.Item, ct);
            if (films is null)
            {
                unanswered++;
                logger.LogWarning("Wikidata could not list the films of {Set}.", set.Key);
            }
            await Task.Delay(TimeSpan.FromSeconds(Math.Max(0.2, took.Elapsed.TotalSeconds * 2)), clock, ct);
        }
        return unanswered;
    }

    /// <summary>The films best first by <paramref name="order"/>; a film IMDb holds no votes for follows those it does, in the order it came.</summary>
    public List<WikidataFilm> Ordered(IReadOnlyList<WikidataFilm> films, string order) => order switch
    {
        Orders.Newest => [.. films.OrderBy(f => f.Released is null).ThenByDescending(f => f.Released)],
        Orders.Oldest => [.. films.OrderBy(f => f.Released is null).ThenBy(f => f.Released)],
        Orders.Rating => ByRating(films),
        _ => [.. films.OrderByDescending(f => imdb.Of(f.ImdbId)?.Votes ?? -1)],
    };

    /// <summary>The names of the orders <see cref="Ordered"/> takes.</summary>
    public static class Orders
    {
        public const string Newest = "newest";
        public const string Oldest = "oldest";
        public const string Votes = "votes";
        public const string Rating = "rating";

        public static string Of(WikidataOrder order) => order == WikidataOrder.Newest ? Newest : Votes;
    }

    /// <summary>Ordered as a question by rating is: IMDb's rating weighted by votes over a floor that scales with the set, the rest after.</summary>
    private List<WikidataFilm> ByRating(IReadOnlyList<WikidataFilm> films)
    {
        var entries = films
            .Select((f, i) => new ImdbRanking.Entry(new ListedTitle { TmdbId = i, Kind = "film", Title = f.Title ?? "" }, imdb.Of(f.ImdbId)))
            .ToList();
        var floor = ImdbRanking.FloorFor([.. entries.Where(e => e.Imdb is not null).Select(e => e.Imdb!.Value.Votes).OrderDescending()]);
        return [.. ImdbRanking.Order(entries, floor).Select(t => films[t.TmdbId])];
    }
}
