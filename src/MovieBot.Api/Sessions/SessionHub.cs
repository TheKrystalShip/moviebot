using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Options;
using TheKrystalShip.MovieBot.Api.Auth;
using TheKrystalShip.MovieBot.Core;

namespace TheKrystalShip.MovieBot.Api.Sessions;

/// <summary>
/// The room's connection to its shared timeline.
///
/// Clients send intent — play, pause, seek — and receive whatever the server decided. They never
/// push state. Every broadcast carries the server clock and a revision, which between them make
/// reordering, duplication and skewed client clocks all harmless.
///
/// Who is in the room is whoever the connection's token names: the connection is admitted on it when
/// it opens, and nothing a client sends says who it is. Whether they may come in is the room's
/// (<see cref="SessionStore.Enter"/>), and a page the room is private to may ask to be let in.
///
/// A connection opened on a room's guest link watches that room and nothing more: it joins with
/// <see cref="JoinAsGuest"/>, and every method not marked <see cref="GuestAllowedAttribute"/> refuses
/// it (<see cref="GuestGate"/>).
/// </summary>
public sealed class SessionHub(
    SessionStore sessions, RoomControls controls, RoomDoors doors, RoomEpisodes episodes, ViewerQuality quality,
    RoomGuests guests, IOptions<RoomOptions> rooms, IOptions<OidcOptions> oidc, ILogger<SessionHub> logger) : Hub
{
    private const string SessionIdKey = "sessionId";
    private const string ActorKey = "actor";
    private const string KnockingKey = "knocking";
    private const string GuestKey = "guest";

    /// <summary>The tallest picture a quality report may name: 8K.</summary>
    private const int MaxHeight = 4320;

    /// <summary>
    /// Enters a session and receives its current state and its door. A late joiner needs nothing
    /// else: the position is derived from the anchor, so it computes where the room is without being
    /// told. A room private to this person answers that and nothing else.
    /// </summary>
    public async Task<JoinReply> Join(string sessionId)
    {
        var person = Person("Sign in to join a room.");
        if (!sessions.Enter(sessionId, person.UserId))
        {
            logger.LogInformation("{DisplayName} was not let into private room {SessionId}", person.DisplayName, sessionId);
            return new JoinReply { Admitted = false };
        }

        // A page that was asking at the door and has come in is asking no longer.
        if (Context.Items.Remove(KnockingKey, out var knocked) && knocked is string knockedAt)
            await doors.WithdrawAsync(knockedAt, Context.ConnectionId);

        var actor = person.Actor;
        Context.Items[SessionIdKey] = sessionId;
        Context.Items[ActorKey] = actor;

        await Groups.AddToGroupAsync(Context.ConnectionId, RoomGroups.Everyone(sessionId));
        await Groups.AddToGroupAsync(Context.ConnectionId, RoomGroups.Members(sessionId));

        var participants = sessions.Join(sessionId, Context.ConnectionId,
            new Participant { UserId = actor.UserId, DisplayName = actor.DisplayName });
        quality.Join(sessionId, Context.ConnectionId);

        await Clients.Group(RoomGroups.Everyone(sessionId)).SendAsync("ParticipantsChanged", participants);
        logger.LogInformation(
            "{DisplayName} joined session {SessionId} (connection {Connection}, {Count} in the room)",
            actor.DisplayName, sessionId, Context.ConnectionId[..6], participants.Count);

        // Somebody arriving is somebody not waiting, so a room held for its players goes on.
        await controls.HoldAsync(sessionId);
        return new JoinReply { Admitted = true, Push = Push(sessions.GetOrCreate(sessionId)), Door = sessions.Door(sessionId) };
    }

    /// <summary>
    /// Enters the room this connection's guest link opens, shown to the room as <paramref name="name"/>.
    /// Answers the room and the guest as it names them, or why they were not let in: the link is no
    /// longer the room's, nobody with an account is in it, or it holds as many guests as it takes.
    /// Coming back in on the same connection keeps the guest who they were.
    /// </summary>
    [GuestAllowed]
    public async Task<JoinReply> JoinAsGuest(string? name)
    {
        var guest = GuestViewer.Of(Context.User) ?? throw new HubException("Only a guest link joins as a guest.");
        var you = Context.Items.TryGetValue(GuestKey, out var held) && held is Participant before
            ? before with { DisplayName = RoomGuests.NameOf(name) }
            : new Participant { UserId = "guest-" + Guid.NewGuid().ToString("n")[..12], DisplayName = RoomGuests.NameOf(name), Guest = true };

        var settings = rooms.Value;
        if (sessions.AdmitGuest(guest.SessionId, guest.PassId, Context.ConnectionId, you, settings.GuestsPerRoom, settings.GuestGrace)
            is { } refused)
        {
            logger.LogInformation("A guest was not let into room {SessionId} ({Reason})", guest.SessionId, refused);
            return new JoinReply { Admitted = false, Refused = refused };
        }

        Context.Items[GuestKey] = you;
        Context.Items[SessionIdKey] = guest.SessionId;
        guests.Add(Context, guest);
        await Groups.AddToGroupAsync(Context.ConnectionId, RoomGroups.Everyone(guest.SessionId));
        quality.Join(guest.SessionId, Context.ConnectionId);

        var participants = sessions.Participants(guest.SessionId);
        await Clients.Group(RoomGroups.Everyone(guest.SessionId)).SendAsync("ParticipantsChanged", participants);
        logger.LogInformation(
            "Guest {DisplayName} joined session {SessionId} (connection {Connection}, {Count} in the room)",
            you.DisplayName, guest.SessionId, Context.ConnectionId[..6], participants.Count);

        return new JoinReply { Admitted = true, Push = Push(sessions.GetOrCreate(guest.SessionId)), You = you };
    }

    /// <summary>
    /// Asks to be let into a room that is private to this person. True when there is no need to ask
    /// and the room can be joined now; otherwise the page is answered with <c>KnockAnswered</c> once
    /// somebody in the room decides, and stops asking when it closes.
    /// </summary>
    public async Task<bool> Knock(string sessionId)
    {
        var person = Person("Sign in to ask to join a room.");
        var knock = new Knock { UserId = person.UserId, DisplayName = person.DisplayName };
        if (await doors.KnockAsync(sessionId, Context.ConnectionId, knock)) return true;
        Context.Items[KnockingKey] = sessionId;
        return false;
    }

    /// <summary>Opens the room to anybody who may watch, or closes it to anybody not already let in.</summary>
    public Task SetOpen(bool open) => doors.SetOpenAsync(SessionId(), open, CurrentActor());

    /// <summary>Answers somebody asking to be let in.</summary>
    public Task Answer(string userId, bool admit) =>
        admit ? doors.AdmitAsync(SessionId(), userId, CurrentActor()) : doors.TurnAwayAsync(SessionId(), userId, CurrentActor());

    /// <summary>
    /// Puts a film on for the room, from <paramref name="startSeconds"/> or from the beginning when it
    /// is null. A hub call carries every argument its method has, so a client with no start sends null.
    /// </summary>
    public Task<SessionStatePush> LoadTitle(string titleId, double? startSeconds) =>
        Mutate("load", actor => sessions.LoadTitle(SessionId(), titleId, actor, startSeconds));

    /// <summary>
    /// Moves the room to the episode after what it holds, deciding on the server whether that loads it
    /// or fetches it. Refused when there is nothing after it that this person can put on.
    /// </summary>
    public async Task Next()
    {
        if (!await episodes.NextAsync(SessionId(), Person(), Context.ConnectionAborted))
            throw new HubException("There is no next episode to put on.");
    }

    /// <summary>Moves the room to the episode before what it holds.</summary>
    public async Task Previous()
    {
        if (!await episodes.PreviousAsync(SessionId(), Person(), Context.ConnectionAborted))
            throw new HubException("There is no previous episode to put on.");
    }

    /// <summary>Puts an episode of the show the room holds on for the room.</summary>
    public async Task PlayEpisode(int season, int number)
    {
        if (!await episodes.PlayAsync(SessionId(), season, number, Person(), Context.ConnectionAborted))
            throw new HubException("That episode cannot be put on.");
    }

    public Task<SessionStatePush> Play(double atSeconds) =>
        Mutate("play", actor => sessions.Play(SessionId(), atSeconds, actor));

    public Task<SessionStatePush> Pause(double atSeconds) =>
        Mutate("pause", actor => sessions.Pause(SessionId(), atSeconds, actor));

    public Task<SessionStatePush> Seek(double toSeconds) =>
        Mutate("seek", actor => sessions.Seek(SessionId(), toSeconds, actor));

    /// <summary>
    /// Says whether this viewer's player is waiting for the film, and where its playhead stopped.
    /// The room is told who it is waiting on, and the timeline moves only when every player in the
    /// room is waiting, which holds it (<see cref="SessionStore.Hold"/>).
    /// </summary>
    public async Task Buffering(bool waiting, double atSeconds)
    {
        CurrentActor();
        if (sessions.SetBuffering(SessionId(), Context.ConnectionId, waiting, atSeconds) is { } participants)
            await Clients.Group(RoomGroups.Everyone(SessionId())).SendAsync("ParticipantsChanged", participants);
        await controls.HoldAsync(SessionId());
    }

    /// <summary>
    /// Says which rung this viewer's player is on and which its own adaptive choice would pick. It
    /// answers nothing and tells nobody: the report is kept for the transcode planner and goes no
    /// further, so a viewer's quality stays theirs. Heights outside what a picture can be are dropped.
    /// A guest reports too: what they watch at is planning, and moves nothing.
    /// </summary>
    [GuestAllowed]
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
    [GuestAllowed]
    public DateTimeOffset ServerTime() => DateTimeOffset.UtcNow;

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        guests.Remove(Context.ConnectionId);

        if (Context.Items.TryGetValue(KnockingKey, out var knocked) && knocked is string knockedAt)
            await doors.WithdrawAsync(knockedAt, Context.ConnectionId);

        if (Context.Items.TryGetValue(SessionIdKey, out var value) && value is string sessionId)
        {
            quality.Leave(sessionId, Context.ConnectionId);
            var participants = sessions.Leave(sessionId, Context.ConnectionId);
            logger.LogInformation(
                "Connection {Connection} left session {SessionId} ({Count} left){Why}",
                Context.ConnectionId[..6], sessionId, participants.Count,
                exception is null ? "" : $": {exception.Message}");
            await Clients.Group(RoomGroups.Everyone(sessionId)).SendAsync("ParticipantsChanged", participants);
            await controls.HoldAsync(sessionId);
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

    private CinemaPerson Person(string refusal = "Sign in to change the room.") =>
        Context.User?.Identities.FirstOrDefault(i => i.AuthenticationType == CinemaSchemes.Bearer) is { } identity
        && CinemaPerson.From(identity, oidc.Value.ActionsClaim) is { } person
            ? person
            : throw new HubException(refusal);

    /// <summary>
    /// Who is acting in the room this connection joined. Only <see cref="Join"/> records one, and only
    /// for a connection whose token names a person, so a guest's connection has none to give: every
    /// method that moves the room asks for this or for <see cref="Person"/>, and refuses a guest by
    /// itself, whatever <see cref="GuestGate"/> does.
    /// </summary>
    private Actor CurrentActor() =>
        GuestViewer.Of(Context.User) is null && Context.Items.TryGetValue(ActorKey, out var value) && value is Actor actor
            ? actor
            : throw new HubException("Join a session first.");
}
