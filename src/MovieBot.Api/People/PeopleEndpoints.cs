using TheKrystalShip.MovieBot.Api.Auth;
using TheKrystalShip.MovieBot.Api.Library;
using TheKrystalShip.MovieBot.Api.Sessions;
using TheKrystalShip.MovieBot.Core;

namespace TheKrystalShip.MovieBot.Api.People;

/// <summary>
/// What the signed-in person keeps for themselves: how they watch, how far they have got in films,
/// what they have watched. Whoever the token names is who is asked about: there is no user id on
/// the route, so nobody reads or writes anybody else's.
/// </summary>
public static class PeopleEndpoints
{
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

        // Films this person started and did not finish, newest first. A film the library no longer
        // holds is left out: there is nothing to continue and no name to show for it.
        app.MapGet("/api/me/progress", (HttpContext context, WatchRecord record, TitleLibrary library, SessionStore sessions, TimeProvider clock) =>
        {
            if (Person(context) is not { } person) return NoPerson();

            var entries = new List<ProgressEntry>();
            foreach (var (titleId, entry) in EntriesOf(person.UserId, record, sessions, library, clock))
            {
                if (entry.Finished || library.Get(titleId) is not { } manifest) continue;
                entries.Add(new ProgressEntry(titleId, NameOf(manifest), entry.PositionSeconds, entry.DurationSeconds,
                    Math.Max(0, entry.DurationSeconds - entry.PositionSeconds), entry.LastWatchedAt,
                    manifest.Episode, manifest.Episode is null ? null : manifest.Film?.ImdbId));
            }

            return Results.Ok(entries);
        }).RequireAction(CinemaActions.ProfileRead);

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
