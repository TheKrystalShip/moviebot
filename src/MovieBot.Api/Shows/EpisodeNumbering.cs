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
/// same name in the same season anchors that episode to its release's number, and every episode
/// between anchors takes the offset of the anchor below it in the database's order. A season with
/// nothing to match keeps the database's numbering. A database episode with no release counterpart
/// before the first one is a special, and leaves the season.
/// </summary>
public sealed partial class EpisodeNumbering
{
    private readonly ConcurrentDictionary<string, Evidence> _evidence = new(StringComparer.Ordinal);

    /// <summary>Each description's anchors and its renumbered copy, worked out once per description and evidence.</summary>
    private readonly ConditionalWeakTable<StoredShow, Worked> _worked = new();

    private sealed record Worked(Evidence Evidence, Dictionary<int, SeasonNumbering> Seasons, StoredShow Applied);

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
    public StoredShow Apply(string showImdbId, StoredShow stored) => Work(showImdbId, stored)?.Applied ?? stored;

    private Worked? Work(string showImdbId, StoredShow stored)
    {
        if (!_evidence.TryGetValue(showImdbId, out var evidence) || evidence.Names.Count == 0) return null;
        if (_worked.TryGetValue(stored, out var worked) && ReferenceEquals(worked.Evidence, evidence)) return worked;

        var seasons = Seasons(stored, evidence);
        worked = new Worked(evidence, seasons, seasons.Values.All(s => s.Agrees) ? stored : Renumber(stored, seasons));
        _worked.AddOrUpdate(stored, worked);
        return worked;
    }

    private static StoredShow Renumber(StoredShow stored, Dictionary<int, SeasonNumbering> seasons)
    {
        int? ToRelease(int season, int database) =>
            seasons.TryGetValue(season, out var numbering) ? numbering.ToRelease(database) : database;

        AiringEpisode? Airing(AiringEpisode? airing) =>
            airing is null ? null
            : ToRelease(airing.Season, airing.Number) is { } number ? airing with { Number = number } : null;

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
                    .Select(e => (Episode: e, Number: ToRelease(e.Season, e.Number)))
                    .Where(e => e.Number is not null)
                    .Select(e => e.Episode with { Number = e.Number!.Value })],
            })],
        };
    }

    /// <summary>Every season with anchors, where a release's episode name is the database's episode of the same name.</summary>
    private static Dictionary<int, SeasonNumbering> Seasons(StoredShow stored, Evidence evidence)
    {
        var seasons = new Dictionary<int, SeasonNumbering>();
        foreach (var season in stored.Seasons)
        {
            var named = season.Episodes
                .Where(e => Key(e.Name) is { Length: >= 4 })
                .GroupBy(e => Key(e.Name)!)
                .Where(g => g.Count() == 1)
                .ToDictionary(g => g.Key, g => g.Single().Number);

            var found = new List<Anchor>();
            foreach (var ((s, release), name) in evidence.Names.OrderBy(n => n.Key.Number))
            {
                if (s != season.Number || Key(name) is not { Length: >= 4 } key) continue;
                var match = named.FirstOrDefault(n => n.Key == key || n.Key.StartsWith(key, StringComparison.Ordinal)
                    || key.StartsWith(n.Key, StringComparison.Ordinal));
                if (match.Key is not null) found.Add(new Anchor(release, match.Value));
            }
            if (found.Count > 0)
                seasons[season.Number] = new SeasonNumbering(found, season.Episodes.Select(e => e.Number));
        }
        return seasons;
    }

    /// <summary>
    /// One season's two numberings put together. An anchored episode takes its release's number
    /// outright, so a season the releases order differently from the database (an episode aired out
    /// of its production order) keeps every name on its own episode. Every other episode takes the
    /// offset of the nearest anchor below it in the database's order, or of the first anchor when
    /// none is below, and leaves the season when that lands before the first episode or on a number
    /// another episode holds.
    /// </summary>
    private sealed class SeasonNumbering
    {
        private readonly List<Anchor> _anchors;
        private readonly Dictionary<int, int> _listed = [];
        private readonly HashSet<int> _taken = [];

        public SeasonNumbering(IEnumerable<Anchor> anchors, IEnumerable<int> listed)
        {
            // One anchor per episode on either side: the first release to name a database episode keeps it.
            _anchors = [.. anchors.DistinctBy(a => a.Database).DistinctBy(a => a.Release).OrderBy(a => a.Database)];
            foreach (var anchor in _anchors)
            {
                _listed[anchor.Database] = anchor.Release;
                _taken.Add(anchor.Release);
            }
            foreach (var database in listed.Order())
                if (!_listed.ContainsKey(database) && Offset(database) is { } release && _taken.Add(release))
                    _listed[database] = release;
        }

        public bool Agrees => _anchors.All(a => a.Release == a.Database);

        public int? ToRelease(int database) =>
            _listed.TryGetValue(database, out var release) ? release
            : Offset(database) is { } offset && !_taken.Contains(offset) ? offset
            : null;

        private int? Offset(int database)
        {
            var anchor = _anchors.LastOrDefault(a => a.Database < database) is { Release: > 0 } below ? below : _anchors[0];
            var release = database - (anchor.Database - anchor.Release);
            return release >= 1 ? release : null;
        }
    }

    private static string? Key(string? name) =>
        name is null ? null : NotLetterOrDigit().Replace(name.ToLower(CultureInfo.InvariantCulture), "");
}
