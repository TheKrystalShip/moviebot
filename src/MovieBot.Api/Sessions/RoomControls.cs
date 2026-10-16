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
    ScreenPresence screens,
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
            sessions.Participants(sessionId).Count, actor.Via is { } client ? $"{via ?? "http"} from {client}" : via ?? "http");

        await hub.Clients.Group(RoomGroups.Everyone(sessionId)).SendAsync("StateChanged", push, ct);
        await screens.RoomChangedAsync(sessionId);

        return new RoomChange(push, result.Clamped);
    }

    /// <summary>
    /// Tells a room what film it is waiting on and how far it has got, and pushes that to everybody in
    /// it. Nobody asked for this, so it is not an intent and is not logged as one; nothing is pushed
    /// when nothing changed.
    /// </summary>
    public async Task ReportAsync(string sessionId, RoomPreparing? preparing, CancellationToken ct = default)
    {
        if (sessions.SetPreparing(sessionId, preparing) is not { } result) return;
        await hub.Clients.Group(RoomGroups.Everyone(sessionId)).SendAsync("StateChanged",
            new SessionStatePush { State = result.State, ServerTime = DateTimeOffset.UtcNow }, ct);
        await screens.RoomChangedAsync(sessionId);
    }

    /// <summary>
    /// Holds the room or lets it go after who is in it, or who of them is waiting, changed
    /// (<see cref="SessionStore.Hold"/>), and pushes that to everybody in it. Nobody asked for this
    /// either; it is logged so a room that stopped on its own can be told apart from one somebody
    /// paused.
    /// </summary>
    public async Task HoldAsync(string sessionId, CancellationToken ct = default)
    {
        if (sessions.Hold(sessionId) is not { } state) return;
        var held = !state.Waiting ? "let go"
            : sessions.Participants(sessionId).Any(p => !p.Guest) ? "held for its players" : "held with nobody in it";
        logger.LogInformation(
            "Session {SessionId} {Held} at {Position:0.00}s -> revision {Revision}",
            sessionId, held, state.PositionSeconds, state.Revision);
        await hub.Clients.Group(RoomGroups.Everyone(sessionId)).SendAsync("StateChanged",
            new SessionStatePush { State = state, ServerTime = DateTimeOffset.UtcNow }, ct);
        await screens.RoomChangedAsync(sessionId);
    }

    /// <summary>
    /// Takes a returning player's account of what it played as the room's state where the room allows
    /// it (<see cref="SessionStore.Resume"/>), and pushes that to everybody in it. True when it did.
    /// </summary>
    public async Task<bool> ResumeAsync(string sessionId, string connectionId, Participant viewer, PlayerAccount account,
        CancellationToken ct = default)
    {
        if (sessions.Resume(sessionId, connectionId, viewer, account) is not { } state) return false;
        logger.LogInformation(
            "Session {SessionId} resumed from {DisplayName}'s player at {Position:0.00}s, {Paused} -> revision {Revision}",
            sessionId, viewer.DisplayName, state.PositionSeconds, state.Paused ? "paused" : "playing", state.Revision);
        await hub.Clients.Group(RoomGroups.Everyone(sessionId)).SendAsync("StateChanged",
            new SessionStatePush { State = state, ServerTime = DateTimeOffset.UtcNow }, ct);
        await screens.RoomChangedAsync(sessionId);
        return true;
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
