using Microsoft.Extensions.Logging;
using TheKrystalShip.MovieBot.Bot.Api;
using TheKrystalShip.MovieBot.Core;

namespace TheKrystalShip.MovieBot.Bot.Notify;

/// <summary>What the command was asked, reduced to the facts it acts on.</summary>
public sealed record NotifyRequest
{
    /// <summary>
    /// What the film option held: the id a picked suggestion carries, or whatever was typed over
    /// the suggestions, which is a pasted link often enough to be read as one.
    /// </summary>
    public required string Chosen { get; init; }
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

    /// <summary>The API did not answer.</summary>
    Unavailable,

    /// <summary>The person's account may not wait on films.</summary>
    NotAllowed,
}

/// <summary>
/// The command's answer. <see cref="Wish"/> is set when the film is on the list; <see cref="Release"/>
/// when the tracker turned out to have it.
/// </summary>
public sealed record NotifyResult(
    NotifyStatus Status,
    string Message,
    Wish? Wish = null,
    ReleaseOffer? Release = null);

public enum CancelStatus
{
    Cancelled,
    NotWaiting,
    NothingPicked,
    Unavailable,
}

public sealed record CancelResult(CancelStatus Status, string Message, Wish? Wish = null);

/// <summary>
/// Puts a person on the list for a film, and takes them off it, by asking the API as them — which
/// holds the list, checks the library, the torrent client and the tracker before making a wish,
/// sweeps the tracker for every film on it, and tells the person on Krystal Cinema when one is out.
/// What stays here is how a Discord channel is told each answer.
/// </summary>
public sealed class NotifyCommand(MovieBotApiClient api, ILogger<NotifyCommand> logger)
{
    /// <summary>What a person whose account may not wait on films is told.</summary>
    public const string NotAllowed = "Your account does not let you wait on films in Krystal Cinema.";

    public async Task<NotifyResult> SubscribeAsync(NotifyRequest request, CancellationToken ct)
    {
        AddWishReply reply;
        try
        {
            reply = await api.AddWishAsync(new AddWishRequest { Chosen = request.Chosen }, ct);
        }
        catch (MovieBotAccessDeniedException)
        {
            return new NotifyResult(NotifyStatus.NotAllowed, NotAllowed);
        }
        catch (MovieBotApiException ex)
        {
            logger.LogWarning(ex, "The API could not be asked to wait on {Chosen}.", request.Chosen);
            return new NotifyResult(NotifyStatus.Unavailable,
                "The MovieBot API is not answering, so nothing was noted. Try again in a minute.");
        }

        return Describe(reply);
    }

    /// <summary>
    /// What the API's answer to a wish is told as in a channel, whoever asked for it: <c>/notify</c>,
    /// or the assistant once somebody agreed.
    /// </summary>
    public static NotifyResult Describe(AddWishReply reply)
    {
        var wish = reply.Wish is { } view ? Wish.From(view) : null;
        var name = wish?.Display ?? reply.FilmTitle ?? reply.ImdbId;
        var title = wish?.Title ?? reply.FilmTitle ?? reply.ImdbId;

        return reply.Outcome switch
        {
            "subscribed" => new NotifyResult(NotifyStatus.Subscribed,
                $"You will be told on Krystal Cinema when {name} can be downloaded.", wish),
            "alreadySubscribed" => new NotifyResult(NotifyStatus.AlreadySubscribed,
                $"You are already waiting on {name}. You will be told on Krystal Cinema when it can be downloaded.", wish),
            "alreadyAvailable" => new NotifyResult(NotifyStatus.AlreadyAvailable,
                $"{title} can be downloaded now. Pick it from the search results in /watch.", wish, reply.Release),
            "inLibrary" => new NotifyResult(NotifyStatus.InLibrary,
                $"{reply.LibraryName ?? name} is already in the library. Watch it with /watch."),
            "downloading" => new NotifyResult(NotifyStatus.Downloading,
                $"{title} is already on its way as {reply.DownloadName}. It will be announced when it is "
                + "ready to watch."),
            "notAFilm" => new NotifyResult(NotifyStatus.NotAFilm,
                $"{title} is not a film, and only films can be fetched here."),
            "notFound" => new NotifyResult(NotifyStatus.NotFound,
                $"Nothing in the catalogue is called {reply.ImdbId}. Pick one of the suggestions, or "
                + "paste the link from the film's own page."),
            _ => new NotifyResult(NotifyStatus.NothingPicked,
                "Pick one of the suggestions, or paste the film's IMDb link. Start typing the "
                + "film's name and a list will appear."),
        };
    }

    /// <summary>What the person the command runs for is waiting on.</summary>
    public async Task<IReadOnlyList<Wish>> ListAsync(CancellationToken ct) =>
        (await api.ListWishesAsync(ct)).Select(Wish.From).ToList();

    public async Task<CancelResult> CancelAsync(string chosen, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(chosen))
            return new CancelResult(CancelStatus.NothingPicked,
                "Pick one of the films you are waiting on from the suggestions.");

        CancelWishReply reply;
        try
        {
            reply = await api.CancelWishAsync(chosen, ct);
        }
        catch (MovieBotAccessDeniedException)
        {
            return new CancelResult(CancelStatus.Unavailable, NotAllowed);
        }
        catch (MovieBotApiException ex)
        {
            logger.LogWarning(ex, "The API could not be asked to cancel {Chosen}.", chosen);
            return new CancelResult(CancelStatus.Unavailable,
                "The MovieBot API is not answering, so nothing changed. Try again in a minute.");
        }

        return reply.Outcome switch
        {
            "cancelled" when reply.Wish is { } view =>
                new CancelResult(CancelStatus.Cancelled, $"You will not be told about {view.Display}.", Wish.From(view)),
            "nothingPicked" => new CancelResult(CancelStatus.NothingPicked,
                "Pick one of the films you are waiting on from the suggestions."),
            _ => new CancelResult(CancelStatus.NotWaiting,
                "You are not waiting on that film. /notify list shows what you are waiting on."),
        };
    }
}
