using TheKrystalShip.MovieBot.Api.Auth;
using TheKrystalShip.MovieBot.Api.Catalogue;
using TheKrystalShip.MovieBot.Api.People;
using TheKrystalShip.MovieBot.Core;

namespace TheKrystalShip.MovieBot.Api.Shows;

/// <summary>
/// Following a show. Everything here answers for whoever the token names; the service key names
/// nobody and is refused, so there is no route by which anybody reads or changes anybody else's.
/// </summary>
public static class FollowEndpoints
{
    public static void MapFollows(this WebApplication app)
    {
        app.MapGet("/api/me/follows", (HttpContext context, FollowStore follows, CatalogueTitles shows) =>
            PeopleEndpoints.Person(context) is { } person
                ? Results.Ok(follows.For(person.UserId).Select(f => shows.View(f)).ToList())
                : NoPerson())
            .RequireAction(CinemaActions.ProfileRead);

        // Follows the show, or changes whether its new episodes are downloaded for the person. The
        // catalogue says its status and gives the baseline: what is already out is not news to
        // somebody who follows it now. A follow that downloads starts downloads as
        // the person later, with nobody there to check, so it is set only by somebody who may download.
        app.MapPut("/api/me/follows/{imdbId}", async (string imdbId, FollowRequest body, HttpContext context,
            FollowStore follows, CatalogueTitles shows, TimeProvider clock, CancellationToken ct) =>
        {
            if (PeopleEndpoints.Person(context) is not { } person) return NoPerson();
            if (!TheKrystalShip.MovieBot.Catalogue.TitleQuery.IsImdbId(imdbId)) return Error("That is not an IMDb id.", StatusCodes.Status400BadRequest);
            if (body.AutoDownload && !person.Holds(CinemaActions.Download))
                return Error("Your account does not let you download in Cinema.", StatusCodes.Status403Forbidden);

            if (shows.Show(imdbId) is not { Details: not null } stored)
                return Error("The catalogue lists no show under that id.", StatusCodes.Status404NotFound);
            if (!FollowViews.Followable(stored.Details))
                return Error("This show has ended, so there is nothing to follow.", StatusCodes.Status409Conflict);

            var follow = follows.Follow(person.UserId, imdbId, body.AutoDownload,
                FollowViews.Baseline(stored, FollowViews.Today(clock)), clock.GetUtcNow());
            return Results.Ok(shows.View(follow));
        }).RequireAction(CinemaActions.ProfileWrite);

        app.MapDelete("/api/me/follows/{imdbId}", (string imdbId, HttpContext context, FollowStore follows) =>
        {
            if (PeopleEndpoints.Person(context) is not { } person) return NoPerson();
            follows.Unfollow(person.UserId, imdbId);
            return Results.NoContent();
        }).RequireAction(CinemaActions.ProfileWrite);
    }

    private static IResult Error(string message, int status) =>
        Results.Json(new ErrorReply(message), ApiJsonContext.Default.ErrorReply, statusCode: status);

    private static IResult NoPerson() =>
        Error("This belongs to a person; sign in as one.", StatusCodes.Status403Forbidden);
}
