using System.Globalization;
using Microsoft.Extensions.Options;
using TheKrystalShip.MovieBot.Acquire.Download;
using TheKrystalShip.MovieBot.Acquire.Search;
using TheKrystalShip.MovieBot.Api.Library;
using TheKrystalShip.MovieBot.Api.People;
using TheKrystalShip.MovieBot.Api.Sessions;
using TheKrystalShip.MovieBot.Core;

namespace TheKrystalShip.MovieBot.Api.Notifications;

/// <summary>How a notification names what it is about, and the page it opens.</summary>
public static class Naming
{
    /// <summary>A film by its name and year; an episode by its show and season, which is what a person keeps and downloads.</summary>
    public static string Name(Manifest manifest) =>
        manifest.Episode is { } episode
            ? $"{manifest.Film?.Name ?? manifest.Title} Season {episode.Season.ToString(CultureInfo.InvariantCulture)}"
            : manifest.Film?.Display ?? manifest.Title;

    /// <summary>A film's page, or its show's opened on the season.</summary>
    public static string PathOf(Manifest manifest) =>
        manifest.Episode is { } episode && manifest.Film?.ImdbId is { Length: > 0 } show
            ? $"/show/{Uri.EscapeDataString(show)}?season={episode.Season.ToString(CultureInfo.InvariantCulture)}"
            : $"/title/{Uri.EscapeDataString(manifest.Id)}";
}

/// <summary>
/// Tells whoever started a download that it can be watched, or that it could not be made watchable.
///
/// Who is owed the news is written on the torrent when it starts (<see cref="TorrentTags.Tell"/>),
/// beside who asked, and taken off once they have been told, so a restart in the middle of a long
/// download tells them still and a download nobody was promised news of is never announced. A
/// download asked for in a Discord channel is announced there by the bot, mentioning the person, so
/// the Discord message stands in for a push only where no channel was named
/// (<see cref="TorrentTags.TellDirect"/>).
/// </summary>
public sealed class ReadyNotices(
    AcquisitionService acquisition,
    TitleLibrary library,
    SessionStore sessions,
    Notifier notifier,
    ILogger<ReadyNotices> logger) : BackgroundService
{
    /// <summary>A person who started a film is waiting on it; this is how late the news may be.</summary>
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(15);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval);
        while (await WaitAsync(timer, stoppingToken))
        {
            try
            {
                await PassAsync(stoppingToken);
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                // The download is still running and the next pass finds it.
                logger.LogWarning(ex, "A pass over the downloads for news failed.");
            }
        }
    }

    public async Task PassAsync(CancellationToken ct)
    {
        foreach (var download in await acquisition.ListAsync(ct))
        {
            if (!download.Tags.Contains(TorrentTags.Tell)) continue;

            var failed = download.Tags.Contains(TorrentTags.IngestFailed);
            if (!failed && !download.Tags.Contains(TorrentTags.Watchable)) continue;

            var id = download.Tags.Select(TorrentTags.ReadLibrary).FirstOrDefault(i => i is not null);
            if (download.Tags.Select(TorrentTags.ReadRequester).FirstOrDefault(r => r is not null) is { } requester
                && !(id is not null && !failed && Watching(requester.ToString(CultureInfo.InvariantCulture), id)))
            {
                var manifest = id is null ? null : library.Get(id);
                var name = manifest is null ? ReleaseParser.ParseName(download.Name).Display : Naming.Name(manifest);

                await notifier.TellAsync(requester.ToString(CultureInfo.InvariantCulture), new NotificationDraft
                {
                    Kind = NotificationKinds.Ready,
                    Key = $"ready:{download.Hash}",
                    Text = failed ? $"{name} could not be made ready to watch." : $"{name} is ready to watch.",
                    Url = failed || manifest is null ? "/profile?tab=requests" : Naming.PathOf(manifest),
                    Action = failed ? null : "Watch",
                    Direct = download.Tags.Contains(TorrentTags.TellDirect),
                }, ct);
            }

            await acquisition.ClearTagAsync(download.Hash, TorrentTags.Tell, ct);
            if (download.Tags.Contains(TorrentTags.TellDirect))
                await acquisition.ClearTagAsync(download.Hash, TorrentTags.TellDirect, ct);
        }
    }

    /// <summary>
    /// Whether the person is in a room playing the title already, as somebody who pressed Watch now
    /// is by the time it can be watched: the room is the news, and a push would be the same thing late.
    /// </summary>
    private bool Watching(string userId, string titleId) =>
        sessions.Viewing().Any(v => v.TitleId == titleId && v.Viewers.Any(p => p.UserId == userId));

    internal static async Task<bool> WaitAsync(PeriodicTimer timer, CancellationToken ct)
    {
        try
        {
            return await timer.WaitForNextTickAsync(ct);
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }
}

/// <summary>
/// Tells everybody who watched something that it leaves the library within a day, once per date it
/// is due: a play moves the date, and the new one is news again.
///
/// The library's clock is the one rule (<see cref="LibraryClock"/>), so what this says leaves is what
/// the sweep deletes. A kept or unsettled title has no date and is never mentioned.
/// </summary>
public sealed class LeavingNotices(
    LibraryClock clock,
    TitleLibrary library,
    WatchRecord record,
    Notifier notifier,
    TimeProvider time,
    ILogger<LeavingNotices> logger) : BackgroundService
{
    public static readonly TimeSpan Warning = TimeSpan.FromDays(1);

    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(30);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval);
        do
        {
            try
            {
                await PassAsync(stoppingToken);
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                logger.LogWarning(ex, "A pass over what leaves soon failed.");
            }
        }
        while (await ReadyNotices.WaitAsync(timer, stoppingToken));
    }

    public async Task PassAsync(CancellationToken ct)
    {
        var now = time.GetUtcNow();
        foreach (var stay in clock.Stays())
        {
            if (stay.KeeperId is not null || stay.LeavesAt is not { } leavesAt) continue;
            if (leavesAt <= now || leavesAt - now > Warning) continue;
            if (library.Get(stay.TitleId) is not { } manifest) continue;

            var ids = library.IdsIn(stay.Unit);
            var name = Naming.Name(manifest);
            foreach (var person in record.Watchers(ids))
            {
                await notifier.TellAsync(person, new NotificationDraft
                {
                    Kind = NotificationKinds.Leaving,
                    Key = $"leaving:{stay.Unit}:{leavesAt.UtcDateTime.ToString("yyyyMMddHHmm", CultureInfo.InvariantCulture)}",
                    Text = $"{name} leaves within a day. Play it or keep it to stop the timer.",
                    Url = Naming.PathOf(manifest),
                    Action = "Keep",
                }, ct);
            }
        }
    }
}

/// <summary>Tells everybody who watched something that somebody kept it, or let it go.</summary>
public sealed class KeepNotices(TitleLibrary library, WatchRecord record, Notifier notifier, TimeProvider time)
{
    public async Task TellAsync(string unit, Actor keeper, bool kept, CancellationToken ct)
    {
        var ids = library.IdsIn(unit);
        if (ids.Count == 0 || library.Get(ids[0]) is not { } manifest) return;

        var name = Naming.Name(manifest);
        var at = time.GetUtcNow().ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);
        foreach (var person in record.Watchers(ids).Where(p => p != keeper.UserId))
        {
            await notifier.TellAsync(person, new NotificationDraft
            {
                Kind = NotificationKinds.Kept,
                Key = $"kept:{unit}:{at}",
                Text = kept ? $"{keeper.DisplayName} kept {name}." : $"{keeper.DisplayName} let {name} go back on the clock.",
                Url = Naming.PathOf(manifest),
            }, ct);
        }
    }
}
