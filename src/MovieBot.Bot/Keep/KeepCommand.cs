using System.Globalization;
using Microsoft.Extensions.Logging;
using TheKrystalShip.MovieBot.Bot.Api;
using TheKrystalShip.MovieBot.Core;

namespace TheKrystalShip.MovieBot.Bot.Keep;

/// <summary>
/// One film in the library, as the library's clock sees it.
/// </summary>
/// <param name="TitleId">The library id, which is what a menu row hands back.</param>
/// <param name="Name">What to call the film.</param>
/// <param name="Keeper">Who kept it: a Discord id, or null when nobody has.</param>
/// <param name="KeeperName">What the API calls the keeper, for a keeper that is not a Discord id.</param>
/// <param name="DaysLeft">Whole days before it leaves, or null when it is kept or has not settled.</param>
public sealed record KeptFilm(string TitleId, string Name, string? Keeper, string? KeeperName, int? DaysLeft)
{
    public bool IsKept => Keeper is not null;

    public static KeptFilm From(TitleStay stay, string name) =>
        new(stay.TitleId, name, stay.KeeperId, stay.KeeperName, stay.DaysLeft);
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

    /// <summary>The library holds no such film.</summary>
    NotFound,

    /// <summary>The API could not be asked.</summary>
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
    /// The sentence for a film in the library, or null when the film is on no clock: one that has
    /// not settled has not started counting, and saying anything about it would be invented.
    /// </summary>
    Task<string?> NoticeForAsync(string libraryId, CancellationToken ct);
}

/// <summary>
/// Keeps a film in the library, lets one go again, and says where every settled film stands, by
/// asking the API, which holds the keeps and the library's clock. Anyone may keep a film and anyone
/// may let it go, and the record says who did. What stays here is how a Discord channel is told.
/// </summary>
public sealed class KeepCommand(MovieBotApiClient api, ILogger<KeepCommand> logger) : IFilmRetention
{
    /// <summary>Every settled film, soonest to leave first, the kept ones after them.</summary>
    public async Task<IReadOnlyList<KeptFilm>> ListAsync(CancellationToken ct)
    {
        var stays = await api.ListStaysAsync(ct);
        var names = await NamesAsync(ct);
        return stays.Select(s => KeptFilm.From(s, NameOf(names, s.TitleId))).ToList();
    }

    /// <summary>Every film in the library, for choosing one to keep.</summary>
    public async Task<IReadOnlyList<(string TitleId, string Name)>> TitlesAsync(CancellationToken ct) =>
        (await api.ListTitlesAsync(ct)).Select(t => (t.Id, t.Name)).ToList();

    public async Task<KeepResult> KeepAsync(string titleId, ulong userId, string displayName, CancellationToken ct)
    {
        try
        {
            var before = await api.StayAsync(titleId, ct);
            if (before is null) return NotFound();

            var name = NameOf(await NamesAsync(ct), titleId);
            if (before.KeeperId is not null) return Describe(KeepStatus.AlreadyKept, before, name);

            var after = await api.KeepAsync(titleId, new Actor(userId.ToString(CultureInfo.InvariantCulture), displayName), ct);
            if (after is null) return NotFound();

            return Describe(after.KeeperId == userId.ToString(CultureInfo.InvariantCulture)
                ? KeepStatus.Kept : KeepStatus.AlreadyKept, after, name);
        }
        catch (MovieBotApiException ex)
        {
            logger.LogWarning(ex, "The API could not be asked to keep {Id}.", titleId);
            return Unavailable();
        }
    }

    public async Task<KeepResult> ReleaseAsync(string titleId, ulong userId, string displayName, CancellationToken ct)
    {
        try
        {
            var before = await api.StayAsync(titleId, ct);
            if (before is null) return NotFound();

            var name = NameOf(await NamesAsync(ct), titleId);
            if (before.KeeperId is null) return Describe(KeepStatus.NotKept, before, name);

            var after = await api.ReleaseKeepAsync(titleId, new Actor(userId.ToString(CultureInfo.InvariantCulture), displayName), ct);
            if (after is null) return NotFound();

            return Describe(KeepStatus.Released, after, name);
        }
        catch (MovieBotApiException ex)
        {
            logger.LogWarning(ex, "The API could not be asked to release {Id}.", titleId);
            return Unavailable();
        }
    }

    /// <summary>What the API's answer to a keep or a release is told as in a channel.</summary>
    public static KeepResult Describe(KeepStatus status, TitleStay stay, string name)
    {
        var film = KeptFilm.From(stay, name);
        return status switch
        {
            KeepStatus.Kept => new KeepResult(status,
                $"{name} stays. It will not leave the library until somebody runs /keep remove.", film),
            KeepStatus.AlreadyKept => new KeepResult(status, $"{name} is already kept, by {Who(film)}.", film),
            KeepStatus.Released => new KeepResult(status, $"{name} is no longer kept. {Notice(film)}".TrimEnd(), film),
            KeepStatus.NotKept => new KeepResult(status, $"{name} was not kept.", film),
            _ => throw new ArgumentOutOfRangeException(nameof(status)),
        };
    }

    public async Task<string?> NoticeForAsync(string libraryId, CancellationToken ct)
    {
        try
        {
            return await api.StayAsync(libraryId, ct) is { } stay ? Notice(KeptFilm.From(stay, libraryId)) : null;
        }
        catch (MovieBotApiException ex)
        {
            logger.LogDebug(ex, "The stay of {Id} could not be read.", libraryId);
            return null;
        }
    }

    /// <summary>
    /// The one sentence about how long a film stays, for every surface that says it, or null for a
    /// film that has not settled.
    /// </summary>
    public static string? Notice(KeptFilm film)
    {
        if (film.IsKept) return $"Kept by {Who(film)}, so it stays.";
        if (film.DaysLeft is not { } days) return null;

        return days == 0
            ? "Leaves today unless somebody plays or keeps it with /keep add."
            : $"Leaves in {days} {(days == 1 ? "day" : "days")} unless somebody plays or keeps it with /keep add.";
    }

    /// <summary>
    /// A mention rather than a name where the keeper is a Discord id, because a person renames
    /// themselves. Rendered as a name wherever the message forbids mentions, which every message
    /// here does.
    /// </summary>
    public static string Who(KeptFilm film) =>
        ulong.TryParse(film.Keeper, NumberStyles.None, CultureInfo.InvariantCulture, out var id)
            ? $"<@{id}>"
            : film.KeeperName ?? film.Keeper ?? "nobody";

    private async Task<Dictionary<string, string>> NamesAsync(CancellationToken ct) =>
        (await api.ListTitlesAsync(ct)).ToDictionary(t => t.Id, t => t.Name, StringComparer.Ordinal);

    private static string NameOf(Dictionary<string, string> names, string titleId) =>
        names.GetValueOrDefault(titleId) ?? titleId;

    private static KeepResult NotFound() => new(KeepStatus.NotFound,
        "The library holds no film like that. Pick a film from the suggestions.");

    private static KeepResult Unavailable() => new(KeepStatus.Unavailable,
        "The library is not answering, so nothing changed. Try again in a moment.");
}
