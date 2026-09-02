using System.Text;
using TheKrystalShip.MovieBot.Acquire.Subtitles;
using TheKrystalShip.MovieBot.Api.Library;
using TheKrystalShip.MovieBot.Core;
using TheKrystalShip.MovieBot.Core.Subtitles;

namespace TheKrystalShip.MovieBot.Api.Subtitles;

/// <summary>One thing that was compared, as the picker shows it.</summary>
public sealed record SubtitleCheckView(string Name, string Result, string Detail);

/// <summary>A candidate, with everything known about how well it fits and nothing that costs a download.</summary>
public sealed record SubtitleCandidateView
{
    public required long FileId { get; init; }
    public required string Release { get; init; }
    public required string Language { get; init; }
    public double? Fps { get; init; }
    public int DownloadCount { get; init; }
    public bool HearingImpaired { get; init; }
    public bool Trusted { get; init; }
    public required int Score { get; init; }
    public required IReadOnlyList<SubtitleCheckView> Checks { get; init; }
}

public sealed record SubtitleSearchView
{
    public required IReadOnlyList<SubtitleCandidateView> Candidates { get; init; }

    /// <summary>Downloads left today, as the index last stated it. Null before any were spent.</summary>
    public int? RemainingDownloads { get; init; }

    /// <summary>Why the list is empty, when it is. An empty menu otherwise reads as a broken search.</summary>
    public string? Explanation { get; init; }
}

public sealed record AddSubtitleRequest(long FileId, string? AddedBy);

/// <summary>
/// Confirming a track fits. The position is how far in the film had been watched, because drift
/// only shows up late: a track pinned two minutes in has not been cleared of it.
/// </summary>
public sealed record PinSubtitleRequest(string? PinnedBy, double? PositionSeconds);

public static class SubtitleEndpoints
{
    /// <summary>Enough candidates to choose from, few enough to read.</summary>
    private const int MostToOffer = 12;

    public static void MapSubtitles(this WebApplication app)
    {
        app.MapGet("/api/titles/{id}/subtitles/search", async (
            string id,
            string? language,
            TitleLibrary library,
            OpenSubtitlesClient index,
            CancellationToken ct) =>
        {
            if (library.Get(id) is not { } manifest) return Results.NotFound();
            if (!index.IsConfigured)
                return Results.Ok(new SubtitleSearchView
                {
                    Candidates = [],
                    Explanation = "No subtitle index is configured on this host."
                });

            var wanted = string.IsNullOrWhiteSpace(language) ? "en" : language;
            var target = TargetOf(manifest);

            var found = await FindAsync(index, manifest, target, wanted, ct);

            var ranked = found
                .Select(c => SubtitleChecks.For(c, target))
                .OrderByDescending(c => c.Score)
                .Take(MostToOffer)
                .Select(View)
                .ToList();

            return Results.Ok(new SubtitleSearchView
            {
                Candidates = ranked,
                RemainingDownloads = index.RemainingDownloads,
                Explanation = ranked.Count > 0
                    ? null
                    : $"The index holds no {wanted} subtitles for this film."
            });
        });

        app.MapPost("/api/titles/{id}/subtitles", async (
            string id,
            AddSubtitleRequest request,
            TitleLibrary library,
            SubtitleStore store,
            PinStore pins,
            OpenSubtitlesClient index,
            ILoggerFactory loggers,
            CancellationToken ct) =>
        {
            var log = loggers.CreateLogger("Subtitles");

            if (library.Get(id) is not { } manifest) return Results.NotFound();
            if (request.FileId <= 0) return Results.BadRequest(new { error = "No subtitle was named." });

            // Fetching a track somebody has already confirmed would re-measure and re-shift a file
            // a person has watched and approved. A later measurement disagreeing with them is the
            // measurement being wrong, so the existing file stands and no allowance is spent.
            var existingId = $"os{request.FileId}";
            if (pins.For(id).ContainsKey(existingId))
            {
                var already = manifest.Subtitles.FirstOrDefault(t => t.Id == existingId);
                if (already is not null) return Results.Ok(new { track = already, alreadyPinned = true });
            }

            try
            {
                var allowed = await index.RequestDownloadAsync(request.FileId, ct);
                var bytes = await index.FetchAsync(allowed.Link!, ct);

                // Converted twice on purpose. The first pass is what gets measured, because the
                // measurement needs timings in a comparable form; the second bakes the offset in,
                // so what lands on disk already fits the film and no viewer has to correct it.
                var measured = Measure(library, manifest, SubRip.Convert(bytes).WebVtt);
                var converted = SubRip.Convert(bytes, measured?.ShiftSeconds ?? 0);

                var subtitle = new SidecarSubtitle
                {
                    Id = $"os{request.FileId}",
                    Language = "en",
                    Label = Label(allowed.FileName, measured),
                    Release = Path.GetFileNameWithoutExtension(allowed.FileName),
                    AddedBy = request.AddedBy,
                    AddedAt = DateTimeOffset.UtcNow,
                    AppliedShiftSeconds = measured?.ShiftSeconds,
                    AlignedFraction = measured?.AlignedFraction
                };

                store.Save(id, subtitle, converted.WebVtt);

                log.LogInformation(
                    "Added {Subtitle} to {Title}: {Cues} cues, {Repaired} characters repaired, "
                    + "shift {Shift}, {Remaining} downloads left today.",
                    subtitle.Id, id, converted.Cues, converted.Repaired,
                    measured is null ? "not measured" : $"{measured.ShiftSeconds:+0.00;-0.00;0.00}s",
                    allowed.Remaining);

                return Results.Ok(new
                {
                    track = SubtitleStore.AsTrack(subtitle),
                    cues = converted.Cues,
                    repaired = converted.Repaired,
                    looksWrong = converted.LooksWrong,
                    shiftSeconds = measured?.ShiftSeconds,
                    alignedFraction = measured?.AlignedFraction,
                    remainingDownloads = allowed.Remaining
                });
            }
            catch (OpenSubtitlesQuotaException ex)
            {
                // Distinct from any other failure because it resolves by itself, at a stated time.
                return Results.Json(
                    new { error = ex.Message, resetsAt = ex.ResetsAt }, statusCode: 429);
            }
            catch (OpenSubtitlesException ex)
            {
                log.LogWarning(ex, "Could not add subtitle {FileId} to {Title}.", request.FileId, id);
                return Results.Json(new { error = ex.Message }, statusCode: 502);
            }
        });

        app.MapPost("/api/titles/{id}/subtitles/{trackId}/pin", (
            string id,
            string trackId,
            PinSubtitleRequest request,
            TitleLibrary library,
            PinStore pins) =>
        {
            if (library.Get(id) is not { } manifest) return Results.NotFound();
            if (!PinStore.IsSafeTrackId(trackId)) return Results.NotFound();

            // Only a track the film actually offers can be confirmed, so a pin can never be left
            // pointing at something that was never there.
            if (manifest.Subtitles.All(t => t.Id != trackId)) return Results.NotFound();

            var pin = new SubtitlePin
            {
                PinnedBy = string.IsNullOrWhiteSpace(request.PinnedBy) ? "someone" : request.PinnedBy,
                PinnedAt = DateTimeOffset.UtcNow,
                WatchedFraction = Watched(request.PositionSeconds, manifest.DurationSeconds)
            };

            pins.Pin(id, trackId, pin);
            return Results.Ok(pin);
        });

        app.MapDelete("/api/titles/{id}/subtitles/{trackId}/pin", (
            string id, string trackId, TitleLibrary library, PinStore pins) =>
        {
            if (library.Get(id) is null || !PinStore.IsSafeTrackId(trackId)) return Results.NotFound();

            pins.Unpin(id, trackId);
            return Results.NoContent();
        });
    }

    /// <summary>How much of the film had been watched, or null when the position is not usable.</summary>
    private static double? Watched(double? position, double duration) =>
        position is > 0 && duration > 0 ? Math.Clamp(position.Value / duration, 0, 1) : null;

    /// <summary>
    /// Asks the narrowest question first. A hash search is exact and almost always empty; an IMDb
    /// search is exact about the film; a title search is neither and returns other films outright,
    /// so it is only reached when nothing identified this one.
    /// </summary>
    private static async Task<IReadOnlyList<SubtitleCandidate>> FindAsync(
        OpenSubtitlesClient index, Manifest manifest, SubtitleTarget target,
        string language, CancellationToken ct)
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

        if (target.ImdbId is { Length: > 0 } imdbId)
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
    private static SyncMeasurement? Measure(TitleLibrary library, Manifest manifest, string webVtt)
    {
        var reference = manifest.Subtitles.FirstOrDefault(t =>
            t.Source == SubtitleSource.EmbeddedText
            && t.Kind == TrackKind.Feature
            && t.Available
            && t.Uri is not null);

        if (reference?.Uri is null) return null;

        var path = Path.Combine(library.MediaRoot, manifest.Id, reference.Uri);
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

    private static SubtitleCandidateView View(CheckedSubtitle judged) => new()
    {
        FileId = judged.Candidate.FileId,
        Release = judged.Candidate.Release,
        Language = judged.Candidate.Language,
        Fps = judged.Candidate.Fps > 0 ? judged.Candidate.Fps : null,
        DownloadCount = judged.Candidate.DownloadCount,
        HearingImpaired = judged.Candidate.HearingImpaired,
        Trusted = judged.Candidate.FromTrusted,
        Score = judged.Score,
        Checks = judged.Checks
            .Select(c => new SubtitleCheckView(c.Name, c.Result.ToString().ToLowerInvariant(), c.Detail))
            .ToList()
    };
}
