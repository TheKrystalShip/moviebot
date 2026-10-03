using TheKrystalShip.MovieBot.Api.Library;
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
                : NoPerson());

        app.MapPut("/api/me/preferences", (ViewerPreferences body, HttpContext context, PreferenceStore store) =>
            Person(context) is { } person
                ? Results.Ok(store.Set(person.UserId, body))
                : NoPerson());

        // Films this person started and did not finish, newest first. A film the library no longer
        // holds is left out: there is nothing to continue and no name to show for it.
        app.MapGet("/api/me/progress", (HttpContext context, WatchRecord record, TitleLibrary library) =>
        {
            if (Person(context) is not { } person) return NoPerson();

            var entries = new List<ProgressEntry>();
            foreach (var (titleId, entry) in record.Entries(person.UserId))
            {
                if (entry.Finished || NameOf(library, titleId) is not { } name) continue;
                entries.Add(new ProgressEntry(titleId, name, entry.PositionSeconds, entry.DurationSeconds,
                    Math.Max(0, entry.DurationSeconds - entry.PositionSeconds), entry.LastWatchedAt));
            }

            return Results.Ok(entries);
        });

        app.MapGet("/api/me/history", (HttpContext context, WatchRecord record, TitleLibrary library) =>
        {
            if (Person(context) is not { } person) return NoPerson();

            var entries = new List<HistoryEntry>();
            foreach (var (titleId, entry) in record.Entries(person.UserId))
            {
                if (NameOf(library, titleId) is not { } name) continue;
                entries.Add(new HistoryEntry(titleId, name, entry.PositionSeconds, entry.DurationSeconds,
                    entry.Finished, entry.LastWatchedAt));
            }

            return Results.Ok(entries);
        });

        // Forgetting a film that was never watched is the same end state, so it is not an error.
        app.MapDelete("/api/me/history/{titleId}", (string titleId, HttpContext context, WatchRecord record) =>
        {
            if (Person(context) is not { } person) return NoPerson();

            record.Forget(person.UserId, titleId);
            return Results.NoContent();
        });
    }

    /// <summary>What the listing says each film carries for the asking person; the service key has none.</summary>
    public static TitleSummary WithPlays(
        this TitleSummary title, RoomTokenPayload? person, WatchRecord record, PersonDirectory people)
    {
        var entry = person is null ? null : record.EntryOf(person.UserId, title.Id);
        var played = record.LastPlayed(title.Id);

        return title with
        {
            Progress = entry is null ? null : new TitleProgress(entry.PositionSeconds, entry.Finished, entry.LastWatchedAt),
            LastPlayed = played is null ? null : new TitleLastPlayed(played.LastPlayedAt, played.LastPlayedBy, people.NameOf(played.LastPlayedBy)),
        };
    }

    public static RoomTokenPayload? Person(HttpContext context) =>
        context.Items.TryGetValue("room-token", out var token) ? token as RoomTokenPayload : null;

    private static string? NameOf(TitleLibrary library, string titleId) =>
        library.Get(titleId) is { } manifest ? manifest.Film?.Display ?? manifest.Title : null;

    /// <summary>The service key names nobody, so there is nobody's preferences, progress or history to answer with.</summary>
    private static IResult NoPerson() =>
        Results.Json(new ErrorReply("This belongs to a person; sign in as one."),
            ApiJsonContext.Default.ErrorReply, statusCode: StatusCodes.Status403Forbidden);
}
