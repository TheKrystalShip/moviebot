using System.Net;
using System.Text;
using TheKrystalShip.Agent.Plugin;
using TheKrystalShip.MovieBot.Assistant.Api;

namespace TheKrystalShip.MovieBot.Assistant.Tools;

/// <summary>Films or shows found by what they are like rather than by name.</summary>
internal sealed class DiscoverTool(CinemaApi api, ILogger<DiscoverTool> logger) : CinemaTool(api, logger)
{
    public override string Name => "discover";

    protected override async ValueTask<PluginToolResult> RunAsync(ToolCall call)
    {
        string kind = call.OneOf("kind", "film", "show") ?? "film";
        IReadOnlyList<string> with = call.List("with");

        // The database cannot filter shows by people, so the request is not sent to be refused.
        if (kind == "show" && with.Count > 0)
            throw new ApiRefusal(HttpStatusCode.BadRequest,
                "Shows cannot be filtered by people here. Call person_credits for each person with kind show, "
                + "or discover without with.");

        var query = new DiscoverQuery
        {
            Kind = kind,
            Genre = call.List("genre"),
            From = call.Number("from"),
            To = call.Number("to"),
            With = with,
            About = call.List("about"),
            Language = call.Text("language"),
            Like = call.Text("like"),
            GoodOnly = call.Flag("good_only", true),
            Sort = call.OneOf("sort", "popular", "rating", "newest", "oldest"),
        };

        DiscoverResult found = await call.Api.DiscoverAsync(query, call.Cancellation);
        string matched = Matched(found.Resolved);

        if (found.Titles.Count == 0)
            return PluginToolResult.Ok(
                $"The film database has no {kind} that fits all of that. {matched} {Loosen(query)} "
                + "A title no tool returned is not to be named.");

        return PluginToolResult.Ok(
            $"{found.Titles.Count} {kind}{(found.Titles.Count == 1 ? "" : "s")}, in the database's {found.Resolved.Sort} order. {matched}\n"
            + Say.Lines(found.Titles),
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
                + (r.MinVotes is { } votes ? $" on at least {Say.Count(votes)} votes" : ""));
        else if (r.MinVotes is { } onlyVotes)
            parts.Add($"at least {Say.Count(onlyVotes)} votes");

        return parts.Count == 0 ? "Matched: no filter." : "Matched: " + string.Join("; ", parts) + ".";
    }
}
