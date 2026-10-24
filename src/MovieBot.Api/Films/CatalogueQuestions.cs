namespace TheKrystalShip.MovieBot.Api.Films;

/// <summary>
/// What a question typed into Discover was read as and what it found: no question at all when the
/// words are a title's name, else its parts and the discovery that answers them, or why nothing does.
/// </summary>
public sealed record QuestionAnswer(bool Question, List<QuestionPart> Parts, DiscoverAnswer? Discovery, string? Message)
{
    public static readonly QuestionAnswer None = new(false, [], null, null);
}

public sealed partial class CatalogueSearch
{
    /// <summary>
    /// Answers words typed into Discover as a question of the catalogue (<see cref="QuestionReader"/>).
    /// The words before the rest that no rule reads are a person by their exact name, or by the
    /// surname of the most looked-at person of the name, else a studio a company is called exactly, else a
    /// keyword by its exact name, else a studio (<see cref="MatchStudio"/>), a studio only where no title is
    /// called exactly so, else the words are no question. A studio's question keeps
    /// every title with <see cref="StudioVotes"/> votes, as its shelf does, rather than the well rated
    /// alone. Words the reader finds no question in are still a studio's, by <see cref="BareStudioAsync"/>. What follows <c>with</c> is a person by their exact name, else a keyword
    /// by its exact name, else the database's best match for a person. A question whose discovery the
    /// database refuses (a genre a show does not have, nobody by a name after <c>starring</c>) is still a
    /// question, answered with the refusal's message and no titles. A question that names a set Wikidata
    /// lists is <see cref="AskWikidataAsync"/>'s.
    /// </summary>
    public Task<Outcome<QuestionAnswer>> AskAsync(string text, int page, CancellationToken ct) => AskAsync(text, page, DiscoverPicks.None, ct);

    /// <summary>
    /// A question answered with a filter's picks added to it (<see cref="DiscoverPicks"/>). A pick in a
    /// section the question names for itself is refused, and a question answered from a set Wikidata
    /// lists takes no picks.
    /// </summary>
    public async Task<Outcome<QuestionAnswer>> AskAsync(string text, int page, DiscoverPicks picks, CancellationToken ct)
    {
        var read = await ReadQuestionAsync(text, ct);
        if (read.Value is not { } asked) return read.Carry<QuestionAnswer>();
        if (asked.Settled is { } settled) return Outcome<QuestionAnswer>.Ok(settled);
        if (asked.Wikidata is { } listed) return await AskWikidataAsync(listed, page, picks, ct);
        if (PicksRefused(asked.Parts, picks) is { } refusal) return Invalid<QuestionAnswer>(refusal);

        var parts = asked.Parts;
        var answer = await DiscoverAsync(picks.On(asked.Request!) with { Page = page }, ct);
        if (answer.Value is not { } discovery)
            return answer.Problem is Problem.NotFound or Problem.Invalid
                ? Outcome<QuestionAnswer>.Ok(new QuestionAnswer(true, parts, null, answer.Message))
                : answer.Carry<QuestionAnswer>();

        // A name is shown as the database names it.
        var people = new Queue<ResolvedPerson>(discovery.Resolved.People);
        var keywords = new Queue<string>(discovery.Resolved.Keywords);
        var studios = new Queue<string>(discovery.Resolved.Studios);
        for (var i = 0; i < parts.Count; i++)
        {
            if (parts[i].Part == "person" && people.TryDequeue(out var person)) parts[i] = parts[i] with { Text = person.Name };
            else if (parts[i].Part == "about" && keywords.TryDequeue(out var keyword)) parts[i] = parts[i] with { Text = keyword };
            else if (parts[i].Part == "studio" && studios.TryDequeue(out var studio)) parts[i] = parts[i] with { Text = studio };
            else if (parts[i].Part == "like" && discovery.Resolved.Like is { } like) parts[i] = parts[i] with { Text = "Like " + like };
        }
        return Outcome<QuestionAnswer>.Ok(new QuestionAnswer(true, parts, discovery, null));
    }

    /// <summary>
    /// Which options each section of a question's answers holds, with a filter's picks on it
    /// (<see cref="DiscoverFacetsAsync"/>). The sections the question names for itself are not
    /// reported; words that are no question, a question nothing answers and a set Wikidata lists have
    /// no sections.
    /// </summary>
    public async Task<Outcome<CatalogueFacetsView>> AskFacetsAsync(string text, DiscoverPicks picks, CancellationToken ct)
    {
        var none = Outcome<CatalogueFacetsView>.Ok(new CatalogueFacetsView { Sections = [] });
        var read = await ReadQuestionAsync(text, ct);
        if (read.Value is not { } asked) return read.Carry<CatalogueFacetsView>();
        if (asked.Settled is not null) return none;
        if (asked.Wikidata is { } listed) return await WikidataFacetsAsync(listed, picks, ct);
        if (PicksRefused(asked.Parts, picks) is { } refusal) return Invalid<CatalogueFacetsView>(refusal);

        var facets = await DiscoverFacetsAsync(picks.On(asked.Request!), FixedBy(asked.Parts), ct);
        return facets.Problem is Problem.NotFound or Problem.Invalid ? none : facets;
    }

    /// <summary>The sections a question's parts name for it, which no filter picks in.</summary>
    internal static HashSet<string> FixedBy(IEnumerable<QuestionPart> parts) =>
        [.. parts.Select(p => p.Part switch
        {
            "kind" or "category" => FacetSections.Kind,
            "genre" => FacetSections.Genre,
            "years" => FacetSections.Decade,
            "runtime" => FacetSections.Length,
            "language" => FacetSections.Language,
            _ => null,
        }).OfType<string>()];

    private static string? PicksRefused(IEnumerable<QuestionPart> parts, DiscoverPicks picks)
    {
        var fixedBy = FixedBy(parts);
        return picks.Sections().FirstOrDefault(fixedBy.Contains) is { } section
            ? $"The question names its own {section}, so a filter does not pick one."
            : null;
    }

    /// <summary>
    /// A question read and its names resolved: the discovery that answers it, the question when a set
    /// Wikidata lists answers it, or an answer already settled (no question, or a refusal of a name).
    /// </summary>
    private sealed record ReadQuestion(List<QuestionPart> Parts, DiscoverRequest? Request, Question? Wikidata, QuestionAnswer? Settled);

    private async Task<Outcome<ReadQuestion>> ReadQuestionAsync(string text, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(text)) return Invalid<ReadQuestion>("Ask something.");
        var vocabulary = await VocabularyAsync(ct);
        if (vocabulary.Value is not { } words) return vocabulary.Carry<ReadQuestion>();
        if (QuestionReader.Read(text, Today(), words) is not { } question) return await BareStudioAsync(text, ct);

        var parts = question.Parts.ToList();
        if (question.Wikidata is not null) return Outcome<ReadQuestion>.Ok(new ReadQuestion(parts, null, question, null));

        var request = question.Request;
        if (question.Named is { } named)
        {
            var found = await NamedAsync(named, lenient: false, ct);
            if (found.Value is not { } name)
                return found.Problem == Problem.NotFound ? Settled(QuestionAnswer.None) : found.Carry<ReadQuestion>();
            request = name.Part switch
            {
                "person" => request with { With = [name.Name, .. request.With] },
                "studio" => request with { Studios = [name.Name, .. request.Studios], GoodOnly = false, MinVotes = request.MinVotes ?? StudioVotes },
                _ => request with { About = [name.Name, .. request.About] },
            };
            parts.Insert(0, new QuestionPart(name.Part, name.Name));
        }
        foreach (var either in question.WithEither)
        {
            var found = await NamedAsync(either, lenient: true, ct);
            if (found.Value is not { } name)
            {
                if (found.Problem != Problem.NotFound) return found.Carry<ReadQuestion>();
                return Settled(new QuestionAnswer(true, parts, null, found.Message));
            }
            request = name.Part == "person"
                ? request with { With = [.. request.With, name.Name] }
                : request with { About = [.. request.About, name.Name] };
            var at = parts.FindIndex(p => p.Part == "person" && p.Text == either);
            if (at >= 0) parts[at] = new QuestionPart(name.Part, name.Name);
        }
        return Outcome<ReadQuestion>.Ok(new ReadQuestion(parts, request, null, null));
    }

    private static Outcome<ReadQuestion> Settled(QuestionAnswer answer) => Outcome<ReadQuestion>.Ok(new ReadQuestion([], null, null, answer));

    /// <summary>The database's own genre, language and streaming service lists, which the reader recognises those words from.</summary>
    private async Task<Outcome<QuestionVocabulary>> VocabularyAsync(CancellationToken ct)
    {
        var films = await catalogue.GenresAsync("film", ct);
        var shows = await catalogue.GenresAsync("show", ct);
        var languages = await LanguagesAsync(ct);
        if (films is null || shows is null) return Unavailable<QuestionVocabulary>("The film database could not be asked about its genres.");
        if (languages.Value is not { } known) return languages.Carry<QuestionVocabulary>();
        return Outcome<QuestionVocabulary>.Ok(new QuestionVocabulary { FilmGenres = films, ShowGenres = shows, Languages = known });
    }

    /// <summary>The fewest titles a studio named alone answers with on its first page, so a company that made a few obscure films does not take a word that names something else.</summary>
    internal const int BareStudioTitles = 10;

    /// <summary>
    /// Words the reader finds no question in, as a studio's films (<c>blumhouse</c>): a studio by
    /// <see cref="MatchStudio"/> that no title or person is called exactly, nor a keyword unless a company
    /// is (<see cref="Studio.Exact"/>), with
    /// <see cref="BareStudioTitles"/> titles on its first page. Anything else is no question, and the
    /// words are a title's name; so is a studio the database cannot be asked about.
    /// </summary>
    private async Task<Outcome<ReadQuestion>> BareStudioAsync(string text, CancellationToken ct)
    {
        var none = Settled(QuestionAnswer.None);
        var words = string.Join(' ', text.Split(' ', StringSplitOptions.RemoveEmptyEntries));
        if ((await CompaniesAsync(words, ct)).Value is not { } companies || MatchStudio(companies, words) is not { } studio) return none;

        if (!await NoTitleCalledAsync(words, ct)) return none;
        if ((await PeopleAsync(words, ct)).Value is not { } people
            || people.Any(p => string.Equals(p.Name, words, StringComparison.OrdinalIgnoreCase))) return none;
        if (!studio.Exact && ((await KeywordsAsync(words, ct)).Value is not { } keywords
            || keywords.Any(k => string.Equals(k.Name, words, StringComparison.OrdinalIgnoreCase)))) return none;

        // Whether the words are a studio's is decided on its first page, whichever page or picks are asked.
        var request = new DiscoverRequest { Studios = [studio.Name], GoodOnly = false, MinVotes = StudioVotes };
        var answer = await DiscoverAsync(request, ct);
        if (answer.Value is not { } discovery || discovery.Hits.Count < BareStudioTitles) return none;
        return Outcome<ReadQuestion>.Ok(new ReadQuestion([new QuestionPart("studio", discovery.Resolved.Studios.FirstOrDefault() ?? studio.Name)], request, null, null));
    }

    /// <summary>Whether the database was asked and holds no title called the words (<see cref="NamedExactly"/>).</summary>
    private async Task<bool> NoTitleCalledAsync(string words, CancellationToken ct) =>
        (await SearchedAsync(words, null, ct)).Value is { } titles && !titles.Any(t => NamedExactly(t.Title, words));

    /// <summary>Whether a title is called the words, a leading article aside (<c>godfather</c> is The Godfather).</summary>
    private static bool NamedExactly(string? title, string words)
    {
        if (string.IsNullOrWhiteSpace(title)) return false;
        var name = title.Trim();
        foreach (var article in (string[])["The ", "A ", "An "])
            if (name.StartsWith(article, StringComparison.OrdinalIgnoreCase)) name = name[article.Length..];
        return string.Equals(name, words, StringComparison.OrdinalIgnoreCase) || string.Equals(title.Trim(), words, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>A name as one part of a question: <c>person</c>, <c>about</c> (a keyword) or <c>studio</c>.</summary>
    private sealed record NamedAs(string Name, string Part);

    /// <summary>A name as a person, a keyword or a studio, by the rules <see cref="AskAsync"/> gives; a studio only where the name is not <paramref name="lenient"/>.</summary>
    private async Task<Outcome<NamedAs>> NamedAsync(string words, bool lenient, CancellationToken ct)
    {
        var people = await PeopleAsync(words, ct);
        if (people.Value is not { } matches) return people.Carry<NamedAs>();
        var exact = matches.Where(p => string.Equals(p.Name, words, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(p => p.Popularity).FirstOrDefault();
        if (exact is not null) return Outcome<NamedAs>.Ok(new NamedAs(exact.Name, "person"));
        if (!words.Contains(' ') && matches.OrderByDescending(p => p.Popularity).FirstOrDefault() is { } best
            && string.Equals(best.Name.Split(' ')[^1], words, StringComparison.OrdinalIgnoreCase))
            return Outcome<NamedAs>.Ok(new NamedAs(best.Name, "person"));

        Studio? studio = null;
        if (!lenient)
        {
            var companies = await CompaniesAsync(words, ct);
            if (companies.Value is not { } listed) return companies.Carry<NamedAs>();
            if (MatchStudio(listed, words) is { } matched && await NoTitleCalledAsync(words, ct)) studio = matched;
        }
        if (studio is { Exact: true }) return Outcome<NamedAs>.Ok(new NamedAs(studio.Name, "studio"));

        var keywords = await KeywordsAsync(words, ct);
        if (keywords.Value is not { } found) return keywords.Carry<NamedAs>();
        if (found.FirstOrDefault(k => string.Equals(k.Name, words, StringComparison.OrdinalIgnoreCase)) is { } keyword)
            return Outcome<NamedAs>.Ok(new NamedAs(keyword.Name, "about"));

        if (studio is not null) return Outcome<NamedAs>.Ok(new NamedAs(studio.Name, "studio"));

        if (lenient && PickPerson(matches, words) is { } person) return Outcome<NamedAs>.Ok(new NamedAs(person.Name, "person"));
        return NotFound<NamedAs>(lenient
            ? $"The film database has nobody and no keyword called \"{words}\"."
            : $"The film database has nobody, no keyword and no studio called \"{words}\".");
    }
}
