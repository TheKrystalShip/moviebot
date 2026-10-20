using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TheKrystalShip.MovieBot.Acquire;
using TheKrystalShip.MovieBot.Bot.Configuration;
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
    /// <summary>The film is wanted and the person will be told when it is out.</summary>
    Subscribed,

    /// <summary>The person already wanted this film.</summary>
    AlreadySubscribed,

    /// <summary>The film is wanted; nothing more is known of when it can be had.</summary>
    Wanted,

    /// <summary>The tracker has it now, so there is nothing to wait for.</summary>
    AlreadyAvailable,

    /// <summary>The library already holds it.</summary>
    InLibrary,

    /// <summary>It is already on its way and will be announced.</summary>
    Downloading,

    /// <summary>The catalogue has no film under that id.</summary>
    NotFound,

    /// <summary>Neither a suggestion nor a link was given.</summary>
    NothingPicked,

    /// <summary>The API did not answer.</summary>
    Unavailable,

    /// <summary>The person's account may not want films.</summary>
    NotAllowed,
}

/// <summary>
/// The command's answer. <see cref="Wish"/> is set when the film is wanted and waits on a release;
/// <see cref="Release"/> when the tracker turned out to have it.
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
/// Makes a person's want of a film, and ends it, by asking the API as them — which keeps the want,
/// checks the library, the torrent client and the tracker, keeps a wish when nothing has the film,
/// sweeps the tracker for every wish, and tells the person on Krystal Cinema when one is out. What
/// stays here is how a Discord channel is told each answer.
/// </summary>
public sealed class NotifyCommand(MovieBotApiClient api, IOptions<ApiOptions> options, ILogger<NotifyCommand> logger)
{
    /// <summary>What a person whose account may not want films is told.</summary>
    public const string NotAllowed = "Your account does not let you want films in Krystal Cinema.";

    public async Task<NotifyResult> SubscribeAsync(NotifyRequest request, CancellationToken ct)
    {
        if (ImdbId.FromText(request.Chosen) is not { } imdbId)
            return new NotifyResult(NotifyStatus.NothingPicked,
                "Pick one of the suggestions, or paste the film's IMDb link. Start typing the "
                + "film's name and a list will appear.");

        SetWantReply? reply;
        try
        {
            reply = await api.SetWantAsync(imdbId, new SetWantRequest { Kind = "film" }, ct);
        }
        catch (MovieBotAccessDeniedException)
        {
            return new NotifyResult(NotifyStatus.NotAllowed, NotAllowed);
        }
        catch (MovieBotApiException ex)
        {
            logger.LogWarning(ex, "The API could not be asked to want {Chosen}.", request.Chosen);
            return new NotifyResult(NotifyStatus.Unavailable,
                "The MovieBot API is not answering, so nothing was noted. Try again in a minute.");
        }

        return reply is null
            ? new NotifyResult(NotifyStatus.NotFound,
                $"Nothing in the catalogue is called {imdbId}. Pick one of the suggestions, or "
                + "paste the link from the film's own page.")
            : Describe(reply, imdbId);
    }

    /// <summary>What the API's answer to a want is told as in a channel.</summary>
    public NotifyResult Describe(SetWantReply reply, string imdbId)
    {
        var want = reply.Want;
        var name = want is null ? imdbId : want.Year is { } year ? $"{want.Title} ({year})" : want.Title;
        var title = want?.Title ?? imdbId;
        var waiting = want is { Wish: { Release: null } } ? Wish.From(want, options.Value.PublicBaseUrl) : null;

        return reply.Outcome switch
        {
            WantOutcomes.Waiting => new NotifyResult(NotifyStatus.Subscribed,
                $"You will be told on Krystal Cinema when {name} can be downloaded.", waiting),
            WantOutcomes.AlreadyWanted when waiting is not null => new NotifyResult(NotifyStatus.AlreadySubscribed,
                $"You are already waiting on {name}. You will be told on Krystal Cinema when it can be downloaded.", waiting),
            WantOutcomes.AlreadyWanted => new NotifyResult(NotifyStatus.AlreadySubscribed,
                $"You already want {name} on Krystal Cinema."),
            WantOutcomes.Available => new NotifyResult(NotifyStatus.AlreadyAvailable,
                $"{title} can be downloaded now. Pick it from the search results in /watch.",
                want is null ? null : Wish.From(want, options.Value.PublicBaseUrl), reply.Release),
            WantOutcomes.InLibrary => new NotifyResult(NotifyStatus.InLibrary,
                $"{reply.LibraryName ?? name} is already in the library. Watch it with /watch."),
            WantOutcomes.Downloading => new NotifyResult(NotifyStatus.Downloading,
                $"{title} is already on its way as {reply.DownloadName}. It will be announced when it is "
                + "ready to watch."),
            _ => new NotifyResult(NotifyStatus.Wanted, $"{name} is on your list on Krystal Cinema."),
        };
    }

    /// <summary>The films the person the command runs for is waiting on: the ones with a wish kept and no release found yet.</summary>
    public async Task<IReadOnlyList<Wish>> ListAsync(CancellationToken ct) =>
        (await api.ListWantsAsync(ct))
            .Where(w => w is { Kind: "film", Wish: { } })
            .Select(w => Wish.From(w, options.Value.PublicBaseUrl))
            .ToList();

    public async Task<CancelResult> CancelAsync(string chosen, CancellationToken ct)
    {
        if (ImdbId.FromText(chosen) is not { } imdbId)
            return new CancelResult(CancelStatus.NothingPicked,
                "Pick one of the films you are waiting on from the suggestions.");

        try
        {
            return await api.RemoveWantAsync(imdbId, ct)
                ? new CancelResult(CancelStatus.Cancelled, $"You no longer want {imdbId}.")
                : new CancelResult(CancelStatus.NotWaiting,
                    "You are not waiting on that film. /notify list shows what you are waiting on.");
        }
        catch (MovieBotAccessDeniedException)
        {
            return new CancelResult(CancelStatus.Unavailable, NotAllowed);
        }
        catch (MovieBotApiException ex)
        {
            logger.LogWarning(ex, "The API could not be asked to remove the want of {Chosen}.", chosen);
            return new CancelResult(CancelStatus.Unavailable,
                "The MovieBot API is not answering, so nothing changed. Try again in a minute.");
        }
    }
}
