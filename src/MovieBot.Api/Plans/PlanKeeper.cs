using Microsoft.Extensions.Options;
using TheKrystalShip.MovieBot.Api.Library;
using TheKrystalShip.MovieBot.Api.Notifications;
using TheKrystalShip.MovieBot.Api.Sessions;
using TheKrystalShip.MovieBot.Core;

namespace TheKrystalShip.MovieBot.Api.Plans;

/// <summary>
/// Carries plans through time. A fixed plan is reminded <see cref="PlanOptions.RemindMinutes"/> ahead
/// and its room is made <see cref="PlanOptions.OpenMinutes"/> ahead: closed, with the title loaded and
/// everybody who said <c>in</c> or <c>maybe</c> let in, so anybody else knocks. The room is made by
/// the same store a first entry makes it in, and every change at its door goes through
/// <see cref="RoomDoors"/>. The reaper leaves it alone until <see cref="RoomOptions.IdleTimeout"/> past
/// the fixed time (<see cref="PlanStore.RoomsHeld"/>); a plan is <c>done</c> once its room is gone after
/// that. A room lost sooner (a restart past its idle window) is made again. A proposed plan every one
/// of whose times has passed ends without a word.
/// </summary>
public sealed class PlanKeeper(
    PlanStore plans,
    PlanNotices notices,
    PlanPush push,
    SessionStore sessions,
    RoomControls controls,
    RoomDoors doors,
    LibraryListing library,
    IOptions<PlanOptions> options,
    IOptions<RoomOptions> rooms,
    TimeProvider clock,
    ILogger<PlanKeeper> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(Math.Max(5, options.Value.PassSeconds)));
        while (await ReadyNotices.WaitAsync(timer, stoppingToken))
        {
            try
            {
                await PassAsync(stoppingToken);
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                logger.LogWarning(ex, "A pass over the plans failed.");
            }
        }
    }

    public async Task PassAsync(CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        foreach (var plan in plans.Active())
        {
            switch (plan.State)
            {
                case PlanStates.Proposed:
                    if (plan.Times.All(t => t <= now) && plans.Cancel(plan.Id))
                        await push.ChangedAsync(plan with { State = PlanStates.Cancelled });
                    break;

                case PlanStates.Fixed:
                    if (PlanRules.OpenDue(plan, now, options.Value.OpenMinutes)) await OpenAsync(plan, ct);
                    await RemindAsync(plans.Get(plan.Id) ?? plan, now, ct);
                    break;

                case PlanStates.Open:
                    await KeepAsync(plan, now, ct);
                    await RemindAsync(plan, now, ct);
                    break;
            }
        }
    }

    private async Task RemindAsync(StoredPlan plan, DateTimeOffset now, CancellationToken ct)
    {
        if (plan.State is PlanStates.Fixed or PlanStates.Open && PlanRules.ReminderDue(plan, now, options.Value.RemindMinutes))
            await notices.ReminderAsync(plan, PlanRules.Coming(plan), ct);
    }

    /// <summary>Makes the plan's room and records it; the plan is then open.</summary>
    private async Task OpenAsync(StoredPlan plan, CancellationToken ct)
    {
        var room = await MakeRoomAsync(plan, ct);
        if (!plans.Open(plan.Id, room)) return;

        logger.LogInformation("Plan {Plan} opened room {Room} for {Count} coming.", plan.Id, room, PlanRules.Coming(plan).Count);
        if (plans.Get(plan.Id) is { } opened) await push.ChangedAsync(opened);
        await LoadAsync(room, plan, ct);
    }

    /// <summary>
    /// Keeps an open plan's room: made again if it was lost before its time ran out, holding its title
    /// once the library has it, and finished once it is reaped after that.
    /// </summary>
    private async Task KeepAsync(StoredPlan plan, DateTimeOffset now, CancellationToken ct)
    {
        var room = plan.RoomId ?? PlanRules.RoomOf(plan.Id);
        if (sessions.Get(room) is null)
        {
            if (plan.FixedTime is { } at && now < at + rooms.Value.IdleTimeout)
            {
                await MakeRoomAsync(plan, ct);
                await LoadAsync(room, plan, ct);
            }
            else if (plans.Finish(plan.Id))
            {
                logger.LogInformation("Plan {Plan} is done: room {Room} is gone.", plan.Id, room);
                await push.ChangedAsync(plan with { State = PlanStates.Done });
            }
            return;
        }
        await LoadAsync(room, plan, ct);
    }

    /// <summary>
    /// The room, closed with the people coming let in. A room that is already there (somebody entered
    /// the plan's room ahead of time) is closed and has them let in through the door's own place.
    /// </summary>
    private async Task<string> MakeRoomAsync(StoredPlan plan, CancellationToken ct)
    {
        var room = PlanRules.RoomOf(plan.Id);
        var coming = PlanRules.Coming(plan);
        if (sessions.CreateClosed(room, coming)) return room;

        await doors.SetOpenAsync(room, false, Server, ct);
        foreach (var user in coming) await doors.AdmitAsync(room, user, Server, ct);
        return room;
    }

    /// <summary>Puts the plan's title into a room holding nothing, paused at the start, once the library holds it.</summary>
    private async Task LoadAsync(string room, StoredPlan plan, CancellationToken ct)
    {
        if (sessions.Get(room) is not { TitleId: null, Preparing: null }) return;
        if (PlanRules.LibraryId(library.Titles(), plan) is not { } titleId) return;

        await controls.ApplyAsync(room, "load", Server, actor => sessions.LoadTitle(room, titleId, actor), ct: ct);
    }

    private static Actor Server => Acquisition.WatchNow.Server;
}
