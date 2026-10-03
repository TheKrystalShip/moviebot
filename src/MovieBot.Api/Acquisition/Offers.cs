using TheKrystalShip.MovieBot.Acquire.Search;
using TheKrystalShip.MovieBot.Core;

namespace TheKrystalShip.MovieBot.Api.Acquisition;

/// <summary>
/// Every release this API has offered, by torrent id.
///
/// A client sends back only the id behind a row it was shown, and this is what turns that id into
/// the release it stood for. An id this API never offered is refused rather than looked up: a
/// release is resolved from a search, never from a number somebody typed, which is what keeps a
/// download from being of a film nobody chose. Kept across requests, because a pick often comes a
/// turn or a few minutes after the list, and bounded, because nothing that old is still a pick.
/// </summary>
public sealed class OfferedReleases(AutocompleteSearch suggestions)
{
    private const int Kept = 500;

    private readonly Dictionary<long, Release> _byId = [];
    private readonly Queue<long> _order = new();

    public void Remember(IEnumerable<Release> releases)
    {
        lock (_byId)
        {
            foreach (var release in releases)
            {
                if (_byId.TryAdd(release.TorrentId, release)) _order.Enqueue(release.TorrentId);
                else _byId[release.TorrentId] = release;
            }

            while (_order.Count > Kept) _byId.Remove(_order.Dequeue());
        }
    }

    /// <summary>The release behind an id this API offered, from a search or from a picker.</summary>
    public Release? Resolve(long torrentId)
    {
        lock (_byId)
        {
            if (_byId.TryGetValue(torrentId, out var release)) return release;
        }

        return suggestions.Resolve(torrentId);
    }

    /// <summary>A release as it crosses the wire.</summary>
    public static ReleaseOffer View(Release release) => new()
    {
        TorrentId = release.TorrentId,
        ReleaseName = release.ReleaseName,
        Title = release.Title,
        Year = release.Year,
        Display = release.Display,
        Summary = release.Summary,
        ImdbId = release.ImdbId,
        SizeBytes = release.SizeBytes,
        Seeders = release.Seeders,
    };
}
