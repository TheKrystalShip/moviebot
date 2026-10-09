using System.ComponentModel;
using System.Text;
using TheKrystalShip.Agent.Plugin;
using TheKrystalShip.MovieBot.Assistant.Api;

namespace TheKrystalShip.MovieBot.Assistant.Tools;

[AssistantTool("whats_new", ToolClass.Read, ResultLimit = 14000, Order = 6)]
[Description("""
    What is current: trending this week, out recently, or coming soon.
    - trending: the most watched films and shows this week.
    - released: films out in the last few weeks.
    - upcoming: films with a release date in the coming months.
    - Also the newest additions to the library.
    """)]
internal sealed partial class WhatsNewTool(CinemaApi api) : AssistantTool<WhatsNewTool.Args, Caller>
{
    public sealed record Args(
        [property: Description("What to list.")] Window Which,
        [property: Description("Default both.")] TitleKind? Kind = null);

    private const int LibraryShown = 20;

    protected override async ValueTask<ToolResult> RunAsync(Args args, ToolCall<Caller> call)
    {
        var database = api.As(call.Principal.AccessToken);
        CancellationToken ct = call.CancellationToken;

        string? kind = args.Kind.Wire();
        string[] kinds = kind is null ? ["film", "show"] : [kind];

        return args.Which switch
        {
            Window.Trending => await TrendingAsync(database, kinds, ct),
            Window.Library => await LibraryAsync(database, kind, ct),
            _ => await WindowAsync(database, args.Which.Wire(), kinds, ct),
        };
    }

    private static async Task<ToolResult> TrendingAsync(CinemaApi.PersonApi database, string[] kinds, CancellationToken ct)
    {
        Popular popular = await database.PopularAsync(ct);
        if (popular.FetchedAt is null)
            return ToolResult.Ok("The film database has not been asked for this week's trending titles yet, so there is no list.");

        var text = new StringBuilder($"Trending this week, most watched first (as of {popular.FetchedAt:yyyy-MM-dd}):\n");
        var cards = new List<TitleCard>();
        foreach (string kind in kinds)
        {
            List<PopularTitle> titles = kind == "film" ? popular.Films : popular.Shows;
            text.Append(kind == "film" ? "Films:\n" : "Shows:\n");
            foreach (PopularTitle t in titles)
            {
                text.Append("- ").Append(TitleText.Named(t.Title, t.Year)).Append('.');
                if (t.Rating is not null) text.Append(' ').Append(TitleText.Rating(t.Rating, t.Votes));
                text.Append(" id ").Append(t.ImdbId).Append(".\n");
                cards.Add(new TitleCard(t.ImdbId, kind, t.Title, t.Year, t.PosterUrl, false, false));
            }

            if (titles.Count == 0) text.Append("None listed.\n");
        }

        return ToolResult.Ok(text.ToString().TrimEnd(), TitleCards.Of(cards));
    }

    private static async Task<ToolResult> WindowAsync(CinemaApi.PersonApi database, string which, string[] kinds, CancellationToken ct)
    {
        TitleList[] lists = await Task.WhenAll(kinds.Select(k => which == "released"
            ? database.ReleasedAsync(k, ct)
            : database.UpcomingAsync(k, ct)));

        var text = new StringBuilder();
        foreach (TitleList list in lists)
        {
            text.Append(which == "released"
                    ? $"{Plural(list.Kind)} released from {list.From} to {list.To}, newest first, the most looked at"
                    : $"{Plural(list.Kind)} with a release date from {list.From} to {list.To}, soonest first, the most looked at")
                .Append(":\n");
            text.Append(list.Titles.Count == 0 ? "None.\n" : TitleText.Lines(list.Titles, withDate: true) + "\n");
        }

        return ToolResult.Ok(text.ToString().TrimEnd(), TitleCards.Of(lists.SelectMany(l => l.Titles)));
    }

    private static async Task<ToolResult> LibraryAsync(CinemaApi.PersonApi database, string? kind, CancellationToken ct)
    {
        LibraryAdditions added = await database.AddedAsync(kind, LibraryShown, ct);
        if (added.Titles.Count == 0)
            return ToolResult.Ok("The library has no titles.");

        var text = new StringBuilder("Newest in the library, newest first:\n");
        foreach (LibraryAddition t in added.Titles)
        {
            text.Append("- ").Append(TitleText.Named(t.Title, t.Year)).Append(", ").Append(TitleText.KindWord(t.Kind))
                .Append(". Added ").Append(t.AddedAt.ToString("yyyy-MM-dd")).Append('.');
            if (t.Genres.Count > 0) text.Append(' ').Append(string.Join(", ", t.Genres)).Append('.');
            if (t.Rating is { } r) text.Append(" Rated ").Append(r.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture)).Append('.');
            if (t.Watched && t.Kind != "show") text.Append(" The person has watched it.");
            text.Append(t.ImdbId is { Length: > 0 } id ? $" id {id}." : " No IMDb id: it has no page here.");
            if (t.Plot is { Length: > 0 } plot) text.Append("\n  ").Append(plot);
            text.Append('\n');
        }

        return ToolResult.Ok(text.ToString().TrimEnd(), TitleCards.Of(
            added.Titles.Where(t => t.ImdbId is { Length: > 0 })
                .Select(t => new TitleCard(t.ImdbId!, t.Kind, t.Title, t.Year, t.PosterUrl, true, t.Watched))));
    }

    private static string Plural(string kind) => kind == "show" ? "Shows" : "Films";
}
