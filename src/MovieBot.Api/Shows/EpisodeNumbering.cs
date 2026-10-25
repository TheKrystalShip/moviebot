using System.Collections.Concurrent;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using TheKrystalShip.MovieBot.Acquire.Search;
using TheKrystalShip.MovieBot.Acquire.Tmdb;

namespace TheKrystalShip.MovieBot.Api.Shows;

/// <summary>
/// The library's numbering of a show's episodes, and the film database's description put into it.
///
/// Everything that acts on an episode numbers it as its release does: the tracker, the torrent's
/// files, the library and the subtitle index. The film database sometimes numbers a season
/// differently, most often by counting a special or a promotion as its first episode, and then every
/// name, still and date it gives is attached to the wrong episode. The evidence is the episode's own
/// name, which a release or file name usually carries after its marker
/// (<c>Superstore.S02E07.Election.Day.720p…</c>): each name matched to the database's episode of the
/// same name in the same season anchors the two numberings, and the offset between them holds until
/// the next anchor. A season with nothing to match keeps the database's numbering. A database
/// episode with no release counterpart before the first one is a special, and leaves the season.
/// </summary>
public sealed partial class EpisodeNumbering
{
    private readonly ConcurrentDictionary<string, Evidence> _evidence = new(StringComparer.Ordinal);

    /// <summary>Each description's anchors and its renumbered copy, worked out once per description and evidence.</summary>
    private readonly ConditionalWeakTable<StoredShow, Worked> _worked = new();

    private sealed record Worked(Evidence Evidence, Dictionary<int, List<Anchor>> Anchors, StoredShow Applied);

    /// <summary>The episode names a show's releases carry, by season and episode as the releases number them.</summary>
    private sealed record Evidence(IReadOnlyDictionary<(int Season, int Number), string> Names);

    /// <summary>One point where the two numberings are known to meet.</summary>
    private readonly record struct Anchor(int Release, int Database);

    [GeneratedRegex(@"^S\d{1,2}(?:E\d{1,3}(?:-?E?\d{1,3})*)?", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex MarkerToken();

    /// <summary>A word that starts the encode's description rather than continuing the episode's name.</summary>
    [GeneratedRegex(
        @"^(?:\d{3,4}[pi]|WEB(?:-?DL|-?Rip)?|HDTV|BluRay|BDRip|BRRip|DVDRip|REMUX|AMZN|DSNP|NF|HULU|ATVP|MAX|HMAX|PCOK|PMTP|iP|CRAV|STAN|"
        + @"PROPER|REPACK|iNTERNAL|REAL|RERIP|x26[45]|[hH]\.?26[45]|HEVC|AVC|AAC(?:2\.0)?|DDP?(?:5\.1|2\.0)?|EAC3|AC3|10bit|HDR|DV)$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex EncodeWord();

    [GeneratedRegex(@"[^a-z0-9]")]
    private static partial Regex NotLetterOrDigit();

    /// <summary>
    /// The episode's own name out of a release or file name, between its marker and the encode, or
    /// null when it carries none.
    /// </summary>
    public static string? EpisodeName(string releaseOrFile)
    {
        var name = Path.GetFileNameWithoutExtension(releaseOrFile);
        if (EpisodeMarker.Read(name) is not { IsSeasonPack: false } marker) return null;
        var rest = name[marker.Index..];
        rest = rest[MarkerToken().Match(rest).Length..];

        var words = new List<string>();
        foreach (var word in rest.Split(['.', ' ', '_'], StringSplitOptions.RemoveEmptyEntries))
        {
            if (EncodeWord().IsMatch(word) || word.Contains('-', StringComparison.Ordinal) && EncodeWord().IsMatch(word.Split('-')[0])) break;
            words.Add(word);
        }
        return words.Count > 0 ? string.Join(' ', words) : null;
    }

    /// <summary>
    /// Replaces what is known of a show's episode names, as its releases number them: one entry per
    /// release or file name. A name without an episode marker or an episode's name adds nothing.
    /// </summary>
    public void Learn(string showImdbId, IEnumerable<string> releaseOrFileNames)
    {
        var names = new Dictionary<(int, int), string>();
        foreach (var source in releaseOrFileNames)
        {
            if (EpisodeMarker.Read(Path.GetFileName(source)) is not { IsSeasonPack: false } marker) continue;
            if (EpisodeName(source) is not { } name) continue;
            names[(marker.Season, marker.Episodes[0])] = name;
        }
        _evidence[showImdbId] = new Evidence(names);
    }

    /// <summary>The description with every episode, and the airing ones, numbered as the show's releases number them.</summary>
    public StoredShow Apply(string showImdbId, StoredShow stored)
    {
        return Work(showImdbId, stored)?.Applied ?? stored;
    }

    private Worked? Work(string showImdbId, StoredShow stored)
    {
        if (!_evidence.TryGetValue(showImdbId, out var evidence) || evidence.Names.Count == 0) return null;
        if (_worked.TryGetValue(stored, out var worked) && ReferenceEquals(worked.Evidence, evidence)) return worked;

        var anchors = Anchors(stored, evidence);
        worked = new Worked(evidence, anchors, anchors.Values.All(a => a.All(x => x.Release == x.Database)) ? stored : Renumber(stored, anchors));
        _worked.AddOrUpdate(stored, worked);
        return worked;
    }

    private static StoredShow Renumber(StoredShow stored, Dictionary<int, List<Anchor>> anchors)
    {

        AiringEpisode? Airing(AiringEpisode? airing) =>
            airing is null ? null
            : ToRelease(anchors, airing.Season, airing.Number) is { } number ? airing with { Number = number } : null;

        return stored with
        {
            Details = stored.Details is { } details
                ? details with
                {
                    LastEpisodeToAir = Airing(details.LastEpisodeToAir),
                    NextEpisodeToAir = Airing(details.NextEpisodeToAir),
                }
                : null,
            Seasons = [.. stored.Seasons.Select(season => season with
            {
                Episodes = [.. season.Episodes
                    .Select(e => (Episode: e, Number: ToRelease(anchors, e.Season, e.Number)))
                    .Where(e => e.Number is not null)
                    .Select(e => e.Episode with { Number = e.Number!.Value })],
            })],
        };
    }

    /// <summary>Every season's anchors, where a release's episode name is the database's episode of the same name.</summary>
    private static Dictionary<int, List<Anchor>> Anchors(StoredShow stored, Evidence evidence)
    {
        var anchors = new Dictionary<int, List<Anchor>>();
        foreach (var season in stored.Seasons)
        {
            var named = season.Episodes
                .Where(e => Key(e.Name) is { Length: >= 4 })
                .GroupBy(e => Key(e.Name)!)
                .Where(g => g.Count() == 1)
                .ToDictionary(g => g.Key, g => g.Single().Number);

            var found = new List<Anchor>();
            foreach (var ((s, release), name) in evidence.Names)
            {
                if (s != season.Number || Key(name) is not { Length: >= 4 } key) continue;
                var match = named.FirstOrDefault(n => n.Key == key || n.Key.StartsWith(key, StringComparison.Ordinal)
                    || key.StartsWith(n.Key, StringComparison.Ordinal));
                if (match.Key is not null) found.Add(new Anchor(release, match.Value));
            }
            if (found.Count > 0) anchors[season.Number] = [.. found.OrderBy(a => a.Release)];
        }
        return anchors;
    }

    private static int? ToRelease(Dictionary<int, List<Anchor>> anchors, int season, int database)
    {
        if (!anchors.TryGetValue(season, out var at)) return database;
        var anchor = at.LastOrDefault(a => a.Database <= database) is { Release: > 0 } below ? below : at[0];
        var release = database - (anchor.Database - anchor.Release);
        return release >= 1 && ToDatabase(anchors, season, release) == database ? release : null;
    }

    private static int? ToDatabase(Dictionary<int, List<Anchor>> anchors, int season, int release)
    {
        if (!anchors.TryGetValue(season, out var at)) return release;
        var anchor = at.LastOrDefault(a => a.Release <= release) is { Release: > 0 } below ? below : at[0];
        var database = release + (anchor.Database - anchor.Release);
        return database >= 1 ? database : null;
    }

    private static string? Key(string? name) =>
        name is null ? null : NotLetterOrDigit().Replace(name.ToLower(CultureInfo.InvariantCulture), "");
}
