using TheKrystalShip.MovieBot.Api.Auth;
using TheKrystalShip.MovieBot.Api.People;

namespace TheKrystalShip.MovieBot.Api.Films;

/// <summary>The Discover page's shelves: its tabs and what is on each, and one shelf's titles.</summary>
public static class DiscoverEndpoints
{
    public static void MapDiscover(this IEndpointRouteBuilder app)
    {
        // The tabs and the shelves on each for the asking person, and what the page's filter picks from.
        app.MapGet("/api/discover", async (HttpContext context, DiscoverShelves shelves, CancellationToken ct) =>
            Results.Ok(await shelves.IndexAsync(PeopleEndpoints.Person(context)?.UserId, ct)))
            .RequireAction(CinemaActions.Watch);

        // One shelf's titles, joined with the library and with what the asking person watched.
        app.MapGet("/api/discover/shelves/{id}", async (string id, HttpContext context, DiscoverShelves shelves, CancellationToken ct) =>
        {
            var outcome = await shelves.ShelfAsync(id, PeopleEndpoints.Person(context)?.UserId, ct);
            return outcome.Value is { } shelf
                ? Results.Ok(shelf)
                : Results.Json(new ErrorReply(outcome.Message ?? "The film database could not answer."), ApiJsonContext.Default.ErrorReply,
                    statusCode: outcome.Problem == Problem.NotFound ? StatusCodes.Status404NotFound : StatusCodes.Status503ServiceUnavailable);
        }).RequireAction(CinemaActions.Watch);
    }
}
