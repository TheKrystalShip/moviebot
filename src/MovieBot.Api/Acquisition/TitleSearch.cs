using System.Collections.Concurrent;
using TheKrystalShip.MovieBot.Acquire.Imdb;
using TheKrystalShip.MovieBot.Api.Films;

namespace TheKrystalShip.MovieBot.Api.Acquisition;

/// <summary>Which titles a search offers.</summary>
public enum SearchKinds { Films, Shows, Both }

/// <summary>
/// A search box's answer: the title index's suggestions for what was typed, each with its glance
/// from the film database (<see cref="TitleGlances"/>).
///
/// The index's answer for a name is held in memory for <see cref="Held"/>, so the same name searched
/// again, by anybody, asks nothing; it is held no longer because the index's order follows what people
/// are searching for today, and a title just announced joins it. An empty answer is never held: the
/// index answers one when it cannot be reached as well as when it knows nothing.
/// </summary>
public sealed class TitleSearch(TitleGlances glances, TitleScores scores, Library.TitleCategories categories, TimeProvider clock)
{
    internal static readonly TimeSpan Held = TimeSpan.FromHours(1);
    private const int HeldAtMost = 1000;

    private readonly ConcurrentDictionary<(SearchKinds, string), (DateTimeOffset At, IReadOnlyList<ImdbTitle> Titles)> _answers = new();

    public async Task<List<FoundTitle>> FindAsync(ImdbClient index, string typed, SearchKinds kinds, CancellationToken ct)
    {
        var key = (kinds, string.Join(' ', typed.Split(' ', StringSplitOptions.RemoveEmptyEntries)).ToLowerInvariant());
        if (key.Item2.Length == 0) return [];

        var now = clock.GetUtcNow();
        if (!_answers.TryGetValue(key, out var held) || now - held.At > Held)
        {
            var titles = await (kinds switch
            {
                SearchKinds.Films => index.SuggestAsync(typed, ct),
                SearchKinds.Shows => index.SuggestShowsAsync(typed, ct),
                _ => index.SuggestTitlesAsync(typed, ct),
            });
            held = (now, titles);
            if (titles.Count > 0) Hold(key, held, now);
        }

        var glanced = await glances.ForAsync([.. held.Titles.Select(t => t.ImdbId)], ct);
        return [.. held.Titles.Select(t =>
        {
            var glance = glanced.GetValueOrDefault(t.ImdbId);
            var found = FoundTitle.From(t, glance, glances.PosterUrl(t.ImdbId, glance));
            var (imdbRating, imdbVotes) = scores.Imdb(found.ImdbId);
            return found with { Category = categories.Of(found.Kind, found.ImdbId), ImdbRating = imdbRating, ImdbVotes = imdbVotes };
        })];
    }

    private void Hold((SearchKinds, string) key, (DateTimeOffset, IReadOnlyList<ImdbTitle>) answer, DateTimeOffset now)
    {
        if (_answers.Count >= HeldAtMost)
        {
            foreach (var (k, v) in _answers)
                if (now - v.At > Held) _answers.TryRemove(k, out _);
            if (_answers.Count >= HeldAtMost) _answers.Clear();
        }
        _answers[key] = answer;
    }
}
