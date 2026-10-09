using System.Globalization;
using System.Text;
using TheKrystalShip.Agent.Plugin;
using TheKrystalShip.MovieBot.Assistant.Api;

namespace TheKrystalShip.MovieBot.Assistant.Tools;

/// <summary>What the asking person has watched, saved and follows, newest first.</summary>
internal sealed class MyTasteTool(CinemaApi api, ILogger<MyTasteTool> logger) : CinemaTool(api, logger)
{
    public override string Name => "my_taste";

    private const int DefaultLimit = 30;
    private const int MostLimit = 100;
    private const int ListsRead = 6;

    protected override async ValueTask<PluginToolResult> RunAsync(ToolCall call)
    {
        int limit = Math.Clamp(call.Number("limit") ?? DefaultLimit, 1, MostLimit);

        // Each part is read on its own: an account that may not read one of them still has the others.
        Task<Attempt<List<HistoryEntry>>> history = Attempt.OfAsync(call.Api.HistoryAsync(call.Cancellation));
        Task<Attempt<List<ListSummary>>> lists = Attempt.OfAsync(call.Api.ListsAsync(call.Cancellation));
        Task<Attempt<List<Follow>>> follows = Attempt.OfAsync(call.Api.FollowsAsync(call.Cancellation));
        await Task.WhenAll(history, lists, follows);

        var text = new StringBuilder();
        var cards = new List<TitleCard>();

        Section(text, "Watched", await history, limit, Watched);
        if ((await lists).Value is { } summaries)
        {
            ListSummary[] read = [.. summaries.Take(ListsRead)];
            var contents = await Task.WhenAll(read.Select(l => Attempt.OfAsync(call.Api.ListAsync(l.Id, call.Cancellation))));
            for (int i = 0; i < read.Length; i++)
            {
                text.Append('\n');
                if (contents[i].Value is not { } list)
                {
                    text.Append($"List \"{read[i].Name}\": could not be read ({contents[i].Error}).\n");
                    continue;
                }

                text.Append($"List \"{read[i].Name}\" ({list.Entries.Count} saved, newest saved first"
                    + (list.Entries.Count > limit ? $", the first {limit} shown" : "") + "):\n");
                if (list.Entries.Count == 0) text.Append("Empty.\n");
                foreach (ListEntry entry in list.Entries.Take(limit))
                {
                    text.Append(Saved(entry)).Append('\n');
                    cards.Add(new TitleCard(entry.ImdbId, entry.Kind, entry.Title, entry.Year, entry.PosterUrl, entry.InLibrary, entry.Watched));
                }
            }

            if (summaries.Count > ListsRead)
                text.Append($"\n{summaries.Count - ListsRead} more lists were not read.\n");
        }
        else
        {
            text.Append($"\nLists: could not be read ({(await lists).Error}).\n");
        }

        text.Append('\n');
        Section(text, "Following", await follows, limit, Followed);
        if ((await follows).Value is { } followed)
            cards.AddRange(followed.Take(limit).Select(f => new TitleCard(f.ImdbId, "show", f.Title, null, null, false, false)));

        return PluginToolResult.Ok(text.ToString().TrimEnd(), TitleCards.Of(cards));
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

    private static string Saved(ListEntry e)
    {
        var text = new StringBuilder("- ").Append(Say.Named(e.Title, e.Year)).Append(", ").Append(Say.KindWord(e.Kind)).Append('.');
        if (e.Genres.Count > 0) text.Append(' ').Append(string.Join(", ", e.Genres)).Append('.');
        if (e.Rating is { } r) text.Append(" Rated ").Append(r.ToString("0.0", CultureInfo.InvariantCulture)).Append('.');
        if (e.Watched && e.Kind != "show") text.Append(" The person has watched it.");
        if (e.Note is { Length: > 0 } note) text.Append(" Note: ").Append(note);
        text.Append($" id {e.ImdbId}.");
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
            catch (ApiRefusal refusal)
            {
                return new Attempt<T>(null, refusal.Message);
            }
        }
    }
}
