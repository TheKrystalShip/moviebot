using TheKrystalShip.MovieBot.Api.Auth;
using TheKrystalShip.MovieBot.Api.Films;
using TheKrystalShip.MovieBot.Api.Library;
using TheKrystalShip.MovieBot.Api.Shows;
using TheKrystalShip.MovieBot.Api.Subtitles;
using TheKrystalShip.MovieBot.Core;

namespace TheKrystalShip.MovieBot.Api.Offline;

/// <summary>
/// Save offline: what a title can be saved as, the saves a person has made, and the one file per
/// track a device fetches. Saving takes <see cref="CinemaActions.Offline"/>; the records are the
/// person's own and read and change on the self actions, so a device can still learn its saves are
/// gone from an account that no longer may save.
/// </summary>
public static class OfflineEndpoints
{
    /// <summary>How long a device refused for being past the limits waits before asking again.</summary>
    private const int RetryAfterSeconds = 10;

    public static void MapOffline(this WebApplication app)
    {
        app.MapGet("/api/titles/{id}/offline", (string id, TitleLibrary library, TrackIndexes indexes, SubtitleStore subtitles) =>
        {
            if (Ready(id, library) is { } refused) return refused;
            var manifest = library.Get(id)!;
            return library.DirectoryOf(id) is { } directory
                ? Results.Ok(Offer(id, manifest, directory, indexes, subtitles))
                : Results.NotFound();
        }).RequireAction(CinemaActions.Offline);

        app.MapPost("/api/me/offline", (OfflineSaveRequest body, HttpContext context, TitleLibrary library,
            TrackIndexes indexes, SubtitleStore subtitles, OfflineSaves saves, FilmStore films, ShowStore shows,
            TimeProvider clock) =>
        {
            if (CinemaPerson.Of(context) is not { } person) return NoPerson();
            if (string.IsNullOrEmpty(body.TitleId)) return Bad("Name the title to save.");
            if (Ready(body.TitleId, library) is { } refused) return refused;

            var id = body.TitleId;
            var manifest = library.Get(id)!;
            if (library.DirectoryOf(id) is not { } directory) return Results.NotFound();

            var device = body.Device?.Trim() ?? "";
            if (device.Length is 0 or > 100) return Bad("Name the device, in 1 to 100 characters.");

            var offer = Offer(id, manifest, directory, indexes, subtitles);
            if (offer.Rungs.FirstOrDefault(r => r.Rendition == body.Rung) is not { } rung)
                return Bad("The title has no such rung.");

            var audioIds = (body.Audio ?? []).Distinct(StringComparer.Ordinal).ToList();
            if (audioIds.Count == 0) return Bad("Pick at least one audio track.");
            var audio = audioIds.Select(a => offer.Audio.FirstOrDefault(o => o.Rendition == a)).ToList();
            if (audio.Any(a => a is null)) return Bad("The title has no such audio track.");

            var subtitleIds = (body.Subtitles ?? []).Distinct(StringComparer.Ordinal).ToList();
            var picked = subtitleIds.Select(s => offer.Subtitles.FirstOrDefault(o => o.Id == s)).ToList();
            if (picked.Any(s => s is null)) return Bad("The title has no such subtitle.");

            var tracks = new List<OfflineTrack>();
            foreach (var (kind, rendition, uri) in audio.Select(a => ("audio", a!.Rendition, a.Uri)).Prepend(("video", rung.Rendition, rung.Uri)))
            {
                if (indexes.Of(directory, uri) is not { } track) return Results.NotFound();
                tracks.Add(new OfflineTrack(kind, rendition, uri, $"/media/{id}/{rendition}/track.mp4", track.Length,
                    track.ETag, track.InitBytes, track.Seconds, track.SegmentBytes));
            }

            var subtitleViews = picked.Select(s => s! with { Url = $"/media/{id}/{SubtitleUri(manifest, s.Id)}" }).ToList();
            var now = clock.GetUtcNow();
            var stored = saves.Save(person.UserId, id, rung.Rendition, audioIds, subtitleIds, device, now);

            var details = manifest.Kind == TitleKind.Show ? shows.Facts(manifest.Film?.ImdbId) : films.Facts(manifest.Film?.ImdbId);
            return Results.Ok(new OfflinePackage(
                View(stored, saves, now),
                Trimmed(manifest, rung.Rendition, audioIds, subtitleIds),
                tracks.Sum(t => t.Bytes) + subtitleViews.Sum(s => s.Bytes),
                tracks,
                subtitleViews,
                details,
                EpisodeOf(manifest, shows),
                ArtworkOf(id, manifest, details, shows)));
        }).RequireAction(CinemaActions.Offline);

        app.MapGet("/api/me/offline", (HttpContext context, OfflineSaves saves, TimeProvider clock) =>
        {
            if (CinemaPerson.Of(context) is not { } person) return NoPerson();
            var now = clock.GetUtcNow();
            return Results.Ok(saves.For(person.UserId).Select(s => View(s, saves, now)).ToList());
        }).RequireAction(CinemaActions.ProfileRead);

        app.MapPost("/api/me/offline/{saveId}/complete", (string saveId, HttpContext context, OfflineSaves saves, TimeProvider clock) =>
        {
            if (CinemaPerson.Of(context) is not { } person) return NoPerson();
            var now = clock.GetUtcNow();
            return saves.Complete(person.UserId, saveId, now) is { } done
                ? Results.Ok(View(done, saves, now))
                : Results.NotFound();
        }).RequireAction(CinemaActions.ProfileWrite);

        // Forgetting a save that is not there is the same end state, so it is not an error: a device
        // coming back online replays the removals it made while away.
        app.MapDelete("/api/me/offline/{saveId}", (string saveId, HttpContext context, OfflineSaves saves) =>
        {
            if (CinemaPerson.Of(context) is not { } person) return NoPerson();
            saves.Forget(person.UserId, saveId);
            return Results.NoContent();
        }).RequireAction(CinemaActions.ProfileWrite);

        // What the hand-off keeps in place: the titles a save still being made holds. It is asked by
        // the one process that moves and deletes films, beside what the rooms hold.
        app.MapGet("/api/library/saving", (OfflineSaves saves, TimeProvider clock) =>
                Results.Ok(saves.Holding(clock.GetUtcNow()).Order(StringComparer.Ordinal).ToArray()))
            .RequireService();

        // A whole track, for a person who has saved the title. The ticket does not open it: the
        // limits are per person, and a ticket names nobody.
        app.MapMethods("/media/{id}/{rendition}/track.mp4", ["GET", "HEAD"], (string id, string rendition,
            HttpContext context, TitleLibrary library, TrackIndexes indexes, OfflineSaves saves, Transfers transfers,
            ILoggerFactory loggers) =>
        {
            if (CinemaPerson.Of(context) is not { } person) return NoPerson();
            if (Ready(id, library) is { } refused) return refused;
            if (!saves.Has(person.UserId, id))
                return Conflict("Save the title offline before fetching its tracks.");

            var manifest = library.Get(id)!;
            var uri = manifest.Video.Renditions.Select(r => r.Uri)
                .Concat(manifest.Audio.Select(a => a.Uri))
                .FirstOrDefault(u => u.StartsWith(rendition + "/", StringComparison.Ordinal));
            if (uri is null || library.DirectoryOf(id) is not { } directory || indexes.Of(directory, uri) is not { } track)
                return Results.NotFound();

            Transfers.Lease? lease = null;
            if (HttpMethods.IsGet(context.Request.Method) && (lease = transfers.TryBegin(person.UserId)) is null)
            {
                context.Response.Headers.RetryAfter = RetryAfterSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture);
                return Results.Json(new ErrorReply("Too many saves are being fetched at once; try again shortly."),
                    ApiJsonContext.Default.ErrorReply, statusCode: StatusCodes.Status429TooManyRequests);
            }

            return new TrackResult(track, directory, () => library.DirectoryOf(id), transfers, lease,
                loggers.CreateLogger(typeof(TrackResult).FullName!));
        }).RequireAction(CinemaActions.Offline);
    }

    /// <summary>Everything a title can be saved as, each track measured on disk.</summary>
    public static OfflineOffer Offer(string id, Manifest manifest, string directory, TrackIndexes indexes, SubtitleStore subtitles)
    {
        var rungs = new List<OfflineRung>();
        foreach (var r in manifest.Video.Renditions.OrderByDescending(r => r.Height))
        {
            if (indexes.Of(directory, r.Uri) is not { } track) continue;
            var (width, height) = r.SizeIn(manifest.Video);
            rungs.Add(new OfflineRung(track.Rendition, r.Uri, r.Name, width, height, track.Length));
        }

        var audio = new List<OfflineAudio>();
        foreach (var a in manifest.Audio)
        {
            if (indexes.Of(directory, a.Uri) is not { } track) continue;
            audio.Add(new OfflineAudio(track.Rendition, a.Uri, a.Kind, a.Language, a.Label, a.Channels, a.Default, track.Length));
        }

        var text = new List<OfflineSubtitle>();
        foreach (var s in manifest.Subtitles.Where(s => s.Available && s.Uri is not null && s.Source != SubtitleSource.Bitmap))
        {
            var path = s.Uri!.StartsWith(SubtitleStore.UriPrefix, StringComparison.Ordinal)
                ? subtitles.Resolve(id, s.Uri[SubtitleStore.UriPrefix.Length..])
                : Contained(directory, s.Uri);
            if (path is null || !File.Exists(path)) continue;
            text.Add(new OfflineSubtitle(s.Id, s.Kind, s.Language, s.Label, s.HearingImpaired, s.Forced, new FileInfo(path).Length));
        }

        return new OfflineOffer(id, rungs, audio, text);
    }

    /// <summary>
    /// The title's manifest holding only what was saved, which is the film as the device has it. A copy:
    /// the library's own is shared by every request.
    /// </summary>
    private static Manifest Trimmed(Manifest manifest, string rung, List<string> audio, List<string> subtitles)
    {
        var copy = ManifestJson.Deserialize(ManifestJson.Serialize(manifest))!;
        copy.Video = new VideoInfo
        {
            Width = copy.Video.Width,
            Height = copy.Video.Height,
            SourceCodec = copy.Video.SourceCodec,
            SourceHdr = copy.Video.SourceHdr,
            Renditions = [.. copy.Video.Renditions.Where(r => DirectoryOf(r.Uri) == rung)],
        };
        copy.Audio = [.. audio.Select(a => copy.Audio.First(t => DirectoryOf(t.Uri) == a))];
        copy.Subtitles = [.. manifest.Subtitles.Where(s => subtitles.Contains(s.Id))];
        return copy;
    }

    private static OfflineSave View(StoredSave save, OfflineSaves saves, DateTimeOffset now) =>
        new(save.Id, save.TitleId, save.Rung, save.Audio, save.Subtitles, save.Device, save.CreatedAt, save.CompletedAt,
            saves.HoldsUntil(save, now));

    private static OfflineEpisode? EpisodeOf(Manifest manifest, ShowStore shows)
    {
        if (manifest.Episode is not { } place) return null;
        var described = manifest.Film?.ImdbId is { } imdb
            ? shows.Held(imdb)?.Seasons.FirstOrDefault(s => s.Number == place.Season)?.Episodes.FirstOrDefault(e => e.Number == place.Number)
            : null;
        return new OfflineEpisode(place.Season, place.Number, described?.Name, described?.Overview);
    }

    private static OfflineArtwork ArtworkOf(string id, Manifest manifest, FilmFacts? details, ShowStore shows)
    {
        string? still = null;
        if (manifest.Episode is { } place && manifest.Film?.ImdbId is { } imdb
            && shows.HasImage(imdb, ShowStore.Still(place.Season, place.Number)))
            still = ShowViews.ImageUrl(imdb, ShowStore.Still(place.Season, place.Number));

        return new OfflineArtwork(
            details?.PosterUrl ?? (manifest.Poster is { } poster ? $"/media/{id}/{poster}" : null),
            details?.BackdropUrl,
            still,
            manifest.Thumbnails is { } sheet ? $"/media/{id}/{sheet.Uri}" : null);
    }

    /// <summary>Null when the title may be saved; otherwise the refusal.</summary>
    private static IResult? Ready(string id, TitleLibrary library) =>
        library.Get(id) switch
        {
            null => Results.NotFound(),
            { Status: not TitleStatus.Ready } => Conflict("Only a title that is ready can be saved offline."),
            _ => null,
        };

    private static string SubtitleUri(Manifest manifest, string id) => manifest.Subtitles.First(s => s.Id == id).Uri!;

    private static string DirectoryOf(string uri)
    {
        var slash = uri.IndexOf('/');
        return slash < 0 ? uri : uri[..slash];
    }

    private static string? Contained(string directory, string uri)
    {
        var path = Path.GetFullPath(Path.Combine(directory, uri));
        return path.StartsWith(directory + Path.DirectorySeparatorChar, StringComparison.Ordinal) ? path : null;
    }

    private static IResult Bad(string why) =>
        Results.Json(new ErrorReply(why), ApiJsonContext.Default.ErrorReply, statusCode: StatusCodes.Status400BadRequest);

    private static IResult Conflict(string why) =>
        Results.Json(new ErrorReply(why), ApiJsonContext.Default.ErrorReply, statusCode: StatusCodes.Status409Conflict);

    private static IResult NoPerson() =>
        Results.Json(new ErrorReply("This belongs to a person; sign in as one."),
            ApiJsonContext.Default.ErrorReply, statusCode: StatusCodes.Status403Forbidden);
}
