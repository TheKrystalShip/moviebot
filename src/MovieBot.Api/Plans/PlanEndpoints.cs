using System.Globalization;
using TheKrystalShip.MovieBot.Api.Auth;
using TheKrystalShip.MovieBot.Api.Films;
using TheKrystalShip.MovieBot.Api.Library;
using TheKrystalShip.MovieBot.Api.People;
using TheKrystalShip.MovieBot.Api.Shows;
using TheKrystalShip.MovieBot.Api.Wants;
using TheKrystalShip.MovieBot.Core;

namespace TheKrystalShip.MovieBot.Api.Plans;

/// <summary>
/// Plans to watch something together. A plan is shown only to its maker and the people invited: for
/// anybody else a plan is answered as if it did not exist. The person is always the token's.
/// </summary>
public static class PlanEndpoints
{
    public static void MapPlans(this WebApplication app)
    {
        app.MapPost("/api/plans", async (MakePlanRequest body, HttpContext context, PlanService service, PlanPresenter present,
            PersonDirectory people, FilmStore films, ShowStore shows, LibraryListing library, People.WatchRecord record,
            TimeProvider clock, CancellationToken ct) =>
        {
            if (PeopleEndpoints.Person(context) is not { } person) return NoPerson();
            if (body.ImdbId is not { } imdb || !FilmStore.IsImdbId(imdb)) return Error("That is not an IMDb id.", StatusCodes.Status400BadRequest);
            if (!WantKinds.IsKnown(body.Kind)) return Error("A plan is for a film or a show.", StatusCodes.Status400BadRequest);

            var (times, refusal) = PlanRules.Times(body.Times, clock.GetUtcNow());
            if (refusal is not null) return Error(refusal, StatusCodes.Status400BadRequest);

            var invited = (body.People ?? []).Where(u => u != person.UserId).Distinct(StringComparer.Ordinal).ToList();
            if (invited.Count > PlanRules.MaxInvited)
                return Error($"A plan invites at most {PlanRules.MaxInvited} people.", StatusCodes.Status400BadRequest);
            if (invited.Any(u => people.NameOf(u) is null))
                return Error("Nobody by that id can be invited.", StatusCodes.Status404NotFound);

            var described = body.Kind == WantKinds.Show
                ? await shows.GetAsync(imdb, ct) is { } show ? show.Details is not null : (bool?)null
                : await films.GetAsync(imdb, ct) is { } film ? film.Details is not null : null;
            if (described is null)
                return Error("The film database could not be asked about this title.", StatusCodes.Status503ServiceUnavailable);
            if (described is false)
                return Error($"The film database has no {body.Kind} under that id.", StatusCodes.Status404NotFound);

            int? season = null, number = null;
            if (body.Kind == WantKinds.Show)
            {
                var at = body.Season is { } s && body.Number is { } n ? new WantEpisode { Season = s, Episode = n }
                    : Wants.ShowProgress.Next(shows.Held(imdb),
                        Wants.ShowProgress.Finished(library.Titles(), record.Entries(person.UserId)).GetValueOrDefault(imdb));
                if (at is null) return Error("That show has no episode to start at.", StatusCodes.Status400BadRequest);
                (season, number) = (at.Season, at.Episode);
            }

            var plan = await service.MakeAsync(person.UserId, imdb, body.Kind!, season, number,
                person.Holds(CinemaActions.Download), times, invited, ct);
            return Results.Ok(await present.ViewAsync(person.UserId, plan, ct));
        }).RequireAction(CinemaActions.Watch);

        app.MapGet("/api/me/plans", async (HttpContext context, PlanStore plans, PlanPresenter present, TimeProvider clock, CancellationToken ct) =>
            PeopleEndpoints.Person(context) is { } person
                ? Results.Ok(await present.ViewsAsync(person.UserId, plans.For(person.UserId, clock.GetUtcNow()), ct))
                : NoPerson())
            .RequireAction(CinemaActions.Watch);

        app.MapGet("/api/plans/{id}", async (string id, HttpContext context, PlanStore plans, PlanPresenter present, CancellationToken ct) =>
        {
            if (PeopleEndpoints.Person(context) is not { } person) return NoPerson();
            return Of(plans, id, person.UserId) is { } plan
                ? Results.Ok(await present.ViewAsync(person.UserId, plan, ct))
                : Missing();
        }).RequireAction(CinemaActions.Watch);

        app.MapPut("/api/plans/{id}/answers/{at}", async (string id, string at, PlanAnswerRequest body, HttpContext context,
            PlanStore plans, PlanService service, PlanPresenter present, CancellationToken ct) =>
        {
            if (PeopleEndpoints.Person(context) is not { } person) return NoPerson();
            if (Of(plans, id, person.UserId) is null) return Missing();
            if (!PlanAnswers.IsKnown(body.Answer)) return Error("Answer in, maybe or cant.", StatusCodes.Status400BadRequest);
            if (!TryInstant(at, out var time)) return Error("That is not a time.", StatusCodes.Status400BadRequest);

            switch (await service.AnswerAsync(id, time, person.UserId, body.Answer!, ct))
            {
                case AnswerOutcome.NoSuchTime: return Error("The plan has no such time.", StatusCodes.Status404NotFound);
                case AnswerOutcome.Closed: return Error("The plan has opened; its answers are in.", StatusCodes.Status409Conflict);
                case AnswerOutcome.NotInPlan: return Missing();
                default: return Results.Ok(await present.ViewAsync(person.UserId, plans.Get(id)!, ct));
            }
        }).RequireAction(CinemaActions.Watch);

        app.MapPost("/api/plans/{id}/fix", async (string id, FixPlanRequest body, HttpContext context,
            PlanStore plans, PlanService service, PlanPresenter present, CancellationToken ct) =>
        {
            if (PeopleEndpoints.Person(context) is not { } person) return NoPerson();
            if (Of(plans, id, person.UserId) is not { } plan) return Missing();
            if (plan.MadeBy != person.UserId) return Error("Only the person who made the plan fixes it.", StatusCodes.Status403Forbidden);

            return await service.FixAsync(id, PlanRules.Minute(body.At), ct) switch
            {
                FixOutcome.NoSuchTime => Error("The plan has no such time ahead.", StatusCodes.Status404NotFound),
                FixOutcome.NotProposed => Error("The plan is settled already.", StatusCodes.Status409Conflict),
                _ => Results.Ok(await present.ViewAsync(person.UserId, plans.Get(id)!, ct)),
            };
        }).RequireAction(CinemaActions.Watch);

        app.MapDelete("/api/plans/{id}", async (string id, HttpContext context, PlanStore plans, PlanService service, CancellationToken ct) =>
        {
            if (PeopleEndpoints.Person(context) is not { } person) return NoPerson();
            if (Of(plans, id, person.UserId) is not { } plan) return Missing();
            if (plan.MadeBy != person.UserId) return Error("Only the person who made the plan cancels it.", StatusCodes.Status403Forbidden);

            return await service.CancelAsync(id, ct)
                ? Results.NoContent()
                : Error("The plan has opened and cannot be cancelled.", StatusCodes.Status409Conflict);
        }).RequireAction(CinemaActions.Watch);
    }

    /// <summary>The plan, when the person is in it; a plan of other people's is no plan to them.</summary>
    private static StoredPlan? Of(PlanStore plans, string id, string userId) =>
        plans.Get(id) is { } plan && plan.People.Contains(userId) ? plan : null;

    private static bool TryInstant(string text, out DateTimeOffset at)
    {
        if (!DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out at)) return false;
        at = PlanRules.Minute(at);
        return true;
    }

    private static IResult Missing() => Error("No such plan.", StatusCodes.Status404NotFound);

    private static IResult Error(string message, int status) =>
        Results.Json(new ErrorReply(message), ApiJsonContext.Default.ErrorReply, statusCode: status);

    private static IResult NoPerson() =>
        Error("This belongs to a person; sign in as one.", StatusCodes.Status403Forbidden);
}
