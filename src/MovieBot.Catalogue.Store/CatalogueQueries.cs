using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using TheKrystalShip.MovieBot.Catalogue.Data;

namespace TheKrystalShip.MovieBot.Catalogue;

/// <summary>
/// Every read of the catalogue the site, the bot and the assistant make: the list query and its facets,
/// a title's page and season, a person, the franchises, and the words resolved to filters. Only listed
/// entries are ever answered; a candidate is shown nowhere.
/// </summary>
public sealed class CatalogueQueries(CatalogueReader reader)
{
    /// <summary>The votes a weighted rating counts as already cast, at <see cref="RatingPrior"/>, so a title few rated does not top a list.</summary>
    public const int RatingWeight = 1000;
    public const double RatingPrior = 6.5;

    /// <summary>How much being well known (the log of a title's votes) weighs in a search beside how well it matches.</summary>
    public const double KnownWeight = 1.0;

    /// <summary>How many similar titles a page carries.</summary>
    public const int SimilarShown = 20;

    /// <summary>
    /// The share of a franchise's titles another must hold for the first to be part of it: Wikidata tags a
    /// franchise's titles unevenly, so one held all but a title or two is the same franchise.
    /// </summary>
    public const double PartOf = 0.75;

    private const int PlotLength = 200;
    private const char Unit = '\u001f';

    /// <summary>The sections the filter drawer offers, each the filter it picks with.</summary>
    public static readonly string[] FacetSections = ["kind", "genre", "decade", "length", "rating", "language"];

    // ---- The list query

    public CardPage Titles(TitleQuery query) => reader.Read(c =>
    {
        var sql = Select(query, skip: null);
        if (sql is null) return new CardPage([], 0, null);
        var total = (int)c.Count($"SELECT count(*) {sql.From} WHERE {sql.Where}", sql.Args);
        var offset = (query.Page - 1) * query.Size;
        var ids = c.Query($"SELECT t.id {sql.From} WHERE {sql.Where} ORDER BY {sql.Order} LIMIT {query.Size} OFFSET {offset}",
            r => r.GetInt64(0), sql.Args);
        var person = query.People.Count == 1 ? query.People[0] : (long?)null;
        var cards = Cards(c, ids, person);
        return new CardPage(cards, total, offset + ids.Count < total ? query.Page + 1 : null);
    });

    /// <summary>
    /// Every section's options over the titles a query names. A section offers the options the titles have
    /// without any section's picks, so picking never moves what a section offers, and counts each option
    /// with every other section's picks applied. One scan reads every title's facet values, and the
    /// sections are counted from it.
    /// </summary>
    public Facets Facets(TitleQuery query) => Count(query, FacetRows(query));

    /// <summary>The facet values of every title a query names without its drawer picks: what <see cref="Count"/> counts.</summary>
    public List<FacetRow> FacetRows(TitleQuery query) => reader.Read(c =>
    {
        var offered = Select(query, skip: "*");
        if (offered is null) return [];
        return c.Query($"""
            SELECT t.imdb_id, t.kind, t.category, t.year, t.runtime, t.rating, t.original_language, t.genres, t.votes {offered.From} WHERE {offered.Where}
            """, r => new FacetRow(r.GetString(0), r.GetString(1), r.GetStringOrNull(2), r.GetIntOrNull(3), r.GetIntOrNull(4), r.GetDoubleOrNull(5),
                r.GetStringOrNull(6), r.GetString(7) is { Length: > 0 } g ? g.Split(',') : [], r.GetIntOrNull(8)), offered.Args);
    });

    /// <summary>
    /// A query's facets counted from the facet values of the titles it names without its drawer picks
    /// (<see cref="FacetRows"/>, or every entry's for a query that narrows nothing else), the asking
    /// person's <see cref="TitleQuery.Only"/> and <see cref="TitleQuery.Except"/> applied here.
    /// </summary>
    public static Facets Count(TitleQuery query, IReadOnlyList<FacetRow> all)
    {
        IReadOnlyList<FacetRow> rows = query.Only is null && query.Except is not { Count: > 0 }
            ? all
            : all.Where(r => (query.Only is not { } only || only.Contains(r.ImdbId)) && (query.Except is not { } except || !except.Contains(r.ImdbId))).ToList();

        // Which sections' picks each row passes, as one bit a section.
        var every = (1 << FacetSections.Length) - 1;
        var passes = new int[rows.Count];
        for (var n = 0; n < rows.Count; n++)
            for (var i = 0; i < FacetSections.Length; i++)
                if (Picks(query, rows[n], FacetSections[i])) passes[n] |= 1 << i;

        var sections = new List<FacetSection>();
        for (var s = 0; s < FacetSections.Length; s++)
        {
            var section = FacetSections[s];
            var others = every & ~(1 << s);
            var offeredCounts = new Dictionary<string, int>(StringComparer.Ordinal);
            var counts = new Dictionary<string, int>(StringComparer.Ordinal);
            for (var n = 0; n < rows.Count; n++)
            {
                var counted = (passes[n] & others) == others;
                void Add(string value)
                {
                    CollectionsMarshal.GetValueRefOrAddDefault(offeredCounts, value, out _)++;
                    if (counted) CollectionsMarshal.GetValueRefOrAddDefault(counts, value, out _)++;
                }
                var row = rows[n];
                switch (s) // The section's values, by its place in FacetSections.
                {
                    case 0:
                        Add(row.Kind);
                        if (row.Category == "anime") Add("anime");
                        break;
                    case 1:
                        foreach (var genre in row.Genres) Add(genre);
                        break;
                    case 2:
                        if (row.Year is { } year) Add(DecadeName(year));
                        break;
                    case 3:
                        if (row.Runtime is { } runtime) Add(LengthOf(runtime));
                        break;
                    case 4:
                        for (var f = 0; f < RatingFloors.Length; f++)
                            if (row.Rating >= RatingFloors[f]) Add(RatingFloorNames[f]);
                        break;
                    case 5:
                        if (row.Language is { } language) Add(language);
                        break;
                }
            }
            var ordered = section switch
            {
                "kind" => offeredCounts.Keys.OrderBy(v => v switch { "film" => 0, "show" => 1, _ => 2 }),
                "decade" => offeredCounts.Keys.OrderDescending(StringComparer.Ordinal),
                "length" => offeredCounts.Keys.OrderBy(v => Array.IndexOf(TitleQuery.LengthNames, v)),
                "rating" => offeredCounts.Keys.OrderDescending(StringComparer.Ordinal),
                "language" => offeredCounts.OrderByDescending(kv => kv.Value).Take(LanguagesOffered).Select(kv => kv.Key),
                _ => offeredCounts.Keys.Order(StringComparer.Ordinal),
            };
            sections.Add(new FacetSection(section, ordered.Select(v => new FacetOption(v, Label(section, v), counts.GetValueOrDefault(v))).ToList()));
        }
        return new Facets(passes.Count(m => m == every), sections);
    }

    /// <summary>Whether a title passes the pick of one facet section of a query.</summary>
    public static bool Picks(TitleQuery query, FacetRow row, string section) => section switch
    {
        "kind" => (query.Kind is null || row.Kind == query.Kind) && (query.Category is null || row.Category == query.Category),
        "genre" => query.AnyGenres.Count == 0 || row.Genres.Any(query.AnyGenres.Contains),
        "decade" => query.Decades.Count == 0 || row.Year is { } y && query.Decades.Contains(y / 10 * 10),
        "length" => query.Lengths.Count == 0 || row.Runtime is { } r && query.Lengths.Contains(LengthOf(r)),
        "rating" => query.MinRating is not { } floor || row.Rating >= floor,
        "language" => query.Languages.Count == 0 || row.Language is { } l && query.Languages.Contains(l),
        _ => true,
    };

    /// <summary>Whether a title passes every facet section's pick of a query.</summary>
    public static bool Matches(TitleQuery query, FacetRow row) => FacetSections.All(s => Picks(query, row, s));

    private static readonly int[] RatingFloors = [8, 7, 6];
    private static readonly string[] RatingFloorNames = [.. RatingFloors.Select(f => f.ToString(CultureInfo.InvariantCulture))];

    /// <summary>Each decade's name by its year over ten, made once, so a count names a decade without making a string.</summary>
    private static readonly string[] DecadeNames = [.. Enumerable.Range(0, 300).Select(d => (d * 10).ToString(CultureInfo.InvariantCulture))];

    private static string DecadeName(int year) =>
        year >= 0 && year / 10 < DecadeNames.Length ? DecadeNames[year / 10] : (year / 10 * 10).ToString(CultureInfo.InvariantCulture);

    private const int LanguagesOffered = 40;

    private static string LengthOf(int runtime) => runtime < 90 ? "short" : runtime <= 150 ? "standard" : "long";

    private static string? Label(string section, string value) => section switch
    {
        "language" => LanguageNames.Of(value),
        "kind" => value switch { "film" => "Films", "show" => "Shows", "anime" => "Anime", _ => null },
        "rating" => value + "+",
        "decade" => value + "s",
        "length" => value switch { "short" => "Under 90 minutes", "standard" => "90 to 150 minutes", "long" => "Over 150 minutes", _ => null },
        _ => null,
    };

    /// <summary>A query as SQL: what it reads from, which rows it keeps and in what order. Null when it can match nothing.</summary>
    private sealed record Query(string From, string Where, string Order, (string Name, object? Value)[] Args);

    /// <summary>
    /// The SQL of a query, leaving out the facet section <paramref name="skip"/> picks with (<c>*</c> for
    /// every section). Every value is bound; nothing a person typed is ever SQL.
    /// </summary>
    private static Query? Select(TitleQuery q, string? skip)
    {
        var args = new List<(string, object?)>();
        string Bind(object? value)
        {
            var name = "$p" + args.Count.ToString(CultureInfo.InvariantCulture);
            args.Add((name, value));
            return name;
        }
        bool Picks(string section) => skip is null || (skip != "*" && skip != section);

        var where = new List<string> { "t.listed = 1" };
        // A list of titles drives the query, an ordered one or the titles a person's join keeps to: SQLite
        // would otherwise scan every listed title and look each up in the list, a second on a full catalogue.
        string Listed(IReadOnlyList<string> ids, string alias) =>
            $"FROM json_each({Bind(JsonSerializer.Serialize(ids.ToArray(), StoreJson.Default.StringArray))}) {alias} CROSS JOIN titles t ON t.imdb_id = {alias}.value";
        var from = new StringBuilder(q.Ordered is not null ? Listed(q.Ordered, "o") : q.Only is { } only ? Listed([.. only], "l") : "FROM titles t");
        if (q.Ordered is not null && q.Only is { } kept)
            where.Add($"t.imdb_id IN (SELECT value FROM json_each({Bind(JsonSerializer.Serialize(kept.ToArray(), StoreJson.Default.StringArray))}))");

        if (q.Words is not null)
        {
            if (Match(q.Words) is not { } match) return null;
            from.Append($" JOIN (SELECT rowid AS id, bm25(titles_fts, 10.0, 5.0, 1.0) AS score FROM titles_fts WHERE titles_fts MATCH {Bind(match)}) m ON m.id = t.id");
        }
        if (q.Ranking is not null)
            from.Append($" JOIN rankings k ON k.title_id = t.id AND k.list = {Bind(q.Ranking)}");
        if (q.SimilarTo is not null)
            from.Append($" JOIN similar s ON s.kind = t.kind AND s.tmdb_id = t.tmdb_id AND s.title_id = (SELECT id FROM titles WHERE imdb_id = {Bind(q.SimilarTo)})");
        if (q.Franchise is not null)
        {
            var (source, id) = q.Franchise[0] == 'c' ? ("tmdb", q.Franchise[1..]) : ("wikidata", q.Franchise);
            from.Append($" JOIN franchise_titles ft ON ft.title_id = t.id AND ft.franchise_id = (SELECT id FROM franchises WHERE source = {Bind(source)} AND source_id = {Bind(id)})");
        }

        if (Picks("kind"))
        {
            if (q.Kind is not null) where.Add($"t.kind = {Bind(q.Kind)}");
            if (q.Category is not null) where.Add($"t.category = {Bind(q.Category)}");
        }
        // A genre is read off the card's genres (titles.genres, which the facets count too), so a list
        // filtered by genre is read down an index that carries them and never opens a row.
        string Genre(string genre) => $"instr(',' || t.genres || ',', ',' || {Bind(genre)} || ',') > 0";
        foreach (var genre in q.Genres)
            where.Add(Genre(genre));
        if (Picks("genre") && q.AnyGenres.Count > 0)
            where.Add("(" + string.Join(" OR ", q.AnyGenres.Select(Genre)) + ")");
        if (q.NotGenres.Count > 0)
            where.Add("NOT (" + string.Join(" OR ", q.NotGenres.Select(Genre)) + ")");
        if (q.From is { } yearFrom) where.Add($"t.year >= {Bind(yearFrom)}");
        if (q.To is { } yearTo) where.Add($"t.year <= {Bind(yearTo)}");
        if (Picks("decade") && q.Decades.Count > 0)
            where.Add($"t.year / 10 * 10 IN ({string.Join(", ", q.Decades.Select(d => Bind(d)))})");
        if (q.Released is not null) where.Add($"t.released >= {Bind(q.Released)}");
        if (q.ReleasedBefore is not null) where.Add($"t.released < {Bind(q.ReleasedBefore)}");
        if (Picks("length") && q.Lengths.Count > 0)
            where.Add("(" + string.Join(" OR ", q.Lengths.Select(l => l switch
            {
                "short" => "t.runtime < 90",
                "standard" => "t.runtime BETWEEN 90 AND 150",
                _ => "t.runtime > 150",
            })) + ")");
        if (q.MinRuntime is { } minRuntime) where.Add($"t.runtime >= {Bind(minRuntime)}");
        if (q.MaxRuntime is { } maxRuntime) where.Add($"t.runtime <= {Bind(maxRuntime)}");
        if (Picks("language") && q.Languages.Count > 0)
            where.Add($"t.original_language IN ({string.Join(", ", q.Languages.Select(l => Bind(l)))})");
        if (q.Countries.Count > 0)
            where.Add($"EXISTS (SELECT 1 FROM title_countries x WHERE x.title_id = t.id AND x.country IN ({string.Join(", ", q.Countries.Select(x => Bind(x)))}))");
        // A keyword, a company, a person or a fact names a few hundred titles at most, so its titles drive
        // the query: checked title by title, it would read every listed title a broad filter beside it keeps.
        foreach (var keyword in q.Keywords)
            where.Add($"t.id IN (SELECT x.title_id FROM title_keywords x WHERE x.keyword_id = {Bind(keyword)})");
        if (q.AnyKeywords.Count > 0)
            where.Add($"t.id IN (SELECT x.title_id FROM title_keywords x WHERE x.keyword_id IN ({string.Join(", ", q.AnyKeywords.Select(x => Bind(x)))}))");
        if (q.Companies.Count > 0)
            where.Add($"t.id IN (SELECT x.title_id FROM title_companies x WHERE x.company_id IN ({string.Join(", ", q.Companies.Select(x => Bind(x)))}))");
        foreach (var person in q.People)
        {
            var department = q.Role switch
            {
                "cast" => " AND x.department = 'cast'",
                "directing" => " AND x.department = 'Directing'",
                "writing" => " AND x.department = 'Writing'",
                _ => "",
            };
            where.Add($"t.id IN (SELECT x.title_id FROM credits x WHERE x.person_id = {Bind(person)}{department})");
        }
        if (Picks("rating") && q.MinRating is { } minRating) where.Add($"t.rating >= {Bind(minRating)}");
        // A range of votes is checked on each title, never searched by (the unary plus): most listed
        // titles pass any floor a shelf sets, and started from the votes index a list reads every one.
        if (q.MinVotes is { } minVotes) where.Add($"+t.votes >= {Bind(minVotes)}");
        if (q.MaxVotes is { } maxVotes) where.Add($"+t.votes <= {Bind(maxVotes)}");
        if (q.MaxRating is { } maxRating) where.Add($"t.rating <= {Bind(maxRating)}");
        if (q.Status is not null) where.Add($"t.status = {Bind(q.Status)}");
        if (q.MaxSeasons is { } maxSeasons) where.Add($"t.seasons <= {Bind(maxSeasons)}");
        foreach (var (property, item) in q.Facts)
            where.Add($"t.id IN (SELECT x.title_id FROM title_facts x WHERE x.property = {Bind(property)} AND x.item = {Bind(item)})");
        if (q.Except is { Count: > 0 } except) where.Add($"t.imdb_id NOT IN (SELECT value FROM json_each({Bind(JsonSerializer.Serialize(except.ToArray(), StoreJson.Default.StringArray))}))");

        var order = q.Order switch
        {
            "order" when q.Ordered is not null => "o.key",
            "order" when q.Ranking is not null => "k.rank",
            "order" when q.SimilarTo is not null => "s.rank",
            "order" => "coalesce(t.released, t.year || '-12-31') NULLS LAST",
            // A title named exactly what was typed comes first, the blend ordering the rest: a widely released
            // title's names in every language lengthen its row, which full-text scoring holds against it.
            "match" => $"(t.english_title = {Bind(q.Words!.Trim())} COLLATE NOCASE OR t.original_title = {Bind(q.Words!.Trim())} COLLATE NOCASE) DESC, "
                + $"m.score - {KnownWeight.ToString(CultureInfo.InvariantCulture)} * known(t.votes)",
            "rating" => $"(t.votes * t.rating + {RatingWeight} * {RatingPrior.ToString(CultureInfo.InvariantCulture)}) / (t.votes + {RatingWeight}.0) DESC NULLS LAST",
            "lowest" => $"(t.votes * t.rating + {RatingWeight} * {RatingPrior.ToString(CultureInfo.InvariantCulture)}) / (t.votes + {RatingWeight}.0) NULLS LAST",
            "newest" => "coalesce(t.released, t.year || '-12-31') DESC NULLS LAST",
            "oldest" => "coalesce(t.released, t.year || '-01-01') NULLS LAST",
            "title" => "t.english_title COLLATE NOCASE",
            "votes" => "t.votes DESC NULLS LAST",
            // The order titles_popularity holds, so a list's first page is read down it.
            _ => "t.popularity DESC NULLS LAST, t.votes DESC NULLS LAST",
        };
        if (q.Order is "rating" or "lowest") where.Add("t.rating IS NOT NULL");
        return new Query(from.ToString(), string.Join(" AND ", where), order + ", t.id", args.ToArray());
    }

    /// <summary>
    /// Words as a full-text match: every word, each as the start of a word in a name, so "fast furi"
    /// finds The Fast and the Furious. Null when there are no words.
    /// </summary>
    public static string? Match(string words)
    {
        var tokens = new List<string>();
        var token = new StringBuilder();
        foreach (var ch in words.Normalize(NormalizationForm.FormC))
        {
            if (char.IsLetterOrDigit(ch)) token.Append(ch);
            else if (token.Length > 0) { tokens.Add(token.ToString()); token.Clear(); }
        }
        if (token.Length > 0) tokens.Add(token.ToString());
        return tokens.Count == 0 ? null : string.Join(' ', tokens.Take(12).Select(t => $"\"{t}\"*"));
    }

    // ---- Cards

    private const string CardColumns = $"""
        t.id, t.imdb_id, t.kind, t.english_title, t.original_title, t.year, t.end_year, t.released, t.runtime, t.category,
        t.original_language, t.rating, t.votes, t.rated_by, t.poster, t.backdrop, t.overview, t.changed_at, t.genres,
        (SELECT group_concat(name, char(31)) FROM (SELECT p.name FROM credits x JOIN people p ON p.id = x.person_id
            WHERE x.title_id = t.id AND x.role IN ('Director', 'Creator') ORDER BY x.ord LIMIT 2)),
        (SELECT group_concat(name, char(31)) FROM (SELECT p.name FROM credits x JOIN people p ON p.id = x.person_id
            WHERE x.title_id = t.id AND x.department = 'cast' ORDER BY x.ord LIMIT 2))
        """;

    /// <summary>The cards of titles by the catalogue's ids, in the order given, with one person's part in each where named.</summary>
    private static List<TitleCard> Cards(SqliteConnection c, List<long> ids, long? person)
    {
        if (ids.Count == 0) return [];
        var list = JsonSerializer.Serialize(ids.ToArray(), StoreJson.Default.Int64Array);
        var cards = c.Query($"SELECT {CardColumns} FROM titles t WHERE t.id IN (SELECT value FROM json_each($ids))",
            r => (Id: r.GetInt64(0), Card: Card(r)), ("$ids", list)).ToDictionary(x => x.Id, x => x.Card);
        if (person is { } p)
        {
            var credits = c.Query($"""
                SELECT title_id, department, group_concat(role, ' · ') FROM credits
                WHERE person_id = $p AND title_id IN (SELECT value FROM json_each($ids)) GROUP BY title_id, department
                """, r => (Id: r.GetInt64(0), Credit: new Credit(r.GetString(1), r.IsDBNull(2) ? "" : r.GetString(2))), ("$p", p), ("$ids", list));
            foreach (var (id, credit) in credits)
                if (cards.TryGetValue(id, out var card) && card.Credit is null) cards[id] = card with { Credit = credit };
        }
        return ids.Where(cards.ContainsKey).Select(id => cards[id]).ToList();
    }

    private static TitleCard Card(SqliteDataReader r)
    {
        var title = r.GetString(3);
        var original = r.GetStringOrNull(4);
        return new TitleCard
        {
            Id = r.GetString(1),
            Kind = r.GetString(2),
            Title = title,
            OriginalTitle = original is not null && original != title ? original : null,
            Year = r.GetIntOrNull(5),
            EndYear = r.GetIntOrNull(6),
            Released = r.GetStringOrNull(7),
            Runtime = r.GetIntOrNull(8),
            Category = r.GetStringOrNull(9) ?? "general",
            Language = r.GetStringOrNull(10),
            Rating = r.IsDBNull(13) ? null : new Rating(r.GetDoubleOrNull(11), r.IsDBNull(12) ? 0 : r.GetInt32(12), r.GetString(13)),
            Poster = ImageUrls.Of(r.GetStringOrNull(14), ImageUrls.Poster),
            Backdrop = ImageUrls.Of(r.GetStringOrNull(15), ImageUrls.Backdrop),
            Plot = Plot(r.GetStringOrNull(16)),
            ChangedAt = r.IsDBNull(17) ? null : Sql.ReadInstant(r.GetString(17)),
            Genres = r.GetString(18) is { Length: > 0 } genres ? genres.Split(',').ToList() : [],
            MadeBy = Split(r.GetStringOrNull(19)),
            Starring = Split(r.GetStringOrNull(20)),
        };
    }

    private static List<string> Split(string? joined) => joined is null ? [] : joined.Split(Unit).ToList();

    /// <summary>An overview's first sentence, at most <see cref="PlotLength"/> characters, cut at a word.</summary>
    public static string? Plot(string? overview)
    {
        if (string.IsNullOrWhiteSpace(overview)) return null;
        var text = overview.Trim();
        for (var i = 0; i < text.Length - 1; i++)
            if (text[i] is '.' or '!' or '?' && text[i + 1] == ' ' && i > 20)
            {
                text = text[..(i + 1)];
                break;
            }
        if (text.Length <= PlotLength) return text;
        var cut = text.LastIndexOf(' ', PlotLength - 1);
        return text[..(cut > 0 ? cut : PlotLength - 1)].TrimEnd(',', ';', ':') + "…";
    }

    // ---- A title

    public TitlePage? Title(string imdbId) => reader.Read(c =>
    {
        var id = c.First("SELECT id FROM titles WHERE imdb_id = $imdb AND listed = 1", r => (long?)r.GetInt64(0), ("$imdb", imdbId));
        if (id is not { } titleId) return null;
        var card = Cards(c, [titleId], null)[0];
        var row = c.First("""
            SELECT overview, tagline, status, logo, budget, revenue, next_air_date, next_season, next_number, next_name
            FROM titles WHERE id = $id
            """, r => (Overview: r.GetStringOrNull(0), Tagline: r.GetStringOrNull(1), Status: r.GetStringOrNull(2), Logo: r.GetStringOrNull(3),
                Budget: r.IsDBNull(4) ? (long?)null : r.GetInt64(4), Revenue: r.IsDBNull(5) ? (long?)null : r.GetInt64(5),
                NextAir: r.GetStringOrNull(6), NextSeason: r.GetIntOrNull(7), NextNumber: r.GetIntOrNull(8), NextName: r.GetStringOrNull(9)),
            ("$id", titleId));

        Rating? RatingBy(string source) => c.First("SELECT rating, votes FROM ratings WHERE title_id = $id AND source = $source",
            r => new Rating(r.GetInt32(1) >= 25 ? r.GetDouble(0) : null, r.GetInt32(1), source), ("$id", titleId), ("$source", source));
        List<Named> Facts(string property) => c.Query(
            "SELECT item, coalesce(label, item) FROM title_facts WHERE title_id = $id AND property = $p ORDER BY label",
            r => new Named(r.GetString(0), r.GetString(1)), ("$id", titleId), ("$p", property));
        List<TitleCard> Linked(string property) => Cards(c, c.Query("""
            SELECT o.id FROM title_facts f JOIN titles o ON o.wikidata_id = f.item AND o.listed = 1
            WHERE f.title_id = $id AND f.property = $p ORDER BY o.released
            """, r => r.GetInt64(0), ("$id", titleId), ("$p", property)), null);

        var basedOn = Facts("P144");
        var authors = Facts("P144/P50");
        var similar = c.Query($"""
            SELECT o.id FROM similar s JOIN titles o ON o.kind = s.kind AND o.tmdb_id = s.tmdb_id AND o.listed = 1
            WHERE s.title_id = $id ORDER BY s.rank LIMIT {SimilarShown}
            """, r => r.GetInt64(0), ("$id", titleId));

        return new TitlePage
        {
            Id = card.Id, Kind = card.Kind, Title = card.Title, OriginalTitle = card.OriginalTitle, Year = card.Year, EndYear = card.EndYear,
            Released = card.Released, Runtime = card.Runtime, Category = card.Category, Genres = card.Genres, Language = card.Language,
            Rating = card.Rating, Poster = card.Poster, Backdrop = card.Backdrop, Plot = card.Plot, MadeBy = card.MadeBy,
            Starring = card.Starring, ChangedAt = card.ChangedAt,
            Overview = row.Overview,
            Tagline = row.Tagline,
            Status = row.Status,
            Logo = ImageUrls.Of(row.Logo, ImageUrls.Logo),
            Ratings = new SourceRatings(RatingBy("imdb"), RatingBy("tmdb")),
            Certificates = c.Query("SELECT country, certificate FROM title_releases WHERE title_id = $id AND certificate IS NOT NULL",
                r => (r.GetString(0), r.GetString(1)), ("$id", titleId)).ToDictionary(x => x.Item1, x => x.Item2),
            Countries = c.Query("SELECT country FROM title_countries WHERE title_id = $id ORDER BY country", r => r.GetString(0), ("$id", titleId)),
            Languages = c.Query("SELECT language FROM title_languages WHERE title_id = $id ORDER BY language", r => r.GetString(0), ("$id", titleId)),
            Companies = c.Query("""
                SELECT 's' || o.id, o.name FROM title_companies x JOIN companies o ON o.id = x.company_id
                WHERE x.title_id = $id AND x.role = 'production' ORDER BY o.name
                """, r => new Named(r.GetString(0), r.GetString(1)), ("$id", titleId)),
            Networks = c.Query("""
                SELECT 's' || o.id, o.name FROM title_companies x JOIN companies o ON o.id = x.company_id
                WHERE x.title_id = $id AND x.role = 'network' ORDER BY o.name
                """, r => new Named(r.GetString(0), r.GetString(1)), ("$id", titleId)),
            Keywords = c.Query("""
                SELECT 'k' || k.id, k.name FROM title_keywords x JOIN keywords k ON k.id = x.keyword_id WHERE x.title_id = $id ORDER BY k.name
                """, r => new Named(r.GetString(0), r.GetString(1)), ("$id", titleId)),
            Cast = c.Query("""
                SELECT p.id, p.name, nullif(x.role, ''), (SELECT i.path FROM images i WHERE i.path = p.profile AND i.size = 'w185') FROM credits x JOIN people p ON p.id = x.person_id
                WHERE x.title_id = $id AND x.department = 'cast' ORDER BY x.ord
                """, r => new CastMember(r.GetInt64(0), r.GetString(1), r.GetStringOrNull(2), ImageUrls.Of(r.GetStringOrNull(3), ImageUrls.Portrait)), ("$id", titleId)),
            Crew = c.Query("""
                SELECT p.id, p.name, x.role FROM credits x JOIN people p ON p.id = x.person_id
                WHERE x.title_id = $id AND x.department <> 'cast' ORDER BY x.ord
                """, r => new CrewMember(r.GetInt64(0), r.GetString(1), r.GetString(2)), ("$id", titleId)),
            Trailers = c.Query("SELECT key, name, type FROM title_videos WHERE title_id = $id ORDER BY ord",
                r => new Trailer(r.GetString(0), r.GetString(1), r.GetString(2)), ("$id", titleId)),
            Facts = new Facts
            {
                SetIn = Facts("P840"),
                FilmedIn = Facts("P915"),
                BasedOn = basedOn.Select(w => new Source(w, authors.Count > 0 && basedOn.Count == 1 ? authors : [])).ToList(),
                Awards = Facts("P166"),
                Nominations = (int)c.Count("SELECT count(*) FROM title_facts WHERE title_id = $id AND property = 'P1411'", ("$id", titleId)),
                Subjects = Facts("P921"),
            },
            Franchises = c.Query("""
                SELECT CASE f.source WHEN 'tmdb' THEN 'c' || f.source_id ELSE f.source_id END, f.name, ft.ordinal
                FROM franchise_titles ft JOIN franchises f ON f.id = ft.franchise_id WHERE ft.title_id = $id ORDER BY f.source DESC, f.name
                """, r => new FranchiseRef(r.GetString(0), r.GetString(1), r.GetStringOrNull(2)), ("$id", titleId)),
            Follows = Linked("P155"),
            FollowedBy = Linked("P156"),
            Budget = row.Budget,
            Revenue = row.Revenue,
            Seasons = c.Query("SELECT number, name, air_date, (SELECT i.path FROM images i WHERE i.path = poster AND i.size = 'w500'), episodes FROM seasons WHERE title_id = $id ORDER BY number",
                r => new SeasonSummary(r.GetInt32(0), r.GetStringOrNull(1), r.GetStringOrNull(2), ImageUrls.Of(r.GetStringOrNull(3), ImageUrls.Poster), r.GetInt32(4)),
                ("$id", titleId)),
            NextEpisode = row is { NextAir: { } air, NextSeason: { } season, NextNumber: { } number } ? new NextEpisode(season, number, row.NextName, air) : null,
            Similar = Cards(c, similar, null),
        };
    });

    public Season? Season(string imdbId, int number) => reader.Read(c =>
    {
        var season = c.First("""
            SELECT s.title_id, s.name, s.overview, s.air_date, (SELECT i.path FROM images i WHERE i.path = s.poster AND i.size = 'w500') FROM seasons s JOIN titles t ON t.id = s.title_id
            WHERE t.imdb_id = $imdb AND t.listed = 1 AND s.number = $n
            """, r => (Id: r.GetInt64(0), Name: r.GetStringOrNull(1), Overview: r.GetStringOrNull(2), Air: r.GetStringOrNull(3), Poster: r.GetStringOrNull(4)),
            ("$imdb", imdbId), ("$n", number));
        if (season.Id == 0) return null;
        var episodes = c.Query("""
            SELECT number, name, overview, air_date, runtime, (SELECT i.path FROM images i WHERE i.path = still AND i.size = 'w300') FROM episodes WHERE title_id = $id AND season = $n ORDER BY number
            """, r => new Episode(r.GetInt32(0), r.GetStringOrNull(1), r.GetStringOrNull(2), r.GetStringOrNull(3), r.GetIntOrNull(4),
                ImageUrls.Of(r.GetStringOrNull(5), ImageUrls.Still)), ("$id", season.Id), ("$n", number));
        return new Season(number, season.Name, season.Overview, season.Air, ImageUrls.Of(season.Poster, ImageUrls.Poster), episodes);
    });

    /// <summary>Every season of a listed show with its episodes, in order; empty for a title with none.</summary>
    public List<Season> Seasons(string imdbId) => reader.Read(c =>
    {
        var id = c.First("SELECT id FROM titles WHERE imdb_id = $imdb AND listed = 1", r => (long?)r.GetInt64(0), ("$imdb", imdbId));
        if (id is not { } titleId) return [];
        var episodes = c.Query("""
            SELECT season, number, name, overview, air_date, runtime, (SELECT i.path FROM images i WHERE i.path = still AND i.size = 'w300') FROM episodes WHERE title_id = $id ORDER BY season, number
            """, r => (Season: r.GetInt32(0), Episode: new Episode(r.GetInt32(1), r.GetStringOrNull(2), r.GetStringOrNull(3), r.GetStringOrNull(4),
                r.GetIntOrNull(5), ImageUrls.Of(r.GetStringOrNull(6), ImageUrls.Still))), ("$id", titleId))
            .ToLookup(e => e.Season, e => e.Episode);
        return c.Query("SELECT number, name, overview, air_date, (SELECT i.path FROM images i WHERE i.path = poster AND i.size = 'w500') FROM seasons WHERE title_id = $id ORDER BY number",
            r => new Season(r.GetInt32(0), r.GetStringOrNull(1), r.GetStringOrNull(2), r.GetStringOrNull(3), ImageUrls.Of(r.GetStringOrNull(4), ImageUrls.Poster),
                episodes[r.GetInt32(0)].ToList()), ("$id", titleId));
    });

    /// <summary>The cards of titles by IMDb id, in the order given; an id the catalogue does not list is left out.</summary>
    public List<TitleCard> CardsOf(IReadOnlyList<string> imdbIds) => reader.Read(c =>
    {
        var ids = c.Query("SELECT t.id, t.imdb_id FROM titles t WHERE t.listed = 1 AND t.imdb_id IN (SELECT value FROM json_each($ids))",
            r => (r.GetInt64(0), r.GetString(1)), ("$ids", JsonSerializer.Serialize(imdbIds.ToArray(), StoreJson.Default.StringArray)))
            .ToDictionary(x => x.Item2, x => x.Item1);
        return Cards(c, imdbIds.Where(ids.ContainsKey).Select(i => ids[i]).ToList(), null);
    });

    // ---- People, franchises, words

    public Person? Person(long id) => reader.Read(c => c.First("""
        SELECT p.id, p.name, (SELECT i.path FROM images i WHERE i.path = p.profile AND i.size = 'w185'), p.department,
               (SELECT count(DISTINCT x.title_id) FROM credits x JOIN titles t ON t.id = x.title_id AND t.listed = 1 WHERE x.person_id = p.id)
        FROM people p WHERE p.id = $id
        """, r => new Person(r.GetInt64(0), r.GetString(1), ImageUrls.Of(r.GetStringOrNull(2), ImageUrls.Portrait), r.GetStringOrNull(3), r.GetInt32(4)),
        ("$id", id)));

    private const string FranchiseColumns = """
        f.id, CASE f.source WHEN 'tmdb' THEN 'c' || f.source_id ELSE f.source_id END, f.source, f.name,
        coalesce(f.poster, (SELECT t.poster FROM franchise_titles x JOIN titles t ON t.id = x.title_id AND t.listed = 1
                            WHERE x.franchise_id = f.id ORDER BY t.released LIMIT 1)),
        coalesce(f.backdrop, (SELECT t.backdrop FROM franchise_titles x JOIN titles t ON t.id = x.title_id AND t.listed = 1
                              WHERE x.franchise_id = f.id ORDER BY t.votes DESC LIMIT 1)),
        count(t.id), min(t.year), max(t.year)
        """;

    private static FranchiseCard FranchiseOf(SqliteDataReader r) => new(
        r.GetString(1), r.GetString(2) == "tmdb" ? "collection" : "universe", r.GetString(3),
        ImageUrls.Of(r.GetStringOrNull(4), ImageUrls.Poster), ImageUrls.Of(r.GetStringOrNull(5), ImageUrls.Backdrop),
        r.GetInt32(6), r.GetIntOrNull(7), r.GetIntOrNull(8));

    /// <summary>
    /// The franchises with at least <paramref name="minTitles"/> listed titles, the most voted first, a page
    /// at a time; <paramref name="kind"/> is <c>collection</c> or <c>universe</c> for one kind alone. Of both
    /// kinds, a collection a listed universe holds <see cref="PartOf"/> of the listed titles of is listed as
    /// that universe's part alone.
    /// </summary>
    public FranchisePage Franchises(int page, int size, string? kind = null, int minTitles = 2) => reader.Read(c =>
    {
        var source = kind switch { "collection" => "tmdb", "universe" => "wikidata", _ => null };
        const string Grouped = """
            FROM franchises f JOIN franchise_titles ft ON ft.franchise_id = f.id JOIN titles t ON t.id = ft.title_id AND t.listed = 1
            WHERE ($source IS NULL OR f.source = $source)
              AND ($source IS NOT NULL OR NOT EXISTS (
                  SELECT 1 FROM franchise_parts pa WHERE pa.collection_id = f.id
                  AND (SELECT count(*) FROM franchise_titles u JOIN titles ut ON ut.id = u.title_id AND ut.listed = 1
                       WHERE u.franchise_id = pa.universe_id) >= $min
                  AND (SELECT count(*) FROM franchise_titles x JOIN titles xt ON xt.id = x.title_id AND xt.listed = 1
                       JOIN franchise_titles u ON u.franchise_id = pa.universe_id AND u.title_id = x.title_id
                       WHERE x.franchise_id = f.id)
                      >= $part * (SELECT count(*) FROM franchise_titles x JOIN titles xt ON xt.id = x.title_id AND xt.listed = 1
                                  WHERE x.franchise_id = f.id)))
            GROUP BY f.id HAVING count(t.id) >= $min
            """;
        var total = (int)c.Count($"SELECT count(*) FROM (SELECT f.id {Grouped})", ("$source", source), ("$min", minTitles), ("$part", PartOf));
        var offset = (page - 1) * size;
        var rows = c.Query($"SELECT {FranchiseColumns} {Grouped} ORDER BY sum(t.votes) DESC, f.id LIMIT {size} OFFSET {offset}",
            FranchiseOf, ("$source", source), ("$min", minTitles), ("$part", PartOf));
        return new FranchisePage(rows, total, offset + rows.Count < total ? page + 1 : null);
    });

    /// <summary>
    /// Every franchise's listed titles in its order (by release, as <c>sort=order</c> lists them), by the
    /// franchise's id: each title's IMDb id, kind and poster. One read for every franchise at once.
    /// </summary>
    public Dictionary<string, List<FranchiseEntry>> FranchiseEntries() => reader.Read(c => c.Query("""
        SELECT CASE f.source WHEN 'tmdb' THEN 'c' || f.source_id ELSE f.source_id END, t.imdb_id, t.kind, t.poster
        FROM franchise_titles ft JOIN franchises f ON f.id = ft.franchise_id JOIN titles t ON t.id = ft.title_id AND t.listed = 1
        ORDER BY f.id, coalesce(t.released, t.year || '-12-31'), t.id
        """, r => (Franchise: r.GetString(0), Entry: new FranchiseEntry(r.GetString(1), r.GetString(2), ImageUrls.Of(r.GetStringOrNull(3), ImageUrls.Poster))))
        .GroupBy(x => x.Franchise, x => x.Entry)
        .ToDictionary(g => g.Key, g => g.ToList()));

    public Franchise? Franchise(string id) => reader.Read(c =>
    {
        var (source, sourceId) = id.StartsWith('c') ? ("tmdb", id[1..]) : ("wikidata", id);
        var card = c.First($"""
            SELECT {FranchiseColumns} FROM franchises f
            LEFT JOIN franchise_titles ft ON ft.franchise_id = f.id LEFT JOIN titles t ON t.id = ft.title_id AND t.listed = 1
            WHERE f.source = $source AND f.source_id = $id GROUP BY f.id
            """, FranchiseOf, ("$source", source), ("$id", sourceId));
        if (card is null) return null;
        var parts = c.Query($"""
            SELECT {FranchiseColumns} FROM franchise_parts pa JOIN franchises u ON u.id = pa.universe_id
            JOIN franchises f ON f.id = pa.collection_id
            LEFT JOIN franchise_titles ft ON ft.franchise_id = f.id LEFT JOIN titles t ON t.id = ft.title_id AND t.listed = 1
            WHERE u.source = $source AND u.source_id = $id GROUP BY f.id ORDER BY min(t.released)
            """, FranchiseOf, ("$source", source), ("$id", sourceId));
        return new Franchise(card, parts);
    });

    /// <summary>The catalogue's people by the film database's person ids, for lists named by those ids.</summary>
    public Dictionary<int, long> PeopleByTmdb(IReadOnlyCollection<int> tmdbIds) => reader.Read(c => c.Query(
        "SELECT tmdb_id, id FROM people WHERE tmdb_id IN (SELECT value FROM json_each($ids))",
        r => (r.GetInt32(0), r.GetInt64(1)),
        ("$ids", JsonSerializer.Serialize(tmdbIds.Select(i => (long)i).ToArray(), StoreJson.Default.Int64Array))).ToDictionary(x => x.Item1, x => x.Item2));

    /// <summary>The rankings the catalogue holds, by name.</summary>
    public List<string> Rankings() => reader.Read(c => c.Query("SELECT DISTINCT list FROM rankings ORDER BY list", r => r.GetString(0)));

    /// <summary>The genres listed entries have, the most held first. Read once an hour: genres come and go over years.</summary>
    public List<string> Genres() => reader.Read(GenresKnown);

    private (List<string> Genres, DateTimeOffset At) _genres = ([], DateTimeOffset.MinValue);

    private List<string> GenresKnown(SqliteConnection c)
    {
        var held = _genres;
        if (DateTimeOffset.UtcNow - held.At < TimeSpan.FromHours(1) && held.Genres.Count > 0) return held.Genres;
        var genres = c.Query("""
            SELECT g.genre FROM title_genres g JOIN titles t ON t.id = g.title_id AND t.listed = 1 WHERE g.source = 'imdb'
            GROUP BY g.genre ORDER BY count(*) DESC
            """, r => r.GetString(0));
        _genres = (genres, DateTimeOffset.UtcNow);
        return genres;
    }

    private static readonly (string Property, string Filter, string Detail)[] FactTerms =
    [
        ("P840", "fact", "set in"), ("P915", "fact", "filmed in"), ("P144/P50", "fact", "adapted from"),
        ("P166", "fact", "award"), ("P921", "fact", "about"), ("P136", "fact", "genre"), ("P179", "fact", "series"),
    ];

    /// <summary>
    /// Words resolved to what the list query takes, at most <paramref name="each"/> of each kind, the
    /// best known first: genres, people, keywords, companies, Wikidata items and franchises.
    /// </summary>
    public List<Term> Terms(string words, int each = 6) => reader.Read(c =>
    {
        var terms = new List<Term>();
        var text = words.Trim();
        if (text.Length == 0) return terms;
        var like = "%" + text.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_") + "%";

        terms.AddRange(GenresKnown(c).Where(g => g.Contains(text, StringComparison.OrdinalIgnoreCase)).Take(each)
            .Select(g => new Term("genre", g, g, null)));
        if (Match(text) is { } match)
            terms.AddRange(c.Query("""
                SELECT p.id, p.name, p.department FROM people_fts f JOIN people p ON p.id = f.rowid
                WHERE people_fts MATCH $match ORDER BY (SELECT count(*) FROM credits x WHERE x.person_id = p.id) DESC LIMIT $n
                """, r => new Term("person", r.GetInt64(0).ToString(CultureInfo.InvariantCulture), r.GetString(1), r.GetStringOrNull(2)),
                ("$match", match), ("$n", each)));
        terms.AddRange(c.Query("""
            SELECT 'k' || k.id, k.name FROM keywords k WHERE k.name LIKE $like ESCAPE '\'
            ORDER BY (SELECT count(*) FROM title_keywords x WHERE x.keyword_id = k.id) DESC LIMIT $n
            """, r => new Term("keyword", r.GetString(0), r.GetString(1), null), ("$like", like), ("$n", each)));
        terms.AddRange(c.Query("""
            SELECT 's' || o.id, o.name, o.country FROM companies o WHERE o.name LIKE $like ESCAPE '\'
            ORDER BY (SELECT count(*) FROM title_companies x WHERE x.company_id = o.id) DESC LIMIT $n
            """, r => new Term("company", r.GetString(0), r.GetString(1), r.GetStringOrNull(2)), ("$like", like), ("$n", each)));
        var facts = c.Query("""
            SELECT property, item, label FROM fact_items WHERE label LIKE $like ESCAPE '\' ORDER BY titles DESC
            """, r => (Property: r.GetString(0), Item: r.GetString(1), Label: r.GetString(2)), ("$like", like));
        foreach (var (property, filter, detail) in FactTerms)
            terms.AddRange(facts.Where(f => f.Property == property).Take(each).Select(f => new Term(filter, $"{property}:{f.Item}", f.Label, detail)));
        terms.AddRange(c.Query("""
            SELECT CASE f.source WHEN 'tmdb' THEN 'c' || f.source_id ELSE f.source_id END, f.name FROM franchises f
            WHERE f.name LIKE $like ESCAPE '\' LIMIT $n
            """, r => new Term("franchise", r.GetString(0), r.GetString(1), null), ("$like", like), ("$n", each)));
        return terms;
    });
}
