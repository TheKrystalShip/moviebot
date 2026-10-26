using TheKrystalShip.Agent.Plugin;
using TheKrystalShip.MovieBot.Assistant.Api;

namespace TheKrystalShip.MovieBot.Assistant.Tools;

/// <summary>
/// An action a tool proposes rather than takes. It runs once the person confirms, as the person who
/// confirms, with the token they present then, so what the API lets them do is decided at that moment; a
/// refusal is the outcome they read.
/// </summary>
internal static class Proposals
{
    /// <summary>
    /// How the reply is to put it, after the proposal's own sentence: a model that has proposed tends to say
    /// it did the thing, and the guard cannot tell that apart from a turn that proposed it.
    /// </summary>
    private const string HowToSay =
        " Tell the person what you proposed and that it waits for them to confirm it, as \"I've proposed ...\" or \"Confirm to ...\"; never \"I've added\", \"I've made\" or \"done\".";

    public static ToolResult Propose(
        ToolCall<Caller> call, CinemaApi api, string verb, string subject, string summary,
        Func<CinemaApi.PersonApi, Caller, CancellationToken, Task<StagedResult>> change)
    {
        ToolResult proposed = Stage(call, api, verb, subject, summary, change);
        return proposed with { Text = proposed.Text + HowToSay };
    }

    private static ToolResult Stage(
        ToolCall<Caller> call, CinemaApi api, string verb, string subject, string summary,
        Func<CinemaApi.PersonApi, Caller, CancellationToken, Task<StagedResult>> change) =>
        call.Stage(verb, subject, summary, async run =>
        {
            if (run.PrincipalAs<Caller>() is not { } who)
                return StagedResult.Fail("Nobody is signed in, so nothing was changed.");
            try
            {
                return await change(api.As(who.AccessToken), who, run.CancellationToken);
            }
            catch (ToolRefusal refused)
            {
                return StagedResult.Fail(refused.Message);
            }
        }, runsFromNotification: false);
}
