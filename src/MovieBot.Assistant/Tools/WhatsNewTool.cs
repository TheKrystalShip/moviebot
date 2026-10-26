using System.ComponentModel;
using System.Text;
using TheKrystalShip.Agent.Plugin;
using TheKrystalShip.MovieBot.Assistant.Api;
using TheKrystalShip.MovieBot.Catalogue;
using TheKrystalShip.MovieBot.Client;

namespace TheKrystalShip.MovieBot.Assistant.Tools;

[AssistantTool("whats_new", ToolClass.Read, ResultLimit = 14000, Order = 6)]
[Description("""
    What is current: trending this week, out recently, or coming soon.
    - trending: the most watched films and shows this week.
    - released: films out in the last few weeks.
    - upcoming: films with a release date in the coming months.
    - Also the newest additions to the library.
    - anime: only anime, for any of these but trending.
    """)]
internal sealed partial class WhatsNewTool(CinemaApi api, TimeProvider clock) : AssistantTool<WhatsNewTool.Args, Caller>
{
    public sealed record Args(
        [property: Description("What to list.")] Window Which,
        [property: Description("Default both.")] TitleKind? Kind = null,
        [property: Description("Only anime. Default false.")] bool? Anime = null);

    /// <summary>How many titles each list holds.</summary>
    private const int Shown = 20;

    /// <summary>How far back "out recently" reaches, and how far ahead "coming soon".</summary>
    internal const int ReleasedDays = 45;
    internal const int UpcomingDays = 180;

    protected override async ValueTask<ToolResult> RunAsync(Args args, ToolCall<Caller> call)
    {
        var database = api.As(call.Principal.AccessToken);
        CancellationToken ct = call.CancellationToken;

        string? kind = args.Kind.Wire();
        string[] kinds = kind is null ? ["film", "show"] : [kind];

        bool anime = args.Anime ?? false;
        if (anime && args.Which == Window.Trending)
            throw new ToolRefusal("The week's trending list cannot be narrowed to anime. Call whats_new with released, upcoming or library, or discover with anime true.");

        return args.Which switch
        {
            Window.Trending => await TrendingAsync(database, kinds, ct),
            Window.Library => await LibraryAsync(database, kind, anime, ct),
            _ => await WindowAsync(database, args.Which == Window.Released, kinds, anime, PersonCreditsTool.Today(clock), ct),
        };
    }

    /// <summary>The film database's trending rankings of the week, one for films and one for shows, in their order.</summary>
    private static async Task<ToolResult> TrendingAsync(CinemaApi.PersonApi database, string[] kinds, CancellationToken ct)
    {
        CardPage[] lists = await Task.WhenAll(kinds.Select(k => database.TitlesAsync(new TitleFilter
        {
            Ranking = k == "film" ? "trending-films" : "trending-shows", Size = Shown,
        }, ct)));

        var text = new StringBuilder("Trending this week, most watched first:\n");
        for (int i = 0; i < kinds.Length; i++)
        {
            text.Append(kinds[i] == "film" ? "Films:\n" : "Shows:\n");
            foreach (TitleCard t in lists[i].Titles)
            {
                text.Append("- ").Append(TitleText.Named(t.Title, t.Year)).Append('.');
                if (t.Category == "anime") text.Append(" Anime.");
                if (TitleText.Rating(t.Rating) is { Length: > 0 } rated) text.Append(' ').Append(rated);
                text.Append(' ').Append(TitleText.Library(t.InLibrary, t.Watched, t.Kind));
                text.Append(" id ").Append(t.Id).Append(".\n");
            }

            if (lists[i].Titles.Count == 0) text.Append("None listed.\n");
        }

        return ToolResult.Ok(text.ToString().TrimEnd(), TitleCards.Of(lists.SelectMany(l => l.Titles)));
    }

    /// <summary>
    /// What came out in the last <see cref="ReleasedDays"/> days, newest first, or what is dated in the next
    /// <see cref="UpcomingDays"/>, soonest first: in each, the titles the film database finds most looked at.
    /// </summary>
    private static async Task<ToolResult> WindowAsync(
        CinemaApi.PersonApi database, bool released, string[] kinds, bool anime, DateOnly today, CancellationToken ct)
    {
        (DateOnly from, DateOnly to) = released ? (today.AddDays(-ReleasedDays), today) : (today, today.AddDays(UpcomingDays));
        CardPage[] lists = await Task.WhenAll(kinds.Select(k => database.TitlesAsync(new TitleFilter
        {
            Kind = k, Category = anime ? "anime" : null, Released = Day(from), ReleasedBefore = Day(to.AddDays(1)), Sort = "popular", Size = Shown,
        }, ct)));

        var text = new StringBuilder();
        var cards = new List<TitleCard>();
        for (int i = 0; i < kinds.Length; i++)
        {
            string what = anime ? "Anime " + Plural(kinds[i]).ToLowerInvariant() : Plural(kinds[i]);
            text.Append(released
                    ? $"{what} released from {Day(from)} to {Day(to)}, newest first, the most looked at"
                    : $"{what} with a release date from {Day(from)} to {Day(to)}, soonest first, the most looked at")
                .Append(":\n");
            List<TitleCard> titles = released
                ? [.. lists[i].Titles.OrderByDescending(t => t.Released, StringComparer.Ordinal)]
                : [.. lists[i].Titles.OrderBy(t => t.Released, StringComparer.Ordinal)];
            text.Append(titles.Count == 0 ? "None.\n" : TitleText.Lines(titles, withDate: true) + "\n");
            cards.AddRange(titles);
        }

        return ToolResult.Ok(text.ToString().TrimEnd(), TitleCards.Of(cards));
    }

    /// <summary>What the library holds ready to play, the newest added first.</summary>
    private static async Task<ToolResult> LibraryAsync(CinemaApi.PersonApi database, string? kind, bool anime, CancellationToken ct)
    {
        CardPage added = await database.TitlesAsync(new TitleFilter
        {
            Kind = kind, Category = anime ? "anime" : null, Here = "ready", Size = Shown,
        }, ct);
        if (added.Titles.Count == 0)
            return ToolResult.Ok(anime ? "The library has no anime." : "The library has no titles.");

        var text = new StringBuilder(anime ? "Newest anime in the library, newest first:\n" : "Newest in the library, newest first:\n");
        foreach (TitleCard t in added.Titles)
        {
            text.Append("- ").Append(TitleText.Named(t.Title, t.Year)).Append(", ").Append(TitleText.KindWord(t.Kind, t.Category)).Append('.');
            if (t.Genres.Count > 0) text.Append(' ').Append(string.Join(", ", t.Genres)).Append('.');
            if (TitleText.Rating(t.Rating) is { Length: > 0 } rated) text.Append(' ').Append(rated);
            if (t.Watched && t.Kind != "show") text.Append(" The person has watched it.");
            text.Append(" id ").Append(t.Id).Append('.');
            if (t.Plot is { Length: > 0 } plot) text.Append("\n  ").Append(plot);
            text.Append('\n');
        }

        return ToolResult.Ok(text.ToString().TrimEnd(), TitleCards.Of(added.Titles));
    }

    private static string Day(DateOnly day) => day.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);

    private static string Plural(string kind) => kind == "show" ? "Shows" : "Films";
}
