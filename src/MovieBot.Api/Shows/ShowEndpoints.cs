using TheKrystalShip.MovieBot.Acquire.Search;
using TheKrystalShip.MovieBot.Acquire.Tracker;
using TheKrystalShip.MovieBot.Api.Acquisition;
using TheKrystalShip.MovieBot.Api.Auth;
using TheKrystalShip.MovieBot.Api.Films;
using TheKrystalShip.MovieBot.Api.Library;
using TheKrystalShip.MovieBot.Api.People;
using TheKrystalShip.MovieBot.Api.Sessions;
using TheKrystalShip.MovieBot.Core;

namespace TheKrystalShip.MovieBot.Api.Shows;

public static class ShowEndpoints
{
    public static void MapShows(this WebApplication app)
    {
        // The shows the library holds, one entry each, with the asking person's place in each.
        app.MapGet("/api/shows", (HttpContext context, LibraryListing library, ShowStore shows,
            WatchRecord record, PersonDirectory people, SessionStore sessions, TimeProvider clock) =>
            Results.Ok(shows.Summaries(Titles(context, library, record, people, sessions, clock))))
            .RequireAction(CinemaActions.Watch);

        // A show by its IMDb id, in the library or not: the page somebody picks episodes on shows it
        // as fully as one on the shelf.
        app.MapGet("/api/shows/{imdbId}", async (string imdbId, HttpContext context, LibraryListing library,
            ShowStore shows, LibraryClock stays, WatchRecord record, PersonDirectory people,
            SessionStore sessions, FollowStore follows, TimeProvider clock, CancellationToken ct) =>
        {
            if (!FilmStore.IsImdbId(imdbId))
                return Results.Json(new ErrorReply("That is not an IMDb id."), ApiJsonContext.Default.ErrorReply,
                    statusCode: StatusCodes.Status400BadRequest);

            var episodes = Titles(context, library, record, people, sessions, clock)
                .Where(t => t.Episode is not null && t.Film?.ImdbId == imdbId)
                .ToList();
            var stored = await shows.GetAsync(imdbId, ct);

            if (stored?.Details is null && episodes.Count == 0)
                return stored is null
                    ? Results.Json(new ErrorReply("The film database could not be asked about this show."),
                        ApiJsonContext.Default.ErrorReply, statusCode: StatusCodes.Status503ServiceUnavailable)
                    : Results.Json(new ErrorReply("The film database has no show under that id."),
                        ApiJsonContext.Default.ErrorReply, statusCode: StatusCodes.Status404NotFound);

            var person = PeopleEndpoints.Person(context);
            return Results.Ok(shows.View(imdbId, stored, episodes, stays.StayOf,
                person is null ? null : follows.Get(person.UserId, imdbId)));
        }).RequireAction(CinemaActions.Watch);

        // The episodes either side of one the library holds, which is what a player's previous and
        // next move the room to. Absent at either end.
        app.MapGet("/api/titles/{id}/neighbours", async (string id, TitleLibrary library, LibraryListing listing, ShowStore shows,
            ShowOfferCache offers, CancellationToken ct) =>
        {
            if (library.Get(id) is not { } manifest) return Results.NotFound();
            if (manifest.Episode is null || manifest.Film?.ImdbId is not { } show)
                return Results.Ok(new EpisodeNeighbours(null, null));

            var titles = ShowViews.Ordered([.. listing.Titles().Where(t => t.Film?.ImdbId == show)]);
            var at = titles.FindIndex(t => t.Id == id);
            var previous = at > 0 ? titles[at - 1] : null;
            var next = at >= 0 && at < titles.Count - 1 ? titles[at + 1] : null;

            // The episode right after this one, when it is not the next one here and the tracker
            // offers it: what next fetches. A tracker that cannot be asked offers nothing.
            EpisodeIdentity? fetchable = null;
            if (shows.EpisodeAfter(show, manifest.Episode) is { } after
                && (next?.Episode is not { } here || here.Season != after.Season || here.Number != after.Number))
            {
                try
                {
                    if (ShowOfferCache.Offers(await offers.GetAsync(show, ct), new EpisodeNumber(after.Season, after.Number)))
                        fetchable = after;
                }
                catch (TrackerException)
                {
                }
            }

            return Results.Ok(new EpisodeNeighbours(
                previous, next,
                shows.EpisodeName(show, manifest.Episode),
                shows.EpisodeName(show, previous?.Episode),
                shows.EpisodeName(show, next?.Episode),
                fetchable,
                shows.EpisodeName(show, fetchable)));
        }).RequireAction(CinemaActions.Watch);

        // What the tracker offers for a show, season by season: packs and single episodes, each best
        // first, every one remembered so a download can name it.
        app.MapGet("/api/shows/{imdbId}/releases", async (string imdbId, ShowOfferCache cache, CancellationToken ct) =>
        {
            if (!FilmStore.IsImdbId(imdbId))
                return Results.Json(new ErrorReply("That is not an IMDb id."), ApiJsonContext.Default.ErrorReply,
                    statusCode: StatusCodes.Status400BadRequest);
            try
            {
                var ranked = await cache.GetAsync(imdbId, ct);
                return Results.Ok(new ShowOffers(
                    [.. ranked.Seasons.Select(s => new SeasonOffers(
                        s.Season,
                        [.. s.Packs.Select(OfferedReleases.View)],
                        [.. s.Episodes.Select(e => new EpisodeOffers(e.Episode, [.. e.Releases.Select(OfferedReleases.View)]))]))],
                    ranked.EmptyExplanation));
            }
            catch (TrackerException)
            {
                return Results.Json(new ErrorReply("The tracker is not answering."), ApiJsonContext.Default.ErrorReply,
                    statusCode: StatusCodes.Status502BadGateway);
            }
        }).RequireAction(CinemaActions.Download);

        // The shows the title index offers for a name, for a search box: a show is fetched by its
        // seasons from its own page, which is where these lead.
        app.MapGet("/api/shows/find", async (string? q, Acquire.Imdb.ImdbClient index, CancellationToken ct) =>
            Results.Ok((await index.SuggestShowsAsync(q ?? "", ct)).Select(FoundTitle.From).ToList()))
            .RequireAction(CinemaActions.Download);

        // The artwork kept beside each description, open like any other: an <img> carries no token.
        app.MapMethods("/shows/{imdbId}/{file}", ["GET", "HEAD"], (string imdbId, string file, ShowStore store, HttpContext context) =>
        {
            if (store.ImageFile(imdbId, file) is not { } path) return Results.NotFound();
            context.Response.Headers.CacheControl = "public, max-age=86400";
            return Results.File(path, "image/jpeg", enableRangeProcessing: false);
        }).AllowAnonymous();
    }

    /// <summary>The library's titles with the asking person's plays, as the listing carries them.</summary>
    private static List<TitleSummary> Titles(HttpContext context, LibraryListing library, WatchRecord record,
        PersonDirectory people, SessionStore sessions, TimeProvider clock)
    {
        var person = PeopleEndpoints.Person(context);
        var viewing = sessions.Viewing();
        var now = clock.GetUtcNow();
        return [.. library.Titles().Select(t => t.WithPlays(person, record, people, viewing, now))];
    }
}
