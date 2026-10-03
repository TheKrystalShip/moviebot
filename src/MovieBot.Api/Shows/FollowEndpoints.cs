using TheKrystalShip.MovieBot.Api.Films;
using TheKrystalShip.MovieBot.Api.People;

namespace TheKrystalShip.MovieBot.Api.Shows;

/// <summary>
/// Following a show. Everything here answers for whoever the token names; the service key names
/// nobody and is refused, so there is no route by which anybody reads or changes anybody else's.
/// </summary>
public static class FollowEndpoints
{
    public static void MapFollows(this WebApplication app)
    {
        app.MapGet("/api/me/follows", (HttpContext context, FollowStore follows, ShowStore shows) =>
            PeopleEndpoints.Person(context) is { } person
                ? Results.Ok(follows.For(person.UserId).Select(f => shows.View(f)).ToList())
                : NoPerson());

        // Follows the show, or changes whether its new episodes are downloaded for the person. The
        // show is asked of the film database for its status and for the baseline: what is already
        // out is not news to somebody who follows it now.
        app.MapPut("/api/me/follows/{imdbId}", async (string imdbId, FollowRequest body, HttpContext context,
            FollowStore follows, ShowStore shows, TimeProvider clock, CancellationToken ct) =>
        {
            if (PeopleEndpoints.Person(context) is not { } person) return NoPerson();
            if (!FilmStore.IsImdbId(imdbId)) return Error("That is not an IMDb id.", StatusCodes.Status400BadRequest);

            var stored = await shows.GetAsync(imdbId, ct);
            if (stored is null)
                return Error("The film database could not be asked about this show.", StatusCodes.Status503ServiceUnavailable);
            if (stored.Details is null)
                return Error("The film database has no show under that id.", StatusCodes.Status404NotFound);
            if (!FollowViews.Followable(stored.Details))
                return Error("This show has ended, so there is nothing to follow.", StatusCodes.Status409Conflict);

            var follow = follows.Follow(person.UserId, imdbId, body.AutoDownload,
                FollowViews.Baseline(stored, FollowViews.Today(clock)), clock.GetUtcNow());
            return Results.Ok(shows.View(follow));
        });

        app.MapDelete("/api/me/follows/{imdbId}", (string imdbId, HttpContext context, FollowStore follows) =>
        {
            if (PeopleEndpoints.Person(context) is not { } person) return NoPerson();
            follows.Unfollow(person.UserId, imdbId);
            return Results.NoContent();
        });
    }

    private static IResult Error(string message, int status) =>
        Results.Json(new ErrorReply(message), ApiJsonContext.Default.ErrorReply, statusCode: status);

    private static IResult NoPerson() =>
        Error("This belongs to a person; sign in as one.", StatusCodes.Status403Forbidden);
}
