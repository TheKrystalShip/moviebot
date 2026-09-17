using System.Text;
using TheKrystalShip.MovieBot.Acquire.Subtitles;
using TheKrystalShip.MovieBot.Api.Library;
using TheKrystalShip.MovieBot.Core;
using TheKrystalShip.MovieBot.Core.Subtitles;

namespace TheKrystalShip.MovieBot.Api.Subtitles;

/// <summary>
/// Confirming a track fits. The position is how far in the film had been watched, because drift
/// only shows up late: a track pinned two minutes in has not been cleared of it.
/// </summary>
public sealed record PinSubtitleRequest(string? PinnedBy, double? PositionSeconds);

public static class SubtitleEndpoints
{
    /// <summary>
    /// Enough candidates to choose from, few enough that the menu does not have to be scrolled.
    /// The list is ranked, so a longer one only adds worse answers below the good ones.
    /// </summary>
    private const int MostToOffer = 6;

    public static void MapSubtitles(this WebApplication app)
    {
        app.MapGet("/api/titles/{id}/subtitles/search", async (
            string id,
            string? language,
            TitleLibrary library,
            OpenSubtitlesClient index,
            ILoggerFactory loggerFactory,
            CancellationToken ct) =>
        {
            var logger = loggerFactory.CreateLogger("MovieBot.Api.Subtitles");
            if (library.Get(id) is not { } manifest) return Results.NotFound();
            if (!index.IsConfigured)
                return Results.Ok(new SubtitleSearchView
                {
                    Candidates = [],
                    Explanation = "No subtitle index is configured on this host."
                });

            var wanted = string.IsNullOrWhiteSpace(language) ? "en" : language;
            var target = TargetOf(manifest);

            IReadOnlyList<SubtitleCandidate> found;
            try
            {
                found = await FindAsync(index, manifest, target, wanted, ct);
            }
            catch (OpenSubtitlesException ex)
            {
                // The index is somebody else's service and answers how it likes. Letting that
                // reach the host is a 500 with a stack trace where the person asking sees a
                // picker that spins and never fills, and nothing telling them why.
                logger.LogWarning(ex, "Subtitle search for {Title} failed at the index", id);
                return Results.Ok(new SubtitleSearchView
                {
                    Candidates = [],
                    Explanation = "The subtitle index could not be reached. Try again in a moment."
                });
            }

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
            Library.TitleChanges changes,
            ILoggerFactory loggers,
            CancellationToken ct) =>
        {
            var log = loggers.CreateLogger("Subtitles");

            if (library.Get(id) is not { } manifest) return Results.NotFound();
            if (request.FileId <= 0) return Results.BadRequest(new ErrorReply("No subtitle was named."));

            // Fetching a track somebody has already confirmed would re-measure and re-shift a file
            // a person has watched and approved. A later measurement disagreeing with them is the
            // measurement being wrong, so the existing file stands and no allowance is spent.
            var existingId = $"os{request.FileId}";
            if (pins.For(id).ContainsKey(existingId))
            {
                var already = manifest.Subtitles.FirstOrDefault(t => t.Id == existingId);
                if (already is not null) return Results.Ok(new SubtitleAdded { Track = already, AlreadyPinned = true });
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

                // One person fetched it and the whole room gets it: the file lives beside the
                // manifest rather than in it, so the manifest's timestamp says nothing happened.
                changes.Announce(id);

                log.LogInformation(
                    "Added {Subtitle} to {Title}: {Cues} cues, {Repaired} characters repaired, "
                    + "shift {Shift}, {Remaining} downloads left today.",
                    subtitle.Id, id, converted.Cues, converted.Repaired,
                    measured is null ? "not measured" : $"{measured.ShiftSeconds:+0.00;-0.00;0.00}s",
                    allowed.Remaining);

                return Results.Ok(new SubtitleAdded
                {
                    Track = SubtitleStore.AsTrack(subtitle),
                    Cues = converted.Cues,
                    Repaired = converted.Repaired,
                    LooksWrong = converted.LooksWrong,
                    ShiftSeconds = measured?.ShiftSeconds,
                    AlignedFraction = measured?.AlignedFraction,
                    RemainingDownloads = allowed.Remaining
                });
            }
            catch (OpenSubtitlesQuotaException ex)
            {
                // Distinct from any other failure because it resolves by itself, at a stated time.
                return Results.Json(
                    new QuotaReply(ex.Message, ex.ResetsAt), ManifestJsonContext.Default.QuotaReply, statusCode: 429);
            }
            catch (OpenSubtitlesException ex)
            {
                log.LogWarning(ex, "Could not add subtitle {FileId} to {Title}.", request.FileId, id);
                return Results.Json(new ErrorReply(ex.Message), ApiJsonContext.Default.ErrorReply, statusCode: 502);
            }
        });

        app.MapPost("/api/titles/{id}/subtitles/{trackId}/pin", (
            string id,
            string trackId,
            PinSubtitleRequest request,
            TitleLibrary library,
            PinStore pins,
            Library.TitleChanges changes) =>
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
            changes.Announce(id);
            return Results.Ok(pin);
        });

        app.MapDelete("/api/titles/{id}/subtitles/{trackId}/pin", (
            string id, string trackId, TitleLibrary library, PinStore pins, Library.TitleChanges changes) =>
        {
            if (library.Get(id) is null || !PinStore.IsSafeTrackId(trackId)) return Results.NotFound();

            pins.Unpin(id, trackId);
            changes.Announce(id);
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
