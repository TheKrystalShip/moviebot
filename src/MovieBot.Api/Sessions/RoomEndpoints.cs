using TheKrystalShip.MovieBot.Api.Auth;
using TheKrystalShip.MovieBot.Api.Library;
using TheKrystalShip.MovieBot.Core;

namespace TheKrystalShip.MovieBot.Api.Sessions;

/// <summary>
/// The room's controls, for a caller that is not in it.
/// </summary>
/// <remarks>
/// <para>
/// <b>The same acts the hub carries, over HTTP.</b> A player holds a hub connection and sends
/// intent down it; the bot and a spoken command have no connection to a room and one request to
/// make. Both doors call <see cref="RoomControls"/>, so what a play does — who it is recorded as,
/// what the room is told — cannot come to differ depending on which door it arrived by.
/// </para>
/// <para>
/// <b>The change is recorded as whoever the token names.</b> The bot carrying out a person's command
/// arrives with that person's token, which names the bot as the client acting for them, so the room
/// records the person and that the bot carried it. Putting a film into a room is also machine work:
/// the bot loads a download into the room that asked for it once it can be watched, on the service
/// key, and that is recorded as MovieBot.
/// </para>
/// <para>
/// <b>A clamp comes back in the answer rather than being broadcast.</b> Over the hub it is sent to
/// the caller alone, and an HTTP caller is the one thing a broadcast cannot reach, so it rides in
/// the response body where that caller already looks.
/// </para>
/// <para>
/// <b>A person reaches only a room they may enter</b> (<see cref="RoomEntry"/>), as over the hub.
/// </para>
/// </remarks>
public static class RoomEndpoints
{
    public static void MapRooms(this WebApplication app)
    {
        // Forgets a room nobody is in, now rather than once the reaper finds it idle: what a person
        // who made a room to try something leaves behind. A room with anybody in it is theirs and
        // stays; a plan's room is held as the reaper holds it.
        app.MapDelete("/api/rooms/{room}", (string room, HttpContext context, SessionStore sessions,
            Plans.PlanStore plans, TimeProvider clock, Microsoft.Extensions.Options.IOptions<RoomOptions> options,
            ILoggerFactory loggers) =>
        {
            var person = CinemaPerson.Of(context)!;
            var held = plans.RoomsHeld(clock.GetUtcNow() - options.Value.IdleTimeout);
            var outcome = sessions.Forget(room, person.UserId, held);
            if (outcome == ForgetOutcome.Forgotten)
                loggers.CreateLogger(typeof(RoomEndpoints)).LogInformation("{Who} forgot room {Room}", person.DisplayName, room);
            return outcome switch
            {
                ForgetOutcome.Forgotten => Results.NoContent(),
                ForgetOutcome.Missing => Results.NotFound(),
                ForgetOutcome.Private => RoomEntry.Private,
                _ => Results.Json(new ErrorReply(outcome == ForgetOutcome.Occupied ? "Somebody is in this room." : "This room is held for a plan."),
                    ApiJsonContext.Default.ErrorReply, statusCode: StatusCodes.Status409Conflict),
            };
        }).RequireAction(CinemaActions.Watch);

        // Puts a film into a room from outside it.
        //
        // The bot knows which film was asked for and cannot tell the player directly: an Activity is
        // launched by Discord, from a URL the bot never writes, so nothing can be handed over in a
        // query string the way a browser link does it. Setting the title on the session instead
        // means the Activity finds the film already loaded when it joins, whichever door the viewer
        // came through.
        app.MapPost("/api/sessions/{sessionId}/title", async (
            string sessionId,
            SetTitleRequest request,
            HttpContext context,
            SessionStore sessions,
            TitleLibrary library,
            RoomControls controls,
            CancellationToken ct) =>
        {
            if (library.Get(request.TitleId) is null) return Results.NotFound();

            var change = await controls.ApplyAsync(
                sessionId, "load", ActorOf(context),
                actor => sessions.LoadTitle(sessionId, request.TitleId, actor, request.StartSeconds), ct: ct);

            return Results.Ok(change.Push);
        }).RequireActionOrService(CinemaActions.Watch).RequireRoomEntry();

        app.MapPost("/api/sessions/{sessionId}/play", (
            string sessionId, RoomPlaybackRequest request, HttpContext context,
            SessionStore sessions, RoomControls controls, CancellationToken ct) =>
            Apply(sessionId, "play", context, controls,
                actor => sessions.Play(sessionId, request.AtSeconds, actor), ct))
            .RequireAction(CinemaActions.Watch)
            .RequireRoomEntry();

        app.MapPost("/api/sessions/{sessionId}/pause", (
            string sessionId, RoomPlaybackRequest request, HttpContext context,
            SessionStore sessions, RoomControls controls, CancellationToken ct) =>
            Apply(sessionId, "pause", context, controls,
                actor => sessions.Pause(sessionId, request.AtSeconds, actor), ct))
            .RequireAction(CinemaActions.Watch)
            .RequireRoomEntry();

        app.MapPost("/api/sessions/{sessionId}/seek", (
            string sessionId, RoomSeekRequest request, HttpContext context,
            SessionStore sessions, RoomControls controls, CancellationToken ct) =>
            Apply(sessionId, "seek", context, controls,
                actor => sessions.Seek(sessionId, request.ToSeconds, actor), ct))
            .RequireAction(CinemaActions.Watch)
            .RequireRoomEntry();

        app.MapPost("/api/sessions/{sessionId}/seek-relative", (
            string sessionId, RoomNudgeRequest request, HttpContext context,
            SessionStore sessions, RoomControls controls, CancellationToken ct) =>
            Apply(sessionId, "nudge", context, controls,
                actor => sessions.SeekRelative(sessionId, request.DeltaSeconds, actor), ct))
            .RequireAction(CinemaActions.Watch)
            .RequireRoomEntry();
    }

    private static async Task<IResult> Apply(
        string sessionId,
        string intent,
        HttpContext context,
        RoomControls controls,
        Func<Actor, MutationResult> mutation,
        CancellationToken ct)
    {
        var change = await controls.ApplyAsync(sessionId, intent, ActorOf(context), mutation, ct: ct);
        return Results.Ok(new RoomChanged(change.Push, change.Clamped));
    }

    /// <summary>
    /// Who a change is recorded as: the person the token names, or MovieBot itself for machine work on
    /// the service key, which is honest about a room that moved with nobody asking.
    /// </summary>
    private static Actor ActorOf(HttpContext context) =>
        CinemaPerson.Of(context)?.Actor ?? Acquisition.WatchNow.Server;
}
