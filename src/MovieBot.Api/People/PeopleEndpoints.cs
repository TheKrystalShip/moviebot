using TheKrystalShip.MovieBot.Api.Catalogue;
using TheKrystalShip.MovieBot.Api.Auth;
using TheKrystalShip.MovieBot.Api.Library;
using TheKrystalShip.MovieBot.Api.Sessions;
using TheKrystalShip.MovieBot.Api.Shows;
using TheKrystalShip.MovieBot.Catalogue;
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

        app.MapPut("/api/me/preferences", async (ViewerPreferences body, HttpContext context, PreferenceStore store, ViewerPush push) =>
        {
            if (Person(context) is not { } person) return NoPerson();
            if (body.Refusal() is { } refusal) return Refuse(refusal);

            var stored = store.Set(person.UserId, body);
            await push.PreferencesChangedAsync(person.UserId, stored);
            return Results.Ok(stored);
        })
            .RequireAction(CinemaActions.ProfileWrite);

        // The subtitle and audio track this person watches a film with, the same on every device. Asked
        // for by the token and the film, so nobody reads another's; the manifest decides what the film has.
        app.MapGet("/api/me/tracks/{titleId}", (string titleId, HttpContext context, TrackStore store, TitleLibrary library) =>
            Person(context) is not { } person ? NoPerson()
            : library.Get(titleId) is null ? Results.Json(new ErrorReply("No such film."), ApiJsonContext.Default.ErrorReply, statusCode: StatusCodes.Status404NotFound)
            : Results.Ok(store.For(person.UserId, titleId)))
            .RequireAction(CinemaActions.ProfileRead);

        app.MapPut("/api/me/tracks/{titleId}", async (string titleId, TrackChoice body, HttpContext context, TrackStore store,
            TitleLibrary library, ViewerPush push) =>
        {
            if (Person(context) is not { } person) return NoPerson();
            if (library.Get(titleId) is not { } manifest)
                return Results.Json(new ErrorReply("No such film."), ApiJsonContext.Default.ErrorReply, statusCode: StatusCodes.Status404NotFound);
            if ((body.Malformed() ?? body.RefusedBy(manifest)) is { } refusal) return Refuse(refusal);

            var tracks = store.Set(person.UserId, titleId, body);
            await push.TracksChangedAsync(person.UserId, tracks);
            return Results.Ok(tracks);
        })
            .RequireAction(CinemaActions.ProfileWrite);

        app.MapGet("/api/me/sorts", (HttpContext context, SortStore store) =>
            Person(context) is { } person
                ? Results.Json(store.For(person.UserId), ApiJsonContext.Default.DictionaryStringSortOrder)
                : NoPerson())
            .RequireAction(CinemaActions.ProfileRead);

        app.MapPut("/api/me/sorts/{surface}", (string surface, SortOrder body, HttpContext context, SortStore store) =>
        {
            if (Person(context) is not { } person) return NoPerson();
            if (!SortStore.IsName(surface) || !body.Valid())
                return Refuse($"A page and its key are lowercase names of at most {SortStore.NameLength} characters, and a direction is asc or desc.");
            var sorts = store.Set(person.UserId, surface, body);
            return sorts is not null
                ? Results.Json(sorts, ApiJsonContext.Default.DictionaryStringSortOrder)
                : Refuse($"An order is kept for at most {SortStore.MaxSurfaces} pages.");
        })
            .RequireAction(CinemaActions.ProfileWrite);

        // What this person can carry on with, newest first: each film they started and did not
        // finish, and for each show one episode, the latest they watched while they have not finished
        // it, else the one after it. A title the library no longer holds is left out: there is
        // nothing to continue and no name to show for it; so is what the person took off the list.
        app.MapGet("/api/me/progress", (HttpContext context, WatchRecord record, TitleLibrary library, CatalogueTitles shows,
            SessionStore sessions, TimeProvider clock) =>
        {
            if (Person(context) is not { } person) return NoPerson();
            return Results.Ok(ContinueWatching(EntriesOf(person.UserId, record, sessions, library, clock), record.Marks(person.UserId),
                record.HiddenFromContinue(person.UserId),
                library, shows, person.Holds(CinemaActions.Download), DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime)));
        }).RequireAction(CinemaActions.ProfileRead);

        // Takes a film or a show off the person's Continue watching, or puts it back. It stays off until
        // they watch it again; what they watched is kept, so History is unchanged.
        app.MapPost("/api/me/progress/hidden", (ContinueHiding body, HttpContext context, WatchRecord record, TitleLibrary library,
            TimeProvider clock) =>
        {
            if (Person(context) is not { } person) return NoPerson();
            if ((body.TitleId is null) == (body.ShowImdbId is null)) return Refuse("Name a film by its title or a show by its IMDb id, one of the two.");

            if (body.ShowImdbId is { } imdbId)
            {
                if (!TitleQuery.IsImdbId(imdbId)) return Refuse("Name the show by its IMDb id.");
                record.HideFromContinue(person.UserId, show: true, imdbId, body.Hidden, clock.GetUtcNow());
                return Results.NoContent();
            }

            var titleId = body.TitleId!;
            if (body.Hidden)
            {
                if (library.Get(titleId) is not { } manifest)
                    return Results.Json(new ErrorReply("The library has no such title."),
                        ApiJsonContext.Default.ErrorReply, statusCode: StatusCodes.Status404NotFound);
                if (manifest.Episode is not null) return Refuse("An episode is taken off by its show.");
            }
            record.HideFromContinue(person.UserId, show: false, titleId, body.Hidden, clock.GetUtcNow());
            return Results.NoContent();
        }).RequireAction(CinemaActions.ProfileWrite);

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
            return Results.Ok(HistoryOf(person.UserId, record, sessions, library, clock));
        }).RequireAction(CinemaActions.ProfileRead);

        // What the person says they watched, here or anywhere, or takes back. Watching marked ends a want
        // as finishing it in a room does; taking it back also forgets their progress in what the library
        // holds of it, so nothing they did says they watched it.
        app.MapPost("/api/me/watched", (WatchedChange body, HttpContext context, WatchRecord record, WatchRecorder recorder,
            CatalogueTitles shows, TitleLibrary library, PersonDirectory people, TimeProvider clock) =>
        {
            if (Person(context) is not { } person) return NoPerson();
            if (body.ImdbId is not { } imdbId || !TitleQuery.IsImdbId(imdbId)) return Refuse("Name the title by its IMDb id.");

            var now = clock.GetUtcNow();
            List<Watchable> what;
            switch (body.Kind)
            {
                case "film":
                    if (body.Season is not null || body.Episode is not null) return Refuse("A film has no seasons or episodes.");
                    what = [Watchable.Film(imdbId)];
                    break;
                case "show":
                    if (body.Episode is not null && body.Season is null) return Refuse("Name the season the episode is in.");
                    what = Episodes(imdbId, body.Season, body.Episode, body.Watched, shows, library, DateOnly.FromDateTime(now.UtcDateTime));
                    if (what.Count == 0)
                        return Results.Json(new ErrorReply(body.Watched ? "Nothing of that has aired." : "The show has no such episode."),
                            ApiJsonContext.Default.ErrorReply, statusCode: StatusCodes.Status404NotFound);
                    break;
                default:
                    return Refuse("Say whether it is a film or a show.");
            }

            people.Remember(person.UserId, person.DisplayName);
            if (body.Watched)
            {
                record.Mark(person.UserId, what, now);
                recorder.Marked(person.UserId, imdbId, show: body.Kind == "show");
            }
            else
            {
                record.Unmark(person.UserId, what);
            }

            return Results.NoContent();
        }).RequireAction(CinemaActions.ProfileWrite);

        // Forgetting a film that was never watched is the same end state, so it is not an error.
        app.MapDelete("/api/me/history/{titleId}", (string titleId, HttpContext context, WatchRecord record) =>
        {
            if (Person(context) is not { } person) return NoPerson();

            record.Forget(person.UserId, titleId);
            return Results.NoContent();
        }).RequireAction(CinemaActions.ProfileWrite);
    }

    /// <summary>
    /// The episodes of a show a change names, in order: every one the catalogue lists or the library
    /// holds, of one season or of all of them, or one. Marked watched, an episode the library does not
    /// hold counts only once it has aired.
    /// </summary>
    private static List<Watchable> Episodes(string imdbId, int? season, int? episode, bool watched, CatalogueTitles shows,
        TitleLibrary library, DateOnly today)
    {
        var described = (shows.Show(imdbId)?.Seasons ?? [])
            .Where(s => s.Number >= 1)
            .SelectMany(s => s.Episodes)
            .Select(e => (e.Season, e.Number, Aired: FollowViews.AirDate(e.AirDate) is { } aired && aired <= today));
        var held = library.EpisodesOf(imdbId).Select(e => (e.Manifest.Episode!.Season, e.Manifest.Episode.Number, Aired: true));

        return [.. described.Concat(held)
            .GroupBy(e => (e.Season, e.Number))
            .Select(g => (g.Key.Season, g.Key.Number, Aired: g.Any(e => e.Aired)))
            .Where(e => (season is null || e.Season == season) && (episode is null || e.Number == episode) && (!watched || e.Aired))
            .OrderBy(e => e.Season).ThenBy(e => e.Number)
            .Select(e => new Watchable(imdbId, e.Season, e.Number))];
    }

    /// <summary>What a person watched of what the library still holds, newest first, as <c>/api/me/history</c> lists it.</summary>
    public static List<HistoryEntry> HistoryOf(string userId, WatchRecord record, SessionStore sessions, TitleLibrary library, TimeProvider clock)
    {
        var entries = new List<HistoryEntry>();
        foreach (var (titleId, entry) in EntriesOf(userId, record, sessions, library, clock))
        {
            if (library.Get(titleId) is not { } manifest) continue;
            entries.Add(new HistoryEntry(titleId, NameOf(manifest), entry.PositionSeconds, entry.DurationSeconds,
                entry.Finished, entry.LastWatchedAt,
                manifest.Episode, manifest.Episode is null ? null : manifest.Film?.ImdbId));
        }
        return entries;
    }

    /// <summary>
    /// What the listing says each film carries for the asking person; the service key has none. A
    /// film a room is playing right now was last played now, and by whoever is watching it.
    /// </summary>
    public static TitleSummary WithPlays(
        this TitleSummary title, CinemaPerson? person, WatchRecord record, PersonDirectory people,
        IReadOnlyList<ViewingRoom> viewing, DateTimeOffset now) =>
        title.WithPlays(person, person is null ? null : record.EntryOf(person.UserId, title.Id), record.LastPlayed(title.Id), people, viewing, now);

    /// <summary>
    /// <see cref="WithPlays(TitleSummary, CinemaPerson?, WatchRecord, PersonDirectory, IReadOnlyList{ViewingRoom}, DateTimeOffset)"/>
    /// for every title of a listing, from the person's entries and every title's last play read once.
    /// </summary>
    public static List<TitleSummary> WithPlays(
        this IEnumerable<TitleSummary> titles, CinemaPerson? person, WatchRecord record, PersonDirectory people,
        IReadOnlyList<ViewingRoom> viewing, DateTimeOffset now)
    {
        var entries = person is null
            ? new Dictionary<string, WatchEntry>(StringComparer.Ordinal)
            : record.Entries(person.UserId).ToDictionary(e => e.TitleId, e => e.Entry, StringComparer.Ordinal);
        var plays = record.LastPlays();
        return [.. titles.Select(t => t.WithPlays(person, entries.GetValueOrDefault(t.Id), plays.GetValueOrDefault(t.Id), people, viewing, now))];
    }

    private static TitleSummary WithPlays(
        this TitleSummary title, CinemaPerson? person, WatchEntry? kept, TitlePlay? played, PersonDirectory people,
        IReadOnlyList<ViewingRoom> viewing, DateTimeOffset now)
    {
        var live = person is null ? null : Live(person.UserId, viewing, title.Id, title.DurationSeconds, now);
        var entry = live ?? kept;
        var playing = viewing.FirstOrDefault(r => r.Playing && r.TitleId == title.Id);

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
    /// place in the list. A person's mark on an episode (<paramref name="marks"/>) is that episode finished
    /// at the time of the mark, here or not, so a season watched elsewhere is carried on from the episode
    /// after it. On an episode the library holds it says what the episode's entry says, unless the entry
    /// is newer, and the newer of the two is the one the show is listed by. A film or a show the person
    /// took off the list (<paramref name="hidden"/>) is left out while nothing about it is newer than the
    /// moment they did.
    /// </summary>
    public static List<ProgressEntry> ContinueWatching(
        IEnumerable<(string TitleId, WatchEntry Entry)> newestFirst, IReadOnlyList<WatchedMark> marks, HiddenFromContinue hidden,
        TitleLibrary library,
        CatalogueTitles shows, bool mayFetch, DateOnly today)
    {
        var entries = newestFirst.ToList();
        var byTitle = entries.ToDictionary(e => e.TitleId, e => e.Entry, StringComparer.Ordinal);
        var activity = entries
            .Select(e => (At: e.Entry.LastWatchedAt, TitleId: (string?)e.TitleId, Entry: (WatchEntry?)e.Entry, Mark: (Watchable?)null))
            .Concat(marks
                .Where(m => !m.What.IsFilm)
                .Select(m => (At: m.MarkedAt, TitleId: (string?)null, Entry: (WatchEntry?)null, Mark: (Watchable?)m.What)))
            .OrderByDescending(a => a.At)
            .ToList();

        var seenShows = new HashSet<string>(StringComparer.Ordinal);
        var listed = new List<ProgressEntry>();

        foreach (var (at, titleId, entry, mark) in activity)
        {
            if (mark is { } marked)
            {
                if (!seenShows.Add(marked.ImdbId) || hidden.HidesShow(marked.ImdbId, at)) continue;
                var place = new EpisodeIdentity { Season = marked.Season, Number = marked.Number };
                if (After(marked.ImdbId, place, shows.Card(marked.ImdbId)?.Title ?? marked.ImdbId, at) is { } carried)
                    listed.Add(carried);
                continue;
            }

            if (library.Get(titleId!) is not { } manifest) continue;
            var show = manifest.Episode is null ? null : manifest.Film?.ImdbId;

            if (show is null)
            {
                if (!entry!.Finished && !hidden.HidesFilm(titleId!, at)) listed.Add(Progress(titleId!, manifest, entry.PositionSeconds, entry.DurationSeconds, entry.LastWatchedAt, shows));
                continue;
            }

            if (!seenShows.Add(show) || hidden.HidesShow(show, at)) continue;
            if (!entry!.Finished)
            {
                listed.Add(Progress(titleId!, manifest, entry.PositionSeconds, entry.DurationSeconds, entry.LastWatchedAt, shows));
                continue;
            }

            if (After(show, manifest.Episode!, manifest.Film?.Name ?? manifest.Title, entry.LastWatchedAt) is { } next)
                listed.Add(next);
        }

        return listed;

        // The episode after a finished one, as Continue watching lists it, or null when there is none to offer.
        ProgressEntry? After(string show, EpisodeIdentity episode, string showName, DateTimeOffset finishedAt)
        {
            var after = shows.EpisodeAfter(show, episode);
            if (HeldAfter(library, show, episode, after) is { } next)
            {
                var started = byTitle.GetValueOrDefault(next.Id) is { Finished: false } begun ? begun.PositionSeconds : 0;
                return Progress(next.Id, next.Manifest, started, next.Manifest.DurationSeconds, finishedAt, shows);
            }
            if (after is not null && mayFetch && Described(shows, show, after) is { } described
                && FollowViews.AirDate(described.AirDate) is { } aired && FollowViews.IsDue(aired, today))
            {
                var duration = (described.RuntimeMinutes ?? 0) * 60.0;
                return new ProgressEntry(null, showName, 0, duration, duration, finishedAt, after, show, described.Name, StillOf(shows, show, after));
            }
            return null;
        }
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
    private static Acquire.Tmdb.EpisodeDetails? Described(CatalogueTitles shows, string show, EpisodeIdentity episode) =>
        shows.Show(show)?.Seasons.FirstOrDefault(s => s.Number == episode.Season)?
            .Episodes.FirstOrDefault(e => e.Number == episode.Number);

    /// <summary>The episode's still as the catalogue keeps it, or null when none is kept.</summary>
    private static string? StillOf(CatalogueTitles shows, string show, EpisodeIdentity episode) =>
        Described(shows, show, episode)?.StillPath;

    private static ProgressEntry Progress(string titleId, Manifest manifest, double position, double duration, DateTimeOffset at,
        CatalogueTitles shows)
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

    private static IResult Refuse(string message) =>
        Results.Json(new ErrorReply(message), ApiJsonContext.Default.ErrorReply, statusCode: StatusCodes.Status400BadRequest);

    /// <summary>The service key names nobody, so there is nobody's preferences, progress or history to answer with.</summary>
    private static IResult NoPerson() =>
        Results.Json(new ErrorReply("This belongs to a person; sign in as one."),
            ApiJsonContext.Default.ErrorReply, statusCode: StatusCodes.Status403Forbidden);
}
