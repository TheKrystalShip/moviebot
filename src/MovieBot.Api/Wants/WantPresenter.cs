using TheKrystalShip.MovieBot.Acquire.Download;
using TheKrystalShip.MovieBot.Api.Acquisition;
using TheKrystalShip.MovieBot.Api.Catalogue;
using TheKrystalShip.MovieBot.Api.Library;
using TheKrystalShip.MovieBot.Api.People;
using TheKrystalShip.MovieBot.Api.Shows;
using TheKrystalShip.MovieBot.Core;

namespace TheKrystalShip.MovieBot.Api.Wants;

/// <summary>
/// Turns stored wants into what a page shows, reading only what is kept: the catalogue, the library as
/// last listed, the watch record, the wishes and the torrent client.
/// Nobody's want is shown to anybody else except as a count and, when visible, as a name.
/// </summary>
public sealed class WantPresenter(
    CatalogueTitles shows,
    LibraryListing library,
    WatchRecord record,
    PersonDirectory people,
    WantStore wants,
    WantMatcher matcher,
    WishList wishes,
    AcquisitionService acquisition,
    ILogger<WantPresenter> logger)
{
    public async Task<List<WantView>> ViewsAsync(string userId, IReadOnlyList<StoredWant> list, CancellationToken ct)
    {
        var titles = library.Titles();
        var shelf = LibraryShelf.Of(titles, record, userId);
        var others = wants.OthersOf(userId);
        var finished = ShowProgress.Finished(titles, record.Entries(userId));
        var arriving = list.Any(w => !shelf.Holds(w.Kind, w.ImdbId)) ? await DownloadsAsync(ct) : [];

        return [.. list.Select(w => View(w, shelf, others, finished, arriving))];
    }

    public async Task<WantView> ViewAsync(StoredWant want, CancellationToken ct) =>
        (await ViewsAsync(want.UserId, [want], ct)).Single();

    /// <summary>The people with a visible want on a title, the one asking left out.</summary>
    public List<WanterView> Wanters(string imdbId, string exceptUserId) =>
        WanterViews(wants.Wanters(imdbId, exceptUserId));

    /// <summary>The titles the person wants that others visibly want at the same point, ready ones first.</summary>
    public async Task<List<TogetherView>> TogetherAsync(string userId, CancellationToken ct)
    {
        var matches = matcher.Of(userId);
        if (matches.Count == 0) return [];

        var views = await ViewsAsync(userId, [.. matches.Select(m => m.Mine)], ct);
        return [.. matches.Select((m, i) => new TogetherView { Want = views[i], With = WanterViews(m.With) })];
    }

    private List<WanterView> WanterViews(IEnumerable<StoredWant> list)
    {
        var titles = library.Titles();
        return [.. list.Select(w => new WanterView
        {
            UserId = w.UserId,
            Name = people.NameOf(w.UserId),
            Since = w.Since,
            NextEpisode = w.Kind == WantKinds.Show
                ? ShowProgress.Next(shows.Show(w.ImdbId), ShowProgress.Finished(titles, record.Entries(w.UserId)).GetValueOrDefault(w.ImdbId))
                : null,
        })];
    }

    private WantView View(
        StoredWant want, LibraryShelf shelf, Dictionary<string, int> others,
        Dictionary<string, HashSet<(int, int)>> finished, List<DownloadStatus> arriving)
    {
        var isShow = want.Kind == WantKinds.Show;
        var wish = isShow ? null : wishes.Find(want.ImdbId);
        var release = wish is null ? null : wishes.OutAs(want.ImdbId);

        var card = shows.Card(want.ImdbId);
        var view = new WantView
        {
            ImdbId = want.ImdbId, Kind = want.Kind, Title = card?.Title ?? wish?.Title ?? want.ImdbId, Card = card,
            Visible = want.Visible, Since = want.Since,
            Others = others.GetValueOrDefault(want.ImdbId),
            Wish = wish is null ? null : new WantWish { Release = release is null ? null : OfferedReleases.View(release) },
        };

        if (isShow)
        {
            view = view with
            {
                InLibrary = shelf.Shows.Contains(want.ImdbId),
                NextEpisode = ShowProgress.Next(shows.Show(want.ImdbId), finished.GetValueOrDefault(want.ImdbId)),
            };
        }
        else
        {
            var libraryId = shelf.Films.GetValueOrDefault(want.ImdbId);
            view = view with { LibraryId = libraryId, InLibrary = libraryId is not null, Watched = shelf.Watched.Contains(want.ImdbId) };
        }

        if (view.InLibrary) return view with { State = WantStates.Ready };

        var download = arriving.FirstOrDefault(d => d.Tags.Select(TorrentTags.ReadImdb)
            .Any(id => string.Equals(id, want.ImdbId, StringComparison.OrdinalIgnoreCase)));
        if (download is not null) return view with { State = WantStates.Fetching, Progress = download.Progress };

        return wish is null ? view : view with { State = release is null ? WantStates.Waiting : WantStates.Available };
    }

    /// <summary>The torrent client's downloads, or none when it cannot be asked: a state that cannot be measured is left out.</summary>
    private async Task<List<DownloadStatus>> DownloadsAsync(CancellationToken ct)
    {
        try
        {
            return [.. await acquisition.ListAsync(ct)];
        }
        catch (QBittorrentException ex)
        {
            logger.LogWarning(ex, "The downloads could not be read for the wants.");
            return [];
        }
    }
}
