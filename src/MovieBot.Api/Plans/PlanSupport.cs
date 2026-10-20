using System.Globalization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Options;
using TheKrystalShip.MovieBot.Api.Films;
using TheKrystalShip.MovieBot.Api.Notifications;
using TheKrystalShip.MovieBot.Api.Sessions;
using TheKrystalShip.MovieBot.Api.Shows;
using TheKrystalShip.MovieBot.Api.People;
using TheKrystalShip.MovieBot.Core;

namespace TheKrystalShip.MovieBot.Api.Plans;

public sealed class PlanOptions
{
    public const string Section = "Plans";

    /// <summary>How many people saying <c>in</c> to a time fixes it, the maker counted.</summary>
    public int FixAt { get; set; } = 2;

    /// <summary>Minutes before the fixed time that everybody coming is reminded.</summary>
    public int RemindMinutes { get; set; } = 15;

    /// <summary>Minutes before the fixed time that the room is made, closed, with the people coming let in.</summary>
    public int OpenMinutes { get; set; } = 30;

    /// <summary>Seconds between passes of the keeper.</summary>
    public int PassSeconds { get; set; } = 15;
}

/// <summary>A plan's title as a person says it.</summary>
public sealed class PlanTitles(FilmStore films, ShowStore shows)
{
    /// <summary>The title's name, or its IMDb id while the film database has not described it.</summary>
    public string Name(string imdbId, string kind) => (kind == Wants.WantKinds.Show
        ? shows.Held(imdbId)?.Details?.Title
        : films.Held(imdbId)?.Details?.Title) ?? imdbId;
}

/// <summary>
/// Tells a plan's people what happened to it. Each is a <c>plan</c> notification keyed by the plan and
/// the event, so each is said once to each person however often it is asked, and it is pushed whatever
/// the person's switches say: a plan is addressed to the people in it on purpose.
/// </summary>
public sealed class PlanNotices(
    Notifier notifier, PlanTitles titles, PersonDirectory people, IOptions<PlanOptions> options)
{
    /// <summary>The zone times are said in: the host's, since the people in a plan are near each other.</summary>
    public TimeZoneInfo Zone { get; init; } = TimeZoneInfo.Local;

    public Task InvitedAsync(StoredPlan plan, string userId, CancellationToken ct) => TellAsync(userId, plan, "invited",
        $"{Maker(plan)} plans to watch {titles.Name(plan.ImdbId, plan.Kind)}. Say when suits you.", Path(plan), "Answer", ct);

    public async Task FixedAsync(StoredPlan plan, IEnumerable<string> users, CancellationToken ct)
    {
        var when = plan.FixedTime is { } at ? " for " + When(at, Zone) : "";
        foreach (var user in users)
            await TellAsync(user, plan, "fixed", $"{titles.Name(plan.ImdbId, plan.Kind)} is on{when}.", Path(plan), "Open", ct);
    }

    public async Task ReminderAsync(StoredPlan plan, IEnumerable<string> users, CancellationToken ct)
    {
        var text = $"{titles.Name(plan.ImdbId, plan.Kind)} starts in {options.Value.RemindMinutes} minutes.";
        foreach (var user in users)
            await TellAsync(user, plan, "reminder", text, SitePaths.Room(PlanRules.RoomOf(plan.Id)), "Join", ct);
    }

    public async Task CancelledAsync(StoredPlan plan, IEnumerable<string> users, CancellationToken ct)
    {
        foreach (var user in users)
            await TellAsync(user, plan, "cancelled",
                $"{Maker(plan)} cancelled the plan to watch {titles.Name(plan.ImdbId, plan.Kind)}.", "/plans", null, ct);
    }

    /// <summary>A day and a time in a sentence: <c>Sat 24 Oct at 21:00</c>.</summary>
    public static string When(DateTimeOffset at, TimeZoneInfo zone) =>
        TimeZoneInfo.ConvertTime(at, zone).ToString("ddd d MMM 'at' HH:mm", CultureInfo.InvariantCulture);

    private string Maker(StoredPlan plan) => people.NameOf(plan.MadeBy) ?? "Someone";

    private static string Path(StoredPlan plan) => $"/plans/{Uri.EscapeDataString(plan.Id)}";

    private Task TellAsync(string userId, StoredPlan plan, string @event, string text, string url, string? action, CancellationToken ct) =>
        notifier.TellAsync(userId, new NotificationDraft
        {
            Kind = NotificationKinds.Plan, Key = $"{plan.Id}:{@event}", Text = text, Url = url, Action = action,
        }, ct);
}

/// <summary>
/// Tells the people in a plan, over the hub's per-person group, that it changed, so an open plan page
/// reads it again without polling. The push names the plan and nothing else: what a person may see of it
/// is read with their own token.
/// </summary>
public sealed class PlanPush(IHubContext<SessionHub> hub)
{
    public Task ChangedAsync(StoredPlan plan) => Task.WhenAll(plan.People.Select(user =>
        hub.Clients.Group(PersonGroups.Everything(user)).SendAsync("PlanChanged", plan.Id)));
}
