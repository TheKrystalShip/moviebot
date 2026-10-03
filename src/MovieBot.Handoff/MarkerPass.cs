using Microsoft.Extensions.Logging;
using TheKrystalShip.MovieBot.Core;
using TheKrystalShip.MovieBot.Ingest.Markers;

namespace TheKrystalShip.MovieBot.Handoff;

/// <summary>
/// Finds the stretches of a finished episode a viewer can skip, and writes them into its manifest.
///
/// A chapter the container names "Intro" or "Recap" is taken as it stands. Where there is none, the
/// episode's audio is fingerprinted and compared with the show's other episodes: the stretch its
/// opening shares with another episode is the title sequence, and the stretches its opening shares
/// with episodes that came before are the clips of a recap. The comparison is chromaprint's, run
/// on the transcoded audio, and uses the CPU alone.
///
/// The pass runs once a transcode is ready and is a rider on it: an episode is watchable long before
/// this, a failure here leaves the markers empty and nothing else, and nothing is ever written
/// that the audio did not show. Whole-episode fingerprints are kept in <see cref="FingerprintStore"/>
/// so an episode can be compared with ones that have left the library.
/// </summary>
public sealed class MarkerPass(
    MediaRoots roots,
    FingerprintStore store,
    TitleLocks locks,
    ILogger<MarkerPass> logger)
{
    /// <summary>How many other episodes a new one is compared with for its title sequence.</summary>
    public const int PartnersTried = 3;

    /// <summary>
    /// How many of the earlier episodes a recap is looked for in, nearest first. A recap quotes the
    /// episodes just before it, and the cost of a comparison is linear in how many there are.
    /// </summary>
    public const int EarlierEpisodesCompared = 40;

    /// <summary>The furthest into an episode a recap is looked for when the titles begin later.</summary>
    public const double MaxRecapSearchSeconds = 480;

    /// <summary>Two partners agree on where the sequence is when their ends are this close.</summary>
    private const double AgreementSeconds = 2;

    /// <summary>
    /// Fingerprints the episode and keeps the fingerprint, without placing any marker. A library
    /// being gone over for the first time takes this pass over every episode before it takes the
    /// other, so that each is compared with all the rest.
    /// </summary>
    public async Task FingerprintAsync(string id, CancellationToken ct)
    {
        try
        {
            using var held = await locks.AcquireAsync(id, ct);
            if (Open(id) is not { } title) return;

            await FingerprintOfAsync(title, reuseStored: true, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "{Id}: the episode could not be fingerprinted.", id);
        }
    }

    /// <summary>
    /// Places the markers of one episode. Never throws except to be cancelled: an episode whose
    /// pass fails keeps the markers it had.
    /// </summary>
    /// <param name="reuseStored">
    /// Use the fingerprint already kept for the episode instead of reading its audio again. An
    /// episode just transcoded is read, since its audio is new; one being gone over again is not.
    /// </param>
    public async Task RunAsync(string id, bool reuseStored, CancellationToken ct)
    {
        IReadOnlyList<(string TitleId, Marker Marker)> partners = [];

        try
        {
            using (await locks.AcquireAsync(id, ct))
                partners = await PlaceAsync(id, reuseStored, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "{Id}: markers could not be placed.", id);
        }

        // After the episode's own lock is released: two episodes finishing together each want the
        // other's, and holding one while waiting for the other is how they would wait forever.
        foreach (var (partnerId, marker) in partners)
        {
            try
            {
                await WritePartnerAsync(partnerId, marker, ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "{Id}: the title sequence could not be written.", partnerId);
            }
        }
    }

    /// <summary>An episode that is ready, with where its files are.</summary>
    private sealed record Title(string Id, string Directory, Manifest Manifest, string Path);

    private Title? Open(string id)
    {
        if (!MediaRoots.IsSafeId(id) || roots.DirectoryOf(id) is not { } directory) return null;

        var path = Path.Combine(directory, "manifest.json");
        var manifest = Read(path);

        return manifest is { Status: TitleStatus.Ready, Episode: not null }
            ? new Title(id, directory, manifest, path)
            : null;
    }

    private static Manifest? Read(string path)
    {
        try
        {
            return File.Exists(path) ? ManifestJson.Deserialize(File.ReadAllText(path)) : null;
        }
        catch (Exception ex) when (ex is IOException or System.Text.Json.JsonException)
        {
            return null;
        }
    }

    private async Task<IReadOnlyList<(string, Marker)>> PlaceAsync(string id, bool reuseStored, CancellationToken ct)
    {
        if (Open(id) is not { } title) return [];

        var manifest = title.Manifest;
        var episode = manifest.Episode!;
        var key = FingerprintStore.ShowKey(manifest);
        var markers = ChapterMarkers.From(manifest.Chapters, manifest.DurationSeconds).ToList();

        var intro = markers.FirstOrDefault(m => m.Kind == MarkerKind.Intro);
        var recap = markers.FirstOrDefault(m => m.Kind == MarkerKind.Recap);
        var partners = new List<(string, Marker)>();

        uint[]? own = null;
        if (key is not null)
        {
            try
            {
                own = await FingerprintOfAsync(title, reuseStored, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // The chapters still stand; what is lost is the audio's say.
                logger.LogWarning(ex, "{Id}: the audio could not be fingerprinted.", id);
            }
        }

        if (own is { Length: > 0 } && key is not null)
        {
            if (intro is null && FindIntro(id, key, episode, own, manifest.DurationSeconds) is { } found)
            {
                intro = found.Marker;
                markers.Add(intro);
                partners.AddRange(found.Partners);
            }

            if (recap is null && FindRecap(id, key, episode, own, intro) is { } match)
            {
                recap = AudioMarker(MarkerKind.Recap, match.StartSeconds, match.EndSeconds, manifest.DurationSeconds);
                markers.Add(recap);
            }
        }

        // What an earlier pass found and this one did not stays: it was found against episodes that
        // may since have left the library, and the audio does not stop being what it was.
        foreach (var kept in manifest.Markers)
            if (!markers.Any(m => m.Kind == kept.Kind)) markers.Add(kept);

        // Whatever found a title sequence, one with somebody speaking in it is cut short of the line.
        var cues = CuesOf(title);
        markers = [.. markers.Select(m => m.Kind == MarkerKind.Intro ? IntroGuard.Guard(m, cues) : m)
            .OfType<Marker>()
            .OrderBy(m => m.StartSeconds)];

        // Read again right before writing, and only the markers are replaced: nothing else in the
        // manifest is this pass's to change.
        var fresh = Read(title.Path);
        if (fresh is not { Status: TitleStatus.Ready }) return partners;

        if (!fresh.Markers.SequenceEqual(markers))
        {
            fresh.Markers = markers;
            ManifestJson.WriteAtomic(title.Path, fresh);
        }

        logger.LogInformation(
            "{Id}: {Markers}", id,
            markers.Count == 0
                ? "nothing to skip."
                : string.Join(", ", markers.Select(m => $"{m.Kind.ToString().ToLowerInvariant()} "
                    + $"{m.StartSeconds:0.0}-{m.EndSeconds:0.0} s ({m.From.ToString().ToLowerInvariant()})")));

        return partners;
    }

    private async Task<uint[]> FingerprintOfAsync(Title title, bool reuseStored, CancellationToken ct)
    {
        var manifest = title.Manifest;
        var key = FingerprintStore.ShowKey(manifest);
        var episode = manifest.Episode!;

        if (key is null) return [];

        if (reuseStored && store.Load(key, episode.Season, episode.Number) is { } kept && kept.TitleId == title.Id)
            return kept.Items;

        var audio = manifest.Audio.FirstOrDefault();
        if (audio is null) return [];

        var path = Path.GetFullPath(Path.Combine(title.Directory, audio.Uri));
        if (!path.StartsWith(Path.GetFullPath(title.Directory) + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            return [];

        var items = await Fpcalc.RunAsync(path, lengthSeconds: 0, ct);
        if (items.Length > 0)
            store.Save(key, new StoredFingerprint(title.Id, episode.Season, episode.Number, items));

        return items;
    }

    private sealed record IntroFound(Marker Marker, IReadOnlyList<(string, Marker)> Partners);

    /// <summary>
    /// The title sequence this episode shares with the episodes nearest it in the show, nearest
    /// first: the same season, then the seasons beside it. The first that shares one decides where
    /// it is in this episode; every other that shares it where this one says is given it too, if it
    /// has none.
    /// </summary>
    private IntroFound? FindIntro(
        string id, string key, EpisodeIdentity episode, uint[] own, double durationSeconds)
    {
        var matches = new List<(StoredFingerprint Partner, IntroMatch Match)>();

        var nearest = store.Episodes(key)
            .Where(e => (e.Season, e.Number) != (episode.Season, episode.Number))
            .OrderBy(e => Math.Abs(e.Season - episode.Season))
            .ThenBy(e => Math.Abs(e.Number - episode.Number))
            .Take(PartnersTried);

        foreach (var (season, number) in nearest)
        {
            if (store.Load(key, season, number) is not { } partner || partner.TitleId == id) continue;
            if (IntroFinder.Find(own, partner.Items) is { } match) matches.Add((partner, match));
        }

        if (matches.Count == 0) return null;

        var first = matches[0].Match;
        var marker = AudioMarker(MarkerKind.Intro, first.StartA, first.EndA, durationSeconds);

        var partners = matches
            .Where(m => Math.Abs(m.Match.StartA - first.StartA) <= AgreementSeconds
                        && Math.Abs(m.Match.EndA - first.EndA) <= AgreementSeconds)
            .Select(m => (m.Partner.TitleId,
                AudioMarker(MarkerKind.Intro, m.Match.StartB, m.Match.EndB, double.MaxValue)))
            .ToList();

        return new IntroFound(marker, partners);
    }

    private RecapMatch? FindRecap(
        string id, string key, EpisodeIdentity episode, uint[] own, Marker? intro)
    {
        var search = Math.Min(intro?.StartSeconds ?? RecapFinder.DefaultSearchSeconds, MaxRecapSearchSeconds);
        if (search < RecapFinder.MinClipSeconds * RecapFinder.MinClips) return null;

        var earlier = store.Episodes(key)
            .Where(e => (e.Season, e.Number).CompareTo((episode.Season, episode.Number)) < 0)
            .OrderByDescending(e => e.Season).ThenByDescending(e => e.Number)
            .Take(EarlierEpisodesCompared)
            .Select(e => store.Load(key, e.Season, e.Number))
            .Where(f => f is not null && f.TitleId != id)
            .Select(f => f!.Items)
            .ToList();

        return earlier.Count == 0 ? null : RecapFinder.Find(own, earlier, search, intro?.StartSeconds);
    }

    /// <summary>A sequence found in another episode's audio, written to that episode when it has none.</summary>
    private async Task WritePartnerAsync(string partnerId, Marker marker, CancellationToken ct)
    {
        using var held = await locks.AcquireAsync(partnerId, ct);

        if (Open(partnerId) is not { } title) return;
        if (title.Manifest.Markers.Any(m => m.Kind == MarkerKind.Intro)) return;

        var end = Math.Min(marker.EndSeconds, title.Manifest.DurationSeconds);
        if (IntroGuard.Guard(marker with { EndSeconds = Math.Round(end, 3) }, CuesOf(title)) is not { } guarded)
            return;

        var markers = title.Manifest.Markers
            .Append(guarded)
            .OrderBy(m => m.StartSeconds)
            .ToList();

        title.Manifest.Markers = markers;
        ManifestJson.WriteAtomic(title.Path, title.Manifest);

        logger.LogInformation(
            "{Id}: intro {Start:0.0}-{End:0.0} s (audio), found from another episode.",
            partnerId, guarded.StartSeconds, guarded.EndSeconds);
    }

    /// <summary>
    /// The spoken cues of the episode's subtitles: its first available text track that is not
    /// forced, preferring English. Subtitles are written before a title is ready, so they are on
    /// disk by the time this runs; an episode with none, or whose track cannot be read, gives no
    /// cues and its markers are not trimmed.
    /// </summary>
    private IReadOnlyList<Cue> CuesOf(Title title)
    {
        var track = title.Manifest.Subtitles
            .Where(t => t is { Available: true, Forced: false, Source: not SubtitleSource.Bitmap, Uri: not null })
            .OrderByDescending(t => t.Language == "eng")
            .ThenBy(t => t.HearingImpaired)
            .FirstOrDefault();

        if (track is null) return [];

        try
        {
            var directory = Path.GetFullPath(title.Directory) + Path.DirectorySeparatorChar;
            var path = Path.GetFullPath(Path.Combine(title.Directory, track.Uri!));

            return path.StartsWith(directory, StringComparison.Ordinal) && File.Exists(path)
                ? IntroGuard.ParseWebVtt(File.ReadAllText(path))
                : [];
        }
        catch (Exception ex) when (ex is IOException or FormatException)
        {
            logger.LogDebug(ex, "{Id}: the subtitles could not be read.", title.Id);
            return [];
        }
    }

    private static Marker AudioMarker(MarkerKind kind, double start, double end, double durationSeconds) => new()
    {
        Kind = kind,
        StartSeconds = Math.Round(start, 3),
        EndSeconds = Math.Round(Math.Min(end, durationSeconds), 3),
        From = MarkerSource.Audio
    };
}
