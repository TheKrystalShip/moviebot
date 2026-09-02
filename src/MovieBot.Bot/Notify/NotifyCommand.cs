using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TheKrystalShip.MovieBot.Acquire;
using TheKrystalShip.MovieBot.Acquire.Download;
using TheKrystalShip.MovieBot.Acquire.Tracker;
using TheKrystalShip.MovieBot.Acquire.Imdb;
using TheKrystalShip.MovieBot.Acquire.Search;
using TheKrystalShip.MovieBot.Bot.Api;

namespace TheKrystalShip.MovieBot.Bot.Notify;

/// <summary>What the command was asked, reduced to the facts it acts on.</summary>
public sealed record NotifyRequest
{
    /// <summary>
    /// What the film option held: the id a picked suggestion carries, or whatever was typed over
    /// the suggestions, which is a pasted link often enough to be read as one.
    /// </summary>
    public required string Chosen { get; init; }

    /// <summary>Where the command was run, which is where the announcement goes.</summary>
    public required ulong ChannelId { get; init; }

    public required ulong UserId { get; init; }

    public required string RequestedBy { get; init; }
}

public enum NotifyStatus
{
    /// <summary>The film is on the list and the person will be told.</summary>
    Subscribed,

    /// <summary>The person was already on the list for this film.</summary>
    AlreadySubscribed,

    /// <summary>The tracker has it now, so there is nothing to wait for.</summary>
    AlreadyAvailable,

    /// <summary>The library already holds it.</summary>
    InLibrary,

    /// <summary>It is already on its way and will be announced.</summary>
    Downloading,

    /// <summary>The id names a series or an episode, which cannot be fetched.</summary>
    NotAFilm,

    /// <summary>The catalogue has nothing under that id.</summary>
    NotFound,

    /// <summary>Neither a suggestion nor a link was given.</summary>
    NothingPicked,
}

/// <summary>
/// The command's answer. <see cref="Wish"/> is set when the film is on the list; <see cref="Release"/>
/// when the tracker turned out to have it; <see cref="Title"/> when the library did.
/// </summary>
public sealed record NotifyResult(
    NotifyStatus Status,
    string Message,
    Wish? Wish = null,
    Release? Release = null,
    LibraryTitle? Title = null);

public enum CancelStatus
{
    Cancelled,
    NotWaiting,
    NothingPicked,
}

public sealed record CancelResult(CancelStatus Status, string Message, Wish? Wish = null);

/// <summary>
/// Puts a person on the list for a film, and takes them off it.
///
/// Deliberately free of Discord's types: what makes this command wrong is watching for the wrong
/// film, promising to watch for one that is already here, or losing the person who asked, and
/// none of those need a gateway to reproduce.
///
/// Before a wish is made, the places the film might already be are checked in the order that
/// answers fastest and means most: the library, the torrent client, then the tracker. Each check
/// that cannot be made is skipped rather than refused, because the sweep will ask the tracker
/// again within the hour and the worst outcome of skipping is one message too many.
/// </summary>
public sealed class NotifyCommand(
    ImdbClient catalogue,
    IReleaseSearch search,
    MovieBotApiClient api,
    AcquisitionService acquisition,
    WishList wishes,
    IOptions<NotifyOptions> options,
    TimeProvider clock,
    ILogger<NotifyCommand> logger)
{
    public async Task<NotifyResult> SubscribeAsync(NotifyRequest request, CancellationToken ct)
    {
        if (ImdbId.FromText(request.Chosen) is not { } imdbId)
            return new NotifyResult(NotifyStatus.NothingPicked,
                "Pick one of the suggestions, or paste the film's IMDb link. Start typing the "
                + "film's name and a list will appear.");

        var film = await catalogue.LookupAsync(imdbId, ct);
        if (film is null)
            return new NotifyResult(NotifyStatus.NotFound,
                $"Nothing in the catalogue is called {imdbId}. Pick one of the suggestions, or "
                + "paste the link from the film's own page.");

        if (!film.IsFeature)
            return new NotifyResult(NotifyStatus.NotAFilm,
                $"{film.Title} is not a film, and only films can be fetched here.");

        if (await InLibraryAsync(imdbId, ct) is { } title)
            return new NotifyResult(NotifyStatus.InLibrary,
                $"{title.Name} is already in the library. Watch it with /watch.", Title: title);

        if (await DownloadingAsync(imdbId, ct) is { } download)
            return new NotifyResult(NotifyStatus.Downloading,
                $"{film.Title} is already on its way as {download.Name}. It will be announced "
                + "when it is ready to watch.");

        if (await AvailableNowAsync(imdbId, ct) is { } release)
            return new NotifyResult(NotifyStatus.AlreadyAvailable,
                $"{film.Title} can be downloaded now. Pick it from the search results in /watch.",
                Wish: Describe(film), Release: release);

        var (outcome, wish) = wishes.Add(film, new WishSubscriber
        {
            UserId = request.UserId,
            ChannelId = request.ChannelId,
            AskedAt = clock.GetUtcNow(),
        });

        if (outcome == WishAdded.AlreadySubscribed)
            return new NotifyResult(NotifyStatus.AlreadySubscribed,
                $"You are already waiting on {wish.Display}. It will be announced when it can be "
                + "downloaded.", wish);

        logger.LogInformation("{User} is waiting on {Film} ({Imdb}).",
            request.RequestedBy, wish.Display, wish.ImdbId);

        return new NotifyResult(NotifyStatus.Subscribed,
            $"You will be told when {wish.Display} can be downloaded.", wish);
    }

    public IReadOnlyList<Wish> List(ulong userId) => wishes.For(userId);

    public CancelResult Cancel(ulong userId, string chosen)
    {
        if (ImdbId.FromText(chosen) is not { } imdbId)
            return new CancelResult(CancelStatus.NothingPicked,
                "Pick one of the films you are waiting on from the suggestions.");

        var wish = wishes.Remove(userId, imdbId);
        if (wish is null)
            return new CancelResult(CancelStatus.NotWaiting,
                "You are not waiting on that film. /notify list shows what you are waiting on.");

        return new CancelResult(CancelStatus.Cancelled,
            $"You will not be told about {wish.Display}.", wish);
    }

    private async Task<LibraryTitle?> InLibraryAsync(string imdbId, CancellationToken ct)
    {
        try
        {
            var library = await api.ListTitlesAsync(ct);
            return library.FirstOrDefault(t =>
                string.Equals(t.Film?.ImdbId, imdbId, StringComparison.OrdinalIgnoreCase));
        }
        catch (MovieBotApiException ex)
        {
            logger.LogWarning(ex, "The library could not be checked for {Imdb}.", imdbId);
            return null;
        }
    }

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

    /// <summary>The film as a wish would describe it, for a message about one that needs no wish.</summary>
    private static Wish Describe(ImdbTitle film) => new()
    {
        ImdbId = film.ImdbId,
        Title = film.Title,
        Year = film.Year,
        Starring = film.Starring,
        PosterUrl = film.PosterUrl?.ToString(),
    };
}
