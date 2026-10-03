namespace TheKrystalShip.MovieBot.Api.Films;

public static class FilmEndpoints
{
    public static void MapFilms(this IEndpointRouteBuilder app)
    {
        // A film by its IMDb id, in the library or not: a page about a film somebody is waiting on
        // shows it as fully as one on the shelf.
        app.MapGet("/api/films/{imdbId}", async (string imdbId, FilmStore store, CancellationToken ct) =>
        {
            if (!FilmStore.IsImdbId(imdbId))
                return Results.Json(new ErrorReply("That is not an IMDb id."), ApiJsonContext.Default.ErrorReply,
                    statusCode: StatusCodes.Status400BadRequest);

            var stored = await store.GetAsync(imdbId, ct);
            if (stored is null)
                return Results.Json(new ErrorReply("The film database could not be asked about this film."),
                    ApiJsonContext.Default.ErrorReply, statusCode: StatusCodes.Status503ServiceUnavailable);

            return store.View(stored) is { } view
                ? Results.Ok(view)
                : Results.Json(new ErrorReply("The film database has no film under that id."),
                    ApiJsonContext.Default.ErrorReply, statusCode: StatusCodes.Status404NotFound);
        });

        // The artwork kept beside each description. Open like a poster under /media: it is the
        // database's public artwork, and an <img> carries no token. It never changes under one name
        // for long, so a browser may keep it a day.
        app.MapMethods("/films/{imdbId}/{file}", ["GET", "HEAD"], (string imdbId, string file, FilmStore store, HttpContext context) =>
        {
            if (store.ImageFile(imdbId, file) is not { } path) return Results.NotFound();
            context.Response.Headers.CacheControl = "public, max-age=86400";
            return Results.File(path, "image/jpeg", enableRangeProcessing: false);
        });
    }
}
