using TheKrystalShip.MovieBot.Api.Acquisition;
using TheKrystalShip.MovieBot.Api.Auth;
using TheKrystalShip.MovieBot.Api.Catalogue;
using TheKrystalShip.MovieBot.Api.People;
using TheKrystalShip.MovieBot.Api.Shows;
using TheKrystalShip.MovieBot.Core;

namespace TheKrystalShip.MovieBot.Api.Wants;

/// <summary>
/// What people want to watch. Everything here answers for whoever the token names: a want is its
/// person's, and the only way anybody else learns of it is a visible one listed with a title's
/// wanters. The service key names nobody and is refused.
/// </summary>
public static class WantEndpoints
{
    public static void MapWants(this WebApplication app)
    {
        app.MapGet("/api/me/wants", async (HttpContext context, WantStore wants, WantPresenter present, CancellationToken ct) =>
            PeopleEndpoints.Person(context) is { } person
                ? Results.Ok(await present.ViewsAsync(person.UserId, wants.For(person.UserId), ct))
                : NoPerson())
            .RequireAction(CinemaActions.ProfileRead);

        // The titles the person wants that somebody else visibly wants too, computed when asked.
        app.MapGet("/api/me/together", async (HttpContext context, WantPresenter present, CancellationToken ct) =>
            PeopleEndpoints.Person(context) is { } person
                ? Results.Ok(await present.TogetherAsync(person.UserId, ct))
                : NoPerson())
            .RequireAction(CinemaActions.ProfileRead);

        // Makes the want, or changes whether it is named. A title new to the person is looked up in the
        // catalogue as the kind it is said to be, which is what tells a film from a show and an id from a
        // typo. What the server may spend on the
        // want is what the token holds now. A film nothing has yet gets a wish when the token may wish.
        app.MapPut("/api/me/wants/{imdbId}", async (string imdbId, SetWantRequest body, HttpContext context,
            WantStore wants, WantPresenter present, Wishes wishes, CatalogueTitles titles,
            TimeProvider clock, CancellationToken ct) =>
        {
            if (PeopleEndpoints.Person(context) is not { } person) return NoPerson();
            if (!TheKrystalShip.MovieBot.Catalogue.TitleQuery.IsImdbId(imdbId)) return Error("That is not an IMDb id.", StatusCodes.Status400BadRequest);
            if (!WantKinds.IsKnown(body.Kind)) return Error("A want is for a film or a show.", StatusCodes.Status400BadRequest);

            var existing = wants.Get(person.UserId, imdbId);
            if (existing is null && (titles.Card(imdbId) is not { } card || card.Kind != body.Kind))
                return Error($"The catalogue lists no {body.Kind} under that id.", StatusCodes.Status404NotFound);

            var mayWish = person.Holds(CinemaActions.Wish);
            var (want, created) = wants.Set(person.UserId, imdbId, body.Kind!, body.Visible,
                person.Holds(CinemaActions.Download), mayWish, clock.GetUtcNow());

            // The checks run for a want made now, and for one that can now ask the tracker and had no
            // wish kept; a want only renamed or shown differently asks nothing of anybody.
            var check = body.Kind == WantKinds.Film && (created || (mayWish && existing is { MayWish: false }))
                ? await wishes.CheckAsync(imdbId, mayWish, person.Actor, ct)
                : null;

            var reply = new SetWantReply
            {
                Outcome = !created ? WantOutcomes.AlreadyWanted : check?.Outcome ?? WantOutcomes.Wanted,
                Want = await present.ViewAsync(want, ct),
            };
            return Results.Ok(created && check is not null
                ? reply with
                {
                    Release = check.Release, LibraryId = check.LibraryId, LibraryName = check.LibraryName,
                    DownloadName = check.DownloadName,
                }
                : reply);
        }).RequireAction(CinemaActions.Watch);

        app.MapDelete("/api/me/wants/{imdbId}", (string imdbId, HttpContext context, WantStore wants) =>
        {
            if (PeopleEndpoints.Person(context) is not { } person) return NoPerson();
            return wants.Remove(person.UserId, imdbId)
                ? Results.NoContent()
                : Error("You do not want that title.", StatusCodes.Status404NotFound);
        }).RequireAction(CinemaActions.Watch);

        // The people with a visible want on the title, which is what a title page shows beside it.
        app.MapGet("/api/titles/{imdbId}/wanters", (string imdbId, HttpContext context, WantPresenter present) =>
        {
            if (PeopleEndpoints.Person(context) is not { } person) return NoPerson();
            return TheKrystalShip.MovieBot.Catalogue.TitleQuery.IsImdbId(imdbId)
                ? Results.Ok(present.Wanters(imdbId, person.UserId))
                : Error("That is not an IMDb id.", StatusCodes.Status400BadRequest);
        }).RequireAction(CinemaActions.Watch);
    }

    private static IResult Error(string message, int status) =>
        Results.Json(new ErrorReply(message), ApiJsonContext.Default.ErrorReply, statusCode: status);

    private static IResult NoPerson() =>
        Error("This belongs to a person; sign in as one.", StatusCodes.Status403Forbidden);
}
