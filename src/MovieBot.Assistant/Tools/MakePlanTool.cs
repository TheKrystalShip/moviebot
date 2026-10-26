using System.ComponentModel;
using System.Text.Json;
using TheKrystalShip.Agent.Plugin;
using TheKrystalShip.MovieBot.Assistant.Api;
using TheKrystalShip.MovieBot.Catalogue;
using TheKrystalShip.MovieBot.Core;

namespace TheKrystalShip.MovieBot.Assistant.Tools;

[AssistantTool("make_plan", ToolClass.Staged, Parallel = false, Order = 12)]
[Description("""
    Propose a watch party: a title, the times to put forward, and who to invite. Nothing is made until the person confirms; say it is waiting for them, and that the people invited are told once it is.
    - Only with times the person gave and the people they named or said to leave out. When they said no time, or did not say who, ask them first with ask_question; never choose times or people for them.
    - title: an id a tool returned, or a title with its year.
    - times: one to six, each YYYY-MM-DD HH:mm in the person's clock, from 5 minutes to 30 days ahead. Put several forward when the person has not settled on one; everybody invited answers each.
    - people: names as the person says them. A name that fits several people or nobody is answered with who it could be.
    - season and episode: for a show, where to start. Default the person's next episode.
    - A film that is not out yet cannot be planned: offer want instead, and the person is told when it is out.
    """)]
internal sealed partial class MakePlanTool(CinemaApi api, TimeProvider clock) : AssistantTool<MakePlanTool.Args, Caller>
{
    public sealed record Args(
        [property: Description("An id, or a title with its year.")] string Title,
        [property: Description("One to six local times, YYYY-MM-DD HH:mm.")] string[] Times,
        [property: Description("Names of the people to invite.")] string[]? People = null,
        [property: Description("A show's season to start at.")] int? Season = null,
        [property: Description("A show's episode to start at.")] int? Episode = null);

    protected override async ValueTask<ToolResult> RunAsync(Args args, ToolCall<Caller> call)
    {
        var person = api.As(call.Principal.AccessToken);
        CancellationToken ct = call.CancellationToken;
        TimeZoneInfo zone = PersonClock.Zone(call.Principal, clock);
        DateTimeOffset now = clock.GetUtcNow();

        // Everything the API would refuse is refused here, so the person never confirms something that fails.
        List<DateTimeOffset> times = [.. args.Times.Select(t => PlanLimits.Minute(PersonClock.Read(t, zone))).Distinct().Order()];
        if (times.Count is < PlanLimits.MinTimes or > PlanLimits.MaxTimes)
            throw new ToolRefusal($"A plan puts forward {PlanLimits.MinTimes} to {PlanLimits.MaxTimes} times.");
        if (times.Select(t => PersonClock.NotAhead(t, now, zone)).FirstOrDefault(why => why is not null) is { } notAhead)
            throw new ToolRefusal(notAhead);
        if (args.Season is null != args.Episode is null)
            throw new ToolRefusal("Give both a season and an episode to start a show at, or neither for the person's next episode.");

        (string id, _) = await TitleLookup.ResolveAsync(person, args.Title, "make_plan", ct);
        TitlePage page = await person.TitleAsync(id, ct);
        string kind = page.Kind == "show" ? WantKinds.Show : WantKinds.Film;
        string name = TitleText.Named(page.Title, page.Year);
        if (kind == WantKinds.Film && (await person.AvailabilityAsync(id, ct))?.NotOut == true)
            throw new ToolRefusal($"{name} is not out yet, so it cannot be planned. Offer want instead: the person is told when it is out.");

        List<PersonView> invited = await PeopleLookup.ResolveAsync(person, args.People ?? [], call.Principal, ct);
        if (invited.Count > PlanLimits.MaxInvited)
            throw new ToolRefusal($"A plan invites at most {PlanLimits.MaxInvited} people.");

        // A plan already made for the title is said, so the model can offer to join it instead.
        PlanView? existing = (await person.PlansAsync(ct)).FirstOrDefault(p => p.ImdbId == id);

        string Join(IReadOnlyList<string> names) => names.Count switch
        {
            0 => "",
            1 => names[0],
            _ => string.Join(", ", names.Take(names.Count - 1)) + " and " + names[^1],
        };
        string summary = $"Watch {name} together"
            + "\n" + string.Join(" · ", times.Select(t => PersonClock.Write(t, zone)))
            + (args.Season is { } s ? $"\nStarting at season {s} episode {args.Episode}." : "")
            + (invited.Count > 0 ? $"\nWith {Join([.. invited.Select(p => p.Name)])}." : "\nNobody invited yet.")
            + (page.InLibrary ? " In the library." : " Not in the library yet.");

        var request = new MakePlanRequest
        {
            ImdbId = id, Kind = kind, Times = times, People = [.. invited.Select(p => p.UserId)], Season = args.Season, Number = args.Episode,
        };
        ToolResult proposed = Proposals.Propose(call, api, "plan", name, summary, async (confirmed, _, token) =>
        {
            PlanView plan = await confirmed.MakePlanAsync(request, token);
            string text = invited.Count > 0
                ? $"The plan to watch {name} is made. {Join([.. invited.Select(p => p.Name)])} {(invited.Count == 1 ? "is" : "are")} told, and each answers the times; you pick the time once they have."
                : $"The plan to watch {name} is made. Invite people from its page, or ask me to.";
            return StagedResult.Ok(text, JsonSerializer.SerializeToElement(PlansCard.Of([plan])!, CardJson.Default.PlansCard));
        });

        return existing is null ? proposed : proposed with
        {
            Text = $"The person is already in a plan for {name} (plan id {existing.Id}). Say so: joining or changing it may be what they want.\n" + proposed.Text,
        };
    }
}
