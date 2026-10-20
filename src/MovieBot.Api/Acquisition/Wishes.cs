using Microsoft.Extensions.Options;
using TheKrystalShip.MovieBot.Acquire;
using TheKrystalShip.MovieBot.Acquire.Download;
using TheKrystalShip.MovieBot.Acquire.Imdb;
using TheKrystalShip.MovieBot.Acquire.Search;
using TheKrystalShip.MovieBot.Acquire.Tracker;
using TheKrystalShip.MovieBot.Api.Library;
using TheKrystalShip.MovieBot.Api.Notifications;
using TheKrystalShip.MovieBot.Core;

namespace TheKrystalShip.MovieBot.Api.Acquisition;

/// <summary>
/// Whether a film can be downloaded, from what the tracker offers for it.
///
/// A row on the tracker is not enough. A film in cinemas has camcorder recordings on the tracker
/// within days, and the selection policy offers them — weighed down, but offered, because somebody
/// who asks for one on purpose should be able to have it. Somebody waiting to be told means the
/// film's digital release, so a floor on the source is what turns "offered" into "available", and
/// the release named is the first the policy would offer that clears it.
/// </summary>
public static class ReleaseAvailability
{
    public static Release? Pick(RankedReleases ranked, Source floor) =>
        ranked.Candidates.FirstOrDefault(r => r.Source >= floor);
}

/// <summary>What the checks made before a film is waited on found, in <see cref="WantOutcomes"/>' words.</summary>
public sealed record WishCheck(
    string Outcome, ReleaseOffer? Release = null, string? LibraryId = null, string? LibraryName = null, string? DownloadName = null);

/// <summary>
/// Finds out what a film that somebody wants already is, and keeps a wish for it when nothing has it.
///
/// The places the film might already be are checked in the order that answers fastest and means most:
/// the library, the torrent client, then the tracker. Each check that cannot be made is skipped rather
/// than refused, because the sweep asks the tracker again within the hour and the worst outcome of
/// skipping is one message too many.
/// </summary>
public sealed class Wishes(
    ImdbClient catalogue,
    IReleaseSearch search,
    TitleLibrary library,
    AcquisitionService acquisition,
    OfferedReleases offered,
    WishList wishes,
    IOptions<NotifyOptions> options,
    TimeProvider clock,
    ILogger<Wishes> logger)
{
    /// <summary>
    /// What a film is to somebody who wants it. When nothing has it and <paramref name="keep"/> says the
    /// person may wish, the catalogue's description is kept and the sweep watches the tracker for it.
    /// </summary>
    public async Task<WishCheck> CheckAsync(string imdbId, bool keep, Actor asker, CancellationToken ct)
    {
        if (InLibrary(imdbId) is { } title)
            return new WishCheck(WantOutcomes.InLibrary, LibraryId: title.Id, LibraryName: title.Film?.Display ?? title.Title);

        if (await DownloadingAsync(imdbId, ct) is { } download)
            return new WishCheck(WantOutcomes.Downloading, DownloadName: download.Name);

        if (await AvailableNowAsync(imdbId, ct) is { } release)
        {
            offered.Remember([release]);
            return new WishCheck(WantOutcomes.Available, Release: OfferedReleases.View(release));
        }

        if (!keep) return new WishCheck(WantOutcomes.Wanted);

        ImdbTitle? film;
        try
        {
            film = await catalogue.LookupAsync(imdbId, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "The catalogue could not be asked about {Imdb}.", imdbId);
            return new WishCheck(WantOutcomes.Wanted);
        }

        if (film is not { IsFeature: true }) return new WishCheck(WantOutcomes.Wanted);

        wishes.Keep(film, clock.GetUtcNow());
        logger.LogInformation("{User} is waiting on {Film} ({Imdb}).", asker.DisplayName, film.Display, imdbId);
        return new WishCheck(WantOutcomes.Waiting);
    }

    /// <summary>
    /// The films people want that are neither out nor already in the library, with how many want each.
    /// A film that is out or here is nothing to anticipate.
    /// </summary>
    public List<AnticipatedView> Anticipated(string userId)
    {
        var here = library.List()
            .Select(t => t.Film?.ImdbId)
            .OfType<string>()
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return wishes.Anticipated(userId)
            .Where(w => !here.Contains(w.ImdbId) && wishes.OutAs(w.ImdbId) is null)
            .ToList();
    }

    private TitleSummary? InLibrary(string imdbId) =>
        library.List().FirstOrDefault(t =>
            string.Equals(t.Film?.ImdbId, imdbId, StringComparison.OrdinalIgnoreCase));

    private async Task<DownloadStatus?> DownloadingAsync(string imdbId, CancellationToken ct)
    {
        try
        {
            var downloads = await acquisition.ListAsync(ct);
            return downloads.FirstOrDefault(d => d.Tags
                .Select(TorrentTags.ReadImdb)
                .Any(id => string.Equals(id, imdbId, StringComparison.OrdinalIgnoreCase)));
        }
        catch (QBittorrentException ex)
        {
            logger.LogWarning(ex, "The downloads could not be checked for {Imdb}.", imdbId);
            return null;
        }
    }

    private async Task<Release?> AvailableNowAsync(string imdbId, CancellationToken ct)
    {
        try
        {
            var ranked = await search.ByImdbAsync(imdbId, ct);
            return ReleaseAvailability.Pick(ranked, options.Value.MinimumSource);
        }
        catch (TrackerException ex)
        {
            logger.LogWarning(ex, "The tracker could not be asked about {Imdb}.", imdbId);
            return null;
        }
    }
}

/// <summary>
/// Asks the tracker, on a slow clock, about every film somebody wants that nothing has, tells the people
/// who can be told when it is out, and remembers the release that answers it.
///
/// Telling is a notification to each person on the wish, in their bell and to their devices
/// (<c>Notifications/</c>), said once per film by its key. A wish that has come out stays, listing the
/// release beside it, until its people remove their wants; a film already out is not asked about again. What came out is held in memory: a restarted API asks the tracker again on its
/// first pass, a minute in, and nobody is told twice.
/// </summary>
public sealed class WishSweep(
    IReleaseSearch search,
    WishList wishes,
    OfferedReleases offered,
    Notifier notifier,
    IOptions<NotifyOptions> options,
    ILogger<WishSweep> logger) : BackgroundService
{
    /// <summary>Long enough for the service to settle, short enough that a restart says so promptly.</summary>
    private static readonly TimeSpan FirstPass = TimeSpan.FromMinutes(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var delay = FirstPass;
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(delay, stoppingToken);
                delay = TimeSpan.FromMinutes(Math.Max(1, options.Value.SweepMinutes));
                await SweepAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                // A failing sweep must not end the watcher: the films are still on the list and
                // the next pass will ask about them.
                logger.LogWarning(ex, "A wish sweep failed.");
            }
        }
    }

    internal async Task SweepAsync(CancellationToken ct)
    {
        foreach (var wish in wishes.All())
        {
            if (wishes.OutAs(wish.ImdbId) is { } known)
            {
                // Somebody who joined the wish after it came out is told too, once.
                offered.Remember([known]);
                await TellAsync(wish, ct);
                continue;
            }

            RankedReleases ranked;
            try
            {
                ranked = await search.ByImdbAsync(wish.ImdbId, ct);
            }
            catch (TrackerException ex)
            {
                // One tracker failure is every tracker failure; the rest of the pass would only
                // repeat it against the same cap.
                logger.LogWarning(ex, "The tracker could not be asked about {Film}; the pass stops here.",
                    wish.Display);
                return;
            }

            if (ReleaseAvailability.Pick(ranked, options.Value.MinimumSource) is not { } release)
                continue;

            offered.Remember([release]);
            wishes.MarkOut(wish.ImdbId, release);
            logger.LogInformation("{Film} is on the tracker as {Release}.", wish.Display, release.ReleaseName);

            await TellAsync(wish, ct);
        }
    }

    /// <summary>Tells everybody waiting on a film that it can be downloaded. Each is told once, by the notification's key.</summary>
    private async Task TellAsync(Wish wish, CancellationToken ct)
    {
        foreach (var subscriber in wish.Subscribers)
        {
            await notifier.TellAsync(subscriber.UserId, new NotificationDraft
            {
                Kind = NotificationKinds.Available,
                Key = $"available:{wish.ImdbId}",
                Text = $"{wish.Display} is out digitally and can be downloaded.",
                Url = SitePaths.Film(wish.ImdbId),
                Action = "Download",
            }, ct);
        }
    }
}
