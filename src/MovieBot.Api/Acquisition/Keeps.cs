using System.Globalization;
using TheKrystalShip.MovieBot.Acquire.Download;
using TheKrystalShip.MovieBot.Acquire.Search;
using TheKrystalShip.MovieBot.Api.Library;
using TheKrystalShip.MovieBot.Core;

namespace TheKrystalShip.MovieBot.Api.Acquisition;

/// <summary>
/// Keeps a film from being pruned, lets one go again, and says where every film on disk stands.
///
/// A keep is a note on the torrent, so this holds nothing: every answer is read from the torrent
/// client at the moment it is asked, and the hand-off reads the same note when it decides what to
/// prune. Anyone may keep a film and anyone may let it go, and the row says who did.
/// </summary>
public sealed class Keeps(
    AcquisitionService acquisition,
    TitleLibrary library,
    Retention retention,
    ILogger<Keeps> logger)
{
    /// <summary>Every download, with the library's name for it where the library has one.</summary>
    public async Task<List<KeptFilmView>?> ListAsync(CancellationToken ct)
    {
        try
        {
            var downloads = await acquisition.ListAsync(ct);
            var titles = library.List();
            return downloads
                .Select(d => Describe(d, titles))
                .OrderBy(f => f.Keeper is not null)
                .ThenBy(f => f.RemainingSeconds)
                .ToList();
        }
        catch (QBittorrentException ex)
        {
            logger.LogWarning(ex, "The downloads could not be read.");
            return null;
        }
    }

    public async Task<KeepReply> KeepAsync(string hash, Actor requestedBy, CancellationToken ct)
    {
        var film = await FindAsync(hash, ct);
        if (film is null) return new KeepReply { Outcome = "notFound" };
        if (film.Keeper is not null) return new KeepReply { Outcome = "alreadyKept", Film = film };
        if (Downloads.Snowflake(requestedBy.UserId) is not { } userId) return new KeepReply { Outcome = "notFound" };

        try
        {
            await acquisition.TagAsync(hash, TorrentTags.Keeper(userId), ct);
            await acquisition.TagAsync(hash, TorrentTags.Keep, ct);
        }
        catch (QBittorrentException ex)
        {
            logger.LogWarning(ex, "Could not keep {Name}.", film.Name);
            return new KeepReply { Outcome = "unavailable", Film = film };
        }

        logger.LogInformation("{User} kept {Name}.", requestedBy.DisplayName, film.Release);
        return new KeepReply { Outcome = "kept", Film = film with { Keeper = requestedBy.UserId } };
    }

    public async Task<KeepReply> ReleaseAsync(string hash, Actor requestedBy, CancellationToken ct)
    {
        var film = await FindAsync(hash, ct);
        if (film is null) return new KeepReply { Outcome = "notFound" };
        if (Downloads.Snowflake(film.Keeper) is not { } keeper)
            return new KeepReply { Outcome = "notKept", Film = film };

        try
        {
            // The keep goes first. A process stopping between the two leaves a keeper without a
            // keep, which reads as nobody keeping it, where the other order leaves a keep nobody
            // owns.
            await acquisition.ClearTagAsync(hash, TorrentTags.Keep, ct);
            await acquisition.ClearTagAsync(hash, TorrentTags.Keeper(keeper), ct);
        }
        catch (QBittorrentException ex)
        {
            logger.LogWarning(ex, "Could not release {Name}.", film.Name);
            return new KeepReply { Outcome = "unavailable", Film = film };
        }

        logger.LogInformation("{User} let {Name} go.", requestedBy.DisplayName, film.Release);
        return new KeepReply { Outcome = "released", Film = film with { Keeper = null } };
    }

    /// <summary>
    /// Where a film in the library stands, or null when no download stands behind it: a film put
    /// there by hand is on no clock, and saying anything about it would be invented.
    /// </summary>
    public async Task<KeptFilmView?> ForTitleAsync(string libraryId, CancellationToken ct)
    {
        try
        {
            var downloads = await acquisition.ListAsync(ct);
            var download = downloads.FirstOrDefault(d =>
                d.Tags.Select(TorrentTags.ReadLibrary).Any(id => id == libraryId));
            return download is null ? null : Describe(download, library.List());
        }
        catch (QBittorrentException ex)
        {
            logger.LogDebug(ex, "The downloads could not be read for {Id}.", libraryId);
            return null;
        }
    }

    private async Task<KeptFilmView?> FindAsync(string hash, CancellationToken ct)
    {
        try
        {
            var download = await acquisition.StatusAsync(hash, ct);
            return download is null ? null : Describe(download, library.List());
        }
        catch (QBittorrentException ex)
        {
            logger.LogWarning(ex, "The downloads could not be read.");
            return null;
        }
    }

    private KeptFilmView Describe(DownloadStatus download, IReadOnlyList<TitleSummary> titles)
    {
        var id = download.Tags.Select(TorrentTags.ReadLibrary).FirstOrDefault(i => i is not null);
        var title = id is null ? null : titles.FirstOrDefault(t => t.Id == id);
        var keeper = download.Tags.Select(TorrentTags.ReadKeeper).FirstOrDefault(k => k is not null);

        return new KeptFilmView
        {
            Hash = download.Hash,
            Name = title?.Film?.Display ?? title?.Title ?? ReleaseParser.ParseName(download.Name).Display,
            Release = download.Name,
            LibraryId = id,
            Keeper = keeper?.ToString(CultureInfo.InvariantCulture),
            RemainingSeconds = (download.IsFinished ? retention.Remaining(download) : retention.Window).TotalSeconds,
            IsFinished = download.IsFinished,
            WindowSeconds = retention.Window.TotalSeconds,
        };
    }
}
