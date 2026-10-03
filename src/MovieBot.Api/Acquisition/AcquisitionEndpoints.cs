using TheKrystalShip.MovieBot.Acquire.Search;
using TheKrystalShip.MovieBot.Acquire.Tracker;
using TheKrystalShip.MovieBot.Core;

namespace TheKrystalShip.MovieBot.Api.Acquisition;

/// <summary>
/// Fetching films, for the services that act on somebody's behalf.
///
/// These spend things — the disk, the tracker's ratio, an hourly call to the tracker for every wish —
/// so they answer the service key alone. A room token proves only that somebody signed in through
/// Discord, and the people a service vouches for are named on each request as an Actor, which is who
/// the tags and the log record.
/// </summary>
public static class AcquisitionEndpoints
{
    public static void MapAcquisition(this WebApplication app)
    {
        var group = app.MapGroup("/api").AddEndpointFilter(ServiceOnly);

        // What to offer for something typed into a picker. `session` keys the debounce, so one
        // person mid-word does not hold back another's finished query.
        group.MapGet("/releases/suggest", async (
            string? q, string? session, AutocompleteSearch suggestions, CancellationToken ct) =>
        {
            var answer = await suggestions.SuggestAsync(q ?? "", session ?? "", ct);
            return Results.Ok(new ReleaseSuggestions(
                answer.Choices.Select(c => new ReleaseChoiceView(c.Label, c.TorrentId)).ToList(),
                answer.Explanation));
        });

        // The releases the tracker holds for a film, named in words or by IMDb id, best first.
        group.MapGet("/releases", async (
            string? q, string? imdb, IReleaseSearch search, OfferedReleases offered, CancellationToken ct) =>
        {
            try
            {
                RankedReleases ranked = imdb is { Length: > 0 }
                    ? await search.ByImdbAsync(imdb, ct)
                    : await search.ByTextAsync(q ?? "", ct);
                offered.Remember(ranked.Candidates);
                return Results.Ok(new RankedOffers(
                    ranked.Candidates.Select(OfferedReleases.View).ToList(),
                    ranked.Film is { } film
                        ? new FilmView { ImdbId = film.ImdbId, Title = film.Title, Year = film.Year, Display = film.Display }
                        : null,
                    ranked.EmptyExplanation));
            }
            catch (TrackerException)
            {
                return Unanswered("The tracker is not answering.");
            }
        });

        group.MapGet("/releases/{torrentId:long}", (long torrentId, OfferedReleases offered) =>
            offered.Resolve(torrentId) is { } release
                ? Results.Ok(OfferedReleases.View(release))
                : Results.NotFound());

        group.MapGet("/downloads", async (Downloads downloads, Library.TitleLibrary library, CancellationToken ct) =>
            await downloads.ListAsync(library, ct) is { } list
                ? Results.Ok(list)
                : Unanswered("The torrent client is not answering."));

        group.MapPost("/downloads", async (StartDownloadRequest request, Downloads downloads, CancellationToken ct) =>
            Results.Ok(await downloads.StartAsync(request, ct)));

        group.MapGet("/wishes", (string? userId, Wishes wishes) => Results.Ok(wishes.List(userId)));

        group.MapPost("/wishes", async (AddWishRequest request, Wishes wishes, CancellationToken ct) =>
            Results.Ok(await wishes.SubscribeAsync(request, ct)));

        group.MapDelete("/wishes/{imdbId}", (string imdbId, string userId, Wishes wishes) =>
            Results.Ok(wishes.Cancel(userId, imdbId)));

        group.MapGet("/wishes/available", (WishSweep sweep) => Results.Ok(sweep.Available()));

        group.MapPost("/wishes/{imdbId}/told", (string imdbId, WishesTold told, WishSweep sweep) =>
        {
            sweep.Told(imdbId, told.ChannelIds);
            return Results.NoContent();
        });

        group.MapGet("/keeps", async (Keeps keeps, CancellationToken ct) =>
            await keeps.ListAsync(ct) is { } films
                ? Results.Ok(films)
                : Unanswered("The torrent client is not answering."));

        group.MapPut("/keeps/{hash}", async (string hash, KeepRequest request, Keeps keeps, CancellationToken ct) =>
            Results.Ok(await keeps.KeepAsync(hash, request.RequestedBy, ct)));

        group.MapPost("/keeps/{hash}/release", async (string hash, KeepRequest request, Keeps keeps, CancellationToken ct) =>
            Results.Ok(await keeps.ReleaseAsync(hash, request.RequestedBy, ct)));

        group.MapGet("/titles/{id}/retention", async (string id, Keeps keeps, CancellationToken ct) =>
            await keeps.ForTitleAsync(id, ct) is { } film ? Results.Ok(film) : Results.NoContent());
    }

    private static IResult Unanswered(string what) =>
        Results.Json(new ErrorReply(what), ApiJsonContext.Default.ErrorReply, statusCode: StatusCodes.Status502BadGateway);

    /// <summary>
    /// Refuses a caller that came in on a room token. The middleware has already let the request
    /// through on the service key or on a token, and a token is what it leaves in Items.
    /// </summary>
    private static ValueTask<object?> ServiceOnly(EndpointFilterInvocationContext context, EndpointFilterDelegate next) =>
        context.HttpContext.Items.ContainsKey("room-token")
            ? ValueTask.FromResult<object?>(Results.Json(
                new ErrorReply("This is for the services that act on somebody's behalf."),
                ApiJsonContext.Default.ErrorReply, statusCode: StatusCodes.Status403Forbidden))
            : next(context);
}
