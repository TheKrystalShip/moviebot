using TheKrystalShip.Agent.Plugin;
using TheKrystalShip.MovieBot.Assistant.Api;

namespace TheKrystalShip.MovieBot.Assistant.Tools;

/// <summary>Films and shows found by their name, or by words from it.</summary>
internal sealed class SearchTitlesTool(CinemaApi api, ILogger<SearchTitlesTool> logger) : CinemaTool(api, logger)
{
    public override string Name => "search_titles";

    protected override async ValueTask<PluginToolResult> RunAsync(ToolCall call)
    {
        if (call.Text("query") is not { } query)
            return PluginToolResult.Error("query is required: the name, or words from it.");

        string? kind = call.OneOf("kind", "film", "show");
        int? year = call.Number("year");

        SearchResult found = await call.Api.SearchAsync(query, kind, year, call.Cancellation);
        if (found.Titles.Count == 0)
            return PluginToolResult.Ok(
                $"The film database has no {kind ?? "film or show"} matching '{query}'" + (year is { } y ? $" released around {y}." : "."));

        return PluginToolResult.Ok(
            $"{found.Titles.Count} match{(found.Titles.Count == 1 ? "" : "es")} for '{query}', best first:\n" + Say.Lines(found.Titles),
            TitleCards.Of(found.Titles));
    }
}
