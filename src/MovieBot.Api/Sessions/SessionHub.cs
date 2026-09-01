using Microsoft.AspNetCore.SignalR;
using TheKrystalShip.MovieBot.Core;

namespace TheKrystalShip.MovieBot.Api.Sessions;

/// <summary>
/// The room's connection to its shared timeline.
///
/// Clients send intent — play, pause, seek — and receive whatever the server decided. They never
/// push state. Every broadcast carries the server clock and a revision, which between them make
/// reordering, duplication and skewed client clocks all harmless.
/// </summary>
public sealed class SessionHub(SessionStore sessions, ILogger<SessionHub> logger) : Hub
{
    private const string SessionIdKey = "sessionId";
    private const string ActorKey = "actor";

    /// <summary>
    /// Enters a session and receives its current state. A late joiner needs nothing else: the
    /// position is derived from the anchor, so it computes where the room is without being told.
    /// </summary>
    public async Task<SessionStatePush> Join(string sessionId, string userId, string displayName)
    {
        var actor = new Actor(userId, displayName);
        Context.Items[SessionIdKey] = sessionId;
        Context.Items[ActorKey] = actor;

        await Groups.AddToGroupAsync(Context.ConnectionId, sessionId);

        var participants = sessions.Join(sessionId, Context.ConnectionId,
            new Participant { UserId = userId, DisplayName = displayName });

        await Clients.Group(sessionId).SendAsync("ParticipantsChanged", participants);
        logger.LogInformation("{DisplayName} joined session {SessionId}", displayName, sessionId);

        return Push(sessions.GetOrCreate(sessionId));
    }

    public Task<SessionStatePush> LoadTitle(string titleId) =>
        Mutate(actor => sessions.LoadTitle(SessionId(), titleId, actor));

    public Task<SessionStatePush> Play(double atSeconds) =>
        Mutate(actor => sessions.Play(SessionId(), atSeconds, actor));

    public Task<SessionStatePush> Pause(double atSeconds) =>
        Mutate(actor => sessions.Pause(SessionId(), atSeconds, actor));

    public Task<SessionStatePush> Seek(double toSeconds) =>
        Mutate(actor => sessions.Seek(SessionId(), toSeconds, actor));

    /// <summary>
    /// Answers with the server's clock so a client can estimate its offset. Position is derived
    /// from that offset, so a viewer whose machine is a minute fast must not drag the room with it.
    /// </summary>
    public DateTimeOffset ServerTime() => DateTimeOffset.UtcNow;

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        if (Context.Items.TryGetValue(SessionIdKey, out var value) && value is string sessionId)
        {
            var participants = sessions.Leave(sessionId, Context.ConnectionId);
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, sessionId);
            await Clients.Group(sessionId).SendAsync("ParticipantsChanged", participants);
        }

        await base.OnDisconnectedAsync(exception);
    }

    private async Task<SessionStatePush> Mutate(Func<Actor, MutationResult> mutation)
    {
        var result = mutation(CurrentActor());
        var push = Push(result.State);

        // Broadcast to the whole group, the caller included: the caller applies the server's
        // answer rather than its own optimistic guess, so every client runs the same code path.
        await Clients.Group(SessionId()).SendAsync("StateChanged", push);

        // The clamp notice goes only to whoever asked, because only they need to be told why
        // they did not land where they clicked.
        if (result.Clamped is { } clamped)
            await Clients.Caller.SendAsync("SeekClamped", clamped);

        return push;
    }

    private static SessionStatePush Push(SessionState state) =>
        new() { State = state, ServerTime = DateTimeOffset.UtcNow };

    private string SessionId() =>
        Context.Items.TryGetValue(SessionIdKey, out var value) && value is string sessionId
            ? sessionId
            : throw new HubException("Join a session first.");

    private Actor CurrentActor() =>
        Context.Items.TryGetValue(ActorKey, out var value) && value is Actor actor
            ? actor
            : throw new HubException("Join a session first.");
}
