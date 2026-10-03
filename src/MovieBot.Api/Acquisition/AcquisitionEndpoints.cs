using TheKrystalShip.MovieBot.Acquire.Search;
using TheKrystalShip.MovieBot.Acquire.Tracker;
using TheKrystalShip.MovieBot.Core;

namespace TheKrystalShip.MovieBot.Api.Acquisition;

/// <summary>
/// Fetching films, for the services that act on somebody's behalf and for members of the server.
///
/// These spend things — the disk, the tracker's ratio, an hourly call to the tracker for every wish —
/// so they answer the service key, or a token that says its bearer is a member of the server MovieBot
/// serves. That is what a slash command takes too, and a member may do here what they may do there.
/// A service names the person it vouches for on each request as an Actor; a member is the person, and
/// whatever a member's request says about who asked is replaced by who the token says they are. The
/// announcing of wishes is the bot's, and answers the service key alone.
/// </summary>
public static class AcquisitionEndpoints
{
    public static void MapAcquisition(this WebApplication app)
    {
        var group = app.MapGroup("/api").AddEndpointFilter(ServiceOrMember);

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

        // A member's download is announced nowhere in Discord: the channel is the bot's to name.
        group.MapPost("/downloads", async (StartDownloadRequest request, HttpContext context, Downloads downloads, CancellationToken ct) =>
            Results.Ok(await downloads.StartAsync(
                MemberOf(context) is { } member
                    ? request with { RequestedBy = new Actor(member.UserId, member.DisplayName), NotifyChannelId = null }
                    : request,
                ct)));

        // A member reads the list as themselves: whose wishes they are is the token's to say.
        group.MapGet("/wishes", (string? userId, HttpContext context, Wishes wishes) =>
            Results.Ok(wishes.List(MemberOf(context)?.UserId ?? userId)));

        group.MapPost("/wishes", async (AddWishRequest request, HttpContext context, Wishes wishes, CancellationToken ct) =>
            Results.Ok(await wishes.SubscribeAsync(
                MemberOf(context) is { } member
                    ? request with { RequestedBy = new Actor(member.UserId, member.DisplayName), ChannelId = null }
                    : request,
                ct)));

        group.MapDelete("/wishes/{imdbId}", (string imdbId, string? userId, HttpContext context, Wishes wishes) =>
            (MemberOf(context)?.UserId ?? userId) is { Length: > 0 } who
                ? Results.Ok(wishes.Cancel(who, imdbId))
                : Results.Json(new ErrorReply("Name whose wish to cancel."), ApiJsonContext.Default.ErrorReply,
                    statusCode: StatusCodes.Status400BadRequest));

        group.MapGet("/wishes/available", (WishSweep sweep) => Results.Ok(sweep.Available()))
            .AddEndpointFilter(ServiceOnly);

        group.MapPost("/wishes/{imdbId}/told", (string imdbId, WishesTold told, WishSweep sweep) =>
        {
            sweep.Told(imdbId, told.ChannelIds);
            return Results.NoContent();
        }).AddEndpointFilter(ServiceOnly);

        group.MapGet("/keeps", async (Keeps keeps, CancellationToken ct) =>
            await keeps.ListAsync(ct) is { } films
                ? Results.Ok(films)
                : Unanswered("The torrent client is not answering."));

        group.MapPut("/keeps/{hash}", async (string hash, KeepRequest request, HttpContext context, Keeps keeps, CancellationToken ct) =>
            Results.Ok(await keeps.KeepAsync(hash, Asker(context, request.RequestedBy), ct)));

        group.MapPost("/keeps/{hash}/release", async (string hash, KeepRequest request, HttpContext context, Keeps keeps, CancellationToken ct) =>
            Results.Ok(await keeps.ReleaseAsync(hash, Asker(context, request.RequestedBy), ct)));

        group.MapGet("/titles/{id}/retention", async (string id, Keeps keeps, CancellationToken ct) =>
            await keeps.ForTitleAsync(id, ct) is { } film ? Results.Ok(film) : Results.NoContent());
    }

    private static IResult Unanswered(string what) =>
        Results.Json(new ErrorReply(what), ApiJsonContext.Default.ErrorReply, statusCode: StatusCodes.Status502BadGateway);

    /// <summary>The member a request came from, or null when it came on the service key.</summary>
    private static RoomTokenPayload? MemberOf(HttpContext context) =>
        context.Items.TryGetValue("room-token", out var token) ? token as RoomTokenPayload : null;

    /// <summary>Who asked: the member a token names, or the person a service vouches for.</summary>
    private static Actor Asker(HttpContext context, Actor named) =>
        MemberOf(context) is { } member ? new Actor(member.UserId, member.DisplayName) : named;

    /// <summary>
    /// Lets through the service key and a member's token. The middleware has already let the request
    /// through on one or the other, and a token is what it leaves in Items.
    /// </summary>
    private static ValueTask<object?> ServiceOrMember(EndpointFilterInvocationContext context, EndpointFilterDelegate next) =>
        context.HttpContext.Items.TryGetValue("room-token", out var token) && token is RoomTokenPayload { Member: false }
            ? Refuse("This is for members of the server, signed in on the site, and for the services that act for them.")
            : next(context);

    private static ValueTask<object?> ServiceOnly(EndpointFilterInvocationContext context, EndpointFilterDelegate next) =>
        context.HttpContext.Items.ContainsKey("room-token")
            ? Refuse("This is for the services that act on somebody's behalf.")
            : next(context);

    private static ValueTask<object?> Refuse(string why) =>
        ValueTask.FromResult<object?>(Results.Json(
            new ErrorReply(why), ApiJsonContext.Default.ErrorReply, statusCode: StatusCodes.Status403Forbidden));
}
