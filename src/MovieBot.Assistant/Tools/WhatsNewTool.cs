using System.Text;
using TheKrystalShip.Agent.Plugin;
using TheKrystalShip.MovieBot.Assistant.Api;

namespace TheKrystalShip.MovieBot.Assistant.Tools;

/// <summary>What is current: trending this week, out recently, coming soon, or newly in the library.</summary>
internal sealed class WhatsNewTool(CinemaApi api, ILogger<WhatsNewTool> logger) : CinemaTool(api, logger)
{
    public override string Name => "whats_new";

    private const int LibraryShown = 20;

    protected override async ValueTask<PluginToolResult> RunAsync(ToolCall call)
    {
        string? which = call.OneOf("which", "trending", "released", "upcoming", "library");
        if (which is null)
            return PluginToolResult.Error("which is required: trending, released, upcoming or library.");

        string? kind = call.OneOf("kind", "film", "show");
        string[] kinds = kind is null ? ["film", "show"] : [kind];

        return which switch
        {
            "trending" => await TrendingAsync(call, kinds),
            "library" => await LibraryAsync(call, kind),
            _ => await WindowAsync(call, which, kinds),
        };
    }

    private static async Task<PluginToolResult> TrendingAsync(ToolCall call, string[] kinds)
    {
        Popular popular = await call.Api.PopularAsync(call.Cancellation);
        if (popular.FetchedAt is null)
            return PluginToolResult.Ok("The film database has not been asked for this week's trending titles yet, so there is no list.");

        var text = new StringBuilder($"Trending this week, most watched first (as of {popular.FetchedAt:yyyy-MM-dd}):\n");
        var cards = new List<TitleCard>();
        foreach (string kind in kinds)
        {
            List<PopularTitle> titles = kind == "film" ? popular.Films : popular.Shows;
            text.Append(kind == "film" ? "Films:\n" : "Shows:\n");
            foreach (PopularTitle t in titles)
            {
                text.Append("- ").Append(Say.Named(t.Title, t.Year)).Append('.');
                if (t.Rating is not null) text.Append(' ').Append(Say.Rating(t.Rating, t.Votes));
                text.Append(" id ").Append(t.ImdbId).Append(".\n");
                cards.Add(new TitleCard(t.ImdbId, kind, t.Title, t.Year, t.PosterUrl, false, false));
            }

            if (titles.Count == 0) text.Append("None listed.\n");
        }

        return PluginToolResult.Ok(text.ToString().TrimEnd(), TitleCards.Of(cards));
    }

    private static async Task<PluginToolResult> WindowAsync(ToolCall call, string which, string[] kinds)
    {
        TitleList[] lists = await Task.WhenAll(kinds.Select(k => which == "released"
            ? call.Api.ReleasedAsync(k, call.Cancellation)
            : call.Api.UpcomingAsync(k, call.Cancellation)));

        var text = new StringBuilder();
        foreach (TitleList list in lists)
        {
            text.Append(which == "released"
                    ? $"{Plural(list.Kind)} released from {list.From} to {list.To}, newest first, the most looked at"
                    : $"{Plural(list.Kind)} with a release date from {list.From} to {list.To}, soonest first, the most looked at")
                .Append(":\n");
            text.Append(list.Titles.Count == 0 ? "None.\n" : Say.Lines(list.Titles, withDate: true) + "\n");
        }

        return PluginToolResult.Ok(text.ToString().TrimEnd(), TitleCards.Of(lists.SelectMany(l => l.Titles)));
    }

    private static async Task<PluginToolResult> LibraryAsync(ToolCall call, string? kind)
    {
        LibraryAdditions added = await call.Api.AddedAsync(kind, LibraryShown, call.Cancellation);
        if (added.Titles.Count == 0)
            return PluginToolResult.Ok("The library has no titles.");

        var text = new StringBuilder("Newest in the library, newest first:\n");
        foreach (LibraryAddition t in added.Titles)
        {
            text.Append("- ").Append(Say.Named(t.Title, t.Year)).Append(", ").Append(Say.KindWord(t.Kind))
                .Append(". Added ").Append(t.AddedAt.ToString("yyyy-MM-dd")).Append('.');
            if (t.Genres.Count > 0) text.Append(' ').Append(string.Join(", ", t.Genres)).Append('.');
            if (t.Rating is { } r) text.Append(" Rated ").Append(r.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture)).Append('.');
            if (t.Watched && t.Kind != "show") text.Append(" The person has watched it.");
            text.Append(t.ImdbId is { Length: > 0 } id ? $" id {id}." : " No IMDb id: it has no page here.");
            if (t.Plot is { Length: > 0 } plot) text.Append("\n  ").Append(plot);
            text.Append('\n');
        }

        return PluginToolResult.Ok(text.ToString().TrimEnd(), TitleCards.Of(
            added.Titles.Where(t => t.ImdbId is { Length: > 0 })
                .Select(t => new TitleCard(t.ImdbId!, t.Kind, t.Title, t.Year, t.PosterUrl, true, t.Watched))));
    }

    private static string Plural(string kind) => kind == "show" ? "Shows" : "Films";
}
