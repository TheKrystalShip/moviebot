using TheKrystalShip.MovieBot.Api.Notifications;
using TheKrystalShip.MovieBot.Api.Sessions;
using TheKrystalShip.MovieBot.Core;
using TheKrystalShip.Notifications;

namespace TheKrystalShip.MovieBot.Api.Plans;

/// <summary>What picking a plan's time came to.</summary>
public enum FixOutcome
{
    Fixed,
    NoSuchTime,
    NoSuchPlan,
}

/// <summary>
/// Makes plans, brings people into them, takes the times they suggest and their answers, picks and
/// cancels, and tells the people in them. Nothing settles by itself: the maker picks a time and can pick
/// another, anybody in the plan suggests one, and only the maker calls it off, which deletes it. Everything that follows a change
/// (the notifications, the push to the people's open pages, the room's door) is written here once, so a
/// change cannot reach one door and miss the other.
/// </summary>
public sealed class PlanService(
    PlanStore plans, PlanNotices notices, PlanPush push, RoomDoors doors, TimeProvider clock,
    ILogger<PlanService> logger)
{
    public async Task<StoredPlan> MakeAsync(
        string madeBy, string imdbId, string kind, int? season, int? number, bool mayFetch,
        IReadOnlyList<DateTimeOffset> times, IReadOnlyList<string> people, CancellationToken ct)
    {
        var plan = plans.Make(Guid.NewGuid().ToString("N"), imdbId, kind, season, number, madeBy, mayFetch, times, people, clock.GetUtcNow());
        logger.LogInformation("{Maker} planned {Imdb} at {Times} with {Count} invited.", madeBy, imdbId, times.Count, people.Count);

        foreach (var user in people) await notices.InvitedAsync(plan, user, madeBy, ct);
        await push.ChangedAsync(plan);
        return plan;
    }

    /// <summary>
    /// Somebody in a plan brings another person into it, and the person is told. While the plan's room
    /// is open they are let into the room as well.
    /// </summary>
    public async Task<(InviteOutcome Outcome, Delivery? Delivery)> InviteAsync(
        string planId, string userId, Actor by, CancellationToken ct)
    {
        var outcome = plans.Invite(planId, userId, clock.GetUtcNow());
        if (outcome != InviteOutcome.Invited || plans.Get(planId) is not { } plan) return (outcome, null);

        logger.LogInformation("{Actor} invited {UserId} into plan {Plan}.", by.DisplayName, userId, planId);
        if (plan is { State: PlanStates.Open, RoomId: { } room }) await doors.AdmitAsync(room, userId, by, ct);
        var delivery = await notices.InvitedAsync(plan, userId, by.UserId, ct);
        await push.ChangedAsync(plan);
        return (outcome, delivery);
    }

    /// <summary>Somebody in a plan puts another time forward, <c>in</c> for it themselves; everybody else is told.</summary>
    public async Task<SuggestOutcome> SuggestAsync(string planId, DateTimeOffset at, Actor by, CancellationToken ct)
    {
        var outcome = plans.Suggest(planId, at, by.UserId, clock.GetUtcNow());
        if (outcome != SuggestOutcome.Suggested || plans.Get(planId) is not { } plan) return outcome;

        logger.LogInformation("{Actor} suggested {At:u} for plan {Plan}.", by.DisplayName, at, planId);
        await notices.SuggestedAsync(plan, at, by.UserId, plan.People.Where(u => u != by.UserId), ct);
        await push.ChangedAsync(plan);
        return outcome;
    }

    /// <summary>
    /// Stores an answer. Once the room is open, saying <c>in</c> or <c>maybe</c> to the picked time lets
    /// the person into it.
    /// </summary>
    public async Task<AnswerOutcome> AnswerAsync(string planId, DateTimeOffset at, Actor by, string answer, CancellationToken ct)
    {
        var outcome = plans.Answer(planId, at, by.UserId, answer, clock.GetUtcNow());
        if (outcome != AnswerOutcome.Stored || plans.Get(planId) is not { } plan) return outcome;

        if (plan is { State: PlanStates.Open, RoomId: { } room } && plan.FixedTime == at && answer is PlanAnswers.In or PlanAnswers.Maybe)
            await doors.AdmitAsync(room, by.UserId, by, ct);
        await push.ChangedAsync(plan);
        return outcome;
    }

    /// <summary>
    /// The maker picks a time, which must still be ahead, or moves the pick to another. The other people
    /// are told; once the room is open, everybody coming to the new time is let in.
    /// </summary>
    public async Task<FixOutcome> FixAsync(string planId, DateTimeOffset at, Actor by, CancellationToken ct)
    {
        if (plans.Get(planId) is not { State: PlanStates.Proposed or PlanStates.Fixed or PlanStates.Open } before) return FixOutcome.NoSuchPlan;
        if (!before.Times.Contains(at) || at <= clock.GetUtcNow()) return FixOutcome.NoSuchTime;
        if (before.FixedTime == at) return FixOutcome.Fixed;
        if (!plans.Fix(planId, at)) return FixOutcome.NoSuchPlan;

        var plan = plans.Get(planId)!;
        logger.LogInformation("{Maker} picked {At:u} for plan {Plan}.", plan.MadeBy, at, planId);
        if (plan is { State: PlanStates.Open, RoomId: { } room })
            foreach (var user in PlanRules.Coming(plan)) await doors.AdmitAsync(room, user, by, ct);
        await notices.FixedAsync(plan, before.FixedTime is not null, plan.People.Where(u => u != plan.MadeBy), ct);
        await push.ChangedAsync(plan);
        return FixOutcome.Fixed;
    }

    /// <summary>Deletes the plan the maker called off, and tells the people in it other than the maker.</summary>
    public async Task<bool> CancelAsync(string planId, CancellationToken ct)
    {
        if (plans.Get(planId) is not { } plan || !plans.Cancel(planId)) return false;
        logger.LogInformation("{Maker} cancelled plan {Plan}.", plan.MadeBy, planId);
        await notices.CancelledAsync(plan, plan.People.Where(u => u != plan.MadeBy), ct);
        await push.ChangedAsync(plan);
        return true;
    }
}
