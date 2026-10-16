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
///
/// A paired phone drives its screen's room with <see cref="JoinAsRemote"/>: it moves the film like any
/// member and is in nothing the room counts, and every method not marked
/// <see cref="RemoteAllowedAttribute"/> refuses it (<see cref="RemoteGate"/>).
/// </summary>
public sealed class SessionHub(
    SessionStore sessions, RoomControls controls, RoomDoors doors, RoomEpisodes episodes, ViewerQuality quality,
    RoomGuests guests, ScreenPresence screens, ScreenPairings pairings, IOptions<RoomOptions> rooms, IOptions<OidcOptions> oidc, ILogger<SessionHub> logger) : Hub
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
    public Task<JoinReply> Join(string sessionId) => EnterRoom(sessionId, null);

    /// <summary>
    /// Comes back into a room this page was in, with what its player did while the room could not hear
    /// it. The room takes that as its own state when nobody else could have seen it differently
    /// (<see cref="SessionStore.Resume"/>), and otherwise answers where it is, as <see cref="Join"/> does.
    /// </summary>
    public Task<JoinReply> Rejoin(string sessionId, PlayerAccount? account) => EnterRoom(sessionId, account);

    private async Task<JoinReply> EnterRoom(string sessionId, PlayerAccount? account)
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
        await screens.JoinedAsync(Context.ConnectionId, sessionId);

        var viewer = new Participant { UserId = actor.UserId, DisplayName = actor.DisplayName };
        var participants = sessions.Join(sessionId, Context.ConnectionId, viewer);
        quality.Join(sessionId, Context.ConnectionId);

        await Clients.Group(RoomGroups.Everyone(sessionId)).SendAsync("ParticipantsChanged", participants);
        logger.LogInformation(
            "{DisplayName} joined session {SessionId} (connection {Connection}, {Count} in the room)",
            actor.DisplayName, sessionId, Context.ConnectionId[..6], participants.Count);

        // A player coming back alone to a room nobody changed carries on from its own playhead; otherwise
        // somebody arriving is somebody not waiting, so a room held for its players goes on.
        if (account is not null) await controls.ResumeAsync(sessionId, Context.ConnectionId, viewer, account);
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
    /// Makes this connection a remote of the screen <paramref name="screenId"/> names, which one of this
    /// person's phones may only do for a screen it is paired with. The remote is in the screen's room
    /// as it stands and moves with the screen to the next; the answer is the room's state, absent while
    /// the screen is in none. It is no participant: the roster, the head count, the hold, the watch
    /// record and the transcode plan never see it, and it reports nothing a player reports.
    /// </summary>
    [RemoteAllowed]
    public async Task<JoinReply> JoinAsRemote(string screenId)
    {
        var person = Person("Sign in to drive a screen.");
        var phone = Phone() ?? throw new HubException("Only a phone paired with a screen drives it.");
        if (Context.Items.ContainsKey(SessionIdKey)) throw new HubException("This connection is in a room.");

        var joined = await screens.JoinRemoteAsync(person.UserId, phone, screenId, Context.ConnectionId)
            ?? throw new HubException("This phone is not paired with that screen.");
        Context.Items[RemoteGate.ItemKey] = screenId;
        Context.Items[ActorKey] = person.Actor;

        logger.LogInformation(
            "{DisplayName}'s phone became a remote of screen {ScreenId} (connection {Connection}, room {Room})",
            person.DisplayName, screenId, Context.ConnectionId[..6], joined.Room ?? "none");
        return new JoinReply { Admitted = true, Push = joined.Push };
    }

    /// <summary>
    /// Tells the screen this remote drives to open <paramref name="room"/>, with <paramref name="titleId"/>
    /// on from <paramref name="startSeconds"/> where the phone knows the film, for a screen that is in no
    /// room. The screen is the remote's binding and the person the token's; a screen in a room is moved
    /// through that room instead.
    /// </summary>
    [RemoteAllowed]
    public async Task WatchOnScreen(string room, string? titleId, double? startSeconds)
    {
        Person("Sign in to drive a screen.");
        if (!Context.Items.ContainsKey(RemoteGate.ItemKey)) throw new HubException("Only a remote puts a film on its screen.");
        if (await screens.WatchAsync(Context.ConnectionId, room, titleId, startSeconds) is { } refusal)
            throw new HubException(refusal);
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
    [RemoteAllowed]
    public Task<SessionStatePush> LoadTitle(string titleId, double? startSeconds) =>
        Mutate("load", actor => sessions.LoadTitle(SessionId(), titleId, actor, startSeconds));

    /// <summary>
    /// Moves the room to the episode after what it holds, deciding on the server whether that loads it
    /// or fetches it. Refused when there is nothing after it that this person can put on.
    /// </summary>
    [RemoteAllowed]
    public async Task Next()
    {
        if (!await episodes.NextAsync(SessionId(), Person(), Context.ConnectionAborted))
            throw new HubException("There is no next episode to put on.");
    }

    /// <summary>Moves the room to the episode before what it holds.</summary>
    [RemoteAllowed]
    public async Task Previous()
    {
        if (!await episodes.PreviousAsync(SessionId(), Person(), Context.ConnectionAborted))
            throw new HubException("There is no previous episode to put on.");
    }

    /// <summary>Puts an episode of the show the room holds on for the room.</summary>
    [RemoteAllowed]
    public async Task PlayEpisode(int season, int number)
    {
        if (!await episodes.PlayAsync(SessionId(), season, number, Person(), Context.ConnectionAborted))
            throw new HubException("That episode cannot be put on.");
    }

    [RemoteAllowed]
    public Task<SessionStatePush> Play(double atSeconds) =>
        Mutate("play", actor => sessions.Play(SessionId(), atSeconds, actor));

    [RemoteAllowed]
    public Task<SessionStatePush> Pause(double atSeconds) =>
        Mutate("pause", actor => sessions.Pause(SessionId(), atSeconds, actor));

    [RemoteAllowed]
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
    [RemoteAllowed]
    public DateTimeOffset ServerTime() => DateTimeOffset.UtcNow;

    /// <summary>
    /// Opens a connection that has an account behind it into its person's groups, and records it as a
    /// screen when its token says it was minted for the television client. Any other connection of an
    /// anchor session joins that session's group, where the screens paired with it are told. A guest's
    /// connection joins no person's group.
    /// </summary>
    public override async Task OnConnectedAsync()
    {
        if (Bearer() is { } identity && CinemaPerson.From(identity, oidc.Value.ActionsClaim) is { } person)
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, PersonGroups.Everything(person.UserId));
            if (ScreenIdentity.Of(identity, oidc.Value.TvClientId) is { } screen)
            {
                await Groups.AddToGroupAsync(Context.ConnectionId, PersonGroups.Screen(person.UserId, screen.Id));
                await screens.ConnectedAsync(person.UserId, screen, Context.ConnectionId);
            }
            else if (SessionClaims.SidOf(identity) is { } sid)
            {
                await Groups.AddToGroupAsync(Context.ConnectionId, PersonGroups.Phone(person.UserId, sid));
                pairings.PhoneSeen(person.UserId, sid);
            }
        }

        await base.OnConnectedAsync();
    }

    /// <summary>
    /// The screens this connection's session is paired with, as they stand. Only its own person's: the
    /// account and the session are the token's, and no argument names another. A screen, and a
    /// connection that belongs to no session, are paired with none.
    /// </summary>
    [RemoteAllowed]
    public Task<List<ScreenView>> Screens()
    {
        var person = Person("Sign in to see your screens.");
        return Phone() is { } phone ? screens.PairedWithAsync(person.UserId, phone) : Task.FromResult(new List<ScreenView>());
    }

    /// <summary>
    /// A code for this screen to show, which pairs a phone of its person's with it until it runs out.
    /// Only a screen is given one: the token decides, and nothing the page sends.
    /// </summary>
    public PairingCodeView PairingCode()
    {
        var (person, screen) = Screen("Only a television shows a pairing code.");
        return screens.CodeFor(person.UserId, screen.Id);
    }

    /// <summary>How many remotes drive this screen now. Only a screen asks.</summary>
    public int Remotes()
    {
        var (person, screen) = Screen("Only a television has remotes.");
        return screens.RemotesOf(person.UserId, screen.Id);
    }

    /// <summary>The phones paired with this screen, oldest first. Only a screen asks.</summary>
    public Task<List<PairedPhone>> Phones()
    {
        var (person, screen) = Screen("Only a television has phones paired with it.");
        return screens.PhonesOfAsync(person.UserId, screen.Id);
    }

    /// <summary>
    /// Ends a pairing of this connection's session: from a screen, with the phone <paramref name="id"/>
    /// names; from a phone, with the screen it names. Only the caller's own person's pairings are reached.
    /// </summary>
    [RemoteAllowed]
    public Task Unpair(string id)
    {
        var person = Person("Sign in to unpair.");
        if (Bearer() is { } identity && ScreenIdentity.Of(identity, oidc.Value.TvClientId) is { } screen)
            return screens.UnpairAsync(person.UserId, id, screen.Id);
        return Phone() is { } phone
            ? screens.UnpairAsync(person.UserId, phone, id)
            : throw new HubException("This sign-in is paired with nothing.");
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        await screens.DisconnectedAsync(Context.ConnectionId);
        if (Bearer() is { } identity && CinemaPerson.From(identity, oidc.Value.ActionsClaim) is { } person && Phone() is { } phone)
            pairings.PhoneSeen(person.UserId, phone);
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
            SessionId(), intent, CurrentActor(), mutation, via: Via());

        // The clamp notice goes only to whoever asked, because only they need to be told why
        // they did not land where they clicked.
        if (change.Clamped is { } clamped)
            await Clients.Caller.SendAsync("SeekClamped", clamped);

        return change.Push;
    }

    private static SessionStatePush Push(SessionState state) =>
        new() { State = state, ServerTime = DateTimeOffset.UtcNow };

    /// <summary>
    /// The room this connection moves: the one it joined, or for a remote the room its screen is in
    /// right now, asked each time so a remote never acts on a room its screen has left.
    /// </summary>
    private string SessionId()
    {
        if (Context.Items.ContainsKey(RemoteGate.ItemKey))
        {
            var (room, refusal) = screens.RoomOfRemote(Context.ConnectionId);
            return room ?? throw new HubException(refusal);
        }

        return Context.Items.TryGetValue(SessionIdKey, out var value) && value is string sessionId
            ? sessionId
            : throw new HubException("Join a session first.");
    }

    /// <summary>The connection as the log names it, with what kind of door it is.</summary>
    private string Via() =>
        Context.Items.ContainsKey(RemoteGate.ItemKey) ? Context.ConnectionId[..6] + " remote" : Context.ConnectionId[..6];

    private CinemaPerson Person(string refusal = "Sign in to change the room.") =>
        Bearer() is { } identity && CinemaPerson.From(identity, oidc.Value.ActionsClaim) is { } person
            ? person
            : throw new HubException(refusal);

    private System.Security.Claims.ClaimsIdentity? Bearer() =>
        Context.User?.Identities.FirstOrDefault(i => i.AuthenticationType == CinemaSchemes.Bearer);

    /// <summary>The person and the screen this connection is, or a refusal for any connection that is no screen.</summary>
    private (CinemaPerson Person, ScreenIdentity Screen) Screen(string refusal)
    {
        var person = Person(refusal);
        return ScreenIdentity.Of(Bearer()!, oidc.Value.TvClientId) is { } screen
            ? (person, screen)
            : throw new HubException(refusal);
    }

    /// <summary>The anchor session of a connection that is not a screen, or null for a screen or one that belongs to none.</summary>
    private string? Phone() =>
        Bearer() is { } identity && ScreenIdentity.Of(identity, oidc.Value.TvClientId) is null ? SessionClaims.SidOf(identity) : null;

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
