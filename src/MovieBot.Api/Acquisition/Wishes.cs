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
    public async Task<AddWishReply> SubscribeAsync(AddWishRequest request, CancellationToken ct)
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

        if (Downloads.Snowflake(request.RequestedBy.UserId) is not { } userId)
            return new AddWishReply { Outcome = "nothingPicked" };

        var (outcome, wish) = wishes.Add(film, new WishSubscriber
        {
            UserId = userId,
            ChannelId = Downloads.Snowflake(request.ChannelId),
            AskedAt = clock.GetUtcNow(),
        });

        if (outcome == WishAdded.AlreadySubscribed)
            return new AddWishReply { Outcome = "alreadySubscribed", Wish = wish.View() };

        logger.LogInformation("{User} is waiting on {Film} ({Imdb}).",
            request.RequestedBy.DisplayName, wish.Display, wish.ImdbId);
        return new AddWishReply { Outcome = "subscribed", Wish = wish.View() };
    }

    public List<WishView> List(string? userId) =>
        (Downloads.Snowflake(userId) is { } id ? wishes.For(id) : wishes.All())
            .Select(w => w.View())
            .ToList();

    public CancelWishReply Cancel(string userId, string chosen)
    {
        if (ImdbId.FromText(chosen) is not { } imdbId || Downloads.Snowflake(userId) is not { } id)
            return new CancelWishReply { Outcome = "nothingPicked" };

        var wish = wishes.Remove(id, imdbId);
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
/// Asks the tracker, on a slow clock, about every film somebody is waiting on, and holds the ones it
/// can now answer until whoever tells people has told them.
///
/// Telling is not done here: the people waiting asked in a Discord channel, and the bot is what
/// speaks there. It reads <see cref="Available"/>, announces each wish in the channels its people
/// asked in, and reports the channels it reached through <see cref="Told"/>, which is what forgets
/// them. A message that failed leaves its people on the list and the wish available, so the next
/// read tries again rather than losing them. Nothing here is persisted: a restarted API asks the
/// tracker again on its first pass, a minute in.
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

    private readonly Dictionary<string, Release> _available = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The wishes the tracker can answer, with whoever is still waiting on each.</summary>
    public List<AvailableWish> Available()
    {
        lock (_available)
        {
            var result = new List<AvailableWish>();
            foreach (var (imdbId, release) in _available.ToList())
            {
                // A wish everybody has been taken off, or told about, is no longer anybody's.
                if (wishes.Find(imdbId) is not { } wish)
                {
                    _available.Remove(imdbId);
                    continue;
                }
                result.Add(new AvailableWish(wish.View(), OfferedReleases.View(release)));
            }
            return result;
        }
    }

    /// <summary>Forgets the people a wish was announced to, in the channels it reached.</summary>
    public void Told(string imdbId, IReadOnlyList<string> channelIds)
    {
        var reached = channelIds
            .Select(Downloads.Snowflake)
            .OfType<ulong>()
            .ToHashSet();
        wishes.Forget(imdbId, reached);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("Wishes are kept at {Path}.", wishes.Path);

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

    private async Task SweepAsync(CancellationToken ct)
    {
        foreach (var wish in wishes.All())
        {
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
            lock (_available)
            {
                if (_available.TryAdd(wish.ImdbId, release))
                    logger.LogInformation("{Film} is on the tracker as {Release}.", wish.Display, release.ReleaseName);
                else
                    _available[wish.ImdbId] = release;
            }

            await TellAsync(wish, ct);
        }
    }

    /// <summary>
    /// Tells everybody waiting on a film that it can be downloaded. Whoever asked in a channel is
    /// also told there by the bot, mentioning them, so only whoever asked without one is sent a
    /// Discord message when no push reaches them, and they come off the wish here because no
    /// announcement will take them off.
    /// </summary>
    private async Task TellAsync(Wish wish, CancellationToken ct)
    {
        foreach (var subscriber in wish.Subscribers)
        {
            await notifier.TellAsync(subscriber.UserId.ToString(System.Globalization.CultureInfo.InvariantCulture),
                new NotificationDraft
                {
                    Kind = NotificationKinds.Available,
                    Key = $"available:{wish.ImdbId}",
                    Text = $"{wish.Display} is out digitally and can be downloaded.",
                    Url = $"/film/{wish.ImdbId}",
                    Action = "Download",
                    Direct = subscriber.ChannelId is null,
                }, ct);
        }
        wishes.ForgetUnchannelled(wish.ImdbId);
    }
}
