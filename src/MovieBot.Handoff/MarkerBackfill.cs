using Microsoft.Extensions.Logging;
using TheKrystalShip.MovieBot.Core;

namespace TheKrystalShip.MovieBot.Handoff;

/// <summary>
/// Places the skippable stretches of the episodes already in the library, which arrived before
/// there was anywhere to keep them.
///
/// Every ready episode is fingerprinted first and compared second. Each is then compared with all
/// the others rather than only with those that happened to be ingested before it, which is what
/// lets the first episode of a season be given its title sequence by the second. Fingerprints
/// already kept are used as they are; nothing is read twice.
/// </summary>
public sealed class MarkerBackfill(MarkerPass pass, ILogger<MarkerBackfill> logger)
{
    public async Task<int> RunAsync(MediaRoots roots, CancellationToken ct)
    {
        if (!Directory.Exists(roots.Hot))
        {
            logger.LogError("No media root at {Root}.", roots.Hot);
            return 1;
        }

        if (roots.Cold is { } cold && !roots.ColdState.Ready)
        {
            logger.LogError(
                "Cold storage at {ColdRoot} is unusable: {Problem} The episodes kept there cannot be "
                + "gone over until it is back.", cold, roots.ColdState.Problem);

            return 1;
        }

        var episodes = roots.Ids()
            .Where(IsReadyEpisode(roots))
            .Order(StringComparer.Ordinal)
            .ToList();

        logger.LogInformation("{Count} episodes to go over.", episodes.Count);

        foreach (var id in episodes)
        {
            ct.ThrowIfCancellationRequested();
            await pass.FingerprintAsync(id, ct);
        }

        foreach (var id in episodes)
        {
            ct.ThrowIfCancellationRequested();
            await pass.RunAsync(id, reuseStored: true, ct);
        }

        return 0;
    }

    private static Func<string, bool> IsReadyEpisode(MediaRoots roots) => id =>
    {
        if (roots.DirectoryOf(id) is not { } directory) return false;

        try
        {
            var path = Path.Combine(directory, "manifest.json");
            return File.Exists(path)
                   && ManifestJson.Deserialize(File.ReadAllText(path))
                       is { Status: TitleStatus.Ready, Episode: not null };
        }
        catch (Exception ex) when (ex is IOException or System.Text.Json.JsonException)
        {
            return false;
        }
    };
}
