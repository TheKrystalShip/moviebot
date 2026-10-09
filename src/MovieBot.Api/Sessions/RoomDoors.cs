using Microsoft.AspNetCore.SignalR;
using TheKrystalShip.MovieBot.Core;

namespace TheKrystalShip.MovieBot.Api.Sessions;

/// <summary>
/// Changes who may come into a room and tells everybody concerned.
/// </summary>
/// <remarks>
/// <para>
/// <b>Every change at a room's door goes through here, whichever door it arrived by</b>: a player
/// opening or closing the room, answering somebody asking, an invite letting somebody in. What has to
/// follow is the same each time — the room is told the door as it now stands (<c>DoorChanged</c>), and
/// every page whose asking was answered is told how (<c>KnockAnswered</c>) — and is written once.
/// </para>
/// <para>
/// <b>The door goes to the people in the room, and the answer to the people outside it.</b> Somebody
/// asking is not in the room's group, so they never see who else is asking or whether it opened; they
/// learn they may come in, or that they were not let in, and nothing more.
/// </para>
/// </remarks>
public sealed class RoomDoors(SessionStore sessions, IHubContext<SessionHub> hub, ILogger<RoomDoors> logger)
{
    public async Task SetOpenAsync(string sessionId, bool open, Actor by, CancellationToken ct = default)
    {
        if (sessions.SetOpen(sessionId, open) is not { } change) return;
        logger.LogInformation("{Actor} {Act} room {SessionId}", by.DisplayName, open ? "opened" : "closed", sessionId);
        await TellAsync(sessionId, change, admitted: true, ct);
    }

    /// <summary>Lets one person in: somebody asking at the door, or somebody invited.</summary>
    public async Task AdmitAsync(string sessionId, string userId, Actor by, CancellationToken ct = default)
    {
        if (sessions.Admit(sessionId, userId) is not { } change) return;
        logger.LogInformation("{Actor} let {UserId} into room {SessionId}", by.DisplayName, userId, sessionId);
        await TellAsync(sessionId, change, admitted: true, ct);
    }

    public async Task TurnAwayAsync(string sessionId, string userId, Actor by, CancellationToken ct = default)
    {
        if (sessions.TurnAway(sessionId, userId) is not { } change) return;
        logger.LogInformation("{Actor} did not let {UserId} into room {SessionId}", by.DisplayName, userId, sessionId);
        await TellAsync(sessionId, change, admitted: false, ct);
    }

    /// <summary>
    /// A page asking to be let in. True when it need not ask, and may come straight in; otherwise the
    /// room is told who is asking, and the page is answered when somebody in the room decides.
    /// </summary>
    public async Task<bool> KnockAsync(string sessionId, string connectionId, Knock knock, CancellationToken ct = default)
    {
        if (sessions.Knock(sessionId, connectionId, knock) is not { } door) return true;
        logger.LogInformation("{DisplayName} asks to be let into room {SessionId}", knock.DisplayName, sessionId);
        await hub.Clients.Group(sessionId).SendAsync("DoorChanged", door, ct);
        return false;
    }

    /// <summary>A page that stops asking: it came in, or it went away.</summary>
    public async Task WithdrawAsync(string sessionId, string connectionId, CancellationToken ct = default)
    {
        if (sessions.Withdraw(sessionId, connectionId) is { } door)
            await hub.Clients.Group(sessionId).SendAsync("DoorChanged", door, ct);
    }

    private async Task TellAsync(string sessionId, DoorChange change, bool admitted, CancellationToken ct)
    {
        await hub.Clients.Group(sessionId).SendAsync("DoorChanged", change.Door, ct);
        if (change.Answered.Count > 0)
            await hub.Clients.Clients(change.Answered).SendAsync("KnockAnswered", admitted, ct);
    }
}
