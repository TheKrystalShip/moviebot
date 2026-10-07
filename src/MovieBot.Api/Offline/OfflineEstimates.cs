using TheKrystalShip.MovieBot.Api.Films;
using TheKrystalShip.MovieBot.Api.Library;
using TheKrystalShip.MovieBot.Api.Shows;
using TheKrystalShip.MovieBot.Core;

namespace TheKrystalShip.MovieBot.Api.Offline;

/// <summary>
/// Roughly what downloading a title the library does not hold would take, before anything of it
/// exists: its runtime times what the library's finished titles take a second at each height, the
/// rung a device downloads at that height (the widest no wider than its 16:9 width, as the device
/// picks) and the default audio. A title is constant quality, so its size follows its picture more
/// than its length; the rate is the median of a sample, which is why it is only ever shown as roughly.
///
/// Measuring reads every segment's length off a disk that may be a platter, so the rates are measured
/// in the background, a minute after the service starts and every <see cref="Every"/>, from at most
/// <see cref="Sampled"/> titles, and an estimate before the first measure has no sizes.
/// </summary>
public sealed class OfflineEstimates(
    TitleLibrary library,
    TrackIndexes indexes,
    FilmStore films,
    ShowStore shows,
    ILogger<OfflineEstimates> logger) : BackgroundService
{
    /// <summary>The heights a download is offered at.</summary>
    public static readonly int[] Heights = [1080, 720, 480];

    public static readonly TimeSpan Every = TimeSpan.FromHours(6);
    public const int Sampled = 12;

    private volatile IReadOnlyDictionary<int, double> _bytesPerSecond = new Dictionary<int, double>();

    /// <summary>
    /// What downloading the film <paramref name="imdbId"/>, or with <paramref name="season"/> those of its
    /// <paramref name="episodes"/>, would come to, from what is described here and measured.
    /// </summary>
    public OfflineEstimate For(string imdbId, int? season, IReadOnlyList<int>? episodes)
    {
        string? original;
        double? seconds;
        if (season is { } s)
        {
            var show = shows.Held(imdbId);
            original = show?.Details?.OriginalLanguage;
            seconds = EpisodeSeconds(show?.Seasons.FirstOrDefault(x => x.Number == s), episodes ?? []);
        }
        else
        {
            var film = films.Held(imdbId)?.Details;
            original = film?.OriginalLanguage;
            seconds = film?.RuntimeMinutes is > 0 and var minutes ? minutes * 60.0 : null;
        }

        var rates = _bytesPerSecond;
        return new OfflineEstimate(original, [.. Heights.Select(h => new OfflineEstimateSize(h,
            seconds is { } length && rates.TryGetValue(h, out var rate) ? (long)(rate * length) : null))]);
    }

    /// <summary>
    /// The episodes' runtimes added up, an episode the database gives none for taken at the season's
    /// typical one; null when the season gives none at all.
    /// </summary>
    private static double? EpisodeSeconds(Acquire.Tmdb.SeasonDetails? season, IReadOnlyList<int> episodes)
    {
        if (season is null || episodes.Count == 0) return null;
        var known = season.Episodes.Where(e => e.RuntimeMinutes is > 0).ToDictionary(e => e.Number, e => e.RuntimeMinutes!.Value);
        if (known.Count == 0) return null;
        var typical = Median(known.Values.Select(m => (double)m));
        return episodes.Sum(n => known.TryGetValue(n, out var m) ? m : typical) * 60;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    Measure();
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    logger.LogWarning(ex, "The library's download sizes could not be measured.");
                }
                await Task.Delay(Every, stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
    }

    /// <summary>
    /// Measures what the sampled titles take a second at each height, and keeps the medians. A title
    /// shorter than <paramref name="shortest"/> seconds, a clip, is left out: its few seconds say little.
    /// </summary>
    internal void Measure(double shortest = 60)
    {
        var samples = Heights.ToDictionary(h => h, _ => new List<double>());
        var sampled = library.List()
            .Where(t => t.Status == TitleStatus.Ready && t.DurationSeconds > shortest)
            .OrderBy(t => t.Id, StringComparer.Ordinal)
            .Take(Sampled);
        foreach (var title in sampled)
        {
            if (library.Get(title.Id) is not { } manifest || library.DirectoryOf(title.Id) is not { } directory) continue;
            var audio = manifest.Audio.FirstOrDefault(a => a.Default) ?? manifest.Audio.FirstOrDefault();
            if (audio is null || indexes.Of(directory, audio.Uri) is not { } heard) continue;

            var rungs = manifest.Video.Renditions
                .Select(r => (Rendition: r, Width: r.SizeIn(manifest.Video).Width))
                .OrderByDescending(r => r.Width)
                .ToList();
            foreach (var height in Heights)
            {
                var width = (int)Math.Round(height * 16 / 9.0);
                var rung = rungs.FirstOrDefault(r => r.Width <= width + 8);
                if (rung.Rendition is null) rung = rungs.LastOrDefault();
                if (rung.Rendition is null || indexes.Of(directory, rung.Rendition.Uri) is not { } seen) continue;
                samples[height].Add((seen.Length + heard.Length) / manifest.DurationSeconds);
            }
        }

        _bytesPerSecond = samples.Where(kv => kv.Value.Count > 0).ToDictionary(kv => kv.Key, kv => Median(kv.Value));
        logger.LogInformation("Measured download sizes from {Count} titles: {Rates}.",
            samples.Values.Max(v => v.Count),
            string.Join(", ", _bytesPerSecond.Select(kv => $"{kv.Key}p {kv.Value * 3600 / 1e9:0.00} GB/h")));
    }

    private static double Median(IEnumerable<double> values)
    {
        var sorted = values.Order().ToList();
        var mid = sorted.Count / 2;
        return sorted.Count % 2 == 1 ? sorted[mid] : (sorted[mid - 1] + sorted[mid]) / 2;
    }
}
