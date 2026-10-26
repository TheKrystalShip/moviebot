using System.ComponentModel;
using TheKrystalShip.Agent.Plugin;
using TheKrystalShip.MovieBot.Assistant.Api;
using TheKrystalShip.MovieBot.Core;

namespace TheKrystalShip.MovieBot.Assistant.Tools;

[AssistantTool("answer_plan", ToolClass.Staged, ToolLoading.OnDemand, Parallel = false, Order = 13)]
[Description("""
    Propose the person's answer to times of a plan they are in: in, maybe or can't. Nothing changes until they confirm.
    - plan: the plan's id from my_plans.
    - times: the times as my_plans wrote them. Leave out for every time still ahead.
    - answer: in, maybe or cant.
    """)]
internal sealed partial class AnswerPlanTool(CinemaApi api, TimeProvider clock) : AssistantTool<AnswerPlanTool.Args, Caller>
{
    public enum Answer { In, Maybe, Cant }

    public sealed record Args(
        [property: Description("The plan's id.")] string Plan,
        [property: Description("The answer.")] Answer Answer,
        [property: Description("Which times; default every one ahead.")] string[]? Times = null);

    protected override async ValueTask<ToolResult> RunAsync(Args args, ToolCall<Caller> call)
    {
        var person = api.As(call.Principal.AccessToken);
        TimeZoneInfo zone = PersonClock.Zone(call.Principal, clock);
        DateTimeOffset now = clock.GetUtcNow();
        PlanView plan = await person.PlanAsync(args.Plan.Trim(), call.CancellationToken);
        string name = TitleText.Named(plan.Title, plan.Card?.Year);
        PlanTimeView[] ahead = [.. plan.Times.Where(t => t.At > now)];

        PlanTimeView[] chosen = args.Times is { Length: > 0 } said
            ? [.. said.Select(t => Find(plan, PersonClock.Read(t, zone), zone))]
            : ahead;
        if (chosen.Length == 0)
            throw new ToolRefusal($"Every time of the plan for {name} has passed. Suggest another with change_plan.");
        if (chosen.FirstOrDefault(t => t.At <= now) is { } passed)
            throw new ToolRefusal($"{PersonClock.Write(passed.At, zone)} has passed; answer a time still ahead.");

        string answer = args.Answer switch { Answer.In => PlanAnswers.In, Answer.Maybe => PlanAnswers.Maybe, _ => PlanAnswers.Cant };
        string word = answer == PlanAnswers.Cant ? "can't" : answer;
        PlanTimeView[] change = [.. chosen.Where(t => t.Mine != answer).DistinctBy(t => t.At)];
        if (change.Length == 0)
            return ToolResult.Ok($"The person's answer to {(chosen.Length == 1 ? "that time" : "those times")} of the plan for {name} is already {word}. Nothing was proposed.");

        string when = string.Join(" · ", change.Select(t => PersonClock.Write(t.At, zone)));
        return Proposals.Propose(call, api, "answer", name, $"Answer {word} to {name}\n{when}", async (confirmed, _, token) =>
        {
            foreach (PlanTimeView time in change)
                await confirmed.AnswerPlanAsync(plan.Id, time.At, answer, token);
            return StagedResult.Ok($"You answered {word} to {when} for {name}. Everybody in the plan sees it.");
        });
    }

    /// <summary>The plan's time the person means, or a refusal naming the times it has.</summary>
    internal static PlanTimeView Find(PlanView plan, DateTimeOffset at, TimeZoneInfo zone) =>
        plan.Times.FirstOrDefault(t => PlanLimits.Minute(t.At) == PlanLimits.Minute(at))
        ?? throw new ToolRefusal($"The plan has no time {PersonClock.Write(at, zone)}. Its times are: "
                                 + string.Join(", ", plan.Times.Select(t => PersonClock.Write(t.At, zone))) + ".");
}
