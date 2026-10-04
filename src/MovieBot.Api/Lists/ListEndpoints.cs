using TheKrystalShip.MovieBot.Api.Films;
using TheKrystalShip.MovieBot.Api.Notifications;
using TheKrystalShip.MovieBot.Api.People;
using TheKrystalShip.MovieBot.Api.Shows;
using TheKrystalShip.MovieBot.Core;

namespace TheKrystalShip.MovieBot.Api.Lists;

/// <summary>
/// Lists of films and shows to watch later. Everything here answers for whoever the token names: a
/// list is shown to its owner and the people the owner put on it, and to nobody else, so a list
/// somebody cannot see is a 404 rather than a 403. The service key names nobody and is refused.
/// </summary>
public static class ListEndpoints
{
    public static void MapLists(this WebApplication app)
    {
        // The lists the person can see, their Watch later first. Asked about a title, each says
        // whether it holds it, which is what a save button needs.
        app.MapGet("/api/me/lists", (string? holding, HttpContext context, ListStore lists, ListPresenter present, TimeProvider clock) =>
            PeopleEndpoints.Person(context) is { } person
                ? Results.Ok(lists.For(person.UserId, clock.GetUtcNow()).Select(l => present.Summary(l, person.UserId, holding)).ToList())
                : NoPerson());

        app.MapPost("/api/lists", (ListNameRequest body, HttpContext context, ListStore lists, ListPresenter present, TimeProvider clock) =>
        {
            if (PeopleEndpoints.Person(context) is not { } person) return NoPerson();
            if (NameOf(body.Name) is not { } name) return BadName();

            var (list, refusal) = lists.Create(person.UserId, name, clock.GetUtcNow());
            return list is null ? Refused(refusal) : Results.Ok(present.View(list, person.UserId));
        });

        app.MapGet("/api/lists/{id}", (string id, HttpContext context, ListStore lists, ListPresenter present, TimeProvider clock) =>
        {
            if (PeopleEndpoints.Person(context) is not { } person) return NoPerson();
            return lists.Get(person.UserId, id, clock.GetUtcNow()) is { } list
                ? Results.Ok(present.View(list, person.UserId))
                : Refused(ListRefusal.NotFound);
        });

        app.MapPatch("/api/lists/{id}", (string id, ListNameRequest body, HttpContext context, ListStore lists, ListPresenter present, TimeProvider clock) =>
        {
            if (PeopleEndpoints.Person(context) is not { } person) return NoPerson();
            if (NameOf(body.Name) is not { } name) return BadName();

            var (list, refusal) = lists.Rename(person.UserId, id, name, clock.GetUtcNow());
            return list is null ? Refused(refusal) : Results.Ok(present.Summary(list, person.UserId));
        });

        app.MapDelete("/api/lists/{id}", (string id, HttpContext context, ListStore lists) =>
        {
            if (PeopleEndpoints.Person(context) is not { } person) return NoPerson();
            var refusal = lists.Delete(person.UserId, id);
            return refusal == ListRefusal.None ? Results.NoContent() : Refused(refusal);
        });

        // Saves a title to a list, or changes its note. The title is asked of the film database as
        // the kind it is said to be, which is what tells a film from a show and an id from a typo,
        // and what keeps its description for the list to show.
        app.MapPut("/api/lists/{id}/entries/{imdbId}", async (string id, string imdbId, SaveEntryRequest body,
            HttpContext context, ListStore lists, ListPresenter present, FilmStore films, ShowStore shows,
            TimeProvider clock, CancellationToken ct) =>
        {
            if (PeopleEndpoints.Person(context) is not { } person) return NoPerson();
            if (!FilmStore.IsImdbId(imdbId)) return Error("That is not an IMDb id.", StatusCodes.Status400BadRequest);
            if (!EntryKinds.IsKnown(body.Kind)) return Error("A list holds a film or a show.", StatusCodes.Status400BadRequest);
            var note = body.Note?.Trim();
            if (note is { Length: > ListStore.NoteLength })
                return Error($"A note is at most {ListStore.NoteLength} characters.", StatusCodes.Status400BadRequest);

            if (lists.Get(person.UserId, id, clock.GetUtcNow()) is not { } list) return Refused(ListRefusal.NotFound);
            if (list.Entries.All(e => e.ImdbId != imdbId))
            {
                var described = body.Kind == EntryKinds.Show
                    ? await shows.GetAsync(imdbId, ct) is { } show ? show.Details is not null : (bool?)null
                    : await films.GetAsync(imdbId, ct) is { } film ? film.Details is not null : null;
                if (described is null)
                    return Error("The film database could not be asked about this title.", StatusCodes.Status503ServiceUnavailable);
                if (described is false)
                    return Error($"The film database has no {body.Kind} under that id.", StatusCodes.Status404NotFound);
            }

            var (entry, _, refusal) = lists.Save(person.UserId, id, imdbId, body.Kind!, note, clock.GetUtcNow());
            return entry is null ? Refused(refusal) : Results.Ok(present.Entry(entry, person.UserId));
        });

        app.MapDelete("/api/lists/{id}/entries/{imdbId}", (string id, string imdbId, HttpContext context, ListStore lists, TimeProvider clock) =>
        {
            if (PeopleEndpoints.Person(context) is not { } person) return NoPerson();
            var refusal = lists.Remove(person.UserId, id, imdbId, clock.GetUtcNow());
            return refusal == ListRefusal.None ? Results.NoContent() : Refused(refusal);
        });

        // Puts somebody on the owner's list. Somebody put on it for the first time is told, once.
        app.MapPut("/api/lists/{id}/members/{userId}", async (string id, string userId, ListMemberRequest body,
            HttpContext context, ListStore lists, ListPresenter present, PersonDirectory people, Notifier notifier,
            TimeProvider clock, CancellationToken ct) =>
        {
            if (PeopleEndpoints.Person(context) is not { } person) return NoPerson();
            if (!ListRoles.IsMemberRole(body.Role))
                return Error("Somebody on a list is an editor or a viewer.", StatusCodes.Status400BadRequest);
            if (people.NameOf(userId) is null)
                return Error("Nobody by that id can be put on a list.", StatusCodes.Status404NotFound);

            var (list, added, refusal) = lists.SetMember(person.UserId, id, userId, body.Role!, clock.GetUtcNow());
            if (list is null) return Refused(refusal);

            if (added)
            {
                await notifier.TellAsync(userId, new NotificationDraft
                {
                    Kind = NotificationKinds.Shared,
                    Key = $"shared:{list.Id}:{userId}",
                    Text = $"{people.NameOf(person.UserId) ?? person.DisplayName} shared their list {list.Name} with you.",
                    Url = SitePaths.Saved(list.Id),
                    Action = "Open",
                }, ct);
            }
            return Results.Ok(present.Summary(list, person.UserId));
        });

        // The owner takes somebody off; a member takes themselves off, which is leaving.
        app.MapDelete("/api/lists/{id}/members/{userId}", (string id, string userId, HttpContext context, ListStore lists, TimeProvider clock) =>
        {
            if (PeopleEndpoints.Person(context) is not { } person) return NoPerson();
            var refusal = lists.RemoveMember(person.UserId, id, userId, clock.GetUtcNow());
            return refusal == ListRefusal.None ? Results.NoContent() : Refused(refusal);
        });
    }

    /// <summary>A list's name, trimmed, or null when it is blank or too long.</summary>
    private static string? NameOf(string? name) =>
        name?.Trim() is { Length: > 0 and <= ListStore.NameLength } trimmed ? trimmed : null;

    private static IResult BadName() =>
        Error($"A list's name is between 1 and {ListStore.NameLength} characters.", StatusCodes.Status400BadRequest);

    private static IResult Refused(ListRefusal refusal) => refusal switch
    {
        ListRefusal.NotFound => Error("No list by that id.", StatusCodes.Status404NotFound),
        ListRefusal.NotAllowed => Error("That is the list owner's to change.", StatusCodes.Status403Forbidden),
        ListRefusal.Personal => Error("Watch later is yours alone and cannot be renamed, shared or deleted.", StatusCodes.Status409Conflict),
        _ => Error("That would take the list past its limit.", StatusCodes.Status409Conflict),
    };

    private static IResult Error(string message, int status) =>
        Results.Json(new ErrorReply(message), ApiJsonContext.Default.ErrorReply, statusCode: status);

    private static IResult NoPerson() =>
        Error("This belongs to a person; sign in as one.", StatusCodes.Status403Forbidden);
}
