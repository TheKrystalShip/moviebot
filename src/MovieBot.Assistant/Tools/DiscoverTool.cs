using System.ComponentModel;
using System.Text;
using TheKrystalShip.Agent.Plugin;
using TheKrystalShip.MovieBot.Assistant.Api;
using TheKrystalShip.MovieBot.Catalogue;
using TheKrystalShip.MovieBot.Client;

namespace TheKrystalShip.MovieBot.Assistant.Tools;

[AssistantTool("discover", ToolClass.Read, ResultLimit = 12000, Order = 4)]
[Description("""
    Find films or shows by what they are like rather than by name. Every filter is optional and they combine.
    - genre: one or more, such as horror or "science fiction".
    - exclude: genres to leave out. A genre search already leaves out animation and family titles unless the person asked for them; include_family true keeps them.
    - from, to: release years.
    - with: people who are in it or made it.
    - about: keywords such as "time loop", "heist", "based on a true story".
    - language: original language.
    - anime: only anime, films or shows. Use it whenever the person asks for anime; it sets the genre and the language itself.
    - good_only: well rated with enough votes to mean it. Default true.
    - sort: popular (default), rating, newest, oldest.
    - Returns up to 20, each with year, genres, rating, a one-line plot and whether the library has it.
    - like: a title; returns films the database counts as similar, and the other filters narrow them.
    """)]
internal sealed partial class DiscoverTool(CinemaApi api) : AssistantTool<DiscoverTool.Args, Caller>
{
    public sealed record Args(
        [property: Description("Default film.")] TitleKind? Kind = null,
        [property: Description("Genres.")] IReadOnlyList<string>? Genre = null,
        [property: Description("Genres to leave out.")] IReadOnlyList<string>? Exclude = null,
        [property: Description("Keep animation and family titles. Default false.")] bool? IncludeFamily = null,
        [property: Description("Release years.")] int? From = null,
        [property: Description("Release years.")] int? To = null,
        [property: Description("People's names.")] IReadOnlyList<string>? With = null,
        [property: Description("Keywords.")] IReadOnlyList<string>? About = null,
        [property: Description("Original language, such as Korean.")] string? Language = null,
        [property: Description("A title to find similar ones to.")] string? Like = null,
        [property: Description("Default true.")] bool? GoodOnly = null,
        [property: Description("Default popular.")] DiscoverSort? Sort = null,
        [property: Description("Only anime. Default false.")] bool? Anime = null);

    /// <summary>What a well-rated title is: rated this or better, on this many votes or more.</summary>
    internal const double GoodRating = 6.5;
    internal const int GoodVotes = 200;

    /// <summary>The genres a genre search leaves out unless the person asked for them.</summary>
    internal static readonly string[] FamilyGenres = ["Animation", "Family"];

    protected override async ValueTask<ToolResult> RunAsync(Args args, ToolCall<Caller> call)
    {
        var database = api.As(call.Principal.AccessToken);
        CancellationToken ct = call.CancellationToken;

        string kind = args.Kind.Wire() ?? "film";
        bool anime = args.Anime ?? false;
        bool goodOnly = args.GoodOnly ?? true;
        IReadOnlyList<string> genreWords = args.Genre ?? [];
        IReadOnlyList<string> excludeWords = args.Exclude ?? [];
        IReadOnlyList<string> with = args.With ?? [];
        IReadOnlyList<string> about = args.About ?? [];

        Vocabulary? vocabulary = genreWords.Count > 0 || excludeWords.Count > 0 || args.Language is not null
            ? await Vocabulary.ReadAsync(database, ct)
            : null;
        List<string> genres = vocabulary?.GenresOf(genreWords) ?? [];
        List<string> excluded = vocabulary?.GenresOf(excludeWords) ?? [];
        if (genres.Count > 0 && args.IncludeFamily != true && !anime)
            excluded.AddRange(FamilyGenres.Where(g => !genres.Contains(g) && !excluded.Contains(g)));
        (string Code, string Name)? language = args.Language is { } said ? vocabulary!.Language(said) : null;

        var people = new List<PersonCreditsTool.PersonMatch>();
        foreach (string name in with)
        {
            (PersonCreditsTool.PersonMatch? person, _) = await PersonCreditsTool.FindAsync(database, name, null, null, ct);
            people.Add(person ?? throw new ToolRefusal($"The film database has nobody named '{name}'."));
        }

        var keywords = new List<Term>();
        foreach (string word in about)
            keywords.Add(await Vocabulary.KeywordAsync(database, word, ct)
                ?? throw new ToolRefusal($"The film database has no keyword '{word}'. Call discover again without it, or with other words."));

        TitleCard? like = null;
        if (args.Like is { } likeSaid)
            like = await Vocabulary.TitleAsync(database, likeSaid, ct)
                ?? throw new ToolRefusal($"The film database has no title named '{likeSaid}'. Find it with search_titles first.");

        var filter = new TitleFilter
        {
            Kind = kind,
            Category = anime ? "anime" : null,
            Genres = genres,
            NotGenres = excluded,
            From = args.From,
            To = args.To,
            Languages = language is { } l ? [l.Code] : [],
            People = [.. people.Select(p => p.Id)],
            Keywords = [.. keywords.Select(k => k.Value)],
            SimilarTo = like?.Id,
            MinRating = goodOnly ? GoodRating : null,
            MinVotes = goodOnly ? GoodVotes : null,
            Sort = args.Sort.Wire(),
        };

        CardPage found = await database.TitlesAsync(filter, ct);
        string order = args.Sort.Wire() ?? (like is not null ? "similarity" : "popular");
        string matched = Matched(filter, people, keywords, language?.Name, like);
        string what = (anime ? "anime " : "") + kind;

        if (found.Titles.Count == 0)
            return ToolResult.Ok(
                $"The film database has no {what} that fits all of that. {matched} {Loosen(filter, goodOnly, keywords)} "
                + "A title no tool returned is not to be named.");

        string count = found.Total > found.Titles.Count ? $"{found.Titles.Count} of {TitleText.Count(found.Total)}" : $"{found.Titles.Count}";
        return ToolResult.Ok(
            $"{count} {what}{(found.Total == 1 ? "" : "s")}, in the database's {order} order. {matched}\n"
            + TitleText.Lines(found.Titles),
            TitleCards.Of(found.Titles));
    }

    /// <summary>
    /// What to take away from a request that found nothing, narrowest filter first: every filter narrows
    /// the rest, and a keyword narrows the most.
    /// </summary>
    private static string Loosen(TitleFilter q, bool goodOnly, List<Term> keywords)
    {
        if (q.Keywords.Count > 0)
            return $"Every filter narrows the rest, and a keyword the most: call discover again without about ({string.Join(", ", keywords.Select(k => k.Name))}).";
        if (q.Genres.Count > 1)
            return "Every filter narrows the rest: call discover again with only one of the genres.";
        if (q.People.Count > 0 || q.SimilarTo is not null || q.Languages.Count > 0)
            return "Every filter narrows the rest: call discover again without the people, the language or the similar title.";
        if (q.From is not null || q.To is not null)
            return "Call discover again with wider years.";
        if (goodOnly)
            return "Call discover again with good_only false.";
        return "Nothing more can be taken away.";
    }

    /// <summary>What each word of the request was taken to mean, so the model can say what was actually searched.</summary>
    private static string Matched(
        TitleFilter f, List<PersonCreditsTool.PersonMatch> people, List<Term> keywords, string? language, TitleCard? like)
    {
        var parts = new List<string>();
        if (f.Category is { } category) parts.Add("only " + category);
        if (f.Genres.Count > 0) parts.Add("genres " + string.Join(", ", f.Genres));
        if (f.NotGenres.Count > 0) parts.Add("leaving out " + string.Join(", ", f.NotGenres));
        if (people.Count > 0) parts.Add("people " + string.Join(", ", people.Select(p => p.Name)));
        if (keywords.Count > 0) parts.Add("keywords " + string.Join(", ", keywords.Select(k => k.Name)));
        if (language is not null) parts.Add("original language " + language);
        if (like is not null) parts.Add("similar to " + TitleText.Named(like.Title, like.Year));
        if (f.MinRating is { } rating)
            parts.Add($"rated at least {rating.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture)}"
                + (f.MinVotes is { } votes ? $" on at least {TitleText.Count(votes)} votes" : ""));

        return parts.Count == 0 ? "Matched: no filter." : "Matched: " + string.Join("; ", parts) + ".";
    }
}
