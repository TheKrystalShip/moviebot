using TheKrystalShip.MovieBot.Acquire;
using TheKrystalShip.MovieBot.Acquire.Imdb;
using TheKrystalShip.MovieBot.Acquire.Search;
using TheKrystalShip.MovieBot.Acquire.Tracker;
using TheKrystalShip.MovieBot.Api.Auth;
using TheKrystalShip.MovieBot.Api.Library;
using TheKrystalShip.MovieBot.Api.Notifications;
using TheKrystalShip.MovieBot.Core;

namespace TheKrystalShip.MovieBot.Api.Acquisition;

/// <summary>
/// Fetching films, waiting on them and keeping them.
///
/// These spend things — the disk, the tracker's ratio, an hourly call to the tracker for every wish,
/// a film's place on the disk — so each takes its own action, checked on the token: downloading,
/// wishing, keeping. Whoever the token names is who asked, whichever client carried the request; a
/// request says nothing about who asked, so no client can say it is somebody else. Reading how long a
/// film stays is reading the library, and takes watching.
/// </summary>
public static class AcquisitionEndpoints
{
    public static void MapAcquisition(this WebApplication app)
    {
        var group = app.MapGroup("/api");

        // What to offer for something typed into a picker. `session` keys the debounce, so one
        // person mid-word does not hold back another's finished query.
        group.MapGet("/releases/suggest", async (
            string? q, string? session, AutocompleteSearch suggestions, CancellationToken ct) =>
        {
            var answer = await suggestions.SuggestAsync(q ?? "", session ?? "", ct);
            return Results.Ok(new ReleaseSuggestions(
                answer.Choices.Select(c => new ReleaseChoiceView(c.Label, c.TorrentId)).ToList(),
                answer.Explanation));
        }).RequireAction(CinemaActions.Download);

        // The films the title index offers for a name, for a search box: each leads to its own page,
        // which is where whether it can be watched is asked. Finding a film is how a download and a
        // wish both begin.
        group.MapGet("/films/find", async (string? q, ImdbClient index, CancellationToken ct) =>
            Results.Ok((await index.SuggestAsync(q ?? "", ct)).Select(FoundTitle.From).ToList()))
            .RequireAction(CinemaActions.Download, CinemaActions.Wish);

        // Films and shows together, in the index's own order, so the likeliest answer to a name comes
        // first whichever it is. A show is fetched by its seasons, so this takes downloading.
        group.MapGet("/titles/find", async (string? q, ImdbClient index, CancellationToken ct) =>
            Results.Ok((await index.SuggestTitlesAsync(q ?? "", ct)).Select(FoundTitle.From).ToList()))
            .RequireAction(CinemaActions.Download);

        // Whether a film can be watched and how: here, arriving, offered at 1080p and 720p, or not out.
        group.MapGet("/films/{imdbId}/availability", async (string imdbId, HttpContext context, WatchNow watch, CancellationToken ct) =>
            ImdbId.IsValid(imdbId)
                ? Results.Ok(await watch.AvailabilityAsync(imdbId, CinemaPerson.Of(context)?.UserId, ct))
                : Results.Json(new ErrorReply("That is not an IMDb id."), ApiJsonContext.Default.ErrorReply,
                    statusCode: StatusCodes.Status400BadRequest))
            .RequireAction(CinemaActions.Download, CinemaActions.Wish);

        // Watch now: the film is fetched and played in a room the moment it can be opened. The answer
        // comes before the release is picked, so the person is in the room watching that happen.
        group.MapPost("/watch", async (WatchNowRequest request, HttpContext context, WatchNow watch, CancellationToken ct) =>
        {
            if (!ImdbId.IsValid(request.ImdbId))
                return Results.Json(new ErrorReply("That is not an IMDb id."), ApiJsonContext.Default.ErrorReply,
                    statusCode: StatusCodes.Status400BadRequest);
            if (CinemaPerson.Of(context) is not { } person) return NoPerson();

            var reply = await watch.StartAsync(request, person.Actor, ct);
            return reply.Outcome == "full"
                ? Results.Json(reply, ManifestJsonContext.Default.WatchNowReply, statusCode: StatusCodes.Status409Conflict)
                : Results.Ok(reply);
        }).RequireAction(CinemaActions.Download);

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
        }).RequireAction(CinemaActions.Download);

        group.MapGet("/releases/{torrentId:long}", (long torrentId, OfferedReleases offered) =>
            offered.Resolve(torrentId) is { } release
                ? Results.Ok(OfferedReleases.View(release))
                : Results.NotFound())
            .RequireAction(CinemaActions.Download);

        // Every download, for a person who may start one and for the bot, which shows each one's
        // progress in the channel it was asked in.
        group.MapGet("/downloads", async (Downloads downloads, Library.TitleLibrary library, CancellationToken ct) =>
            await downloads.ListAsync(library, ct) is { } list
                ? Results.Ok(list)
                : Unanswered("The torrent client is not answering."))
            .RequireActionOrService(CinemaActions.Download);

        group.MapPost("/downloads", async (StartDownloadRequest request, HttpContext context, Downloads downloads, CancellationToken ct) =>
            CinemaPerson.Of(context) is { } person
                ? Results.Ok(await downloads.StartAsync(request, person.Actor, ct))
                : NoPerson())
            .RequireAction(CinemaActions.Download);

        // A person's own wishes: whose they are is the token's to say.
        group.MapGet("/wishes", (HttpContext context, Wishes wishes) =>
            CinemaPerson.Of(context) is { } person ? Results.Ok(wishes.List(person.UserId)) : NoPerson())
            .RequireAction(CinemaActions.Wish);

        group.MapPost("/wishes", async (AddWishRequest request, HttpContext context, Wishes wishes, CancellationToken ct) =>
            CinemaPerson.Of(context) is { } person
                ? Results.Ok(await wishes.SubscribeAsync(request, person.Actor, ct))
                : NoPerson())
            .RequireAction(CinemaActions.Wish);

        group.MapDelete("/wishes/{imdbId}", (string imdbId, HttpContext context, Wishes wishes) =>
            CinemaPerson.Of(context) is { } person ? Results.Ok(wishes.Cancel(person.UserId, imdbId)) : NoPerson())
            .RequireAction(CinemaActions.Wish);

        // How long each film stays in the library, and who keeps it. A film the library does not hold
        // is a 404, since nothing about one that is not there would be a fact. The bot reads a film's
        // stay for the announcement it posts when a download can be watched.
        group.MapGet("/titles/{id}/stay", (string id, LibraryClock library) =>
            library.StayOf(id) is { } stay ? Results.Ok(stay) : NoSuchTitle())
            .RequireActionOrService(CinemaActions.Watch);

        group.MapGet("/stays", (LibraryClock library) => Results.Ok(library.Stays().ToList()))
            .RequireAction(CinemaActions.Watch);

        // A keep is on what stays or leaves as one, so keeping an episode keeps its season.
        group.MapPut("/titles/{id}/keep", async (string id, HttpContext context,
            LibraryClock library, KeepStore keeps, KeepNotices notices, TimeProvider clock, ILogger<KeepStore> logger) =>
        {
            if (library.StayOf(id) is not { } stay) return NoSuchTitle();
            if (CinemaPerson.Of(context) is not { } person) return NoPerson();

            var already = keeps.Get(stay.Unit) is not null;
            keeps.Add(stay.Unit, person.UserId, clock.GetUtcNow());
            if (!already)
            {
                logger.LogInformation("{User} kept {Unit}{Via}.", person.DisplayName, stay.Unit, Through(person));
                await notices.TellAsync(stay.Unit, person.Actor, kept: true, context.RequestAborted);
            }

            return Results.Ok(library.StayOf(id));
        }).RequireAction(CinemaActions.Keep);

        group.MapDelete("/titles/{id}/keep", async (string id, HttpContext context,
            LibraryClock library, KeepStore keeps, KeepNotices notices, ILogger<KeepStore> logger) =>
        {
            if (library.StayOf(id) is not { } stay) return NoSuchTitle();
            if (CinemaPerson.Of(context) is not { } person) return NoPerson();

            if (keeps.Remove(stay.Unit))
            {
                logger.LogInformation("{User} let {Unit} go{Via}.", person.DisplayName, stay.Unit, Through(person));
                await notices.TellAsync(stay.Unit, person.Actor, kept: false, context.RequestAborted);
            }

            return Results.Ok(library.StayOf(id));
        }).RequireAction(CinemaActions.Keep);

        // What the hand-off deletes: the settled films whose time is up. It is asked by the one process
        // that writes the media roots, and a person has no business with it.
        group.MapGet("/library/due", (LibraryClock library) => Results.Ok(library.Due().ToArray()))
            .RequireService();
    }

    /// <summary>The client that carried an act, for the log line that records it.</summary>
    internal static string Through(CinemaPerson person) => person.Via is { } via ? $" through {via}" : "";

    private static IResult NoSuchTitle() =>
        Results.Json(new ErrorReply("The library holds no film under that id."), ApiJsonContext.Default.ErrorReply,
            statusCode: StatusCodes.Status404NotFound);

    /// <summary>An act a person performs, asked for by something that is nobody.</summary>
    private static IResult NoPerson() =>
        Results.Json(new ErrorReply("This is done as a person; sign in as one."), ApiJsonContext.Default.ErrorReply,
            statusCode: StatusCodes.Status403Forbidden);

    private static IResult Unanswered(string what) =>
        Results.Json(new ErrorReply(what), ApiJsonContext.Default.ErrorReply, statusCode: StatusCodes.Status502BadGateway);
}
