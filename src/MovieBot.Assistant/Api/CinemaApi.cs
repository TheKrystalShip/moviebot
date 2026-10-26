using System.Net;
using TheKrystalShip.Agent.Plugin;
using TheKrystalShip.MovieBot.Catalogue;
using TheKrystalShip.MovieBot.Client;
using TheKrystalShip.MovieBot.Core;

namespace TheKrystalShip.MovieBot.Assistant.Api;

/// <summary>
/// Cinema's API, asked as the person whose token a turn carries, through the client every program that
/// acts for a person shares (<c>MovieBotApiClient</c>) and in the shapes the API itself publishes
/// (MovieBot.Core). This adds one thing: a refusal worded for the model, as a <see cref="ToolRefusal"/>.
/// The service holds no state of the API's and no credential of its own.
/// </summary>
public sealed class CinemaApi(MovieBotApiClient client)
{
    /// <summary>The API as the person <paramref name="accessToken"/> names.</summary>
    public PersonApi As(string accessToken) => new(client.As(accessToken));

    /// <summary>The API's answer to a question as a person asks it.</summary>
    public sealed class PersonApi(MovieBotApiClient person)
    {
        /// <summary>A list of titles: every search, discovery, ranking, filmography and window of dates is one.</summary>
        public Task<CardPage> TitlesAsync(TitleFilter filter, CancellationToken ct) => Ask(() => person.TitlesAsync(filter, ct));

        /// <summary>A list the API may decline as unanswerable (not found, bad request): the answer, or why there is none.</summary>
        public async Task<Answer<CardPage>> TryTitlesAsync(TitleFilter filter, CancellationToken ct)
        {
            try
            {
                return new Answer<CardPage>(await person.TitlesAsync(filter, ct), null);
            }
            catch (MovieBotApiException e) when (e.Status is HttpStatusCode.NotFound or HttpStatusCode.BadRequest)
            {
                return new Answer<CardPage>(null, Words(e));
            }
            catch (MovieBotApiException e)
            {
                throw new ToolRefusal(Words(e));
            }
        }

        /// <summary>A title's page, film or show.</summary>
        public Task<TitlePage> TitleAsync(string imdbId, CancellationToken ct) => Ask(() => person.TitleAsync(imdbId, ct));

        /// <summary>Words resolved to what the list filters take: genres, people, keywords, companies, Wikidata items, franchises.</summary>
        public Task<List<Term>> TermsAsync(string words, CancellationToken ct) => Ask(() => person.TermsAsync(words, ct));

        /// <summary>The catalogue's facets over every title, which name the genres and the original languages it holds.</summary>
        public Task<Facets> FacetsAsync(CancellationToken ct) => Ask(() => person.FacetsAsync(ct));

        /// <summary>What the asking person has of a show: where they pick up and their follow.</summary>
        public Task<PersonShowView> PersonShowAsync(string imdbId, CancellationToken ct) => Ask(() => person.PersonShowAsync(imdbId, ct));

        /// <summary>The films people are waiting on, most waiters first; a person without the right to wait on films is refused in words the model can read.</summary>
        public async Task<List<AnticipatedView>> AnticipatedAsync(CancellationToken ct)
        {
            try
            {
                return await person.AnticipatedAsync(ct);
            }
            catch (MovieBotAccessDeniedException)
            {
                throw new ToolRefusal("This account may not wait on films in Cinema, so the list of films people are waiting for is not open to the person.");
            }
            catch (MovieBotApiException e)
            {
                throw new ToolRefusal(Words(e));
            }
        }

        public Task<List<HistoryEntry>> HistoryAsync(CancellationToken ct) => Ask(() => person.HistoryAsync(ct));

        public async Task<List<WantView>> WantsAsync(CancellationToken ct) => [.. await Ask(() => person.ListWantsAsync(ct))];

        public Task<List<FollowView>> FollowsAsync(CancellationToken ct) => Ask(() => person.FollowsAsync(ct));

        /// <summary>The titles the person wants that somebody else visibly wants too, with those people.</summary>
        public Task<List<TogetherView>> TogetherAsync(CancellationToken ct) => Ask(() => person.TogetherAsync(ct));

        /// <summary>The people with a visible want on a title, the person left out.</summary>
        public Task<List<WanterView>> WantersAsync(string imdbId, CancellationToken ct) => Ask(() => person.WantersAsync(imdbId, ct));

        /// <summary>Makes the person's want of a title, or changes whether it is visible.</summary>
        public async Task<SetWantReply> SetWantAsync(string imdbId, string kind, bool visible, CancellationToken ct) =>
            await Ask(() => person.SetWantAsync(imdbId, new SetWantRequest { Kind = kind, Visible = visible }, ct))
            ?? throw new ToolRefusal($"The catalogue lists no {kind} under {imdbId}.");

        /// <summary>Ends the person's want of a title; false when they had none.</summary>
        public Task<bool> RemoveWantAsync(string imdbId, CancellationToken ct) => Ask(() => person.RemoveWantAsync(imdbId, ct));

        /// <summary>Everybody the server has a name for but the person, most recently seen first.</summary>
        public Task<List<PersonView>> PeopleAsync(CancellationToken ct) => Ask(() => person.PeopleAsync(null, ct));

        /// <summary>Whether a film can be had; null when it cannot be asked, which leaves the question to the API when the act is made.</summary>
        public async Task<FilmAvailability?> AvailabilityAsync(string imdbId, CancellationToken ct)
        {
            try
            {
                return await person.FilmAvailabilityAsync(imdbId, ct);
            }
            catch (MovieBotApiException)
            {
                return null;
            }
        }

        /// <summary>The plans the person is in, soonest first.</summary>
        public Task<List<PlanView>> PlansAsync(CancellationToken ct) => Ask(() => person.PlansAsync(ct));

        /// <summary>One plan the person is in.</summary>
        public async Task<PlanView> PlanAsync(string id, CancellationToken ct) =>
            await Ask(() => person.PlanAsync(id, ct))
            ?? throw new ToolRefusal($"The person is in no plan with the id {id}; my_plans lists the ones they are in.");

        public Task<PlanView> MakePlanAsync(MakePlanRequest request, CancellationToken ct) => Ask(() => person.MakePlanAsync(request, ct));

        public Task<PlanView> SuggestPlanTimeAsync(string id, DateTimeOffset at, CancellationToken ct) => Ask(() => person.SuggestPlanTimeAsync(id, at, ct));

        public Task<PlanView> AnswerPlanAsync(string id, DateTimeOffset at, string answer, CancellationToken ct) =>
            Ask(() => person.AnswerPlanAsync(id, at, answer, ct));

        public Task<InviteReply> InviteToPlanAsync(string id, string userId, CancellationToken ct) => Ask(() => person.InviteToPlanAsync(id, userId, ct));

        public Task<PlanView> FixPlanAsync(string id, DateTimeOffset at, CancellationToken ct) => Ask(() => person.FixPlanAsync(id, at, ct));

        public Task CancelPlanAsync(string id, CancellationToken ct) => Ask(async () =>
        {
            await person.CancelPlanAsync(id, ct);
            return true;
        });

        /// <summary>The client's answer, or its failure as a refusal the model reads.</summary>
        private static async Task<T> Ask<T>(Func<Task<T>> ask)
        {
            try
            {
                return await ask();
            }
            catch (MovieBotApiException e)
            {
                throw new ToolRefusal(Words(e));
            }
        }
    }

    /// <summary>A read that the API may have declined: the answer, or the sentence that says why there is none.</summary>
    public readonly record struct Answer<T>(T? Value, string? Why) where T : class;

    /// <summary>
    /// What the model reads for a failed call: the API's own sentence where it refused with one about this
    /// request, else what the status means for the person. A refusal for want of an action is said for the
    /// person, never as the action's id.
    /// </summary>
    internal static string Words(MovieBotApiException e) => e.Status switch
    {
        null => "Cinema could not be reached, so nothing is known about this.",
        HttpStatusCode.Unauthorized => "Cinema did not accept this sign-in. The person has to sign in again.",
        HttpStatusCode.Forbidden => e.Said is { Length: > 0 } said && !said.StartsWith("Your account", StringComparison.Ordinal)
            ? said
            : "This account does not let the person do that in Cinema.",
        HttpStatusCode.ServiceUnavailable => e.Said is { Length: > 0 } said
            ? $"The film database could not be read just now ({said}), so nothing is known about this."
            : "The film database could not be read just now, so nothing is known about this.",
        _ => e.Said is { Length: > 0 } said ? said : $"Cinema answered {(int)e.Status}.",
    };
}
