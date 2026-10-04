using Microsoft.AspNetCore.SignalR;
using TheKrystalShip.MovieBot.Api.People;
using TheKrystalShip.MovieBot.Core;

namespace TheKrystalShip.MovieBot.Api.Sessions;

/// <summary>
/// The room's connection to its shared timeline.
///
/// Clients send intent — play, pause, seek — and receive whatever the server decided. They never
/// push state. Every broadcast carries the server clock and a revision, which between them make
/// reordering, duplication and skewed client clocks all harmless.
/// </summary>
public sealed class SessionHub(
    SessionStore sessions, RoomControls controls, PersonDirectory people, ViewerQuality quality,
    ILogger<SessionHub> logger) : Hub
{
    private const string SessionIdKey = "sessionId";
    private const string ActorKey = "actor";

    /// <summary>The tallest picture a quality report may name: 8K.</summary>
    private const int MaxHeight = 4320;

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
        people.Remember(userId, displayName);

        var participants = sessions.Join(sessionId, Context.ConnectionId,
            new Participant { UserId = userId, DisplayName = displayName });
        quality.Join(sessionId, Context.ConnectionId);

        await Clients.Group(sessionId).SendAsync("ParticipantsChanged", participants);
        logger.LogInformation(
            "{DisplayName} joined session {SessionId} (connection {Connection}, {Count} in the room)",
            displayName, sessionId, Context.ConnectionId[..6], participants.Count);

        return Push(sessions.GetOrCreate(sessionId));
    }

    /// <summary>
    /// Puts a film on for the room, from <paramref name="startSeconds"/> or from the beginning when it
    /// is null. A hub call carries every argument its method has, so a client with no start sends null.
    /// </summary>
    public Task<SessionStatePush> LoadTitle(string titleId, double? startSeconds) =>
        Mutate("load", actor => sessions.LoadTitle(SessionId(), titleId, actor, startSeconds));

    public Task<SessionStatePush> Play(double atSeconds) =>
        Mutate("play", actor => sessions.Play(SessionId(), atSeconds, actor));

    public Task<SessionStatePush> Pause(double atSeconds) =>
        Mutate("pause", actor => sessions.Pause(SessionId(), atSeconds, actor));

    public Task<SessionStatePush> Seek(double toSeconds) =>
        Mutate("seek", actor => sessions.Seek(SessionId(), toSeconds, actor));

    /// <summary>
    /// Says whether this viewer's player is waiting for the film. It moves nothing: the room is told
    /// who it is waiting on, and the timeline stays the server's.
    /// </summary>
    public async Task Buffering(bool waiting)
    {
        if (sessions.SetBuffering(SessionId(), Context.ConnectionId, waiting) is { } participants)
            await Clients.Group(SessionId()).SendAsync("ParticipantsChanged", participants);
    }

    /// <summary>
    /// Says which rung this viewer's player is on and which its own adaptive choice would pick. It
    /// answers nothing and tells nobody: the report is kept for the transcode planner and goes no
    /// further, so a viewer's quality stays theirs. Heights outside what a picture can be are dropped.
    /// </summary>
    public void ReportQuality(int? playingHeight, int? wantedHeight)
    {
        static int? InRange(int? height) => height is > 0 and <= MaxHeight ? height : null;
        quality.Report(SessionId(), Context.ConnectionId,
            new ViewerQualityReport(InRange(playingHeight), InRange(wantedHeight)));
    }

    /// <summary>
    /// Answers with the server's clock so a client can estimate its offset. Position is derived
    /// from that offset, so a viewer whose machine is a minute fast must not drag the room with it.
    /// </summary>
    public DateTimeOffset ServerTime() => DateTimeOffset.UtcNow;

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        if (Context.Items.TryGetValue(SessionIdKey, out var value) && value is string sessionId)
        {
            quality.Leave(sessionId, Context.ConnectionId);
            var participants = sessions.Leave(sessionId, Context.ConnectionId);
            logger.LogInformation(
                "Connection {Connection} left session {SessionId} ({Count} left){Why}",
                Context.ConnectionId[..6], sessionId, participants.Count,
                exception is null ? "" : $": {exception.Message}");
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, sessionId);
            await Clients.Group(sessionId).SendAsync("ParticipantsChanged", participants);
        }

        await base.OnDisconnectedAsync(exception);
    }

    private async Task<SessionStatePush> Mutate(
        string intent, Func<Actor, MutationResult> mutation)
    {
        var change = await controls.ApplyAsync(
            SessionId(), intent, CurrentActor(), mutation, via: Context.ConnectionId[..6]);

        // The clamp notice goes only to whoever asked, because only they need to be told why
        // they did not land where they clicked.
        if (change.Clamped is { } clamped)
            await Clients.Caller.SendAsync("SeekClamped", clamped);

        return change.Push;
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
