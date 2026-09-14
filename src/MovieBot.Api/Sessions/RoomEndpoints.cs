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
/// <b>A clamp comes back in the answer rather than being broadcast.</b> Over the hub it is sent to
/// the caller alone, and an HTTP caller is the one thing a broadcast cannot reach, so it rides in
/// the response body where that caller already looks.
/// </para>
/// </remarks>
public static class RoomEndpoints
{
    public static void MapRooms(this WebApplication app)
    {
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
            SessionStore sessions,
            TitleLibrary library,
            RoomControls controls,
            CancellationToken ct) =>
        {
            if (library.Get(request.TitleId) is null) return Results.NotFound();

            var change = await controls.ApplyAsync(
                sessionId, "load", ActorFor(request.UserId, request.DisplayName),
                actor => sessions.LoadTitle(sessionId, request.TitleId, actor), ct: ct);

            return Results.Ok(change.Push);
        });

        app.MapPost("/api/sessions/{sessionId}/play", (
            string sessionId, RoomPlaybackRequest request,
            SessionStore sessions, RoomControls controls, CancellationToken ct) =>
            Apply(sessionId, "play", request, controls,
                actor => sessions.Play(sessionId, request.AtSeconds, actor), ct));

        app.MapPost("/api/sessions/{sessionId}/pause", (
            string sessionId, RoomPlaybackRequest request,
            SessionStore sessions, RoomControls controls, CancellationToken ct) =>
            Apply(sessionId, "pause", request, controls,
                actor => sessions.Pause(sessionId, request.AtSeconds, actor), ct));

        app.MapPost("/api/sessions/{sessionId}/seek", (
            string sessionId, RoomSeekRequest request,
            SessionStore sessions, RoomControls controls, CancellationToken ct) =>
            Apply(sessionId, "seek", request, controls,
                actor => sessions.Seek(sessionId, request.ToSeconds, actor), ct));

        app.MapPost("/api/sessions/{sessionId}/seek-relative", (
            string sessionId, RoomNudgeRequest request,
            SessionStore sessions, RoomControls controls, CancellationToken ct) =>
            Apply(sessionId, "nudge", request, controls,
                actor => sessions.SeekRelative(sessionId, request.DeltaSeconds, actor), ct));
    }

    private static async Task<IResult> Apply(
        string sessionId,
        string intent,
        IRoomAct request,
        RoomControls controls,
        Func<Actor, MutationResult> mutation,
        CancellationToken ct)
    {
        var change = await controls.ApplyAsync(
            sessionId, intent, ActorFor(request.UserId, request.DisplayName), mutation, ct: ct);

        return change.Clamped is { } clamped
            ? Results.Ok(new RoomChanged(change.Push, clamped))
            : Results.Ok(new RoomChanged(change.Push, null));
    }

    /// <summary>
    /// Who a change is recorded as. A caller naming nobody is the bot acting on its own, and saying
    /// so is better than attributing it to whoever happened to ask something a minute ago.
    /// </summary>
    private static Actor ActorFor(string? userId, string? displayName) =>
        new(userId ?? "bot", displayName ?? "MovieBot");
}

/// <summary>
/// What a room control answers: the state the room was sent, and the clamp when the position asked
/// for was not the position granted.
/// </summary>
public sealed record RoomChanged(SessionStatePush Push, SeekClamped? Clamped);
