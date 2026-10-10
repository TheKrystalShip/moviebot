using TheKrystalShip.MovieBot.Api.Auth;
using TheKrystalShip.MovieBot.Api.Films;

namespace TheKrystalShip.MovieBot.Api.Library;

public static class CategoryEndpoints
{
    public static void MapCategories(this IEndpointRouteBuilder app)
    {
        // Shelves a film or a show as anime or not, where the rule over its description is wrong for it.
        // Somebody who fetches titles decides what the library holds, and so where it is shelved.
        app.MapPut("/api/categories/{imdbId}", (string imdbId, CategoryRequest body, TitleCategories categories) =>
        {
            if (!FilmStore.IsImdbId(imdbId))
                return Refused("That is not an IMDb id.");
            if (body.Kind is not ("film" or "show"))
                return Refused("Kind is film or show.");
            if (TitleCategories.Parse(body.Category) is not { } category)
                return Refused("Category is anime or general.");

            var now = categories.Set(body.Kind, imdbId, category);
            return Results.Ok(new CategoryView(imdbId, now, categories.IsSet(imdbId)));
        }).RequireAction(CinemaActions.Download);
    }

    private static IResult Refused(string message) =>
        Results.Json(new ErrorReply(message), ApiJsonContext.Default.ErrorReply, statusCode: StatusCodes.Status400BadRequest);
}
