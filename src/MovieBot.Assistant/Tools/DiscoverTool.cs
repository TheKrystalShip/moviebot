using System.ComponentModel;
using System.Text;
using TheKrystalShip.Agent.Plugin;
using TheKrystalShip.MovieBot.Assistant.Api;

namespace TheKrystalShip.MovieBot.Assistant.Tools;

[AssistantTool("discover", ToolClass.Read, ResultLimit = 12000, Order = 4)]
[Description("""
    Find films or shows by what they are like rather than by name. Every filter is optional and they combine.
    - genre: one or more, such as horror or "science fiction".
    - from, to: release years.
    - with: people who are in it or made it.
    - about: keywords such as "black and white", "time loop", "heist", "based on a true story".
    - language: original language.
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
        [property: Description("Release years.")] int? From = null,
        [property: Description("Release years.")] int? To = null,
        [property: Description("People's names.")] IReadOnlyList<string>? With = null,
        [property: Description("Keywords.")] IReadOnlyList<string>? About = null,
        [property: Description("Original language, such as Korean.")] string? Language = null,
        [property: Description("A title to find similar ones to.")] string? Like = null,
        [property: Description("Default true.")] bool? GoodOnly = null,
        [property: Description("Default popular.")] DiscoverSort? Sort = null);

    protected override async ValueTask<ToolResult> RunAsync(Args args, ToolCall<Caller> call)
    {
        string kind = args.Kind.Wire() ?? "film";
        IReadOnlyList<string> with = args.With ?? [];

        // The database cannot filter shows by people, so the request is not sent to be refused.
        if (kind == "show" && with.Count > 0)
            throw new ToolRefusal(
                "Shows cannot be filtered by people here. Call person_credits for each person with kind show, "
                + "or discover without with.");

        var query = new DiscoverQuery
        {
            Kind = kind,
            Genre = args.Genre ?? [],
            From = args.From,
            To = args.To,
            With = with,
            About = args.About ?? [],
            Language = args.Language,
            Like = args.Like,
            GoodOnly = args.GoodOnly ?? true,
            Sort = args.Sort.Wire(),
        };

        DiscoverResult found = await api.As(call.Principal.AccessToken).DiscoverAsync(query, call.CancellationToken);
        string matched = Matched(found.Resolved);

        if (found.Titles.Count == 0)
            return ToolResult.Ok(
                $"The film database has no {kind} that fits all of that. {matched} {Loosen(query)} "
                + "A title no tool returned is not to be named.");

        return ToolResult.Ok(
            $"{found.Titles.Count} {kind}{(found.Titles.Count == 1 ? "" : "s")}, in the database's {found.Resolved.Sort} order. {matched}\n"
            + TitleText.Lines(found.Titles),
            TitleCards.Of(found.Titles));
    }

    /// <summary>
    /// What to take away from a request that found nothing, narrowest filter first: every filter narrows
    /// the rest, and a keyword narrows the most.
    /// </summary>
    private static string Loosen(DiscoverQuery q)
    {
        if (q.About.Count > 0)
            return $"Every filter narrows the rest, and a keyword the most: call discover again without about ({string.Join(", ", q.About)}).";
        if (q.Genre.Count > 1)
            return "Every filter narrows the rest: call discover again with only one of the genres.";
        if (q.With.Count > 0 || q.Like is not null || q.Language is not null)
            return "Every filter narrows the rest: call discover again without the people, the language or the similar title.";
        if (q.From is not null || q.To is not null)
            return "Call discover again with wider years.";
        if (q.GoodOnly)
            return "Call discover again with good_only false.";
        return "Nothing more can be taken away.";
    }

    /// <summary>What each word of the request was taken to mean, so the model can say what was actually searched.</summary>
    private static string Matched(DiscoverResolved r)
    {
        var parts = new List<string>();
        if (r.Genres.Count > 0) parts.Add("genres " + string.Join(", ", r.Genres));
        if (r.People.Count > 0) parts.Add("people " + string.Join(", ", r.People.Select(p => p.Name)));
        if (r.Keywords.Count > 0) parts.Add("keywords " + string.Join(", ", r.Keywords));
        if (r.Language is { Length: > 0 } language) parts.Add("original language " + language);
        if (r.Like is { Length: > 0 } like) parts.Add("similar to " + like);
        if (r.MinRating is { } rating)
            parts.Add($"rated at least {rating.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture)}"
                + (r.MinVotes is { } votes ? $" on at least {TitleText.Count(votes)} votes" : ""));
        else if (r.MinVotes is { } onlyVotes)
            parts.Add($"at least {TitleText.Count(onlyVotes)} votes");

        return parts.Count == 0 ? "Matched: no filter." : "Matched: " + string.Join("; ", parts) + ".";
    }
}
