using System.Text;
using TheKrystalShip.MovieBot.Core;

namespace TheKrystalShip.MovieBot.Assistant.Tools;

/// <summary>
/// How the tools write a plan for the model to read: who is in it, every time put forward with who
/// answered what, the time picked, and what is measured of the title. Times are in the person's clock.
/// </summary>
internal static class PlanText
{
    /// <summary>The plan, as one entry of a list.</summary>
    public static string Describe(PlanView plan, Caller caller, TimeZoneInfo zone, DateTimeOffset now)
    {
        var text = new StringBuilder("- ").Append(TitleText.Named(plan.Title, plan.Card?.Year)).Append(", ")
            .Append(TitleText.KindWord(plan.Kind)).Append(". Plan id ").Append(plan.Id).Append(". Made by ")
            .Append(Name(plan.MadeBy, caller)).Append(".\n");
        if (plan.Season is { } season && plan.Number is { } number)
            text.Append($"  Starts at season {season} episode {number}.\n");

        text.Append(plan.FixedTime is { } picked
            ? $"  Picked time: {PersonClock.Write(picked, zone)}.\n"
            : "  No time is picked yet; only whoever made the plan picks one.\n");

        text.Append("  Times put forward:\n");
        foreach (PlanTimeView time in plan.Times)
        {
            text.Append("    ").Append(PersonClock.Write(time.At, zone));
            if (time.At == plan.FixedTime) text.Append(" (picked)");
            if (time.At <= now) text.Append(" (passed)");
            text.Append(": ").Append(Answers(plan, time, caller)).Append('\n');
        }

        text.Append("  People in it: ").Append(string.Join(", ", plan.People.Select(p => Name(p, caller) + (p.Maker ? " (made it)" : ""))))
            .Append(".\n");
        text.Append("  ").Append(Readiness(plan)).Append('\n');
        if (plan.State == PlanStates.Open)
            text.Append("  Its room is open, and everybody who said in or maybe is let in.\n");
        return text.ToString().TrimEnd();
    }

    /// <summary>Who said what to one time, and who has not answered.</summary>
    private static string Answers(PlanView plan, PlanTimeView time, Caller caller)
    {
        string Who(string answer) =>
            string.Join(", ", time.Answers.Where(a => a.Answer == answer).Select(a => Name(a.UserId, a.Name, caller)));
        var parts = new List<string>();
        foreach ((string answer, string word) in new[] { (PlanAnswers.In, "in"), (PlanAnswers.Maybe, "maybe"), (PlanAnswers.Cant, "can't") })
            if (Who(answer) is { Length: > 0 } who) parts.Add($"{word}: {who}");
        var silent = plan.People.Where(p => time.Answers.All(a => a.UserId != p.UserId)).Select(p => Name(p, caller)).ToList();
        if (silent.Count > 0) parts.Add("no answer yet: " + string.Join(", ", silent));
        return parts.Count == 0 ? "nobody has answered" : string.Join("; ", parts);
    }

    /// <summary>What is measured of the plan's title, never a guess at whether it arrives in time.</summary>
    public static string Readiness(PlanView plan) => plan.Readiness switch
    {
        PlanReadiness.Ready => "The title is in the library, ready to watch.",
        PlanReadiness.Fetching => plan.Progress is { } p ? $"The title is arriving, {Math.Round(p * 100)}% there." : "The title is arriving.",
        PlanReadiness.Waiting => "The tracker offered nothing for the title when it was last asked.",
        PlanReadiness.Queued => "The title is not being fetched yet.",
        _ => "Nothing is known of whether the title is here.",
    };

    public static string Name(PlanPersonView person, Caller caller) => Name(person.UserId, person.Name, caller);

    /// <summary>A person by name, the asking person marked as such; somebody the server has no name for is said so.</summary>
    public static string Name(string userId, string? name, Caller caller) =>
        userId == caller.UserId ? $"{name ?? caller.DisplayName} (the person)" : name ?? "somebody with no name";
}
