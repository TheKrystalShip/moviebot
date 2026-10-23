using System.Globalization;
using Microsoft.Extensions.Options;
using TheKrystalShip.MovieBot.Acquire.Download;
using TheKrystalShip.MovieBot.Acquire.Search;
using TheKrystalShip.MovieBot.Api.Library;
using TheKrystalShip.MovieBot.Api.Sessions;
using TheKrystalShip.MovieBot.Core;
using TheKrystalShip.Notifications;

namespace TheKrystalShip.MovieBot.Api.Notifications;

/// <summary>How a notification names what it is about, and the page it opens.</summary>
public static class Naming
{
    /// <summary>A film by its name and year; an episode by its show and season, which is what a person downloads.</summary>
    public static string Name(Manifest manifest) =>
        manifest.Episode is { } episode
            ? $"{manifest.Film?.Name ?? manifest.Title} Season {episode.Season.ToString(CultureInfo.InvariantCulture)}"
            : manifest.Film?.Display ?? manifest.Title;

    /// <summary>A film's page, or its show's opened on the season.</summary>
    public static string PathOf(Manifest manifest) =>
        manifest.Episode is { } episode && manifest.Film?.ImdbId is { Length: > 0 } show
            ? SitePaths.Show(show, episode.Season)
            : SitePaths.Title(manifest.Id);
}

/// <summary>The pages of Krystal Cinema that a notification opens, as paths on the site's own origin.</summary>
public static class SitePaths
{
    /// <summary>A title the library holds.</summary>
    public static string Title(string titleId) => $"/movies/{Uri.EscapeDataString(titleId)}";

    /// <summary>A show, opened on a season when one is given.</summary>
    public static string Show(string imdbId, int? season = null) =>
        $"/tv/{Uri.EscapeDataString(imdbId)}"
        + (season is { } n ? $"/season/{n.ToString(CultureInfo.InvariantCulture)}" : "");

    /// <summary>A film the library does not hold, in the catalogue.</summary>
    public static string Film(string imdbId) => $"/discover/film/{Uri.EscapeDataString(imdbId)}";

    /// <summary>The signed-in person's own page, on a tab; the default tab is the page itself.</summary>
    public static string Profile(string? tab = null) => tab is { Length: > 0 } ? $"/profile/{Uri.EscapeDataString(tab)}" : "/profile";

    /// <summary>A room, opened on a title when one is given.</summary>
    public static string Room(string room, string? titleId = null) =>
        $"/r/{Uri.EscapeDataString(room)}" + (titleId is null ? "" : $"?title={Uri.EscapeDataString(titleId)}");
}

/// <summary>
/// Tells whoever started a download that it can be watched, or that it could not be made watchable.
///
/// Who is owed the news is written on the torrent when it starts (<see cref="TorrentTags.Tell"/>),
/// beside who asked, and taken off once they have been told, so a restart in the middle of a long
/// download tells them still and a download nobody was promised news of is never announced. A
/// download fetched for people's wants (<see cref="TorrentTags.Want"/>) tells every person whose want
/// on the title lets the server fetch it, read from the wants when the news is told.
/// </summary>
public sealed class ReadyNotices(
    AcquisitionService acquisition,
    TitleLibrary library,
    SessionStore sessions,
    INotifier notifier,
    Wants.WantStore wants,
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
            var manifest = id is null ? null : library.Get(id);
            var name = manifest is null ? ReleaseParser.ParseName(download.Name).Display : Naming.Name(manifest);

            foreach (var person in Owed(download))
            {
                if (id is not null && !failed && Watching(person, id)) continue;

                await notifier.TellAsync(person, new NotificationDraft
                {
                    Kind = NotificationKinds.Ready,
                    Key = $"ready:{download.Hash}",
                    Text = failed ? $"{name} could not be made ready to watch." : $"{name} is ready to watch.",
                    Url = failed || manifest is null ? SitePaths.Profile("requests") : Naming.PathOf(manifest),
                    Action = failed ? null : "Watch",
                }, ct);
            }

            await acquisition.ClearTagAsync(download.Hash, TorrentTags.Tell, ct);
        }
    }

    /// <summary>The person who asked, and for a download fetched for wants, everyone who can be told it is here.</summary>
    private IEnumerable<string> Owed(DownloadStatus download)
    {
        var people = new List<string>();
        if (download.Tags.Select(TorrentTags.ReadRequester).FirstOrDefault(r => r is not null) is { } requester)
            people.Add(requester);
        if (download.Tags.Select(TorrentTags.ReadWant).FirstOrDefault(i => i is not null) is { } title)
            people.AddRange(wants.Fetchers(title).Select(w => w.UserId));
        return people.Distinct(StringComparer.Ordinal);
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
