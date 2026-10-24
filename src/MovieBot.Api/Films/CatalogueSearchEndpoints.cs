using System.Globalization;
using Microsoft.AspNetCore.Mvc;
using TheKrystalShip.MovieBot.Api.Auth;
using TheKrystalShip.MovieBot.Api.Library;
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

        // Words typed into Discover as a question (top 10 horror movies from 2000), or no question
        // when they are a title's name, narrowed by a filter's picks; page and next walk the titles it found.
        app.MapGet("/api/catalogue/ask", async (string? q, int? page, string? kind, string? category, [FromQuery] string[]? anyGenre,
            [FromQuery] string[]? decade, [FromQuery] string[]? length, double? minRating, [FromQuery] string[]? language,
            HttpContext context, CatalogueSearch search, CancellationToken ct) =>
        {
            if (Picks(kind, category, anyGenre, decade, length, minRating, language) is not { } picks) return BadDecade();
            var outcome = await search.AskAsync(q ?? "", page ?? 1, picks, ct);
            if (outcome.Value is not { } answer) return Refused(outcome);

            var shelf = search.ShelfFor(Viewer(context));
            return Results.Ok(new CatalogueQuestionView
            {
                Query = q!.Trim(),
                Question = answer.Question,
                Parts = answer.Parts,
                Kind = answer.Discovery?.Kind,
                Resolved = answer.Discovery?.Resolved,
                Titles = [.. (answer.Discovery?.Hits ?? []).Select(h => search.View(h, shelf))],
                Next = answer.Discovery?.Next,
                Message = answer.Message,
            });
        }).RequireAction(CinemaActions.Watch);

        // Which options each section of a question's answers holds, with the filter's picks on it.
        app.MapGet("/api/catalogue/ask/facets", async (string? q, string? kind, string? category, [FromQuery] string[]? anyGenre,
            [FromQuery] string[]? decade, [FromQuery] string[]? length, double? minRating, [FromQuery] string[]? language,
            CatalogueSearch search, CancellationToken ct) =>
        {
            if (Picks(kind, category, anyGenre, decade, length, minRating, language) is not { } picks) return BadDecade();
            var outcome = await search.AskFacetsAsync(q ?? "", picks, ct);
            return outcome.Value is { } facets ? Results.Ok(facets) : Refused(outcome);
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

        // Films or shows found by what they are like. Every filter is optional and they combine; a
        // page of at most twenty, and next names the page that follows.
        app.MapGet("/api/catalogue/discover", async (HttpContext context, CatalogueSearch search, CancellationToken ct) =>
        {
            var (request, refusal) = Discovery(context.Request.Query);
            if (request is null) return refusal!;
            var outcome = await search.DiscoverAsync(request, ct);
            if (outcome.Value is not { } answer) return Refused(outcome);

            var shelf = search.ShelfFor(Viewer(context));
            return Results.Ok(new CatalogueDiscoverView
            {
                Kind = answer.Kind,
                Resolved = answer.Resolved,
                Titles = [.. answer.Hits.Select(h => search.View(h, shelf))],
                Next = answer.Next,
            });
        }).RequireAction(CinemaActions.Watch);

        // Which options each section of a discovery's answers holds, with every other section's picks on it.
        app.MapGet("/api/catalogue/discover/facets", async (HttpContext context, CatalogueSearch search, CancellationToken ct) =>
        {
            var (request, refusal) = Discovery(context.Request.Query);
            if (request is null) return refusal!;
            var outcome = await search.DiscoverFacetsAsync(request, new HashSet<string>(), ct);
            return outcome.Value is { } facets ? Results.Ok(facets) : Refused(outcome);
        }).RequireAction(CinemaActions.Watch);

        // What came out lately and what has a date coming, the most looked at of each; with
        // category=anime, of the anime alone.
        app.MapGet("/api/catalogue/released", async (string? kind, int? days, string? category, HttpContext context, CatalogueSearch search, CancellationToken ct) =>
            AnimeOnly(category) is not { } anime
                ? BadCategory()
                : Window(await search.RecentAsync(kind ?? "film", Math.Clamp(days ?? CatalogueSearch.RecentDays, 1, 120), anime, ct), context, search))
            .RequireAction(CinemaActions.Watch);

        app.MapGet("/api/catalogue/upcoming", async (string? kind, int? days, string? category, HttpContext context, CatalogueSearch search, CancellationToken ct) =>
            AnimeOnly(category) is not { } anime
                ? BadCategory()
                : Window(await search.UpcomingAsync(kind ?? "film", Math.Clamp(days ?? CatalogueSearch.UpcomingDays, 1, 365), anime, ct), context, search))
            .RequireAction(CinemaActions.Watch);

        // What the library gained most recently, from what is kept: nothing here waits on the database.
        app.MapGet("/api/catalogue/added", (string? kind, string? category, int? limit, HttpContext context, CatalogueSearch search) =>
        {
            if (!Catalogue.IsKind(kind) && kind is not null)
                return Results.Json(new ErrorReply("Kind is film or show."), ApiJsonContext.Default.ErrorReply, statusCode: StatusCodes.Status400BadRequest);
            var shelved = TitleCategories.Parse(category);
            if (category is not null && shelved is null)
                return Results.Json(new ErrorReply("Category is anime or general."), ApiJsonContext.Default.ErrorReply, statusCode: StatusCodes.Status400BadRequest);
            return Results.Ok(search.Added(kind, shelved, limit ?? CatalogueSearch.ResultsListed, Viewer(context)));
        }).RequireAction(CinemaActions.Watch);
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

    /// <summary>
    /// Whether a list of the film database's is narrowed to anime: false with no category, true for
    /// <c>anime</c>, null for anything else. Everything that is not anime is not a question the database
    /// can be asked.
    /// </summary>
    private static bool? AnimeOnly(string? category) =>
        category is null ? false : TitleCategories.Parse(category) == TitleCategory.Anime ? true : null;

    private static IResult BadCategory() => Results.Json(
        new ErrorReply("A list is narrowed to anime alone; leave category out for everything."), ApiJsonContext.Default.ErrorReply,
        statusCode: StatusCodes.Status400BadRequest);

    /// <summary>Who is asking: the token's person, or nobody for the service key.</summary>
    private static string? Viewer(HttpContext context) => PeopleEndpoints.Person(context)?.UserId;

    /// <summary>A discovery as its query names it, or why the query is not one: a number that is not a number.</summary>
    private static (DiscoverRequest? Request, IResult? Refusal) Discovery(IQueryCollection query)
    {
        string? bad = null;
        string? One(string key) => query[key].LastOrDefault(v => !string.IsNullOrWhiteSpace(v))?.Trim();
        int? Int(string key)
        {
            if (One(key) is not { } text) return null;
            if (int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n)) return n;
            bad ??= key;
            return null;
        }
        double? Real(string key)
        {
            if (One(key) is not { } text) return null;
            if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var n)) return n;
            bad ??= key;
            return null;
        }
        bool? Flag(string key)
        {
            if (One(key) is not { } text) return null;
            if (bool.TryParse(text, out var b)) return b;
            bad ??= key;
            return null;
        }
        if (Years(query["decade"]) is not { } decades) return (null, BadDecade());

        // One language is the question's own; several are a pick of any of them.
        var languages = Words(query["language"]);
        var request = new DiscoverRequest
        {
            Kind = One("kind"),
            Genres = Words(query["genre"]),
            AnyGenres = Words(query["anyGenre"]),
            Exclude = Words(query["exclude"]),
            IncludeFamily = Flag("includeFamily") ?? false,
            From = Int("from"),
            To = Int("to"),
            With = Words(query["with"]),
            About = Words(query["about"]),
            Language = languages.Count == 1 ? languages[0] : null,
            Languages = languages.Count > 1 ? languages : [],
            Decades = decades,
            Lengths = Words(query["length"]),
            Like = One("like"),
            GoodOnly = Flag("goodOnly") ?? true,
            MinRating = Real("minRating"),
            MinVotes = Int("minVotes"),
            MinRuntime = Int("minRuntime"),
            MaxRuntime = Int("maxRuntime"),
            Sort = One("sort"),
            Limit = Int("limit") ?? CatalogueSearch.MostListed,
            Page = Int("page") ?? 1,
            Category = One("category"),
        };
        return bad is null
            ? (request, null)
            : (null, Results.Json(new ErrorReply($"{bad} is not a {(bad is "includeFamily" or "goodOnly" ? "true or false" : "number")}."),
                ApiJsonContext.Default.ErrorReply, statusCode: StatusCodes.Status400BadRequest));
    }

    /// <summary>A filter's picks on a question; null for a decade that is not a number.</summary>
    internal static DiscoverPicks? Picks(string? kind, string? category, string[]? anyGenre, string[]? decade, string[]? length, double? minRating, string[]? language) =>
        Years(decade) is { } decades
            ? new DiscoverPicks
            {
                Kind = string.IsNullOrWhiteSpace(kind) ? null : kind.Trim(),
                Category = string.IsNullOrWhiteSpace(category) ? null : category.Trim(),
                AnyGenres = Words(anyGenre),
                Decades = decades,
                Lengths = Words(length),
                MinRating = minRating,
                Languages = Words(language),
            }
            : null;

    /// <summary>The decades a query names, each once; null when one is not a number.</summary>
    private static List<int>? Years(IEnumerable<string?>? values)
    {
        var years = new List<int>();
        foreach (var word in Words([.. values ?? []]))
        {
            if (!int.TryParse(word.TrimEnd('s'), NumberStyles.None, CultureInfo.InvariantCulture, out var year)) return null;
            if (!years.Contains(year)) years.Add(year);
        }
        return years;
    }

    internal static IResult BadDecade() => Results.Json(
        new ErrorReply("A decade is named by its first year, 1990."), ApiJsonContext.Default.ErrorReply,
        statusCode: StatusCodes.Status400BadRequest);

    /// <summary>The words a query carries, whether repeated or joined by commas, each once.</summary>
    private static List<string> Words(IEnumerable<string?>? values) =>
        (values ?? []).OfType<string>().SelectMany(v => v.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
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
