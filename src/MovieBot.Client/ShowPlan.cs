using System.Globalization;
using TheKrystalShip.MovieBot.Core;

namespace TheKrystalShip.MovieBot.Client;

/// <summary>One download a plan makes: a release, and the episodes to fetch out of it (null for all).</summary>
public sealed record PlannedDownload(ReleaseOffer Release, IReadOnlyList<EpisodePick>? Episodes);

/// <summary>
/// Which of the tracker's releases fetch the episodes somebody picked, at the quality they asked for,
/// and roughly what that takes on disk.
///
/// A season's pack at the quality is preferred, fetching only the picked files out of it; an episode
/// with no pack comes on its own; an episode offered only at another quality comes at that one and is
/// counted, so whoever words the answer can say so. One torrent covering several seasons is one
/// download. /show and the assistant's download_show both plan this way.
/// </summary>
public sealed record ShowPlan(
    IReadOnlyList<PlannedDownload> Downloads, long Bytes, int OtherQuality, IReadOnlyList<EpisodePick> NotOffered)
{
    /// <summary>The quality a show is fetched at unless somebody asks otherwise.</summary>
    public const string DefaultQuality = "720p";

    public static ShowPlan For(ShowOffers offers, IReadOnlyList<EpisodePick> picked, string quality)
    {
        var downloads = new Dictionary<long, (ReleaseOffer Release, List<EpisodePick>? Episodes)>();
        var order = new List<long>();
        long bytes = 0;
        var other = 0;
        var missing = new List<EpisodePick>();

        void Add(ReleaseOffer release, EpisodePick episode, bool whole)
        {
            if (!downloads.TryGetValue(release.TorrentId, out var entry))
            {
                entry = (release, whole ? null : []);
                order.Add(release.TorrentId);
            }
            entry.Episodes?.Add(episode);
            downloads[release.TorrentId] = entry;
        }

        foreach (var pick in picked.Distinct())
        {
            var season = offers.Seasons.FirstOrDefault(s => s.Season == pick.Season);
            if (season is null)
            {
                missing.Add(pick);
                continue;
            }

            var pack = season.Packs.FirstOrDefault(p => p.Resolution == quality);
            if (pack is not null)
            {
                Add(pack, pick, whole: false);
                bytes += pack.SizePerEpisode ?? 0;
                continue;
            }

            var own = season.Episodes.FirstOrDefault(e => e.Episode == pick.Episode);
            if (own?.Releases.FirstOrDefault(r => r.Resolution == quality) is { } single)
            {
                Add(single, pick, whole: true);
                bytes += single.SizeBytes;
                continue;
            }

            if (own?.Releases.FirstOrDefault() is { } anySingle)
            {
                other++;
                Add(anySingle, pick, whole: true);
                bytes += anySingle.SizeBytes;
            }
            else if (season.Packs.FirstOrDefault() is { } anyPack)
            {
                other++;
                Add(anyPack, pick, whole: false);
                bytes += anyPack.SizePerEpisode ?? 0;
            }
            else
            {
                missing.Add(pick);
            }
        }

        return new ShowPlan(
            [.. order.Select(id => new PlannedDownload(downloads[id].Release, downloads[id].Episodes))],
            bytes, other, missing);
    }

    /// <summary>
    /// The episodes of one season a person named: <c>all</c> (or nothing) for every episode the
    /// tracker offers of it, or a list of numbers and ranges, <c>1-3, 5</c>. Null when the text is
    /// neither.
    /// </summary>
    public static IReadOnlyList<EpisodePick>? Episodes(ShowOffers offers, int season, string? named)
    {
        var text = (named ?? "").Trim();
        if (text.Length == 0 || text.Equals("all", StringComparison.OrdinalIgnoreCase))
        {
            if (offers.Seasons.FirstOrDefault(s => s.Season == season) is not { } offered) return [];
            var numbers = offered.Episodes.Select(e => e.Episode)
                .Concat(offered.Packs.Count > 0 ? Enumerable.Range(1, offered.Packs.Max(p => p.EpisodeCount ?? 0)) : [])
                .Distinct().Order();
            return [.. numbers.Select(e => new EpisodePick(season, e))];
        }

        var picks = new List<EpisodePick>();
        foreach (var part in text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var ends = part.TrimStart('e', 'E').Split('-', StringSplitOptions.TrimEntries);
            if (!int.TryParse(ends[0].TrimStart('e', 'E'), NumberStyles.None, CultureInfo.InvariantCulture, out var from)) return null;
            var to = from;
            if (ends.Length == 2 && !int.TryParse(ends[1].TrimStart('e', 'E'), NumberStyles.None, CultureInfo.InvariantCulture, out to)) return null;
            if (ends.Length > 2 || from < 1 || to < from || to - from > 100) return null;
            picks.AddRange(Enumerable.Range(from, to - from + 1).Select(e => new EpisodePick(season, e)));
        }
        return [.. picks.Distinct()];
    }
}
