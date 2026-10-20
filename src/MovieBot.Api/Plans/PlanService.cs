using Microsoft.Extensions.Options;
using TheKrystalShip.MovieBot.Core;

namespace TheKrystalShip.MovieBot.Api.Plans;

/// <summary>What fixing a plan came to.</summary>
public enum FixOutcome
{
    Fixed,
    NoSuchTime,
    NotProposed,
}

/// <summary>
/// Makes plans, takes their answers, fixes and cancels them, and tells the people in them. A time is
/// fixed by the maker, or by the answer that brings it to <see cref="PlanOptions.FixAt"/> people saying
/// <c>in</c>: the first to get there. Everything that follows a change (the notifications, the push to
/// the people's open pages) is written here once, so a change cannot reach one door and miss the other.
/// </summary>
public sealed class PlanService(
    PlanStore plans, PlanNotices notices, PlanPush push, IOptions<PlanOptions> options, TimeProvider clock,
    ILogger<PlanService> logger)
{
    public async Task<StoredPlan> MakeAsync(
        string madeBy, string imdbId, string kind, int? season, int? number, bool mayFetch,
        IReadOnlyList<DateTimeOffset> times, IReadOnlyList<string> people, CancellationToken ct)
    {
        var plan = plans.Make(Guid.NewGuid().ToString("N"), imdbId, kind, season, number, madeBy, mayFetch, times, people, clock.GetUtcNow());
        logger.LogInformation("{Maker} planned {Imdb} at {Times} with {Count} invited.", madeBy, imdbId, times.Count, people.Count);

        foreach (var user in people) await notices.InvitedAsync(plan, user, ct);

        foreach (var at in times)
            if (PlanRules.AutoFix(plan, at, options.Value.FixAt) is { } fix && plans.Fix(plan.Id, fix))
            {
                plan = plans.Get(plan.Id)!;
                await notices.FixedAsync(plan, plan.People.Where(u => u != madeBy), ct);
                break;
            }

        await push.ChangedAsync(plan);
        return plan;
    }

    /// <summary>Stores an answer, and fixes the time when it brings it to <see cref="PlanOptions.FixAt"/>.</summary>
    public async Task<AnswerOutcome> AnswerAsync(string planId, DateTimeOffset at, string userId, string answer, CancellationToken ct)
    {
        var outcome = plans.Answer(planId, at, userId, answer, clock.GetUtcNow());
        if (outcome != AnswerOutcome.Stored || plans.Get(planId) is not { } plan) return outcome;

        if (PlanRules.AutoFix(plan, at, options.Value.FixAt) is { } fix && plans.Fix(planId, fix))
        {
            plan = plans.Get(planId)!;
            logger.LogInformation("Plan {Plan} fixed at {At:u}: {Count} said in.", planId, fix, PlanRules.In(plan, fix));
            await notices.FixedAsync(plan, plan.People, ct);
        }
        await push.ChangedAsync(plan);
        return outcome;
    }

    /// <summary>The maker settles on a time, which must still be ahead. The other people are told.</summary>
    public async Task<FixOutcome> FixAsync(string planId, DateTimeOffset at, CancellationToken ct)
    {
        if (plans.Get(planId) is not { State: PlanStates.Proposed } before) return FixOutcome.NotProposed;
        if (!before.Times.Contains(at) || at <= clock.GetUtcNow()) return FixOutcome.NoSuchTime;
        if (!plans.Fix(planId, at)) return FixOutcome.NotProposed;

        var plan = plans.Get(planId)!;
        logger.LogInformation("{Maker} fixed plan {Plan} at {At:u}.", plan.MadeBy, planId, at);
        await notices.FixedAsync(plan, plan.People.Where(u => u != plan.MadeBy), ct);
        await push.ChangedAsync(plan);
        return FixOutcome.Fixed;
    }

    /// <summary>Ends a plan before it opens, and tells the people in it other than the one who ended it.</summary>
    public async Task<bool> CancelAsync(string planId, CancellationToken ct)
    {
        if (!plans.Cancel(planId) || plans.Get(planId) is not { } plan) return false;
        logger.LogInformation("{Maker} cancelled plan {Plan}.", plan.MadeBy, planId);
        await notices.CancelledAsync(plan, plan.People.Where(u => u != plan.MadeBy), ct);
        await push.ChangedAsync(plan);
        return true;
    }
}
