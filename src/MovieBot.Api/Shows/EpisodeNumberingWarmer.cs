using TheKrystalShip.MovieBot.Acquire.Download;
using TheKrystalShip.MovieBot.Api.Library;

namespace TheKrystalShip.MovieBot.Api.Shows;

/// <summary>
/// Keeps <see cref="EpisodeNumbering"/> told the episode names each show's releases carry: every
/// library episode's release name, and every file of the show's torrents in the client, which names
/// the episodes of a season not fetched yet as well. A torrent client that cannot be asked leaves the
/// library's names alone.
/// </summary>
public sealed class EpisodeNumberingWarmer(
    EpisodeNumbering numbering,
    TitleLibrary library,
    AcquisitionService acquisition,
    ILogger<EpisodeNumberingWarmer> logger) : BackgroundService
{
    private static readonly TimeSpan FirstPass = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan Every = TimeSpan.FromMinutes(10);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await Task.Delay(FirstPass, stoppingToken);
            while (!stoppingToken.IsCancellationRequested)
            {
                await PassAsync(stoppingToken);
                await Task.Delay(Every, stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
    }

    public async Task PassAsync(CancellationToken ct)
    {
        var names = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var title in library.List())
        {
            if (title.Episode is null || title.Film?.ImdbId is not { Length: > 0 } show) continue;
            if (library.Get(title.Id)?.Source?.Release is not { Length: > 0 } release) continue;
            Names(show).Add(release);
        }

        try
        {
            foreach (var download in await acquisition.ListAsync(ct))
            {
                if (download.Tags.Select(TorrentTags.ReadImdb).FirstOrDefault(i => i is not null) is not { } show) continue;
                foreach (var file in await acquisition.FilesAsync(download.Hash, ct)) Names(show).Add(file.Name);
            }
        }
        catch (Exception ex) when (ex is QBittorrentException or HttpRequestException)
        {
            logger.LogDebug(ex, "The torrent client could not be asked for the shows' file names.");
        }

        foreach (var (show, found) in names) numbering.Learn(show, found);
        return;

        List<string> Names(string show) => names.TryGetValue(show, out var list) ? list : names[show] = [];
    }
}
