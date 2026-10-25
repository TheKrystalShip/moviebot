using System.Globalization;
using TheKrystalShip.MovieBot.Catalogue;

namespace TheKrystalShip.MovieBot.Api.Catalogue;

/// <summary>
/// Words typed into Discover, read as a question (<see cref="QuestionReader"/>) and answered as the list
/// query's filters: <c>query</c> is the parameters the site asks <c>/api/catalogue/titles</c> with, and
/// <c>parts</c> what the words were read as. Words that are no question say so, and a question naming
/// something the catalogue does not know says what, in <c>message</c>, with no query: a list that
/// dropped the unknown name would answer a question nobody asked.
/// </summary>
public sealed record CatalogueAnswer(bool Question, List<QuestionPart> Parts, Dictionary<string, List<string>>? Query, string? Message);

public sealed class CatalogueQuestions(CatalogueQueries queries, TimeProvider clock)
{
    private static readonly Dictionary<WikidataRelation, string> Properties = new()
    {
        [WikidataRelation.Won] = "P166",
        [WikidataRelation.Nominated] = "P1411",
        [WikidataRelation.Adaptation] = "P144/P50",
        [WikidataRelation.Set] = "P840",
        [WikidataRelation.Filmed] = "P915",
        [WikidataRelation.Genre] = "P136",
        [WikidataRelation.Subject] = "P921",
    };

    public CatalogueAnswer Ask(string words)
    {
        var vocabulary = new QuestionVocabulary
        {
            FilmGenres = queries.Genres().Select(g => new GenreName(g)).ToList(),
            ShowGenres = queries.Genres().Select(g => new GenreName(g)).ToList(),
            Languages = LanguageNames.All.Select(kv => new LanguageName(kv.Key, kv.Value)).ToList(),
        };
        var question = QuestionReader.Read(words, DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime), vocabulary);
        if (question is null) return new CatalogueAnswer(false, [], null, null);

        var request = question.Request;
        var parts = question.Parts.ToList();
        var query = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        void Put(string name, string? value)
        {
            if (value is null) return;
            if (!query.TryGetValue(name, out var values)) query[name] = values = [];
            if (!values.Contains(value)) values.Add(value);
        }
        string Number(int n) => n.ToString(CultureInfo.InvariantCulture);
        CatalogueAnswer Unknown(string what) => new(true, parts, null, what);

        Put("kind", request.Kind);
        Put("category", request.Category);
        foreach (var genre in request.Genres) Put("genre", genre);
        foreach (var genre in request.Exclude) Put("notGenre", genre);
        if (request.From is { } from) Put("from", Number(from));
        if (request.To is { } to) Put("to", Number(to));
        Put("language", request.Language);
        if (request.MinRuntime is { } min) Put("minRuntime", Number(min));
        if (request.MaxRuntime is { } max) Put("maxRuntime", Number(max));
        if (request.MinVotes is { } votes) Put("minVotes", Number(votes));
        Put("sort", request.Sort);
        if (request.Count is { } count) Put("size", Number(Math.Min(count, TitleQuery.MostPerPage)));

        foreach (var name in request.With)
        {
            if (Person(name) is not { } person) return Unknown($"Nobody called {name} is in the catalogue.");
            Put("person", person);
        }
        foreach (var subject in request.About)
        {
            if (Keyword(subject) is not { } keyword) return Unknown($"Nothing in the catalogue is about {subject}.");
            Put("keyword", keyword);
        }
        foreach (var name in question.WithEither)
        {
            if (Person(name) is { } person) Put("person", person);
            else if (Keyword(name) is { } keyword) Put("keyword", keyword);
            else return Unknown($"Nobody and nothing called {name} is in the catalogue.");
        }
        if (question.Named is { } named)
        {
            if (Person(named) is { } person) Put("person", person);
            else if (Keyword(named) is { } keyword) Put("keyword", keyword);
            else if (Fact(named, null) is { } fact) Put("fact", fact);
            else return Unknown($"Nobody and nothing called {named} is in the catalogue.");
        }
        if (request.Like is { } like)
        {
            if (queries.Titles(new TitleQuery { Words = like, Size = 1 }).Titles is not [var title]) return Unknown($"No title called {like} is in the catalogue.");
            Put("similarTo", title.Id);
            if (request.Sort is null) Put("sort", "order");
        }
        if (question.Wikidata is { } ask)
        {
            var property = Properties[ask.Relation];
            var fact = ask.Item is { } item ? $"{property}:{item}" : Fact(ask.Name, property);
            if (fact is null) return Unknown($"The catalogue knows nothing {Relation(ask.Relation)} {ask.Name}.");
            Put("fact", fact);
        }
        return new CatalogueAnswer(true, parts, query, null);
    }

    /// <summary>The person a name means: one called exactly that, the most credited first; else the best match.</summary>
    private string? Person(string name)
    {
        var people = queries.Terms(name).Where(t => t.Filter == "person").ToList();
        return (people.FirstOrDefault(p => string.Equals(p.Name, name.Trim(), StringComparison.OrdinalIgnoreCase)) ?? people.FirstOrDefault())?.Value;
    }

    /// <summary>The keyword words mean: the one named exactly that, else the most used match.</summary>
    private string? Keyword(string words)
    {
        var keywords = queries.Terms(words).Where(t => t.Filter == "keyword").ToList();
        return (keywords.FirstOrDefault(k => string.Equals(k.Name, words.Trim(), StringComparison.OrdinalIgnoreCase)) ?? keywords.FirstOrDefault())?.Value;
    }

    /// <summary>The Wikidata fact a name means, of one property or any: the item named exactly that first.</summary>
    private string? Fact(string name, string? property)
    {
        var facts = queries.Terms(name).Where(t => t.Filter == "fact" && (property is null || t.Value.StartsWith(property + ":", StringComparison.Ordinal))).ToList();
        return (facts.FirstOrDefault(f => string.Equals(f.Name, name.Trim(), StringComparison.OrdinalIgnoreCase)) ?? facts.FirstOrDefault())?.Value;
    }

    private static string Relation(WikidataRelation relation) => relation switch
    {
        WikidataRelation.Won or WikidataRelation.Nominated => "of the award",
        WikidataRelation.Adaptation => "adapted from",
        WikidataRelation.Set => "set in",
        WikidataRelation.Filmed => "filmed in",
        WikidataRelation.Genre => "of the genre",
        _ => "about",
    };
}
