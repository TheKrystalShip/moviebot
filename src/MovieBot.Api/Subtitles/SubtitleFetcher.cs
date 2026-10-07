using System.Text;
using TheKrystalShip.MovieBot.Acquire.Subtitles;
using TheKrystalShip.MovieBot.Api.Library;
using TheKrystalShip.MovieBot.Core;
using TheKrystalShip.MovieBot.Core.Subtitles;

namespace TheKrystalShip.MovieBot.Api.Subtitles;

/// <summary>
/// Finding a title's subtitles at the subtitle index and fetching one onto the title: what a person
/// picking from the player's menu does, and what Download does for a title it was asked to bring with
/// subtitles in a language the title does not carry.
/// </summary>
public sealed class SubtitleFetcher(
    TitleLibrary library,
    SubtitleStore store,
    PinStore pins,
    OpenSubtitlesClient index,
    TitleChanges changes,
    ILogger<SubtitleFetcher> logger)
{
    public bool IsConfigured => index.IsConfigured;

    /// <summary>Downloads left today, as the index last stated it. Null before any were spent.</summary>
    public int? RemainingDownloads => index.RemainingDownloads;

    /// <summary>
    /// Every candidate in <paramref name="language"/>, judged against the film as it sits on disk, the
    /// best first. Throws <see cref="OpenSubtitlesException"/> when the index answers badly.
    /// </summary>
    public async Task<List<CheckedSubtitle>> SearchAsync(Manifest manifest, string language, CancellationToken ct)
    {
        var target = TargetOf(manifest);
        var found = await FindAsync(manifest, target, language, ct);
        return [.. found.Select(c => SubtitleChecks.For(c, target)).OrderByDescending(c => c.Score)];
    }

    /// <summary>
    /// Fetches one candidate onto the title in <paramref name="language"/>, shifted to fit the film, and
    /// tells every room watching it. A track somebody already confirmed stands as it is and spends
    /// nothing. Throws <see cref="OpenSubtitlesQuotaException"/> when the day's allowance is spent and
    /// <see cref="OpenSubtitlesException"/> when the index fails.
    /// </summary>
    public async Task<SubtitleAdded> AddAsync(Manifest manifest, long fileId, string language, string? addedBy, CancellationToken ct)
    {
        var id = manifest.Id;

        // Fetching a track somebody has already confirmed would re-measure and re-shift a file
        // a person has watched and approved. A later measurement disagreeing with them is the
        // measurement being wrong, so the existing file stands and no allowance is spent.
        var existingId = $"os{fileId}";
        if (pins.For(id).ContainsKey(existingId)
            && manifest.Subtitles.FirstOrDefault(t => t.Id == existingId) is { } already)
            return new SubtitleAdded { Track = already, AlreadyPinned = true };

        var allowed = await index.RequestDownloadAsync(fileId, ct);
        var bytes = await index.FetchAsync(allowed.Link!, ct);

        // Converted twice on purpose. The first pass is what gets measured, because the
        // measurement needs timings in a comparable form; the second bakes the offset in,
        // so what lands on disk already fits the film and no viewer has to correct it.
        var measured = Measure(manifest, SubRip.Convert(bytes).WebVtt);
        var converted = SubRip.Convert(bytes, measured?.ShiftSeconds ?? 0);

        var subtitle = new SidecarSubtitle
        {
            Id = existingId,
            Language = language,
            Label = Label(allowed.FileName, measured),
            Release = Path.GetFileNameWithoutExtension(allowed.FileName),
            AddedBy = addedBy,
            AddedAt = DateTimeOffset.UtcNow,
            AppliedShiftSeconds = measured?.ShiftSeconds,
            AlignedFraction = measured?.AlignedFraction
        };

        store.Save(id, subtitle, converted.WebVtt);

        // One person fetched it and the whole room gets it: the file lives beside the
        // manifest rather than in it, so the manifest's timestamp says nothing happened.
        changes.Announce(id);

        logger.LogInformation(
            "Added {Subtitle} to {Title}: {Cues} cues, {Repaired} characters repaired, "
            + "shift {Shift}, {Remaining} downloads left today.",
            subtitle.Id, id, converted.Cues, converted.Repaired,
            measured is null ? "not measured" : $"{measured.ShiftSeconds:+0.00;-0.00;0.00}s",
            allowed.Remaining);

        return new SubtitleAdded
        {
            Track = SubtitleStore.AsTrack(subtitle),
            Cues = converted.Cues,
            Repaired = converted.Repaired,
            LooksWrong = converted.LooksWrong,
            ShiftSeconds = measured?.ShiftSeconds,
            AlignedFraction = measured?.AlignedFraction,
            RemainingDownloads = allowed.Remaining
        };
    }

    /// <summary>
    /// Makes sure the title carries a text subtitle in <paramref name="language"/>: one it holds already
    /// stands, else the best candidate the index offers that nothing says is wrong for the film is
    /// fetched. False when the title has none and none could be fetched; a failing index is logged and
    /// answered the same way, since nothing waits on it.
    /// </summary>
    public async Task<bool> EnsureAsync(string titleId, string language, string addedBy, CancellationToken ct)
    {
        if (library.Get(titleId) is not { } manifest) return false;
        if (Carries(manifest, language)) return true;
        if (!index.IsConfigured) return false;

        try
        {
            var best = (await SearchAsync(manifest, language, ct)).FirstOrDefault(c => !c.HasMismatch);
            if (best is null) return false;
            await AddAsync(manifest, best.Candidate.FileId, language, addedBy, ct);
            return true;
        }
        catch (OpenSubtitlesException ex)
        {
            logger.LogWarning(ex, "No {Language} subtitle could be fetched onto {Title}.", language, titleId);
            return false;
        }
    }

    /// <summary>Whether a title carries a text subtitle in a language, from the film or fetched.</summary>
    public static bool Carries(Manifest manifest, string language) =>
        manifest.Subtitles.Any(s => s.Available && s.Source != SubtitleSource.Bitmap && Languages.Same(s.Language, language));

    /// <summary>
    /// Asks the narrowest question first. A hash search is exact and almost always empty; an IMDb
    /// search is exact about the film; a title search is neither and returns other films outright,
    /// so it is only reached when nothing identified this one.
    /// </summary>
    private async Task<IReadOnlyList<SubtitleCandidate>> FindAsync(
        Manifest manifest, SubtitleTarget target, string language, CancellationToken ct)
    {
        var found = new Dictionary<long, SubtitleCandidate>();

        void Collect(IEnumerable<OpenSubtitlesItem> items)
        {
            foreach (var item in items)
            {
                if (SubtitleCandidate.From(item) is { } candidate)
                    found.TryAdd(candidate.FileId, candidate);
            }
        }

        if (target.MovieHash is { Length: > 0 } hash)
            Collect(await index.SearchByHashAsync(hash, language, ct));

        // An episode is asked for by its show and its place: the show's own id names every episode
        // and special the show has.
        if (manifest.Episode is { } episode && target.ImdbId is { Length: > 0 } showId)
            Collect(await index.SearchByEpisodeAsync(showId, episode.Season, episode.Number, language, ct));
        else if (target.ImdbId is { Length: > 0 } imdbId)
            Collect(await index.SearchByImdbAsync(imdbId, language, ct));
        else
            Collect(await index.SearchByTitleAsync(manifest.Title, YearOf(manifest), language, ct));

        return found.Values.ToList();
    }

    /// <summary>
    /// Measures the candidate against a track already known to fit the film.
    ///
    /// The reference has to be one that came out of the file itself, because that is the only
    /// thing that fits it by construction. A previously fetched subtitle might be wrong, and
    /// measuring against a wrong one would confidently propagate its error.
    /// </summary>
    private SyncMeasurement? Measure(Manifest manifest, string webVtt)
    {
        var reference = manifest.Subtitles.FirstOrDefault(t =>
            t.Source == SubtitleSource.EmbeddedText
            && t.Kind == TrackKind.Feature
            && t.Available
            && t.Uri is not null);

        if (reference?.Uri is null) return null;

        if (library.DirectoryOf(manifest.Id) is not { } directory) return null;

        var path = Path.Combine(directory, reference.Uri);
        if (!File.Exists(path)) return null;

        return SubtitleSync.Against(
            CueTimings.Read(File.ReadAllText(path, Encoding.UTF8)),
            CueTimings.Read(webVtt));
    }

    private static SubtitleTarget TargetOf(Manifest manifest) => new()
    {
        Release = manifest.Source?.Release,
        FrameRate = manifest.Source?.FrameRate ?? 0,
        MovieHash = manifest.Source?.MovieHash,
        ImdbId = manifest.Film?.ImdbId
    };

    /// <summary>
    /// The year out of a title like <c>Heat (1995)</c>, which is how the library writes them. Only
    /// used when nothing identified the film, where narrowing on a year is better than not.
    /// </summary>
    private static int? YearOf(Manifest manifest)
    {
        var title = manifest.Title;
        var open = title.LastIndexOf('(');

        return open >= 0 && open + 5 <= title.Length
               && int.TryParse(title.AsSpan(open + 1, 4), out var year)
            ? year
            : null;
    }

    /// <summary>
    /// What the menu shows. It names the release rather than the language, because a room that has
    /// fetched three of these otherwise sees three rows all reading "English" and no way to choose.
    /// </summary>
    private static string Label(string? fileName, SyncMeasurement? measured)
    {
        var release = Path.GetFileNameWithoutExtension(fileName) ?? "Downloaded";

        // Trailing language and format markers are noise in a menu that is already grouped by them.
        foreach (var suffix in (string[])[".ENG", ".EN", ".English"])
        {
            if (release.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                release = release[..^suffix.Length];
        }

        return measured is { ShiftSeconds: not 0 }
            ? $"{release} ({measured.ShiftSeconds:+0.0;-0.0}s)"
            : release;
    }
}
