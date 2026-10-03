using System.Globalization;
using Microsoft.Extensions.Logging;
using TheKrystalShip.MovieBot.Acquire.Download;
using TheKrystalShip.MovieBot.Bot.Api;
using TheKrystalShip.MovieBot.Core;

namespace TheKrystalShip.MovieBot.Bot.Keep;

/// <summary>
/// One film on disk, as the retention rule sees it.
/// </summary>
/// <param name="Hash">The download's identity, which is what a menu row hands back.</param>
/// <param name="Name">What to call the film: the library's name where it has one, the release's otherwise.</param>
/// <param name="Release">What it arrived as.</param>
/// <param name="Keeper">Who kept it, or null when nobody has.</param>
/// <param name="Remaining">How much more seeding it has before it is due to go.</param>
/// <param name="IsFinished">Whether the download is complete. Seeding, and so the clock, starts then.</param>
/// <param name="Window">How long a film nobody keeps seeds before it goes.</param>
public sealed record KeptFilm(
    string Hash,
    string Name,
    string Release,
    ulong? Keeper,
    TimeSpan Remaining,
    bool IsFinished,
    TimeSpan Window)
{
    public bool IsKept => Keeper is not null;

    public static KeptFilm From(KeptFilmView view) => new(
        view.Hash,
        view.Name,
        view.Release,
        ulong.TryParse(view.Keeper, NumberStyles.None, CultureInfo.InvariantCulture, out var keeper) ? keeper : null,
        TimeSpan.FromSeconds(view.RemainingSeconds),
        view.IsFinished,
        TimeSpan.FromSeconds(view.WindowSeconds));
}

public enum KeepStatus
{
    /// <summary>The film is kept from now on.</summary>
    Kept,

    /// <summary>Somebody had already kept it.</summary>
    AlreadyKept,

    /// <summary>The film is back on the clock.</summary>
    Released,

    /// <summary>Nobody had kept it, so there was nothing to undo.</summary>
    NotKept,

    /// <summary>Nothing on disk answers to that.</summary>
    NotFound,

    /// <summary>The torrent client could not be asked.</summary>
    Unavailable,
}

public sealed record KeepResult(KeepStatus Status, string Message, KeptFilm? Film = null);

/// <summary>
/// What a launch says about how long the film stays. Separated from the command so the surfaces
/// that post a launch depend on one sentence rather than on the API.
/// </summary>
public interface IFilmRetention
{
    /// <summary>
    /// The sentence for a film in the library, or null when no download stands behind it: a
    /// film put there by hand is on no clock, and saying anything about it would be invented.
    /// </summary>
    Task<string?> NoticeForAsync(string libraryId, CancellationToken ct);
}

/// <summary>
/// Keeps a film from being pruned, lets one go again, and says where every film on disk stands, by
/// asking the API — which writes the keep as a note on the torrent, where the hand-off reads it when
/// it decides what to prune. Anyone may keep a film and anyone may let it go, and the row says who
/// did. What stays here is how a Discord channel is told.
/// </summary>
public sealed class KeepCommand(MovieBotApiClient api, ILogger<KeepCommand> logger) : IFilmRetention
{
    /// <summary>Every download, with the library's name for it where the library has one.</summary>
    public async Task<IReadOnlyList<KeptFilm>> ListAsync(CancellationToken ct) =>
        (await api.ListKeepsAsync(ct)).Select(KeptFilm.From).ToList();

    public async Task<KeepResult> KeepAsync(string hash, ulong userId, string displayName, CancellationToken ct)
    {
        KeepReply reply;
        try
        {
            reply = await api.KeepAsync(hash, new Actor(userId.ToString(), displayName), ct);
        }
        catch (MovieBotApiException ex)
        {
            logger.LogWarning(ex, "The API could not be asked to keep {Hash}.", hash);
            return Unavailable();
        }

        return Describe(reply);
    }

    public async Task<KeepResult> ReleaseAsync(string hash, ulong userId, string displayName, CancellationToken ct)
    {
        KeepReply reply;
        try
        {
            reply = await api.ReleaseKeepAsync(hash, new Actor(userId.ToString(), displayName), ct);
        }
        catch (MovieBotApiException ex)
        {
            logger.LogWarning(ex, "The API could not be asked to release {Hash}.", hash);
            return Unavailable();
        }

        return Describe(reply);
    }

    /// <summary>
    /// What the API's answer to a keep or a release is told as in a channel, whoever asked for it:
    /// <c>/keep</c>, or the assistant once somebody agreed.
    /// </summary>
    public static KeepResult Describe(KeepReply reply)
    {
        var film = reply.Film is { } view ? KeptFilm.From(view) : null;
        return (reply.Outcome, film) switch
        {
            ("kept", { } kept) => new KeepResult(KeepStatus.Kept,
                $"{kept.Name} stays. It will not be pruned until somebody runs /keep remove.", kept),
            ("alreadyKept", { } kept) => new KeepResult(KeepStatus.AlreadyKept,
                kept.Keeper is { } keeper
                    ? $"{kept.Name} is already kept, by {Mention(keeper)}."
                    : $"{kept.Name} is already kept.", kept),
            ("released", { } released) => new KeepResult(KeepStatus.Released,
                $"{released.Name} is no longer kept. {Notice(released)}", released),
            ("notKept", { } loose) => new KeepResult(KeepStatus.NotKept, $"{loose.Name} was not kept.", loose),
            ("unavailable", _) => Unavailable(),
            _ => NotFound(),
        };
    }

    public async Task<string?> NoticeForAsync(string libraryId, CancellationToken ct)
    {
        try
        {
            return await api.RetentionAsync(libraryId, ct) is { } view ? Notice(KeptFilm.From(view)) : null;
        }
        catch (MovieBotApiException ex)
        {
            logger.LogDebug(ex, "The retention of {Id} could not be read.", libraryId);
            return null;
        }
    }

    /// <summary>
    /// The one sentence about how long a film stays, for every surface that says it.
    /// </summary>
    public static string Notice(KeptFilm film)
    {
        if (film.Keeper is { } keeper)
            return $"Kept by {Mention(keeper)}, so it stays.";

        if (!film.IsFinished)
            return $"Leaves after {Retention.Describe(film.Window)} of seeding once it has "
                   + "downloaded, unless somebody keeps it with /keep add.";

        return $"Leaves after {Retention.Describe(film.Remaining)} more of seeding, unless somebody "
               + "keeps it with /keep add.";
    }

    /// <summary>
    /// A mention rather than a name, because a tag carries an id and a person renames themselves.
    /// Rendered as a name wherever the message forbids mentions, which every message here does.
    /// </summary>
    public static string Mention(ulong userId) => $"<@{userId}>";

    private static KeepResult NotFound() => new(KeepStatus.NotFound,
        "Nothing on disk answers to that. Pick a film from the suggestions.");

    private static KeepResult Unavailable() => new(KeepStatus.Unavailable,
        "The torrent client is not answering, so nothing changed. Try again in a moment.");
}
