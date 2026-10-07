using TheKrystalShip.MovieBot.Api.Auth;
using TheKrystalShip.MovieBot.Api.Library;
using TheKrystalShip.MovieBot.Api.Sessions;
using TheKrystalShip.MovieBot.Api.Shows;
using TheKrystalShip.MovieBot.Core;

namespace TheKrystalShip.MovieBot.Api.People;

/// <summary>
/// What the signed-in person keeps for themselves: how they watch, how far they have got in films,
/// what they have watched. Whoever the token names is who is asked about: there is no user id on
/// the route, so nobody reads or writes anybody else's.
/// </summary>
public static class PeopleEndpoints
{
    /// <summary>The most progress entries one request carries: weeks of watching on one device.</summary>
    private const int MaxProgressBatch = 500;

    public static void MapPeople(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/me/preferences", (HttpContext context, PreferenceStore store) =>
            Person(context) is { } person
                ? Results.Ok(store.For(person.UserId))
                : NoPerson())
            .RequireAction(CinemaActions.ProfileRead);

        app.MapPut("/api/me/preferences", (ViewerPreferences body, HttpContext context, PreferenceStore store) =>
            Person(context) is { } person
                ? Results.Ok(store.Set(person.UserId, body))
                : NoPerson())
            .RequireAction(CinemaActions.ProfileWrite);

        // What this person can carry on with, newest first: each film they started and did not
        // finish, and for each show one episode, the latest they watched while they have not finished
        // it, else the one after it. A title the library no longer holds is left out: there is
        // nothing to continue and no name to show for it.
        app.MapGet("/api/me/progress", (HttpContext context, WatchRecord record, TitleLibrary library, ShowStore shows,
            SessionStore sessions, TimeProvider clock) =>
        {
            if (Person(context) is not { } person) return NoPerson();
            return Results.Ok(ContinueWatching(EntriesOf(person.UserId, record, sessions, library, clock), library, shows,
                person.Holds(CinemaActions.Download), DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime)));
        }).RequireAction(CinemaActions.ProfileRead);

        // Where a device that watched with no connection got to. Each entry is recorded the way a
        // room's stretch is, and only where it is newer than what is held: the person may have
        // watched further elsewhere since.
        app.MapPost("/api/me/progress", (List<Offline.OfflineProgress> body, HttpContext context, WatchRecord record,
            TitleLibrary library, PersonDirectory people, TimeProvider clock) =>
        {
            if (Person(context) is not { } person) return NoPerson();
            if (body.Count > MaxProgressBatch)
                return Results.Json(new ErrorReply($"Send at most {MaxProgressBatch} at a time."),
                    ApiJsonContext.Default.ErrorReply, statusCode: StatusCodes.Status400BadRequest);
            if (body.Any(p => string.IsNullOrEmpty(p.TitleId) || !double.IsFinite(p.PositionSeconds) || p.PositionSeconds < 0))
                return Results.Json(new ErrorReply("Each entry names a title and a position that is not negative."),
                    ApiJsonContext.Default.ErrorReply, statusCode: StatusCodes.Status400BadRequest);

            var now = clock.GetUtcNow();
            people.Remember(person.UserId, person.DisplayName);
            foreach (var entry in body)
            {
                if (library.Get(entry.TitleId!) is not { } manifest) continue;
                // A device's clock can run ahead; a play cannot have happened after now.
                var at = entry.WatchedAt > now ? now : entry.WatchedAt;
                record.RecordIfNewer(person.UserId, entry.TitleId!, entry.PositionSeconds, manifest.DurationSeconds, at);
            }

            return Results.NoContent();
        }).RequireAction(CinemaActions.ProfileWrite);

        app.MapGet("/api/me/history", (HttpContext context, WatchRecord record, TitleLibrary library, SessionStore sessions, TimeProvider clock) =>
        {
            if (Person(context) is not { } person) return NoPerson();

            var entries = new List<HistoryEntry>();
            foreach (var (titleId, entry) in EntriesOf(person.UserId, record, sessions, library, clock))
            {
                if (library.Get(titleId) is not { } manifest) continue;
                entries.Add(new HistoryEntry(titleId, NameOf(manifest), entry.PositionSeconds, entry.DurationSeconds,
                    entry.Finished, entry.LastWatchedAt,
                    manifest.Episode, manifest.Episode is null ? null : manifest.Film?.ImdbId));
            }

            return Results.Ok(entries);
        }).RequireAction(CinemaActions.ProfileRead);

        // Forgetting a film that was never watched is the same end state, so it is not an error.
        app.MapDelete("/api/me/history/{titleId}", (string titleId, HttpContext context, WatchRecord record) =>
        {
            if (Person(context) is not { } person) return NoPerson();

            record.Forget(person.UserId, titleId);
            return Results.NoContent();
        }).RequireAction(CinemaActions.ProfileWrite);
    }

    /// <summary>
    /// What the listing says each film carries for the asking person; the service key has none. A
    /// film a room is playing right now was last played now, and by whoever is watching it.
    /// </summary>
    public static TitleSummary WithPlays(
        this TitleSummary title, CinemaPerson? person, WatchRecord record, PersonDirectory people,
        IReadOnlyList<ViewingRoom> viewing, DateTimeOffset now)
    {
        var live = person is null ? null : Live(person.UserId, viewing, title.Id, title.DurationSeconds, now);
        var entry = live ?? (person is null ? null : record.EntryOf(person.UserId, title.Id));
        var playing = viewing.FirstOrDefault(r => r.Playing && r.TitleId == title.Id);
        var played = record.LastPlayed(title.Id);

        return title with
        {
            Progress = entry is null ? null : new TitleProgress(entry.PositionSeconds, entry.Finished, entry.LastWatchedAt),
            LastPlayed = playing is not null
                ? new TitleLastPlayed(now, playing.Viewers[0].UserId, playing.Viewers[0].DisplayName)
                : played is null ? null : new TitleLastPlayed(played.LastPlayedAt, played.LastPlayedBy, people.NameOf(played.LastPlayedBy)),
        };
    }

    /// <summary>
    /// One person's films, newest first: what is written down, with whatever a room is playing to
    /// them this instant in place of it, because a room's position is newer than any record of it.
    /// </summary>
    private static IEnumerable<(string TitleId, WatchEntry Entry)> EntriesOf(
        string userId, WatchRecord record, SessionStore sessions, TitleLibrary library, TimeProvider clock)
    {
        var now = clock.GetUtcNow();
        var viewing = sessions.Viewing();
        var entries = record.Entries(userId).ToDictionary(e => e.TitleId, e => e.Entry);

        foreach (var room in viewing.Where(r => r.Playing && r.Viewers.Any(v => v.UserId == userId)))
        {
            if (library.Get(room.TitleId) is not { } manifest) continue;
            if (Live(userId, viewing, room.TitleId, manifest.DurationSeconds, now) is { } live)
                entries[room.TitleId] = live;
        }

        return entries.OrderByDescending(e => e.Value.LastWatchedAt).Select(e => (e.Key, e.Value));
    }

    /// <summary>
    /// What a person can carry on with, from their entries newest first. A film is listed while it is
    /// unfinished. A show is listed once, by the episode they watched most recently, wherever in the
    /// show that is: that episode while it is unfinished, and once it is finished the episode after
    /// it, from where they got to in it if they started it before, or from the beginning. The episode
    /// after is the next of the season, else the first of the next season, as the film database lists
    /// the show, or as the library holds it for a show the database has not described. When the
    /// library does not hold it, or holds a copy whose preparation failed, it is listed from the
    /// show's description with no title id, for a person who may fetch it (<paramref name="mayFetch"/>)
    /// and once it has aired: choosing it is Watch now. A show whose last episode is finished is not
    /// listed. An episode after takes the time its predecessor was finished, so the show keeps its
    /// place in the list.
    /// </summary>
    public static List<ProgressEntry> ContinueWatching(
        IEnumerable<(string TitleId, WatchEntry Entry)> newestFirst, TitleLibrary library, ShowStore shows,
        bool mayFetch, DateOnly today)
    {
        var entries = newestFirst.ToList();
        var byTitle = entries.ToDictionary(e => e.TitleId, e => e.Entry, StringComparer.Ordinal);
        var seenShows = new HashSet<string>(StringComparer.Ordinal);
        var listed = new List<ProgressEntry>();

        foreach (var (titleId, entry) in entries)
        {
            if (library.Get(titleId) is not { } manifest) continue;
            var show = manifest.Episode is null ? null : manifest.Film?.ImdbId;

            if (show is null)
            {
                if (!entry.Finished) listed.Add(Progress(titleId, manifest, entry.PositionSeconds, entry.DurationSeconds, entry.LastWatchedAt, shows));
                continue;
            }

            if (!seenShows.Add(show)) continue;
            if (!entry.Finished)
            {
                listed.Add(Progress(titleId, manifest, entry.PositionSeconds, entry.DurationSeconds, entry.LastWatchedAt, shows));
                continue;
            }

            var after = shows.EpisodeAfter(show, manifest.Episode!);
            if (HeldAfter(library, show, manifest.Episode!, after) is { } next)
            {
                var started = byTitle.GetValueOrDefault(next.Id) is { Finished: false } begun ? begun.PositionSeconds : 0;
                listed.Add(Progress(next.Id, next.Manifest, started, next.Manifest.DurationSeconds, entry.LastWatchedAt, shows));
            }
            else if (after is not null && mayFetch && Described(shows, show, after) is { } described
                     && FollowViews.AirDate(described.AirDate) is { } aired && FollowViews.IsDue(aired, today))
            {
                var duration = (described.RuntimeMinutes ?? 0) * 60.0;
                listed.Add(new ProgressEntry(null, manifest.Film?.Name ?? manifest.Title, 0, duration, duration,
                    entry.LastWatchedAt, after, show, described.Name, StillOf(shows, show, after)));
            }
        }

        return listed;
    }

    /// <summary>
    /// The library's title for the episode after one, or null when it holds none to play: the one the
    /// film database names (<paramref name="after"/>), or for a show it has not described the next the
    /// library holds.
    /// </summary>
    private static (string Id, Manifest Manifest)? HeldAfter(TitleLibrary library, string show, EpisodeIdentity episode, EpisodeIdentity? after)
    {
        var held = library.EpisodesOf(show).Where(e => e.Manifest.Status != TitleStatus.Failed).ToList();
        foreach (var e in held)
        {
            var at = e.Manifest.Episode!;
            if (after is not null ? at.Season == after.Season && at.Number == after.Number
                : at.Season > episode.Season || (at.Season == episode.Season && at.Number > episode.Number)) return e;
        }
        return null;
    }

    /// <summary>One episode as the show's description lists it, or null.</summary>
    private static Acquire.Tmdb.EpisodeDetails? Described(ShowStore shows, string show, EpisodeIdentity episode) =>
        shows.Held(show)?.Seasons.FirstOrDefault(s => s.Number == episode.Season)?
            .Episodes.FirstOrDefault(e => e.Number == episode.Number);

    /// <summary>The episode's still as this server serves it, or null when none is kept.</summary>
    private static string? StillOf(ShowStore shows, string show, EpisodeIdentity episode) =>
        shows.HasImage(show, ShowStore.Still(episode.Season, episode.Number))
            ? ShowViews.ImageUrl(show, ShowStore.Still(episode.Season, episode.Number))
            : null;

    private static ProgressEntry Progress(string titleId, Manifest manifest, double position, double duration, DateTimeOffset at,
        ShowStore shows)
    {
        var show = manifest.Episode is null ? null : manifest.Film?.ImdbId;
        return new(titleId, NameOf(manifest), position, duration, Math.Max(0, duration - position), at,
            manifest.Episode, show,
            show is null ? null : shows.EpisodeName(show, manifest.Episode),
            show is null ? null : StillOf(shows, show, manifest.Episode!));
    }

    /// <summary>Where a room playing the film to this person is now, or null when none is.</summary>
    private static WatchEntry? Live(string userId, IReadOnlyList<ViewingRoom> viewing, string titleId, double duration, DateTimeOffset now)
    {
        var room = viewing.FirstOrDefault(r => r.Playing && r.TitleId == titleId && r.Viewers.Any(v => v.UserId == userId));
        if (room is null || duration <= 0) return null;
        var position = Math.Clamp(room.PositionSeconds, 0, duration);
        return new WatchEntry
        {
            PositionSeconds = position,
            DurationSeconds = duration,
            LastWatchedAt = now,
            Finished = position >= duration * WatchRecord.FinishedFraction,
        };
    }

    /// <summary>The person the request's token names, or null for a caller that is nobody.</summary>
    public static CinemaPerson? Person(HttpContext context) => CinemaPerson.Of(context);

    /// <summary>A film by its name and year; an episode by its show's name, the episode said apart.</summary>
    private static string NameOf(Manifest manifest) =>
        manifest.Episode is not null
            ? manifest.Film?.Name ?? manifest.Title
            : manifest.Film?.Display ?? manifest.Title;

    /// <summary>The service key names nobody, so there is nobody's preferences, progress or history to answer with.</summary>
    private static IResult NoPerson() =>
        Results.Json(new ErrorReply("This belongs to a person; sign in as one."),
            ApiJsonContext.Default.ErrorReply, statusCode: StatusCodes.Status403Forbidden);
}
