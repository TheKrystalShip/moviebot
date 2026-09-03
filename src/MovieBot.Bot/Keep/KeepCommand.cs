using Microsoft.Extensions.Logging;
using TheKrystalShip.MovieBot.Acquire.Download;
using TheKrystalShip.MovieBot.Acquire.Search;
using TheKrystalShip.MovieBot.Bot.Api;

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
public sealed record KeptFilm(
    string Hash,
    string Name,
    string Release,
    ulong? Keeper,
    TimeSpan Remaining,
    bool IsFinished)
{
    public bool IsKept => Keeper is not null;
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
/// that post a launch depend on one sentence rather than on the torrent client.
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
/// Keeps a film from being pruned, lets one go again, and says where every film on disk stands.
///
/// A keep is a note on the torrent, so this holds nothing: every answer is read from the torrent
/// client at the moment it is asked, and the hand-off reads the same note when it decides what
/// to prune. Anyone may keep a film and anyone may let it go, and the row says who did.
/// </summary>
public sealed class KeepCommand(
    AcquisitionService acquisition,
    MovieBotApiClient api,
    Retention retention,
    ILogger<KeepCommand> logger) : IFilmRetention
{
    /// <summary>Every download, with the library's name for it where the library has one.</summary>
    public async Task<IReadOnlyList<KeptFilm>> ListAsync(CancellationToken ct)
    {
        var downloads = await acquisition.ListAsync(ct);

        // A name is a courtesy: the release name stands in when the library cannot be read.
        IReadOnlyList<LibraryTitle> library;
        try
        {
            library = await api.ListTitlesAsync(ct);
        }
        catch (MovieBotApiException ex)
        {
            logger.LogWarning(ex, "The library could not be read; films are named by their releases.");
            library = [];
        }

        return downloads
            .Select(d => Describe(d, library))
            .OrderBy(f => f.IsKept)
            .ThenBy(f => f.Remaining)
            .ToList();
    }

    public async Task<KeepResult> KeepAsync(string hash, ulong userId, CancellationToken ct)
    {
        var film = await FindAsync(hash, ct);
        if (film is null) return NotFound();

        if (film.IsKept)
            return new KeepResult(KeepStatus.AlreadyKept,
                $"{film.Name} is already kept, by {Mention(film.Keeper!.Value)}.", film);

        try
        {
            await acquisition.TagAsync(hash, TorrentTags.Keeper(userId), ct);
            await acquisition.TagAsync(hash, TorrentTags.Keep, ct);
        }
        catch (QBittorrentException ex)
        {
            logger.LogWarning(ex, "Could not keep {Name}.", film.Name);
            return Unavailable();
        }

        logger.LogInformation("{UserId} kept {Name}.", userId, film.Release);

        return new KeepResult(KeepStatus.Kept,
            $"{film.Name} stays. It will not be pruned until somebody runs /keep remove.",
            film with { Keeper = userId });
    }

    public async Task<KeepResult> ReleaseAsync(string hash, ulong userId, CancellationToken ct)
    {
        var film = await FindAsync(hash, ct);
        if (film is null) return NotFound();

        if (!film.IsKept)
            return new KeepResult(KeepStatus.NotKept, $"{film.Name} was not kept.", film);

        try
        {
            // The keep goes first. A process stopping between the two leaves a keeper without a
            // keep, which reads as nobody keeping it, where the other order leaves a keep nobody
            // owns.
            await acquisition.ClearTagAsync(hash, TorrentTags.Keep, ct);
            await acquisition.ClearTagAsync(hash, TorrentTags.Keeper(film.Keeper!.Value), ct);
        }
        catch (QBittorrentException ex)
        {
            logger.LogWarning(ex, "Could not release {Name}.", film.Name);
            return Unavailable();
        }

        logger.LogInformation("{UserId} let {Name} go.", userId, film.Release);

        var released = film with { Keeper = null };
        return new KeepResult(KeepStatus.Released,
            $"{film.Name} is no longer kept. {Notice(released)}", released);
    }

    public async Task<string?> NoticeForAsync(string libraryId, CancellationToken ct)
    {
        try
        {
            var downloads = await acquisition.ListAsync(ct);
            var download = downloads.FirstOrDefault(d =>
                d.Tags.Select(TorrentTags.ReadLibrary).Any(id => id == libraryId));

            return download is null ? null : Notice(Describe(download, []));
        }
        catch (QBittorrentException ex)
        {
            logger.LogDebug(ex, "The downloads could not be read for {Id}.", libraryId);
            return null;
        }
    }

    /// <summary>
    /// The one sentence about how long a film stays, for every surface that says it.
    /// </summary>
    public string Notice(KeptFilm film)
    {
        if (film.Keeper is { } keeper)
            return $"Kept by {Mention(keeper)}, so it stays.";

        if (!film.IsFinished)
            return $"Leaves after {Retention.Describe(retention.Window)} of seeding once it has "
                   + "downloaded, unless somebody keeps it with /keep add.";

        return $"Leaves after {Retention.Describe(film.Remaining)} more of seeding, unless somebody "
               + "keeps it with /keep add.";
    }

    /// <summary>
    /// A mention rather than a name, because a tag carries an id and a person renames themselves.
    /// Rendered as a name wherever the message forbids mentions, which every message here does.
    /// </summary>
    public static string Mention(ulong userId) => $"<@{userId}>";

    private async Task<KeptFilm?> FindAsync(string hash, CancellationToken ct)
    {
        try
        {
            var download = await acquisition.StatusAsync(hash, ct);
            return download is null ? null : Describe(download, []);
        }
        catch (QBittorrentException ex)
        {
            logger.LogWarning(ex, "The downloads could not be read.");
            return null;
        }
    }

    private KeptFilm Describe(DownloadStatus download, IReadOnlyList<LibraryTitle> library)
    {
        var id = download.Tags.Select(TorrentTags.ReadLibrary).FirstOrDefault(i => i is not null);
        var title = id is null ? null : library.FirstOrDefault(t => t.Id == id);

        return new KeptFilm(
            download.Hash,
            title?.Name ?? ReleaseParser.ParseName(download.Name).Display,
            download.Name,
            download.Tags.Select(TorrentTags.ReadKeeper).FirstOrDefault(k => k is not null),
            download.IsFinished ? retention.Remaining(download) : retention.Window,
            download.IsFinished);
    }

    private static KeepResult NotFound() => new(KeepStatus.NotFound,
        "Nothing on disk answers to that. Pick a film from the suggestions.");

    private static KeepResult Unavailable() => new(KeepStatus.Unavailable,
        "The torrent client is not answering, so nothing changed. Try again in a moment.");
}
