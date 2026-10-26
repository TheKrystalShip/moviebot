using TheKrystalShip.MovieBot.Api.Library;
using TheKrystalShip.MovieBot.Api.People;
using TheKrystalShip.MovieBot.Api.Shows;
using TheKrystalShip.MovieBot.Core;

namespace TheKrystalShip.MovieBot.Api.Wants;

/// <summary>
/// Where a person is in a show, read from the watch record when asked and never stored. Only the
/// episodes the library holds or held have a record, so the point in a show is the episode after the
/// latest one the person finished, which is also where nothing else is known to contradict it.
/// </summary>
internal static class ShowProgress
{
    /// <summary>
    /// The episodes of each show a person finished or marked watched, by the show's IMDb id, the
    /// library's titles naming the episodes their entries are for.
    /// </summary>
    public static Dictionary<string, HashSet<(int Season, int Number)>> Finished(
        IReadOnlyList<TitleSummary> titles, WatchRecord record, string userId)
    {
        var finished = Finished(titles, record.Entries(userId));
        foreach (var mark in record.Marks(userId))
        {
            if (mark.What.IsFilm) continue;
            if (!finished.TryGetValue(mark.What.ImdbId, out var set)) finished[mark.What.ImdbId] = set = [];
            set.Add((mark.What.Season, mark.What.Number));
        }
        return finished;
    }

    /// <summary>The episodes of each show a person finished, by the show's IMDb id.</summary>
    private static Dictionary<string, HashSet<(int Season, int Number)>> Finished(
        IReadOnlyList<TitleSummary> titles, IEnumerable<(string TitleId, WatchEntry Entry)> entries)
    {
        var episodes = new Dictionary<string, (string Show, int Season, int Number)>(StringComparer.Ordinal);
        foreach (var title in titles)
            if (title is { Kind: TitleKind.Show, Episode: { } episode, Film.ImdbId: { } show })
                episodes[title.Id] = (show, episode.Season, episode.Number);

        var finished = new Dictionary<string, HashSet<(int, int)>>(StringComparer.Ordinal);
        foreach (var (titleId, entry) in entries)
        {
            if (!entry.Finished || !episodes.TryGetValue(titleId, out var at)) continue;
            if (!finished.TryGetValue(at.Show, out var set)) finished[at.Show] = set = [];
            set.Add((at.Season, at.Number));
        }
        return finished;
    }

    /// <summary>
    /// The episode after the latest one finished, else the show's first. Null for a show the film
    /// database has not described, and at the end of one.
    /// </summary>
    public static WantEpisode? Next(StoredShow? show, IReadOnlySet<(int Season, int Number)>? finished)
    {
        var episodes = Episodes(show);
        if (episodes.Count == 0) return null;
        if (finished is not { Count: > 0 }) return Of(episodes[0]);

        var latest = finished.Max();
        return episodes.Where(e => (e.Season, e.Number).CompareTo(latest) > 0).Select(Of).FirstOrDefault();
    }

    /// <summary>Whether the last episode of the show that has aired is among those finished.</summary>
    public static bool FinishedAired(StoredShow? show, IReadOnlySet<(int Season, int Number)>? finished, DateOnly today)
    {
        if (finished is not { Count: > 0 }) return false;

        var aired = Episodes(show)
            .Where(e => DateOnly.TryParse(e.AirDate, System.Globalization.CultureInfo.InvariantCulture, out var day) && day <= today)
            .ToList();
        return aired.Count > 0 && finished.Contains((aired[^1].Season, aired[^1].Number));
    }

    private static List<Acquire.Tmdb.EpisodeDetails> Episodes(StoredShow? show) =>
        show is null ? [] : [.. show.Seasons.Where(s => s.Number >= 1).SelectMany(s => s.Episodes)
            .OrderBy(e => e.Season).ThenBy(e => e.Number)];

    private static WantEpisode Of(Acquire.Tmdb.EpisodeDetails e) => new() { Season = e.Season, Episode = e.Number, Name = e.Name };
}
