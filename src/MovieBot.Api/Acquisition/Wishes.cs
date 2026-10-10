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

/// <summary>
/// Puts a person on the list for a film, and takes them off it.
///
/// Before a wish is made, the places the film might already be are checked in the order that answers
/// fastest and means most: the library, the torrent client, then the tracker. Each check that cannot
/// be made is skipped rather than refused, because the sweep asks the tracker again within the hour
/// and the worst outcome of skipping is one message too many.
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
    public async Task<AddWishReply> SubscribeAsync(AddWishRequest request, Actor asker, CancellationToken ct)
    {
        if (ImdbId.FromText(request.Chosen) is not { } imdbId)
            return new AddWishReply { Outcome = "nothingPicked" };

        var film = await catalogue.LookupAsync(imdbId, ct);
        if (film is null)
            return new AddWishReply { Outcome = "notFound", ImdbId = imdbId };

        if (!film.IsFeature)
            return new AddWishReply { Outcome = "notAFilm", FilmTitle = film.Title, ImdbId = imdbId };

        var described = Describe(film).View();

        if (InLibrary(imdbId) is { } title)
            return new AddWishReply
            {
                Outcome = "inLibrary", Wish = described,
                LibraryId = title.Id, LibraryName = title.Film?.Display ?? title.Title,
            };

        if (await DownloadingAsync(imdbId, ct) is { } download)
            return new AddWishReply { Outcome = "downloading", Wish = described, DownloadName = download.Name };

        if (await AvailableNowAsync(imdbId, ct) is { } release)
        {
            offered.Remember([release]);
            return new AddWishReply
            {
                Outcome = "alreadyAvailable", Wish = described, Release = OfferedReleases.View(release),
            };
        }

        var (outcome, wish) = wishes.Add(film, new WishSubscriber
        {
            UserId = asker.UserId,
            AskedAt = clock.GetUtcNow(),
        });

        if (outcome == WishAdded.AlreadySubscribed)
            return new AddWishReply { Outcome = "alreadySubscribed", Wish = wish.View(Out(wish.ImdbId)) };

        logger.LogInformation("{User} is waiting on {Film} ({Imdb}).", asker.DisplayName, wish.Display, wish.ImdbId);
        return new AddWishReply { Outcome = "subscribed", Wish = wish.View() };
    }

    /// <summary>The films one person is waiting on, each with the release that answers it once the tracker offers one.</summary>
    public List<WishView> List(string userId) =>
        wishes.For(userId)
            .Select(w => w.View(Out(w.ImdbId)))
            .ToList();

    /// <summary>The release a wished film came out as, as a page shows it; null while it is not out.</summary>
    private ReleaseOffer? Out(string imdbId) => wishes.OutAs(imdbId) is { } release ? OfferedReleases.View(release) : null;

    public CancelWishReply Cancel(string userId, string chosen)
    {
        if (ImdbId.FromText(chosen) is not { } imdbId)
            return new CancelWishReply { Outcome = "nothingPicked" };

        var wish = wishes.Remove(userId, imdbId);
        return wish is null
            ? new CancelWishReply { Outcome = "notWaiting" }
            : new CancelWishReply { Outcome = "cancelled", Wish = wish.View() };
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

    /// <summary>The film as a wish would describe it, for an answer that needs no wish.</summary>
    private static Wish Describe(ImdbTitle film) => new()
    {
        ImdbId = film.ImdbId,
        Title = film.Title,
        Year = film.Year,
        Starring = film.Starring,
        PosterUrl = film.PosterUrl?.ToString(),
    };
}

/// <summary>
/// Asks the tracker, on a slow clock, about every film somebody is waiting on, tells the people waiting
/// on one when it is out, and remembers the release that answers it.
///
/// Telling is a notification to each person waiting, in their bell and to their devices
/// (<c>Notifications/</c>), said once per film by its key. A person stays on a wish that has come out,
/// and the wish lists the release beside it, until they stop waiting on it; a film already out is not
/// asked about again. What came out is held in memory: a restarted API asks the tracker again on its
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
