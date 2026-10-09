using Microsoft.AspNetCore.Mvc;
using TheKrystalShip.MovieBot.Api.Auth;
using TheKrystalShip.MovieBot.Api.People;

namespace TheKrystalShip.MovieBot.Api.Films;

/// <summary>
/// Finding films and shows in the film database rather than opening one: by name, by a person, by
/// what a title is like, and by what is new. Each result is joined with the library and with whether
/// the asking person has watched it.
/// </summary>
public static class CatalogueSearchEndpoints
{
    public static void MapCatalogueSearch(this IEndpointRouteBuilder app)
    {
        // Films and shows by their name or words from it, optionally of one kind and of a year or
        // the year either side.
        app.MapGet("/api/catalogue/search", async (string? q, string? kind, int? year, HttpContext context, CatalogueSearch search, CancellationToken ct) =>
        {
            var outcome = await search.SearchAsync(q ?? "", kind, year, ct);
            if (outcome.Value is not { } hits) return Refused(outcome);

            var shelf = search.ShelfFor(Viewer(context));
            return Results.Ok(new CatalogueSearchView { Query = q!.Trim(), Titles = [.. hits.Select(h => search.View(h, shelf))] });
        }).RequireAction(CinemaActions.Watch);

        // The people a name could mean, with what each is known for: the way to a person's id.
        app.MapGet("/api/catalogue/people", async (string? q, CatalogueSearch search, CancellationToken ct) =>
        {
            var outcome = await search.PeopleAsync(q ?? "", ct);
            if (outcome.Value is not { } people) return Refused(outcome);

            return Results.Ok(new CataloguePeopleView { Query = q!.Trim(), People = [.. people.Select(search.View)] });
        }).RequireAction(CinemaActions.Watch);

        // Everything a person is credited on, cast and crew, films and shows, oldest first, each with
        // its date and whether it is out, coming or only announced.
        app.MapGet("/api/catalogue/people/{personId:int}/credits", async (
            int personId, int? from, int? to, string? role, string? kind, bool? appearances,
            HttpContext context, CatalogueSearch search, CancellationToken ct) =>
        {
            var outcome = await search.CreditsAsync(personId, new CreditsRequest(from, to, (role ?? "any").ToLowerInvariant(), kind, appearances ?? false), ct);
            return outcome.Value is { } answer
                ? Results.Ok(search.View(answer, search.ShelfFor(Viewer(context))))
                : Refused(outcome);
        }).RequireAction(CinemaActions.Watch);

        // Films or shows found by what they are like. Every filter is optional and they combine.
        app.MapGet("/api/catalogue/discover", async (
            string? kind, [FromQuery] string[]? genre, int? from, int? to, [FromQuery(Name = "with")] string[]? people, [FromQuery] string[]? about,
            string? language, string? like, bool? goodOnly, double? minRating, int? minVotes, string? sort, int? limit,
            HttpContext context, CatalogueSearch search, CancellationToken ct) =>
        {
            var outcome = await search.DiscoverAsync(new DiscoverRequest
            {
                Kind = kind,
                Genres = Words(genre),
                From = from,
                To = to,
                With = Words(people),
                About = Words(about),
                Language = language,
                Like = like,
                GoodOnly = goodOnly ?? true,
                MinRating = minRating,
                MinVotes = minVotes,
                Sort = sort,
                Limit = limit ?? CatalogueSearch.ResultsListed,
            }, ct);
            if (outcome.Value is not { } answer) return Refused(outcome);

            var shelf = search.ShelfFor(Viewer(context));
            return Results.Ok(new CatalogueDiscoverView
            {
                Kind = answer.Kind,
                Resolved = answer.Resolved,
                Titles = [.. answer.Hits.Select(h => search.View(h, shelf))],
            });
        }).RequireAction(CinemaActions.Watch);

        // What came out lately and what has a date coming, the most looked at of each.
        app.MapGet("/api/catalogue/released", async (string? kind, int? days, HttpContext context, CatalogueSearch search, CancellationToken ct) =>
            Window(await search.RecentAsync(kind ?? "film", Math.Clamp(days ?? CatalogueSearch.RecentDays, 1, 120), ct), context, search))
            .RequireAction(CinemaActions.Watch);

        app.MapGet("/api/catalogue/upcoming", async (string? kind, int? days, HttpContext context, CatalogueSearch search, CancellationToken ct) =>
            Window(await search.UpcomingAsync(kind ?? "film", Math.Clamp(days ?? CatalogueSearch.UpcomingDays, 1, 365), ct), context, search))
            .RequireAction(CinemaActions.Watch);

        // What the library gained most recently, from what is kept: nothing here waits on the database.
        app.MapGet("/api/catalogue/added", (string? kind, int? limit, HttpContext context, CatalogueSearch search) =>
            Catalogue.IsKind(kind) || kind is null
                ? Results.Ok(search.Added(kind, limit ?? CatalogueSearch.ResultsListed, Viewer(context)))
                : Results.Json(new ErrorReply("Kind is film or show."), ApiJsonContext.Default.ErrorReply, statusCode: StatusCodes.Status400BadRequest))
            .RequireAction(CinemaActions.Watch);
    }

    private static IResult Window(Outcome<ListAnswer> outcome, HttpContext context, CatalogueSearch search)
    {
        if (outcome.Value is not { } answer) return Refused(outcome);

        var shelf = search.ShelfFor(Viewer(context));
        return Results.Ok(new CatalogueListView
        {
            Kind = answer.Kind,
            From = answer.From.ToString("yyyy-MM-dd"),
            To = answer.To.ToString("yyyy-MM-dd"),
            Titles = [.. answer.Hits.Select(h => search.View(h, shelf))],
        });
    }

    /// <summary>Who is asking: the token's person, or nobody for the service key.</summary>
    private static string? Viewer(HttpContext context) => PeopleEndpoints.Person(context)?.UserId;

    /// <summary>The words a query carries, whether repeated or joined by commas, each once.</summary>
    private static List<string> Words(string[]? values) =>
        (values ?? []).SelectMany(v => v.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .Distinct(StringComparer.OrdinalIgnoreCase).ToList();

    private static IResult Refused<T>(Outcome<T> outcome) where T : class => Results.Json(
        new ErrorReply(outcome.Message ?? "The film database could not answer."), ApiJsonContext.Default.ErrorReply,
        statusCode: outcome.Problem switch
        {
            Problem.Invalid => StatusCodes.Status400BadRequest,
            Problem.NotFound => StatusCodes.Status404NotFound,
            _ => StatusCodes.Status503ServiceUnavailable,
        });
}
