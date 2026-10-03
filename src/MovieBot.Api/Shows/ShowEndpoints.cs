using TheKrystalShip.MovieBot.Acquire.Search;
using TheKrystalShip.MovieBot.Acquire.Tracker;
using TheKrystalShip.MovieBot.Api.Acquisition;
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
        app.MapGet("/api/shows", (HttpContext context, TitleLibrary library, ShowStore shows,
            WatchRecord record, PersonDirectory people, SessionStore sessions, TimeProvider clock) =>
            Results.Ok(shows.Summaries(Titles(context, library, record, people, sessions, clock))));

        // A show by its IMDb id, in the library or not: the page somebody picks episodes on shows it
        // as fully as one on the shelf.
        app.MapGet("/api/shows/{imdbId}", async (string imdbId, HttpContext context, TitleLibrary library,
            ShowStore shows, LibraryClock stays, WatchRecord record, PersonDirectory people,
            SessionStore sessions, TimeProvider clock, CancellationToken ct) =>
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

            return Results.Ok(shows.View(imdbId, stored, episodes, stays.StayOf));
        });

        // The episodes either side of one the library holds, which is what a player's previous and
        // next move the room to. Absent at either end.
        app.MapGet("/api/titles/{id}/neighbours", (string id, TitleLibrary library, ShowStore shows) =>
        {
            if (library.Get(id) is not { } manifest) return Results.NotFound();
            if (manifest.Episode is null || manifest.Film?.ImdbId is not { } show)
                return Results.Ok(new EpisodeNeighbours(null, null));

            var titles = ShowViews.Ordered([.. library.List().Where(t => t.Film?.ImdbId == show)]);
            var at = titles.FindIndex(t => t.Id == id);
            var previous = at > 0 ? titles[at - 1] : null;
            var next = at >= 0 && at < titles.Count - 1 ? titles[at + 1] : null;
            return Results.Ok(new EpisodeNeighbours(
                previous, next,
                shows.EpisodeName(show, manifest.Episode),
                shows.EpisodeName(show, previous?.Episode),
                shows.EpisodeName(show, next?.Episode)));
        });

        // What the tracker offers for a show, season by season: packs and single episodes, each best
        // first, every one remembered so a download can name it.
        app.MapGet("/api/shows/{imdbId}/releases", async (string imdbId, IShowSearch search,
            OfferedReleases offered, CancellationToken ct) =>
        {
            if (!FilmStore.IsImdbId(imdbId))
                return Results.Json(new ErrorReply("That is not an IMDb id."), ApiJsonContext.Default.ErrorReply,
                    statusCode: StatusCodes.Status400BadRequest);
            try
            {
                var ranked = await search.ByShowAsync(imdbId, ct);
                offered.Remember(ranked.Seasons.SelectMany(s => s.Packs.Concat(s.Episodes.SelectMany(e => e.Releases))));
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
        }).AddEndpointFilter(AcquisitionEndpoints.ServiceOrMember);

        // The shows the title index offers for a name, for a search box: a show is fetched by its
        // seasons from its own page, which is where these lead.
        app.MapGet("/api/shows/find", async (string? q, Acquire.Imdb.ImdbClient index, CancellationToken ct) =>
            Results.Ok((await index.SuggestShowsAsync(q ?? "", ct))
                .Select(s => new Core.FilmView { ImdbId = s.ImdbId, Title = s.Title, Year = s.Year, Display = s.Display })
                .ToList()))
            .AddEndpointFilter(AcquisitionEndpoints.ServiceOrMember);

        app.MapMethods("/shows/{imdbId}/{file}", ["GET", "HEAD"], (string imdbId, string file, ShowStore store, HttpContext context) =>
        {
            if (store.ImageFile(imdbId, file) is not { } path) return Results.NotFound();
            context.Response.Headers.CacheControl = "public, max-age=86400";
            return Results.File(path, "image/jpeg", enableRangeProcessing: false);
        });
    }

    /// <summary>The library's titles with the asking person's plays, as the listing carries them.</summary>
    private static List<TitleSummary> Titles(HttpContext context, TitleLibrary library, WatchRecord record,
        PersonDirectory people, SessionStore sessions, TimeProvider clock)
    {
        var person = PeopleEndpoints.Person(context);
        var viewing = sessions.Viewing();
        var now = clock.GetUtcNow();
        return [.. library.List().Select(t => t.WithPlays(person, record, people, viewing, now))];
    }
}
