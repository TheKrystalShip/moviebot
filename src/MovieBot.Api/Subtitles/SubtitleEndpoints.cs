using TheKrystalShip.MovieBot.Acquire.Subtitles;
using TheKrystalShip.MovieBot.Api.Auth;
using TheKrystalShip.MovieBot.Api.Library;
using TheKrystalShip.MovieBot.Core;

namespace TheKrystalShip.MovieBot.Api.Subtitles;

/// <summary>
/// Confirming a track fits. The position is how far in the film had been watched, because drift
/// only shows up late: a track pinned two minutes in has not been cleared of it.
/// </summary>
public sealed record PinSubtitleRequest(double? PositionSeconds);

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
            SubtitleFetcher fetcher,
            ILoggerFactory loggerFactory,
            CancellationToken ct) =>
        {
            var logger = loggerFactory.CreateLogger("MovieBot.Api.Subtitles");
            if (library.Get(id) is not { } manifest) return Results.NotFound();
            if (!fetcher.IsConfigured)
                return Results.Ok(new SubtitleSearchView
                {
                    Candidates = [],
                    Explanation = "No subtitle index is configured on this host."
                });

            var wanted = string.IsNullOrWhiteSpace(language) ? "en" : language;

            List<CheckedSubtitle> found;
            try
            {
                found = await fetcher.SearchAsync(manifest, wanted, ct);
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
                .Take(MostToOffer)
                .Select(View)
                .ToList();

            return Results.Ok(new SubtitleSearchView
            {
                Candidates = ranked,
                RemainingDownloads = fetcher.RemainingDownloads,
                Explanation = ranked.Count > 0
                    ? null
                    : $"The index holds no {wanted} subtitles for this film."
            });
        }).RequireAction(CinemaActions.Watch);

        app.MapPost("/api/titles/{id}/subtitles", async (
            string id,
            AddSubtitleRequest request,
            HttpContext context,
            TitleLibrary library,
            SubtitleFetcher fetcher,
            ILoggerFactory loggers,
            CancellationToken ct) =>
        {
            var log = loggers.CreateLogger("Subtitles");

            if (library.Get(id) is not { } manifest) return Results.NotFound();
            if (request.FileId <= 0) return Results.BadRequest(new ErrorReply("No subtitle was named."));

            try
            {
                var language = string.IsNullOrWhiteSpace(request.Language) ? "en" : request.Language.Trim();
                return Results.Ok(await fetcher.AddAsync(manifest, request.FileId, language, CinemaPerson.Of(context)?.DisplayName, ct));
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
        }).RequireAction(CinemaActions.Watch);

        app.MapPost("/api/titles/{id}/subtitles/{trackId}/pin", (
            string id,
            string trackId,
            PinSubtitleRequest request,
            HttpContext context,
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
                PinnedBy = CinemaPerson.Of(context)?.DisplayName ?? "someone",
                PinnedAt = DateTimeOffset.UtcNow,
                WatchedFraction = Watched(request.PositionSeconds, manifest.DurationSeconds)
            };

            pins.Pin(id, trackId, pin);
            changes.Announce(id);
            return Results.Ok(pin);
        }).RequireAction(CinemaActions.Watch);

        app.MapDelete("/api/titles/{id}/subtitles/{trackId}/pin", (
            string id, string trackId, TitleLibrary library, PinStore pins, Library.TitleChanges changes) =>
        {
            if (library.Get(id) is null || !PinStore.IsSafeTrackId(trackId)) return Results.NotFound();

            pins.Unpin(id, trackId);
            changes.Announce(id);
            return Results.NoContent();
        }).RequireAction(CinemaActions.Watch);
    }

    /// <summary>How much of the film had been watched, or null when the position is not usable.</summary>
    private static double? Watched(double? position, double duration) =>
        position is > 0 && duration > 0 ? Math.Clamp(position.Value / duration, 0, 1) : null;

    private static SubtitleCandidateView View(CheckedSubtitle judged) => new()
    {
        FileId = judged.Candidate.FileId,
        Release = judged.Candidate.Release,
        Language = judged.Candidate.Language,
        Fps = judged.Candidate.Fps > 0 ? judged.Candidate.Fps : null,
        DownloadCount = judged.Candidate.DownloadCount,
        Rating = judged.Candidate.Rating,
        HearingImpaired = judged.Candidate.HearingImpaired,
        Trusted = judged.Candidate.FromTrusted,
        Score = judged.Score,
        Checks = judged.Checks
            .Select(c => new SubtitleCheckView(c.Name, c.Result.ToString().ToLowerInvariant(), c.Detail))
            .ToList()
    };
}
