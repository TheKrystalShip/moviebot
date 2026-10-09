using System.ComponentModel;
using TheKrystalShip.Agent.Plugin;
using TheKrystalShip.MovieBot.Assistant.Api;

namespace TheKrystalShip.MovieBot.Assistant.Tools;

[AssistantTool("search_titles", ToolClass.Read, ResultLimit = 8000, Order = 1)]
[Description("""
    Find films and shows by name, or by words from a name.
    - Returns up to 10 matches, best first, each with year, kind (film or show), director or creator, two lead actors, and whether the library has it.
    - year narrows to titles released that year, or one either side.
    - Use when the person gives a name, part of one, or a title in another language.
    - Not for finding by actor, genre or era: use person_credits or discover.
    """)]
internal sealed partial class SearchTitlesTool(CinemaApi api) : AssistantTool<SearchTitlesTool.Args, Caller>
{
    public sealed record Args(
        [property: Description("The name or words from it.")] string Query,
        [property: Description("Limit to films or shows.")] TitleKind? Kind = null,
        [property: Description("Roughly when it came out.")] int? Year = null);

    protected override async ValueTask<ToolResult> RunAsync(Args args, ToolCall<Caller> call)
    {
        string query = args.Query;
        string? kind = args.Kind.Wire();
        int? year = args.Year;

        SearchResult found = await api.As(call.Principal.AccessToken).SearchAsync(query, kind, year, call.CancellationToken);
        if (found.Titles.Count == 0)
            return ToolResult.Ok(
                $"The film database has no {kind ?? "film or show"} matching '{query}'" + (year is { } y ? $" released around {y}." : ".")
                + " Do not name another film from memory. To find what the person may mean, use person_credits for an actor they named, "
                + "discover for what it is like, or web_search for a scene or news; or ask for one more detail.");

        return ToolResult.Ok(
            $"{found.Titles.Count} match{(found.Titles.Count == 1 ? "" : "es")} for '{query}', best first:\n" + TitleText.Lines(found.Titles),
            TitleCards.Of(found.Titles));
    }
}
