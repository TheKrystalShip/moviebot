using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Text;
using TheKrystalShip.Agent.Plugin;
using TheKrystalShip.MovieBot.Assistant.Api;

namespace TheKrystalShip.MovieBot.Assistant.Tools;

[AssistantTool("my_taste", ToolClass.Read, ResultLimit = 12000, Order = 7)]
[Description("""
    What the asking person has watched, rated and saved: their history, the titles they want to watch and the shows they follow, newest first.
    - Use to tailor a recommendation and to leave out what they have already seen.
    - Not for answering "what did I watch": the site shows that.
    """)]
internal sealed partial class MyTasteTool(CinemaApi api) : AssistantTool<MyTasteTool.Args, Caller>
{
    public sealed record Args(
        [property: Description("How many of each, default 30.")] [property: Range(1, 100)] int? Limit = null);

    private const int DefaultLimit = 30;

    protected override async ValueTask<ToolResult> RunAsync(Args args, ToolCall<Caller> call)
    {
        var database = api.As(call.Principal.AccessToken);
        CancellationToken ct = call.CancellationToken;
        int limit = args.Limit ?? DefaultLimit;

        // Each part is read on its own: an account that may not read one of them still has the others.
        Task<Attempt<List<HistoryEntry>>> history = Attempt.OfAsync(database.HistoryAsync(ct));
        Task<Attempt<List<Want>>> wants = Attempt.OfAsync(database.WantsAsync(ct));
        Task<Attempt<List<Follow>>> follows = Attempt.OfAsync(database.FollowsAsync(ct));
        await Task.WhenAll(history, wants, follows);

        var text = new StringBuilder();
        var cards = new List<TitleCard>();

        Section(text, "Watched", await history, limit, Watched);
        text.Append('\n');
        Section(text, "Wants to watch", await wants, limit, Wanted);
        if ((await wants).Value is { } wanted)
            cards.AddRange(wanted.Take(limit).Select(w => new TitleCard(w.ImdbId, w.Kind, w.Title, w.Year, w.PosterUrl, w.InLibrary, w.Watched)));

        text.Append('\n');
        Section(text, "Following", await follows, limit, Followed);
        if ((await follows).Value is { } followed)
            cards.AddRange(followed.Take(limit).Select(f => new TitleCard(f.ImdbId, "show", f.Title, null, null, false, false)));

        return ToolResult.Ok(text.ToString().TrimEnd(), TitleCards.Of(cards));
    }

    private static void Section<T>(StringBuilder text, string heading, Attempt<List<T>> attempt, int limit, Func<T, string> write)
    {
        if (attempt.Value is not { } rows)
        {
            text.Append($"{heading}: could not be read ({attempt.Error}).\n");
            return;
        }

        text.Append(rows.Count == 0
            ? $"{heading}: nothing recorded.\n"
            : $"{heading} (newest first, {Math.Min(limit, rows.Count)} of {rows.Count}):\n");
        foreach (T row in rows.Take(limit))
            text.Append(write(row)).Append('\n');
    }

    private static string Watched(HistoryEntry e)
    {
        string name = e.Episode is { } ep ? $"{e.Name} S{ep.Season:00}E{ep.Number:00}" : e.Name;
        string how = e.Finished
            ? "finished"
            : e.DurationSeconds > 0
                ? $"stopped at {Math.Round(e.PositionSeconds / e.DurationSeconds * 100)}%"
                : "started";
        return $"- {name}: {how}, last watched {e.LastWatchedAt:yyyy-MM-dd}.";
    }

    private static string Wanted(Want w)
    {
        var text = new StringBuilder("- ").Append(TitleText.Named(w.Title, w.Year)).Append(", ").Append(TitleText.KindWord(w.Kind)).Append('.');
        if (w.Genres.Count > 0) text.Append(' ').Append(string.Join(", ", w.Genres)).Append('.');
        if (w.Rating is { } r) text.Append(" Rated ").Append(r.ToString("0.0", CultureInfo.InvariantCulture)).Append('.');
        text.Append(w.State switch
        {
            "ready" => " In the library.",
            "fetching" => w.Progress is { } p ? $" Arriving, {Math.Round(p * 100)}% there." : " Arriving.",
            "available" => " Out digitally, not downloaded.",
            "waiting" => " Not out digitally yet.",
            _ => "",
        });
        if (w.NextEpisode is { } next) text.Append($" Their next episode is season {next.Season} episode {next.Episode}.");
        text.Append($" id {w.ImdbId}.");
        return text.ToString();
    }

    private static string Followed(Follow f)
    {
        var text = new StringBuilder($"- {f.Title}: followed since {f.Since:yyyy-MM-dd}.");
        if (f.NextEpisode is { } next)
            text.Append($" Next episode season {next.Season} episode {next.Episode}, {next.AirDate}.");
        text.Append($" id {f.ImdbId}.");
        return text.ToString();
    }

    /// <summary>A read that may have failed: the answer, or the sentence that says why there is none.</summary>
    private readonly record struct Attempt<T>(T? Value, string? Error) where T : class;

    private static class Attempt
    {
        public static async Task<Attempt<T>> OfAsync<T>(Task<T> read) where T : class
        {
            try
            {
                return new Attempt<T>(await read, null);
            }
            catch (ToolRefusal refusal)
            {
                return new Attempt<T>(null, refusal.Message);
            }
        }
    }
}
