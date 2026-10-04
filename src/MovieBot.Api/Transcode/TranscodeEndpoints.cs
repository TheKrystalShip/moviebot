using TheKrystalShip.MovieBot.Api.Acquisition;
using TheKrystalShip.MovieBot.Core;

namespace TheKrystalShip.MovieBot.Api.Transcode;

/// <summary>
/// The desired state of the card and the executor's report of it. Both are for the hand-off, the one
/// process that runs ffmpeg and writes the media roots, and answer the service key alone: a plan
/// names every film in the library and what rooms are watching.
/// </summary>
public static class TranscodeEndpoints
{
    public static void MapTranscode(this WebApplication app)
    {
        var group = app.MapGroup("/api/transcode").AddEndpointFilter(AcquisitionEndpoints.ServiceOnly);

        group.MapGet("/plan", (TranscodePlanService plans) => Results.Ok(plans.Plan()));

        group.MapPut("/status", (TranscodeStatus report, TranscodeStatusStore store) =>
        {
            store.Put(report);
            return Results.NoContent();
        });
    }
}
