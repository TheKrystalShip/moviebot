using System.ComponentModel;
using System.Text;
using TheKrystalShip.Agent.Plugin;
using TheKrystalShip.MovieBot.Assistant.Api;
using TheKrystalShip.MovieBot.Core;

namespace TheKrystalShip.MovieBot.Assistant.Tools;

[AssistantTool("my_plans", ToolClass.Read, ResultLimit = 12000, Order = 10)]
[Description("""
    The watch parties the asking person is in, soonest first.
    - Each has the title, who made it and whether that is the person, the people in it, every time put forward with who said in, maybe or can't to it, the time picked if one is, whether the title is ready, and whether its room is open.
    - Use for "when is movie night", "who is coming", "did anyone answer", and to find the plan a change is about.
    """)]
internal sealed partial class MyPlansTool(CinemaApi api, TimeProvider clock) : AssistantTool<MyPlansTool.Args, Caller>
{
    public sealed record Args();

    protected override async ValueTask<ToolResult> RunAsync(Args args, ToolCall<Caller> call)
    {
        List<PlanView> plans = await api.As(call.Principal.AccessToken).PlansAsync(call.CancellationToken);
        if (plans.Count == 0)
            return ToolResult.Ok("The person is in no plan to watch something together.");

        TimeZoneInfo zone = PersonClock.Zone(call.Principal, clock);
        DateTimeOffset now = clock.GetUtcNow();
        var text = new StringBuilder("The plans the person is in, soonest first. Times are in the person's clock.\n");
        foreach (PlanView plan in plans)
            text.Append(PlanText.Describe(plan, call.Principal, zone, now)).Append('\n');

        return ToolResult.Ok(text.ToString().TrimEnd(), PlansCard.Of(plans));
    }
}
