using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TheKrystalShip.MovieBot.Core;

namespace TheKrystalShip.MovieBot.Handoff;

/// <summary>
/// Measures what each track of a ready title weighs where its manifest records no rate, and writes the
/// master playlist that states it (<see cref="MeasuredRates"/>).
/// </summary>
/// <remarks>
/// <para>
/// The ingest measures a title as it turns ready. This finds the rest: a title made before rates were
/// recorded, and a rung the library's re-encode put in place, which arrives with none. Measuring reads
/// playlists and file sizes and nothing of the media, so a pass costs a listing of each title owed one.
/// </para>
/// <para>
/// It takes the title's lock (<see cref="TitleLocks"/>) like every writer of a finished title here,
/// looks the directory up again once it holds it, and reads the manifest again right before writing,
/// replacing only the rates. The master goes first, so the manifest never says a rate the master
/// does not.
/// </para>
/// </remarks>
public sealed class RatesWorker(
    MediaRoots roots,
    TitleLocks locks,
    ILogger<RatesWorker> logger) : BackgroundService
{
    /// <summary>How often the library is looked over. A title is owed rates once, as it arrives.</summary>
    private static readonly TimeSpan Interval = TimeSpan.FromHours(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval);
        do
        {
            try
            {
                await SweepAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "A rates sweep failed.");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    public async Task SweepAsync(CancellationToken ct)
    {
        var measured = 0;
        foreach (var id in roots.Ids())
        {
            ct.ThrowIfCancellationRequested();
            if (roots.DirectoryOf(id) is not { } found || Read(found) is not { Status: TitleStatus.Ready } manifest
                || !MeasuredRates.Owed(manifest))
                continue;

            using (await locks.AcquireAsync(id, ct))
                if (Measure(id)) measured++;
        }

        if (measured > 0) logger.LogInformation("Measured the rates of {Count} title(s).", measured);
    }

    /// <summary>Measures one title under its lock. True when it wrote anything.</summary>
    private bool Measure(string id)
    {
        if (roots.DirectoryOf(id) is not { } directory) return false;
        var path = Path.Combine(directory, "manifest.json");
        if (Read(directory) is not { Status: TitleStatus.Ready } manifest || !MeasuredRates.Owed(manifest)) return false;

        var whole = MeasuredRates.Measure(directory, manifest);

        // Read again right before writing: what else changed in the manifest meanwhile stands. A track
        // that cannot be measured leaves nothing to write, and is not said again every pass.
        if (Read(directory) is not { } fresh) return false;
        var changed = false;
        foreach (var rendition in fresh.Video.Renditions)
            if (manifest.Video.Renditions.FirstOrDefault(r => r.Uri == rendition.Uri)?.Rate is { } rate && rate != rendition.Rate)
                (rendition.Rate, changed) = (rate, true);
        foreach (var track in fresh.Audio)
            if (manifest.Audio.FirstOrDefault(a => a.Uri == track.Uri)?.Rate is { } rate && rate != track.Rate)
                (track.Rate, changed) = (rate, true);
        if (!changed) return false;
        if (!whole) logger.LogWarning("{Id} has a track whose playlist could not be measured.", id);

        MasterPlaylist.Write(directory, fresh);
        ManifestJson.WriteAtomic(path, fresh);
        return true;
    }

    private static Manifest? Read(string directory)
    {
        var path = Path.Combine(directory, "manifest.json");
        try
        {
            return File.Exists(path) ? ManifestJson.Deserialize(File.ReadAllText(path)) : null;
        }
        catch (Exception ex) when (ex is IOException or System.Text.Json.JsonException)
        {
            return null;
        }
    }
}
