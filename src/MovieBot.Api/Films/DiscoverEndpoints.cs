using TheKrystalShip.MovieBot.Api.Auth;
using TheKrystalShip.MovieBot.Api.Library;
using TheKrystalShip.MovieBot.Api.People;

namespace TheKrystalShip.MovieBot.Api.Films;

/// <summary>The Discover page's shelves: its tabs and what is on each, and one shelf's titles.</summary>
public static class DiscoverEndpoints
{
    /// <summary>How long the index waits for its first screen's shelves and films before answering without the rest.</summary>
    private static readonly TimeSpan FirstScreenWait = TimeSpan.FromMilliseconds(400);

    /// <summary>How many of the page's shelf rows, from the top, the index carries the titles of.</summary>
    private const int FirstRows = 3;

    /// <summary>How many films the spotlight turns through: its shelf's first films the viewer has not watched.</summary>
    private const int SpotlightFilms = 4;

    public static void MapDiscover(this IEndpointRouteBuilder app)
    {
        // The page's rows and categories for the asking person and what the filter picks from, with what
        // its first screen draws: the spotlight's shelf and films, and the first rows' titles. Those are
        // asked for together and carried when they answer within FirstScreenWait, which a kept answer
        // does; one that does not is left to its own route, and finishes and is kept all the same.
        app.MapGet("/api/discover", async (HttpContext context, DiscoverShelves shelves, FilmStore films, LibraryListing library,
            WatchRecord record, TitleCategories categories, WikidataFacts facts, StreamingServices streaming, CancellationToken ct) =>
        {
            var viewer = PeopleEndpoints.Person(context)?.UserId;
            var index = await shelves.IndexAsync(viewer, ct);
            var deadline = Task.Delay(FirstScreenWait, ct);

            var ids = new List<string> { index.Spotlight };
            ids.AddRange(index.Rows.Where(r => r.Shelf is not null).Select(r => r.Shelf!.Id).Take(FirstRows));
            var asked = ids.Distinct(StringComparer.Ordinal)
                .Select(id => shelves.ShelfAsync(id, viewer, 1, CancellationToken.None)).ToList();
            await Task.WhenAny(Task.WhenAll(asked), deadline);
            var answered = asked.Where(t => t.IsCompletedSuccessfully && t.Result.Value is not null).Select(t => t.Result.Value!).ToList();

            var described = new List<FilmView>();
            if (answered.FirstOrDefault(s => s.Id == index.Spotlight) is { } lead && !deadline.IsCompleted)
            {
                var shelf = LibraryShelf.Of(library.Titles(), record, viewer);
                var picks = lead.Titles.Where(t => t.Kind == "film" && !t.Watched).Take(SpotlightFilms)
                    .Select(t => FilmEndpoints.ViewAsync(films, shelf, categories, facts, streaming, t.ImdbId, CancellationToken.None)).ToList();
                await Task.WhenAny(Task.WhenAll(picks), deadline);
                described.AddRange(picks.Where(t => t.IsCompletedSuccessfully && t.Result is not null).Select(t => t.Result!));
            }

            return Results.Ok(index with { Shelves = answered, Films = described });
        }).RequireAction(CinemaActions.Watch);

        // One page of a shelf's titles, joined with the library and with what the asking person
        // watched; next names the page that follows.
        app.MapGet("/api/discover/shelves/{id}", async (string id, int? page, HttpContext context, DiscoverShelves shelves, CancellationToken ct) =>
        {
            var outcome = await shelves.ShelfAsync(id, PeopleEndpoints.Person(context)?.UserId, page ?? 1, ct);
            return outcome.Value is { } shelf
                ? Results.Ok(shelf)
                : Results.Json(new ErrorReply(outcome.Message ?? "The film database could not answer."), ApiJsonContext.Default.ErrorReply,
                    statusCode: outcome.Problem switch
                    {
                        Problem.Invalid => StatusCodes.Status400BadRequest,
                        Problem.NotFound => StatusCodes.Status404NotFound,
                        _ => StatusCodes.Status503ServiceUnavailable,
                    });
        }).RequireAction(CinemaActions.Watch);
    }
}
