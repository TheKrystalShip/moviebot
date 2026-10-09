using TheKrystalShip.MovieBot.Api.Auth;
using TheKrystalShip.MovieBot.Api.Library;
using TheKrystalShip.MovieBot.Api.People;

namespace TheKrystalShip.MovieBot.Api.Films;

public static class FilmEndpoints
{
    public static void MapFilms(this IEndpointRouteBuilder app)
    {
        // A film by its IMDb id, in the library or not: a page about a film somebody is waiting on
        // shows it as fully as one on the shelf.
        app.MapGet("/api/films/{imdbId}", async (string imdbId, HttpContext context, FilmStore store, LibraryListing library,
            WatchRecord record, CancellationToken ct) =>
        {
            if (!FilmStore.IsImdbId(imdbId))
                return Results.Json(new ErrorReply("That is not an IMDb id."), ApiJsonContext.Default.ErrorReply,
                    statusCode: StatusCodes.Status400BadRequest);

            var stored = await store.GetAsync(imdbId, ct);
            if (stored is null)
                return Results.Json(new ErrorReply("The film database could not be asked about this film."),
                    ApiJsonContext.Default.ErrorReply, statusCode: StatusCodes.Status503ServiceUnavailable);

            var shelf = LibraryShelf.Of(library.Titles(), record, PeopleEndpoints.Person(context)?.UserId);
            return store.View(stored, shelf.LibraryIdOf("film", imdbId), shelf.Saw("film", imdbId)) is { } view
                ? Results.Ok(view)
                : Results.Json(new ErrorReply("The film database has no film under that id."),
                    ApiJsonContext.Default.ErrorReply, statusCode: StatusCodes.Status404NotFound);
        }).RequireAction(CinemaActions.Watch);

        // The films and shows trending this week, from what is kept: a page never waits on the database.
        app.MapGet("/api/popular", (PopularNow popular) => Results.Ok(popular.View()))
            .RequireAction(CinemaActions.Watch);

        // A person and the films and shows they are credited on, best known first, by the film
        // database's id for them, which a cast member on a film's or show's page carries.
        app.MapGet("/api/catalogue/people/{personId:int}", async (int personId, Catalogue catalogue, CancellationToken ct) =>
        {
            var kept = await catalogue.PersonAsync(personId, ct);
            if (kept is null)
                return Results.Json(new ErrorReply("The film database could not be asked about this person."),
                    ApiJsonContext.Default.ErrorReply, statusCode: StatusCodes.Status503ServiceUnavailable);
            return kept.Value is { } person
                ? Results.Ok(catalogue.View(person))
                : Results.Json(new ErrorReply("The film database has nobody under that id."),
                    ApiJsonContext.Default.ErrorReply, statusCode: StatusCodes.Status404NotFound);
        }).RequireAction(CinemaActions.Watch);

        // The most popular films or shows in a genre, by the genre's name: the one call a shelf of a
        // genre makes to lead on into the catalogue.
        app.MapGet("/api/catalogue/genre", async (string? kind, string? name, Catalogue catalogue, CancellationToken ct) =>
        {
            if (!Catalogue.IsKind(kind) || string.IsNullOrWhiteSpace(name))
                return Results.Json(new ErrorReply("Name a kind, film or show, and a genre."), ApiJsonContext.Default.ErrorReply,
                    statusCode: StatusCodes.Status400BadRequest);

            if (await catalogue.GenreAsync(kind!, name, ct) is not { } answer)
                return Results.Json(new ErrorReply("The film database could not be asked about this genre."),
                    ApiJsonContext.Default.ErrorReply, statusCode: StatusCodes.Status503ServiceUnavailable);
            if (answer.Genre is not { } genre)
                return Results.Json(new ErrorReply("The film database has no genre by that name."),
                    ApiJsonContext.Default.ErrorReply, statusCode: StatusCodes.Status404NotFound);

            return Results.Ok(new CatalogueGenreView
            {
                Kind = kind!,
                Genre = genre.Name,
                Titles = (answer.Titles?.Value ?? []).Select(catalogue.View).ToList(),
            });
        }).RequireAction(CinemaActions.Watch);

        // A person's portrait, open like any other artwork.
        app.MapMethods("/catalogue/people/{file}", ["GET", "HEAD"], (string file, Catalogue catalogue, HttpContext context) =>
        {
            if (catalogue.PortraitFile(file) is not { } path) return Results.NotFound();
            context.Response.Headers.CacheControl = "public, max-age=86400";
            return Results.File(path, "image/jpeg", enableRangeProcessing: false);
        }).AllowAnonymous();

        // A trending entry's poster, open like any other artwork.
        app.MapMethods("/popular/{file}", ["GET", "HEAD"], (string file, PopularNow popular, HttpContext context) =>
        {
            if (popular.PosterFile(file) is not { } path) return Results.NotFound();
            context.Response.Headers.CacheControl = "public, max-age=86400";
            return Results.File(path, "image/jpeg", enableRangeProcessing: false);
        }).AllowAnonymous();

        // The artwork kept beside each description. Open like a poster under /media: it is the
        // database's public artwork, and an <img> carries no token. It never changes under one name
        // for long, so a browser may keep it a day.
        app.MapMethods("/films/{imdbId}/{file}", ["GET", "HEAD"], (string imdbId, string file, FilmStore store, HttpContext context) =>
        {
            if (store.ImageFile(imdbId, file) is not { } path) return Results.NotFound();
            context.Response.Headers.CacheControl = "public, max-age=86400";
            return Results.File(path, "image/jpeg", enableRangeProcessing: false);
        }).AllowAnonymous();

        // A search result's poster, the one its page shows, fetched the first time it is asked for if
        // the glance's own fetch has not kept it yet.
        app.MapMethods("/glances/{imdbId}/poster.jpg", ["GET", "HEAD"], async (string imdbId, TitleGlances glances, HttpContext context) =>
        {
            if (await glances.PosterFileAsync(imdbId) is not { } path) return Results.NotFound();
            context.Response.Headers.CacheControl = "public, max-age=86400";
            return Results.File(path, "image/jpeg", enableRangeProcessing: false);
        }).AllowAnonymous();
    }
}
