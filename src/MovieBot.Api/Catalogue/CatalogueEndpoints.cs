using System.Globalization;
using Microsoft.Extensions.Options;
using TheKrystalShip.MovieBot.Api.Auth;
using TheKrystalShip.MovieBot.Api.People;
using TheKrystalShip.MovieBot.Catalogue;

namespace TheKrystalShip.MovieBot.Api.Catalogue;

/// <summary>
/// The catalogue's routes (authority: <c>docs/api-contract.md</c>, Catalogue). Every list is
/// <c>/api/catalogue/titles</c> and its filters; every title the one card, joined with the asking
/// person (<c>you</c>); every image <c>/images/{size}/{file}</c>, and a title's by its IMDb id at
/// <c>/images/titles/{imdbId}/{name}</c>. Nothing here reaches a source: a
/// catalogue that cannot be read is a <c>503</c> that says why.
/// </summary>
public static partial class CatalogueEndpoints
{
    /// <summary>The list query's parameters the API answers itself, from the asking person's library and record.</summary>
    private static readonly string[] PersonParameters = ["unwatched", "inLibrary", "here"];

    public static void MapCatalogue(this IEndpointRouteBuilder app)
    {
        var routes = app.MapGroup("/api/catalogue").RequireAction(CinemaActions.Watch);

        routes.MapGet("/titles", (HttpContext context, CatalogueQueries queries, CatalogueState state, CatalogueViewers viewers, TimeProvider clock) =>
        {
            if (state.Problem() is { } problem) return Unavailable(problem);
            var viewer = viewers.For(Person(context));
            var (query, refusal) = Query(context, viewer, clock);
            return query is null ? Refused(refusal!) : Results.Ok(viewer.Join(queries.Titles(query)));
        });

        routes.MapGet("/facets", (HttpContext context, FacetIndex facets, CatalogueState state, CatalogueViewers viewers, TimeProvider clock) =>
        {
            if (state.Problem() is { } problem) return Unavailable(problem);
            var viewer = viewers.For(Person(context));
            var (query, refusal) = Query(context, viewer, clock);
            return query is null ? Refused(refusal!) : Results.Ok(facets.Facets(query));
        });

        routes.MapGet("/titles/{id}", (string id, HttpContext context, CatalogueQueries queries, CatalogueState state, CatalogueViewers viewers) =>
        {
            if (!TitleQuery.IsImdbId(id)) return Refused("A title is named by its IMDb id, as tt0232500.");
            if (state.Problem() is { } problem) return Unavailable(problem);
            return queries.Title(id) is { } page ? Results.Ok(viewers.For(Person(context)).Join(page)) : NotFound($"The catalogue lists no title {id}.");
        });

        routes.MapGet("/titles/{id}/seasons/{number:int}", (string id, int number, CatalogueQueries queries, CatalogueState state) =>
        {
            if (!TitleQuery.IsImdbId(id)) return Refused("A title is named by its IMDb id, as tt0232500.");
            if (state.Problem() is { } problem) return Unavailable(problem);
            return queries.Season(id, number) is { } season ? Results.Ok(season) : NotFound($"The catalogue lists no season {number} of {id}.");
        });

        routes.MapGet("/terms", (string? q, CatalogueQueries queries, CatalogueState state) =>
        {
            if (string.IsNullOrWhiteSpace(q)) return Refused("q is the words to resolve.");
            if (state.Problem() is { } problem) return Unavailable(problem);
            return Results.Ok(queries.Terms(q[..Math.Min(q.Length, 100)]));
        });

        routes.MapGet("/ask", (string? q, CatalogueQuestions questions, CatalogueState state) =>
        {
            if (string.IsNullOrWhiteSpace(q)) return Refused("q is the question.");
            if (state.Problem() is { } problem) return Unavailable(problem);
            return Results.Ok(questions.Ask(q[..Math.Min(q.Length, 300)]));
        });

        routes.MapGet("/people/{id:long}", (long id, CatalogueQueries queries, CatalogueState state) =>
        {
            if (state.Problem() is { } problem) return Unavailable(problem);
            return queries.Person(id) is { } person ? Results.Ok(person) : NotFound($"The catalogue names nobody {id}.");
        });

        // The franchises in the asking person's order: the series they are partway through first.
        routes.MapGet("/franchises", async (int? page, int? size, HttpContext context, DiscoverIndex discover, CatalogueState state, CatalogueViewers viewers) =>
        {
            if (state.Problem() is { } problem) return Unavailable(problem);
            var (at, count) = (Math.Max(1, page ?? 1), Math.Clamp(size ?? 40, 1, TitleQuery.MostPerPage));
            var (series, total) = await discover.FranchisesAsync(viewers.For(Person(context)), at, count);
            return Results.Ok(new FranchiseHeads(series, total, at * count < total ? at + 1 : null));
        });

        routes.MapGet("/franchises/{id}", (string id, CatalogueQueries queries, CatalogueState state) =>
        {
            if (state.Problem() is { } problem) return Unavailable(problem);
            return queries.Franchise(id) is { } franchise ? Results.Ok(franchise) : NotFound($"The catalogue holds no franchise {id}.");
        });

        routes.MapGet("/discover", async (HttpContext context, DiscoverIndex discover, CatalogueState state, CatalogueViewers viewers) =>
        {
            if (state.Problem() is { } problem) return Unavailable(problem);
            var person = Person(context);
            return Results.Ok(await discover.PageAsync(person, viewers.For(person)));
        });

        // Every image the catalogue keeps, by the film database's file name and the size it was kept
        // at. A file never changes under its name, so it is served immutable for a year.
        app.MapMethods("/images/{size}/{file}", [HttpMethods.Get, HttpMethods.Head], (string size, string file, HttpContext context, IOptions<CatalogueReadOptions> options) =>
        {
            if (CatalogueSetup.ImageFile(options.Value, size, file) is not { } path || !File.Exists(path)) return Results.NotFound();
            context.Response.Headers.CacheControl = "public, max-age=31536000, immutable";
            return Results.File(path, file.EndsWith(".png", StringComparison.OrdinalIgnoreCase) ? "image/png"
                : file.EndsWith(".svg", StringComparison.OrdinalIgnoreCase) ? "image/svg+xml" : "image/jpeg");
        }).AllowAnonymous();

        // A title's poster, backdrop or an episode's still (s01e02, numbered as the releases number it)
        // by its IMDb id, for what knows a title and not its card: the player's frame, a remote, a guest.
        // It answers with the image's own address, which is what a card names, relative to this one, so
        // a proxy serving the API under a prefix (the Activity's) keeps it; the redirect is kept an
        // hour, since a title's artwork can change under its id.
        app.MapMethods("/images/titles/{imdbId}/{name}", [HttpMethods.Get, HttpMethods.Head], (string imdbId, string name, HttpContext context, CatalogueTitles titles) =>
        {
            var url = name switch
            {
                "poster" => titles.Card(imdbId)?.Poster,
                "backdrop" => titles.Card(imdbId) is { } card ? card.Backdrop ?? card.Poster : null,
                _ when EpisodeName().Match(name) is { Success: true } m => titles.Show(imdbId)?.Seasons
                    .FirstOrDefault(s => s.Number == int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture))?.Episodes
                    .FirstOrDefault(e => e.Number == int.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture))?.StillPath,
                _ => null,
            };
            if (url is null || !url.StartsWith("/images/", StringComparison.Ordinal)) return Results.NotFound();
            context.Response.Headers.CacheControl = "public, max-age=3600";
            return Results.Redirect("../../" + url["/images/".Length..]);
        }).AllowAnonymous();
    }

    [System.Text.RegularExpressions.GeneratedRegex(@"^s(\d{1,3})e(\d{1,4})$")]
    private static partial System.Text.RegularExpressions.Regex EpisodeName();

    /// <summary>
    /// A request's list query: the catalogue's filters and the person's (<c>unwatched</c>, <c>inLibrary</c>,
    /// and <c>here</c>: <c>watched</c> for what the people here watched most lately, <c>ready</c> for what
    /// the library holds ready, newest first). A parameter neither knows is refused.
    /// </summary>
    private static (TitleQuery? Query, string? Refusal) Query(HttpContext context, ViewerJoin viewer, TimeProvider clock)
    {
        var parameters = context.Request.Query;
        if (parameters.Keys.FirstOrDefault(k => !TitleQuery.Parameters.Contains(k) && !PersonParameters.Contains(k)) is { } unknown)
            return (null, $"The list query takes no {unknown}.");
        var (query, refusal) = TitleQuery.Parse(name => parameters[name].OfType<string>().ToList());
        if (query is null) return (null, refusal);

        bool? Flag(string name) => parameters[name].LastOrDefault() is { } value ? bool.TryParse(value, out var flag) ? flag : null : null;
        if (parameters.ContainsKey("unwatched") && Flag("unwatched") is null || parameters.ContainsKey("inLibrary") && Flag("inLibrary") is null)
            return (null, "unwatched and inLibrary are true or false.");
        var except = new HashSet<string>(StringComparer.Ordinal);
        if (Flag("unwatched") == true) except.UnionWith(viewer.Watched());
        if (Flag("inLibrary") == false) except.UnionWith(viewer.Held());
        query = query with
        {
            Except = except.Count > 0 ? except : null,
            Only = Flag("inLibrary") == true ? viewer.Held() : null,
        };
        switch (parameters["here"].LastOrDefault())
        {
            case null:
                break;
            case "watched":
                query = query with { Ordered = viewer.PopularHere(clock.GetUtcNow()) };
                break;
            case "ready":
                query = query with { Ordered = viewer.ReadyHere() };
                break;
            default:
                return (null, "here is watched or ready.");
        }
        return (query, null);
    }

    private static string? Person(HttpContext context) => PeopleEndpoints.Person(context)?.UserId;

    private static IResult Unavailable(string problem) =>
        Results.Json(new ErrorReply(problem), ApiJsonContext.Default.ErrorReply, statusCode: StatusCodes.Status503ServiceUnavailable);

    private static IResult Refused(string reason) => Results.BadRequest(new ErrorReply(reason));

    private static IResult NotFound(string reason) => Results.NotFound(new ErrorReply(reason));
}

/// <summary>A page of franchises as their heads, in the asking person's order.</summary>
public sealed record FranchiseHeads(List<ShelfHead> Franchises, int Total, int? Next);
