using System.Globalization;
using System.Text.RegularExpressions;
using TheKrystalShip.MovieBot.Api.Auth;
using TheKrystalShip.MovieBot.Api.Catalogue;
using TheKrystalShip.MovieBot.Api.People;
using TheKrystalShip.MovieBot.Catalogue;

namespace TheKrystalShip.MovieBot.Api.Votes;

/// <summary>
/// What people like and dislike. Everything here answers for whoever the token names: a vote is its
/// person's alone, and the service key names nobody and is refused.
/// </summary>
public static partial class VoteEndpoints
{
    public static void MapVotes(this WebApplication app)
    {
        app.MapGet("/api/me/votes", (HttpContext context, VoteStore votes, VotePresenter present, CatalogueState state) =>
        {
            if (PeopleEndpoints.Person(context) is not { } person) return NoPerson();
            if (state.Problem() is { } problem) return Unavailable(problem);
            return Results.Ok(present.Views(person.UserId, votes.For(person.UserId)));
        }).RequireAction(CinemaActions.Watch);

        // Casts the vote, or replaces the one the person has with its time. The subject has to be one the
        // catalogue lists, and a person is kept by the film database's id, which the catalogue's own does
        // not outlive a rebuild.
        app.MapPut("/api/me/votes/{kind}/{id}", (string kind, string id, SetVoteRequest body, HttpContext context, VoteStore votes,
            VotePresenter present, CatalogueQueries queries, CatalogueTitles titles, CatalogueState state, TimeProvider clock) =>
        {
            if (PeopleEndpoints.Person(context) is not { } person) return NoPerson();
            if (!VoteKinds.IsKnown(kind)) return Error("A vote is for a title, a person, a franchise or a company.", StatusCodes.Status400BadRequest);
            if (!IsWellFormed(kind, id)) return Error($"That is not the id of a {kind}.", StatusCodes.Status400BadRequest);
            if (body.Vote is not (1 or -1)) return Error("A vote is 1 to like or -1 to dislike.", StatusCodes.Status400BadRequest);
            if (state.Problem() is { } problem) return Unavailable(problem);

            var stored = kind switch
            {
                VoteKinds.Title => titles.Card(id) is not null ? id : null,
                VoteKinds.Person => queries.TmdbOfPerson(long.Parse(id, CultureInfo.InvariantCulture))?.ToString(CultureInfo.InvariantCulture),
                VoteKinds.Franchise => queries.FranchiseCards([id]).ContainsKey(id) ? id : null,
                _ => queries.Companies([long.Parse(id, CultureInfo.InvariantCulture)]).Count > 0 ? id : null,
            };
            if (stored is null) return Error($"The catalogue lists no {kind} {id}.", StatusCodes.Status404NotFound);

            var vote = votes.Set(person.UserId, kind, stored, body.Vote, clock.GetUtcNow());
            return Results.Ok(present.Views(person.UserId, [vote]).Single());
        }).RequireAction(CinemaActions.Watch);

        app.MapDelete("/api/me/votes/{kind}/{id}", (string kind, string id, HttpContext context, VoteStore votes,
            CatalogueQueries queries, CatalogueState state) =>
        {
            if (PeopleEndpoints.Person(context) is not { } person) return NoPerson();
            if (!VoteKinds.IsKnown(kind)) return Error("A vote is for a title, a person, a franchise or a company.", StatusCodes.Status400BadRequest);
            if (!IsWellFormed(kind, id)) return Error($"That is not the id of a {kind}.", StatusCodes.Status400BadRequest);

            var stored = id;
            if (kind == VoteKinds.Person)
            {
                if (state.Problem() is { } problem) return Unavailable(problem);
                stored = queries.TmdbOfPerson(long.Parse(id, CultureInfo.InvariantCulture))?.ToString(CultureInfo.InvariantCulture);
            }
            if (stored is not null) votes.Remove(person.UserId, kind, stored);
            return Results.NoContent();
        }).RequireAction(CinemaActions.Watch);
    }

    /// <summary>Whether the id is in the one form its kind's routes take, so the same subject is never stored under two spellings.</summary>
    private static bool IsWellFormed(string kind, string id) => kind switch
    {
        VoteKinds.Title => TitleQuery.IsImdbId(id),
        VoteKinds.Franchise => FranchiseId().IsMatch(id),
        _ => Digits().IsMatch(id),
    };

    [GeneratedRegex(@"^[1-9][0-9]{0,17}$")]
    private static partial Regex Digits();

    [GeneratedRegex(@"^(c[1-9][0-9]{0,17}|Q[1-9][0-9]{0,17})$")]
    private static partial Regex FranchiseId();

    private static IResult Error(string message, int status) =>
        Results.Json(new ErrorReply(message), ApiJsonContext.Default.ErrorReply, statusCode: status);

    private static IResult Unavailable(string problem) => Error(problem, StatusCodes.Status503ServiceUnavailable);

    private static IResult NoPerson() =>
        Error("This belongs to a person; sign in as one.", StatusCodes.Status403Forbidden);
}
