using System.ComponentModel;
using TheKrystalShip.Agent.Plugin;
using TheKrystalShip.MovieBot.Assistant.Api;
using TheKrystalShip.MovieBot.Core;

namespace TheKrystalShip.MovieBot.Assistant.Tools;

[AssistantTool("change_plan", ToolClass.Staged, ToolLoading.OnDemand, Parallel = false, Order = 14)]
[Description("""
    Propose a change to a plan the person is in. Nothing changes until they confirm.
    - suggest: put another time forward; anybody in the plan may, and the person is in for it.
    - invite: bring somebody else into it; anybody in the plan may.
    - pick: pick one of the plan's times, or move the pick to another. Only whoever made the plan, and only when they ask.
    - cancel: cancel the plan, which deletes it and tells everybody in it. Only whoever made the plan, and only when they ask plainly.
    - plan: the plan's id from my_plans. at: the time, for suggest and pick, YYYY-MM-DD HH:mm in the person's clock. person: the name, for invite.
    """)]
internal sealed partial class ChangePlanTool(CinemaApi api, TimeProvider clock) : AssistantTool<ChangePlanTool.Args, Caller>
{
    public enum Change { Suggest, Invite, Pick, Cancel }

    public sealed record Args(
        [property: Description("The plan's id.")] string Plan,
        [property: Description("What to change.")] Change Change,
        [property: Description("For suggest and pick: YYYY-MM-DD HH:mm.")] string? At = null,
        [property: Description("For invite: a name.")] string? Person = null);

    protected override async ValueTask<ToolResult> RunAsync(Args args, ToolCall<Caller> call)
    {
        var person = api.As(call.Principal.AccessToken);
        CancellationToken ct = call.CancellationToken;
        TimeZoneInfo zone = PersonClock.Zone(call.Principal, clock);
        DateTimeOffset now = clock.GetUtcNow();
        PlanView plan = await person.PlanAsync(args.Plan.Trim(), ct);
        string name = TitleText.Named(plan.Title, plan.Card?.Year);
        string maker = plan.MadeBy.Name ?? "whoever made it";

        DateTimeOffset At(string verb) => args.At is { Length: > 0 } at
            ? PlanLimits.Minute(PersonClock.Read(at, zone))
            : throw new ToolRefusal($"Say the time to {verb}, as YYYY-MM-DD HH:mm.");

        switch (args.Change)
        {
            case Change.Suggest:
            {
                DateTimeOffset at = At("suggest");
                if (PersonClock.NotAhead(at, now, zone) is { } why) throw new ToolRefusal(why);
                if (plan.Times.Any(t => PlanLimits.Minute(t.At) == at))
                    throw new ToolRefusal($"{PersonClock.Write(at, zone)} is already one of the plan's times; answer it with answer_plan.");
                if (plan.Times.Count(t => t.At > now) >= PlanLimits.MaxTimes)
                    throw new ToolRefusal($"The plan already holds {PlanLimits.MaxTimes} times ahead, the most it can.");
                string when = PersonClock.Write(at, zone);
                return Proposals.Propose(call, api, "suggest_time", name,
                    $"Suggest {when} for {name}\nEverybody in the plan is told, and you are in for it.", async (confirmed, _, token) =>
                    {
                        await confirmed.SuggestPlanTimeAsync(plan.Id, at, token);
                        return StagedResult.Ok($"{when} is put forward for {name}. Everybody in the plan is told and answers it.");
                    });
            }

            case Change.Invite:
            {
                if (args.Person is not { Length: > 0 } said) throw new ToolRefusal("Say who to invite.");
                List<PersonView> found = await PeopleLookup.ResolveAsync(person, [said], call.Principal, ct);
                if (found is not [var who]) throw new ToolRefusal("The person is in the plan already.");
                if (plan.People.Any(p => p.UserId == who.UserId)) throw new ToolRefusal($"{who.Name} is in the plan already.");
                if (plan.People.Count(p => !p.Maker) >= PlanLimits.MaxInvited)
                    throw new ToolRefusal($"The plan already invites {PlanLimits.MaxInvited} people, the most it can.");
                return Proposals.Propose(call, api, "invite", who.Name,
                    $"Invite {who.Name} to watch {name}\n{PlanText.Readiness(plan)}", async (confirmed, _, token) =>
                    {
                        InviteReply reply = await confirmed.InviteToPlanAsync(plan.Id, who.UserId, token);
                        return StagedResult.Ok($"{who.Name} is in the plan to watch {name}, and is told"
                                               + (reply.Delivery == "push" ? " on their devices." : " in their notifications."));
                    });
            }

            case Change.Pick:
            {
                if (!plan.Mine) throw new ToolRefusal($"Only {maker}, who made the plan, picks its time. Suggest one instead.");
                PlanTimeView time = AnswerPlanTool.Find(plan, At("pick"), zone);
                if (time.At <= now) throw new ToolRefusal($"{PersonClock.Write(time.At, zone)} has passed; pick a time still ahead.");
                if (plan.FixedTime == time.At) throw new ToolRefusal($"{PersonClock.Write(time.At, zone)} is already the picked time.");
                string when = PersonClock.Write(time.At, zone);
                int maybe = time.Answers.Count(a => a.Answer == PlanAnswers.Maybe);
                return Proposals.Propose(call, api, "pick_time", name,
                    $"Pick {when} for {name}\n{time.In} in, {maybe} maybe for that time. Everybody is told.", async (confirmed, _, token) =>
                    {
                        await confirmed.FixPlanAsync(plan.Id, time.At, token);
                        return StagedResult.Ok($"{when} is the time for {name}. Everybody in the plan is told, and reminded before it starts.");
                    });
            }

            default:
            {
                if (!plan.Mine) throw new ToolRefusal($"Only {maker}, who made the plan, can cancel it.");
                return Proposals.Propose(call, api, "cancel_plan", name,
                    $"Cancel the plan to watch {name}\nIt is deleted, and everybody in it is told.", async (confirmed, _, token) =>
                    {
                        await confirmed.CancelPlanAsync(plan.Id, token);
                        return StagedResult.Ok($"The plan to watch {name} is cancelled, and everybody in it is told.");
                    });
            }
        }
    }
}
