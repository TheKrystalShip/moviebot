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
    /// surname of the most looked-at person of the name, else a keyword by its exact name, else the
    /// words are no question. What follows <c>with</c> is a person by their exact name, else a keyword
    /// by its exact name, else the database's best match for a person. A question whose discovery the
    /// database refuses (a genre a show does not have, nobody by a name after <c>starring</c>) is still a
    /// question, answered with the refusal's message and no titles. A question that names a set Wikidata
    /// lists is <see cref="AskWikidataAsync"/>'s.
    /// </summary>
    public async Task<Outcome<QuestionAnswer>> AskAsync(string text, int page, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(text)) return Invalid<QuestionAnswer>("Ask something.");
        var vocabulary = await VocabularyAsync(ct);
        if (vocabulary.Value is not { } words) return vocabulary.Carry<QuestionAnswer>();
        if (QuestionReader.Read(text, Today(), words) is not { } question) return Outcome<QuestionAnswer>.Ok(QuestionAnswer.None);

        if (question.Wikidata is not null) return await AskWikidataAsync(question, page, ct);

        var request = question.Request;
        var parts = question.Parts.ToList();
        if (question.Named is { } named)
        {
            var found = await NamedAsync(named, lenient: false, ct);
            if (found.Value is not { } name) return found.Problem == Problem.NotFound ? Outcome<QuestionAnswer>.Ok(QuestionAnswer.None) : found.Carry<QuestionAnswer>();
            request = name.Person
                ? request with { With = [name.Name, .. request.With] }
                : request with { About = [name.Name, .. request.About] };
            parts.Insert(0, new QuestionPart(name.Person ? "person" : "about", name.Name));
        }
        foreach (var either in question.WithEither)
        {
            var found = await NamedAsync(either, lenient: true, ct);
            if (found.Value is not { } name)
            {
                if (found.Problem != Problem.NotFound) return found.Carry<QuestionAnswer>();
                return Outcome<QuestionAnswer>.Ok(new QuestionAnswer(true, parts, null, found.Message));
            }
            request = name.Person
                ? request with { With = [.. request.With, name.Name] }
                : request with { About = [.. request.About, name.Name] };
            var at = parts.FindIndex(p => p.Part == "person" && p.Text == either);
            if (at >= 0) parts[at] = new QuestionPart(name.Person ? "person" : "about", name.Name);
        }

        var answer = await DiscoverAsync(request with { Page = page }, ct);
        if (answer.Value is not { } discovery)
            return answer.Problem is Problem.NotFound or Problem.Invalid
                ? Outcome<QuestionAnswer>.Ok(new QuestionAnswer(true, parts, null, answer.Message))
                : answer.Carry<QuestionAnswer>();

        // A name is shown as the database names it.
        var people = new Queue<ResolvedPerson>(discovery.Resolved.People);
        var keywords = new Queue<string>(discovery.Resolved.Keywords);
        for (var i = 0; i < parts.Count; i++)
        {
            if (parts[i].Part == "person" && people.TryDequeue(out var person)) parts[i] = parts[i] with { Text = person.Name };
            else if (parts[i].Part == "about" && keywords.TryDequeue(out var keyword)) parts[i] = parts[i] with { Text = keyword };
            else if (parts[i].Part == "like" && discovery.Resolved.Like is { } like) parts[i] = parts[i] with { Text = "Like " + like };
        }
        return Outcome<QuestionAnswer>.Ok(new QuestionAnswer(true, parts, discovery, null));
    }

    /// <summary>The database's own genre and language lists, which the reader recognises those words from.</summary>
    private async Task<Outcome<QuestionVocabulary>> VocabularyAsync(CancellationToken ct)
    {
        var films = await catalogue.GenresAsync("film", ct);
        var shows = await catalogue.GenresAsync("show", ct);
        var languages = await LanguagesAsync(ct);
        if (films is null || shows is null) return Unavailable<QuestionVocabulary>("The film database could not be asked about its genres.");
        if (languages.Value is not { } known) return languages.Carry<QuestionVocabulary>();
        return Outcome<QuestionVocabulary>.Ok(new QuestionVocabulary { FilmGenres = films, ShowGenres = shows, Languages = known });
    }

    private sealed record NamedAs(string Name, bool Person);

    /// <summary>A name as a person or a keyword, by the rules <see cref="AskAsync"/> gives.</summary>
    private async Task<Outcome<NamedAs>> NamedAsync(string words, bool lenient, CancellationToken ct)
    {
        var people = await PeopleAsync(words, ct);
        if (people.Value is not { } matches) return people.Carry<NamedAs>();
        var exact = matches.Where(p => string.Equals(p.Name, words, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(p => p.Popularity).FirstOrDefault();
        if (exact is not null) return Outcome<NamedAs>.Ok(new NamedAs(exact.Name, true));
        if (!words.Contains(' ') && matches.OrderByDescending(p => p.Popularity).FirstOrDefault() is { } best
            && string.Equals(best.Name.Split(' ')[^1], words, StringComparison.OrdinalIgnoreCase))
            return Outcome<NamedAs>.Ok(new NamedAs(best.Name, true));

        var keywords = await KeywordsAsync(words, ct);
        if (keywords.Value is not { } found) return keywords.Carry<NamedAs>();
        if (found.FirstOrDefault(k => string.Equals(k.Name, words, StringComparison.OrdinalIgnoreCase)) is { } keyword)
            return Outcome<NamedAs>.Ok(new NamedAs(keyword.Name, false));

        if (lenient && PickPerson(matches, words) is { } person) return Outcome<NamedAs>.Ok(new NamedAs(person.Name, true));
        return NotFound<NamedAs>($"The film database has nobody and no keyword called \"{words}\".");
    }
}
