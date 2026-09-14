using Microsoft.AspNetCore.SignalR;
using TheKrystalShip.MovieBot.Core;

namespace TheKrystalShip.MovieBot.Api.Sessions;

/// <summary>
/// Changes a room and tells everyone in it.
/// </summary>
/// <remarks>
/// <para>
/// <b>Every change to a room goes through here, whichever door it arrived by.</b> A player sends
/// intent down the hub; the bot and a spoken command post it over HTTP. Those are different doors
/// onto the same act, and what has to happen afterwards — record who did it, push the new state to
/// everybody watching — is the same in both cases and is the part that is easy to get subtly
/// different. Written once, a change to it cannot reach one door and miss the other.
/// </para>
/// <para>
/// <b>The broadcast goes to the whole room including whoever asked.</b> Nobody applies their own
/// optimistic guess: the server decides what happened, and every client — the one that asked
/// included — runs the same path applying the same answer.
/// </para>
/// </remarks>
public sealed class RoomControls(
    SessionStore sessions,
    IHubContext<SessionHub> hub,
    ILogger<RoomControls> logger)
{
    /// <summary>
    /// Applies one change and pushes the result to the room.
    /// </summary>
    /// <param name="sessionId">The room.</param>
    /// <param name="intent">What was asked for, for the log.</param>
    /// <param name="mutation">The change itself, which is the store's to decide.</param>
    /// <param name="via">Which connection it came down, when the door has one to name.</param>
    /// <param name="ct">Abandons the broadcast, never the change: by the time this is cancellable
    /// the room has already changed, and a caller that hung up does not undo it.</param>
    public async Task<RoomChange> ApplyAsync(
        string sessionId,
        string intent,
        Actor actor,
        Func<Actor, MutationResult> mutation,
        string? via = null,
        CancellationToken ct = default)
    {
        var result = mutation(actor);
        var push = new SessionStatePush
        {
            State = result.State,
            ServerTime = DateTimeOffset.UtcNow
        };

        // Every intent, with who sent it. A room that pauses itself is either somebody publishing a
        // pause or a player stopping without saying so, and those have nothing in common but the
        // symptom — this is what tells them apart.
        logger.LogInformation(
            "{Actor} {Intent} -> revision {Revision}, {Paused}, position {Position:0.00}s"
            + " ({Participants} in the room, via {Via})",
            actor.DisplayName, intent, result.State.Revision,
            result.State.Paused ? "paused" : "playing", result.State.PositionSeconds,
            sessions.Participants(sessionId).Count, via ?? "http");

        await hub.Clients.Group(sessionId).SendAsync("StateChanged", push, ct);

        return new RoomChange(push, result.Clamped);
    }
}

/// <summary>
/// What a room change produced: the state everybody was sent, and a clamp notice when the position
/// asked for was not the position granted.
/// </summary>
/// <remarks>
/// The clamp is answered to whoever asked rather than broadcast, because it explains a refusal that
/// only concerns them — the rest of the room simply sees where the film went.
/// </remarks>
public sealed record RoomChange(SessionStatePush Push, SeekClamped? Clamped);
