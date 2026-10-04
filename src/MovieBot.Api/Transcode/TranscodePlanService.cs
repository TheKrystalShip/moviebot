using Microsoft.Extensions.Options;
using TheKrystalShip.MovieBot.Api.Sessions;
using TheKrystalShip.MovieBot.Core;

namespace TheKrystalShip.MovieBot.Api.Transcode;

/// <summary>
/// Gathers what the planner decides from, out of the rooms, the library and the executor's report,
/// and plans. Nothing here is stored: the plan is computed from the facts of the moment it is asked.
/// </summary>
public sealed class TranscodePlanService(
    SessionStore sessions,
    TranscodeCatalog catalog,
    IViewerQuality quality,
    TranscodeStatusStore status,
    IOptionsMonitor<TranscodeOptions> options,
    TimeProvider clock)
{
    public TranscodePlan Plan()
    {
        // A room somebody is in with a film set, paused or playing, is demand. Rooms that merely hold
        // a film are not in Viewing(): they are kept idle for a while and would pin work for nobody.
        var rooms = sessions.Viewing()
            .Select(r => new PlanRoom(
                r.SessionId, r.TitleId, r.PositionSeconds, r.Viewers.Count, quality.WantedHeights(r.SessionId)))
            .ToList();

        var held = rooms.Select(r => r.TitleId).ToHashSet(StringComparer.Ordinal);
        return TranscodePlanner.Plan(new PlanInput(
            clock.GetUtcNow(), rooms, catalog.Titles(held), status.Current(), options.CurrentValue));
    }
}
