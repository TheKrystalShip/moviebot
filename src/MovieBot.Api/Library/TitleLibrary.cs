using System.Collections.Concurrent;
using TheKrystalShip.MovieBot.Core;

namespace TheKrystalShip.MovieBot.Api.Library;

/// <summary>What the library listing shows, without the full track graph behind it.</summary>
public sealed record TitleSummary
{
    public required string Id { get; init; }
    public required string Title { get; init; }
    public required double DurationSeconds { get; init; }
    public required TitleStatus Status { get; init; }

    /// <summary>Whether the title is a film or part of a show, as its manifest says.</summary>
    public required TitleKind Kind { get; init; }

    /// <summary>For an episode, its place in the show; the show is <see cref="Film"/>.</summary>
    public EpisodeIdentity? Episode { get; init; }

    /// <summary>
    /// What stays or leaves with it as one, and what a keep is on: the title itself for a film, the
    /// season for an episode (<see cref="TitleLibrary.UnitOf(Manifest)"/>).
    /// </summary>
    public required string Unit { get; init; }

    public double? HeadSeconds { get; init; }
    public string? Poster { get; init; }

    /// <summary>
    /// Which film this is, as far as anything that catalogues films is concerned. Carried on the
    /// summary rather than left to the manifest, so a surface listing the library can show a film
    /// properly without fetching each one.
    /// </summary>
    public FilmIdentity? Film { get; init; }

    /// <summary>What the film takes on disk, transcoded: every rung, every track.</summary>
    public long? SizeBytes { get; init; }

    /// <summary>When the film came into the library: when its directory was made.</summary>
    public DateTimeOffset? AddedAt { get; init; }

    /// <summary>
    /// What the film database says about the film, as far as a listing shows it; absent until the
    /// film has been described.
    /// </summary>
    public Films.FilmFacts? Details { get; init; }

    /// <summary>
    /// How far the asking person has got in the film. Absent when they have not watched it, and for
    /// a caller that is nobody in particular.
    /// </summary>
    public People.TitleProgress? Progress { get; init; }

    /// <summary>When anybody last played the film and who; absent when nobody has.</summary>
    public People.TitleLastPlayed? LastPlayed { get; init; }
}

/// <summary>
/// Reads the manifests the ingest worker writes.
///
/// A manifest is rewritten every couple of seconds during a transcode, so this caches by
/// last-write time rather than holding a snapshot: the head a seek is judged against has to be
/// the head that is actually on disk, and re-reading a small JSON file when its timestamp moves
/// costs nothing next to being wrong.
/// </summary>
public sealed class TitleLibrary(
    MediaRoots roots,
    Subtitles.SubtitleStore subtitles,
    Subtitles.PinStore pins,
    ILogger<TitleLibrary> logger)
{
    private readonly ConcurrentDictionary<string, CacheEntry> _cache = new();

    /// <summary>
    /// Where a title's files are, or null when the library does not hold it.
    ///
    /// Asked per lookup rather than held, because a film moves from the disk it was made on to
    /// the disk it is kept on while the service is running, and a path resolved once would go on
    /// naming the copy that was deleted.
    /// </summary>
    public string? DirectoryOf(string id) => IsSafeId(id) ? roots.DirectoryOf(id) : null;

    private sealed record CacheEntry(Manifest Manifest, DateTime LastWriteUtc);

    public IReadOnlyList<TitleSummary> List()
    {
        var summaries = new List<TitleSummary>();
        foreach (var id in roots.Ids())
        {
            if (Get(id) is not { } manifest) continue;
            var footprint = Footprint(id);

            summaries.Add(new TitleSummary
            {
                Id = manifest.Id,
                Title = manifest.Title,
                DurationSeconds = manifest.DurationSeconds,
                Status = manifest.Status,
                Kind = manifest.Kind,
                Episode = manifest.Episode,
                Unit = UnitOf(manifest),
                HeadSeconds = manifest.HeadSeconds,
                Poster = manifest.Poster,
                Film = manifest.Film,
                SizeBytes = footprint?.SizeBytes,
                AddedAt = footprint?.AddedAt
            });
        }

        return summaries.OrderBy(s => s.Title, StringComparer.OrdinalIgnoreCase).ToList();
    }

    public Manifest? Get(string id)
    {
        if (DirectoryOf(id) is not { } directory) return null;

        var path = Path.Combine(directory, "manifest.json");
        if (!File.Exists(path)) return null;

        var lastWrite = File.GetLastWriteTimeUtc(path);
        if (_cache.TryGetValue(id, out var cached) && cached.LastWriteUtc == lastWrite)
            return Merge(id, cached.Manifest);

        try
        {
            var manifest = ManifestJson.Deserialize(File.ReadAllText(path));
            if (manifest is null) return null;

            _cache[id] = new CacheEntry(manifest, lastWrite);
            return Merge(id, manifest);
        }
        catch (Exception ex) when (ex is IOException or System.Text.Json.JsonException)
        {
            // The ingest worker writes through a temp file and an atomic move, so a torn read
            // should not happen; if it somehow does, the previous good copy is better than none.
            logger.LogWarning(ex, "Could not read manifest for {TitleId}", id);
            return _cache.TryGetValue(id, out var stale) ? stale.Manifest : null;
        }
    }

    /// <summary>
    /// Folds in the subtitles fetched from outside, which live under their own root and so are
    /// invisible to the manifest on disk.
    ///
    /// Done on the way out rather than at the point the manifest is read, because a subtitle added
    /// while a manifest sits unchanged in the cache still has to appear. Replacing the whole
    /// sidecar half each time is what makes calling it repeatedly harmless.
    /// </summary>
    private Manifest Merge(string id, Manifest manifest)
    {
        var fetched = subtitles.For(id);
        var embedded = manifest.Subtitles.Where(t => t.Source != SubtitleSource.Sidecar).ToList();
        var pinned = pins.For(id);

        List<SubtitleTrack> tracks = fetched.Count == 0
            ? embedded
            : [.. embedded, .. fetched.Select(Subtitles.SubtitleStore.AsTrack)];

        // A pin naming a track that is no longer here is simply not applied, which is how a stale
        // one disappears rather than attaching itself to whatever now holds that id.
        manifest.Subtitles = pinned.Count == 0
            ? tracks
            : tracks.Select(t => pinned.TryGetValue(t.Id, out var pin) ? t with { Pin = pin } : t).ToList();

        return manifest;
    }

    private sealed record FootprintEntry(long SizeBytes, DateTimeOffset AddedAt, DateTime ManifestWriteUtc);

    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, FootprintEntry> _footprints = new();

    /// <summary>
    /// What a film takes on disk and when it arrived. Walking a film's segments is thousands of
    /// files, so it is measured again only when the manifest moves: a finished film's size is
    /// fixed, and a transcoding one rewrites its manifest as it grows.
    /// </summary>
    private FootprintEntry? Footprint(string id)
    {
        if (DirectoryOf(id) is not { } directory || LastWriteOf(id) is not { } written) return null;
        if (_footprints.TryGetValue(id, out var known) && known.ManifestWriteUtc == written) return known;
        try
        {
            var size = new DirectoryInfo(directory)
                .EnumerateFiles("*", SearchOption.AllDirectories)
                .Sum(f => f.Length);
            var entry = new FootprintEntry(size, new DateTimeOffset(Directory.GetCreationTimeUtc(directory), TimeSpan.Zero), written);
            _footprints[id] = entry;
            return entry;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return known;
        }
    }

    /// <summary>When the manifest on disk was last written, or null when there is none.</summary>
    public DateTime? LastWriteOf(string id)
    {
        if (DirectoryOf(id) is not { } directory) return null;
        var path = Path.Combine(directory, "manifest.json");
        return File.Exists(path) ? File.GetLastWriteTimeUtc(path) : null;
    }

    /// <summary>
    /// The head a seek is judged against: how far the best rung has reached with audio under it,
    /// or null when the title is complete and the whole film is seekable.
    /// </summary>
    public double? HeadOf(string? titleId) =>
        titleId is not null && Get(titleId) is { } manifest ? TranscodeHeads.PlayableOf(manifest) : null;

    /// <summary>
    /// What a title stays or leaves with as one. A film is its own unit. An episode's is its season,
    /// named by the show's IMDb id where the show is known and by the show's part of the episode's
    /// id where it is not: <c>tt11280740:s02</c>, or <c>severance:s02</c>.
    /// </summary>
    public static string UnitOf(Manifest manifest)
    {
        if (manifest.Episode is not { } episode) return manifest.Id;

        var show = manifest.Film?.ImdbId is { Length: > 0 } imdbId
            ? imdbId
            : ShowPartOf(manifest.Id, episode);
        return string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{show}:s{episode.Season:00}");
    }

    /// <summary>The unit a title is in, or null when the library does not hold it.</summary>
    public string? UnitOf(string id) => Get(id) is { } manifest ? UnitOf(manifest) : null;

    /// <summary>Every title the library holds in a unit.</summary>
    public IReadOnlyList<string> IdsIn(string unit) =>
        [.. roots.Ids().Where(id => Get(id) is { } m && UnitOf(m) == unit)];

    /// <summary>
    /// The episodes of one show the library holds, in order, by the show's IMDb id.
    /// </summary>
    public IReadOnlyList<(string Id, Manifest Manifest)> EpisodesOf(string showImdbId) =>
        [.. roots.Ids()
            .Select(id => (Id: id, Manifest: Get(id)))
            .Where(t => t.Manifest is { Episode: not null } m && m.Film?.ImdbId == showImdbId)
            .Select(t => (t.Id, t.Manifest!))
            .OrderBy(t => t.Item2.Episode!.Season)
            .ThenBy(t => t.Item2.Episode!.Number)];

    private static string ShowPartOf(string id, EpisodeIdentity episode)
    {
        var suffix = "-" + episode.Label.ToLowerInvariant();
        return id.EndsWith(suffix, StringComparison.Ordinal) ? id[..^suffix.Length] : id;
    }

    /// <summary>
    /// Ids name a directory under the media root, so anything that could climb out of it is
    /// refused rather than sanitised — a rewritten path that still resolves is worse than a 404.
    /// </summary>
    public static bool IsSafeId(string id) => MediaRoots.IsSafeId(id);
}
