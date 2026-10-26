using TheKrystalShip.MovieBot.Acquire.Tracker;
using TheKrystalShip.MovieBot.Api.Acquisition;
using TheKrystalShip.MovieBot.Api.Auth;
using TheKrystalShip.MovieBot.Api.Catalogue;
using TheKrystalShip.MovieBot.Api.Library;
using TheKrystalShip.MovieBot.Api.People;
using TheKrystalShip.MovieBot.Api.Sessions;
using TheKrystalShip.MovieBot.Catalogue;
using TheKrystalShip.MovieBot.Core;

namespace TheKrystalShip.MovieBot.Api.Shows;

public static class ShowEndpoints
{
    public static void MapShows(this WebApplication app)
    {
        // The shows the library holds, one entry each, with the asking person's place in each.
        app.MapGet("/api/shows", (HttpContext context, LibraryListing library, CatalogueTitles catalogue,
            WatchRecord record, PersonDirectory people, SessionStore sessions, TimeProvider clock) =>
            Results.Ok(catalogue.Summaries(Titles(context, library, record, people, sessions, clock))))
            .RequireAction(CinemaActions.Watch);

        // What the asking person has of a show, in the library or not: its episodes numbered as the
        // releases number them, which the library holds and how far they got, their place and their follow.
        app.MapGet("/api/me/shows/{imdbId}", (string imdbId, HttpContext context, LibraryListing library, CatalogueTitles catalogue,
            WatchRecord record, PersonDirectory people, SessionStore sessions, FollowStore follows, TimeProvider clock) =>
        {
            if (!TitleQuery.IsImdbId(imdbId))
                return Results.Json(new ErrorReply("That is not an IMDb id."), ApiJsonContext.Default.ErrorReply, statusCode: StatusCodes.Status400BadRequest);
            var episodes = Titles(context, library, record, people, sessions, clock)
                .Where(t => t.Episode is not null && t.Film?.ImdbId == imdbId)
                .ToList();
            if (catalogue.Card(imdbId) is not { Kind: "show" } && episodes.Count == 0)
                return Results.Json(new ErrorReply("Neither the catalogue nor the library holds a show under that id."), ApiJsonContext.Default.ErrorReply,
                    statusCode: StatusCodes.Status404NotFound);
            var person = PeopleEndpoints.Person(context);
            var marked = person is null ? null : record.Marks(person.UserId)
                .Where(m => m.What.ImdbId == imdbId && !m.What.IsFilm)
                .Select(m => (m.What.Season, m.What.Number))
                .ToHashSet();
            return Results.Ok(catalogue.View(imdbId, episodes, person is null ? null : follows.Get(person.UserId, imdbId), marked));
        }).RequireAction(CinemaActions.Watch);

        // What the tracker offers for a show, season by season: packs and single episodes, each best
        // first, every one remembered so a download can name it.
        app.MapGet("/api/shows/{imdbId}/releases", async (string imdbId, ShowOfferCache cache, CancellationToken ct) =>
        {
            if (!TitleQuery.IsImdbId(imdbId))
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

    }

    /// <summary>The library's titles with the asking person's plays, as the listing carries them.</summary>
    private static List<TitleSummary> Titles(HttpContext context, LibraryListing library, WatchRecord record,
        PersonDirectory people, SessionStore sessions, TimeProvider clock)
    {
        var person = PeopleEndpoints.Person(context);
        var viewing = sessions.Viewing();
        var now = clock.GetUtcNow();
        return library.Titles().WithPlays(person, record, people, viewing, now);
    }
}
