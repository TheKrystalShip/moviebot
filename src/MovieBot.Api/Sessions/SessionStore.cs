using System.Collections.Concurrent;
using TheKrystalShip.MovieBot.Api.Library;
using TheKrystalShip.MovieBot.Core;

namespace TheKrystalShip.MovieBot.Api.Sessions;

/// <summary>The outcome of a mutation: the new state, plus a clamp notice when one was applied.</summary>
public sealed record MutationResult(SessionState State, SeekClamped? Clamped);

/// <summary>
/// A room holding a film with people in it: where it is at the instant asked, whether it is
/// playing, and each person in it once.
/// </summary>
public sealed record ViewingRoom(
    string SessionId, string TitleId, double PositionSeconds, bool Playing, IReadOnlyList<Participant> Viewers);

/// <summary>
/// A stretch of a film a room played, ending now: the people who were watching and where the film
/// got to. Raised when a playing room is changed, when somebody leaves one, and when the server
/// stops, which are the only moments the position stops following from the room's anchor.
/// </summary>
public sealed record WatchedSpan(string TitleId, double PositionSeconds, IReadOnlyList<Participant> Viewers, DateTimeOffset At);

/// <summary>
/// Holds every live session and is the only thing allowed to change one.
///
/// The server is authoritative and there is no host: anyone in a session can play, pause and
/// seek, and the state records who did it. Clients send intent; this decides what actually
/// happened, stamps it with the server clock, and gives it the next revision.
/// </summary>
public sealed class SessionStore(TitleLibrary library, TimeProvider clock)
{
    /// <summary>
    /// Raised whenever a room changes, so whatever keeps rooms between runs of the server knows
    /// there is something to keep. An event rather than a write from in here: what a store does is
    /// decide what happened, and how often that reaches a disk is somebody else's judgement.
    /// </summary>
    public event Action? Changed;

    /// <summary>
    /// Raised when a stretch of playing ends, with who watched it and where it ended. The room's
    /// state is the record of what is being watched: between two changes the position follows from
    /// the anchor, so the moment a playing state is replaced, or somebody leaves it, is the one
    /// moment worth writing anything down. Only a playing state counts; a paused room left open is
    /// nobody watching.
    /// </summary>
    public event Action<WatchedSpan>? Watched;

    /// <summary>
    /// How far short of the transcode head a clamped seek lands. Seeking exactly to the head
    /// puts the playhead on the last written segment with nothing after it, which stalls
    /// immediately; a few seconds back leaves something to play into.
    /// </summary>
    private const double HeadSafetyMargin = 10;

    private readonly ConcurrentDictionary<string, SessionEntry> _sessions = new();

    private sealed class SessionEntry
    {
        public readonly Lock Gate = new();
        public SessionState State = null!;
        public readonly ConcurrentDictionary<string, Participant> Participants = new();

        /// <summary>Where each waiting connection's playhead stopped, by connection.</summary>
        public readonly ConcurrentDictionary<string, double> WaitingAt = new();

        /// <summary>When somebody last did something here. Opening the room to look does not count.</summary>
        public DateTimeOffset LastActivityUtc;

        /// <summary>Who may come in, and who is asking to. Guarded by <see cref="Gate"/>.</summary>
        public RoomAdmission Admission = null!;

        /// <summary>
        /// Since when nobody with an account has been in the room: from when the room was made or put
        /// back, and from when the last of them left. Null while one is here. Guarded by <see cref="Gate"/>.
        /// </summary>
        public DateTimeOffset? MembersGoneSince;
    }

    /// <summary>Every live guest link's room, by the pass's id.</summary>
    private readonly ConcurrentDictionary<string, string> _passes = new(StringComparer.Ordinal);

    /// <summary>
    /// The room, made open and holding nothing when there is none. A room made here has nobody let in:
    /// machine work makes rooms this way, and a person makes one by entering it (<see cref="Enter"/>).
    /// </summary>
    public SessionState GetOrCreate(string sessionId)
    {
        var entry = _sessions.GetOrAdd(sessionId, id => NewRoom(id, []));
        lock (entry.Gate)
            return WithCurrentHead(entry.State);
    }

    private SessionEntry NewRoom(string sessionId, IEnumerable<string> admitted) => new()
    {
        State = new SessionState
        {
            SessionId = sessionId,
            Paused = true,
            PositionSeconds = 0,
            AnchorUtc = clock.GetUtcNow(),
            Revision = 0,
            // Minted with the room, carried by every state it ever produces, and different
            // from the one the room before it carried.
            Epoch = Guid.NewGuid().ToString("n")
        },
        LastActivityUtc = clock.GetUtcNow(),
        Admission = new RoomAdmission(open: true, admitted),
        MembersGoneSince = clock.GetUtcNow()
    };

    // ---- Who may come in -------------------------------------------------------------

    /// <summary>
    /// Lets a person into the room when it admits them, and keeps them let in; false when it is
    /// private to them. A room there is none of is made open with them let in, whoever makes a room
    /// being the first person it admits. Every door a person comes through asks this.
    /// </summary>
    public bool Enter(string sessionId, string userId)
    {
        var entry = _sessions.GetOrAdd(sessionId, id => NewRoom(id, [userId]));
        lock (entry.Gate)
            if (!entry.Admission.Enter(userId)) return false;

        Changed?.Invoke();
        return true;
    }

    /// <summary>Who may come into the room, or null when there is no such room.</summary>
    public RoomDoor? Door(string sessionId)
    {
        if (!_sessions.TryGetValue(sessionId, out var entry)) return null;
        lock (entry.Gate) return entry.Admission.Door;
    }

    /// <summary>Opens or closes the room. Null when there is no such room, or it already stood so.</summary>
    public DoorChange? SetOpen(string sessionId, bool open) =>
        AtDoor(sessionId, admission => admission.SetOpen(open));

    /// <summary>Lets one person into the room. Null when there is no such room.</summary>
    public DoorChange? Admit(string sessionId, string userId) =>
        AtDoor(sessionId, admission => admission.Admit(userId));

    /// <summary>Answers one person asking to come in that they are not let in. Null when there is no such room.</summary>
    public DoorChange? TurnAway(string sessionId, string userId) =>
        AtDoor(sessionId, admission => admission.TurnAway(userId));

    /// <summary>
    /// Records a page asking to be let into the room. Null when it need not ask: the room admits the
    /// person, or there is no such room, which entering would make for them.
    /// </summary>
    public RoomDoor? Knock(string sessionId, string connectionId, Knock knock)
    {
        if (!_sessions.TryGetValue(sessionId, out var entry)) return null;
        lock (entry.Gate)
            return entry.Admission.Admits(knock.UserId) ? null : entry.Admission.Knock(connectionId, knock);
    }

    /// <summary>Stops a page asking to be let in. Null when it was not asking.</summary>
    public RoomDoor? Withdraw(string sessionId, string connectionId)
    {
        if (!_sessions.TryGetValue(sessionId, out var entry)) return null;
        lock (entry.Gate) return entry.Admission.Withdraw(connectionId);
    }

    private DoorChange? AtDoor(string sessionId, Func<RoomAdmission, DoorChange?> act)
    {
        if (!_sessions.TryGetValue(sessionId, out var entry)) return null;
        DoorChange? change;
        lock (entry.Gate) change = act(entry.Admission);
        if (change is not null) Changed?.Invoke();
        return change;
    }

    // ---- Guests ---------------------------------------------------------------------

    /// <summary>
    /// Gives the room a guest link lasting <paramref name="lifetime"/>, or answers the one it has while
    /// that is still good, so everybody in the room who asks is handed the same link. The change is
    /// null when the room already had it. Null when there is no such room.
    /// </summary>
    public (GuestPass Pass, DoorChange? Change)? IssueGuests(string sessionId, string createdBy, TimeSpan lifetime)
    {
        if (!_sessions.TryGetValue(sessionId, out var entry)) return null;
        var now = clock.GetUtcNow();
        GuestPass? before;
        (GuestPass Pass, DoorChange? Change) issued;
        lock (entry.Gate)
        {
            before = entry.Admission.Guests;
            issued = entry.Admission.IssueGuests(() => new GuestPass
            {
                Id = GuestCodes.NewPassId(), CreatedBy = createdBy, CreatedAt = now, ExpiresAt = now + lifetime,
            }, now);
        }

        if (issued.Change is null) return issued;
        if (before is not null) _passes.TryRemove(before.Id, out _);
        _passes[issued.Pass.Id] = sessionId;
        Changed?.Invoke();
        return issued;
    }

    /// <summary>Takes the room's guest link away. Null when it had none, or there is no such room.</summary>
    public (GuestPass Pass, DoorChange Change)? RevokeGuests(string sessionId)
    {
        if (!_sessions.TryGetValue(sessionId, out var entry)) return null;
        (GuestPass Pass, DoorChange Change)? revoked;
        lock (entry.Gate) revoked = entry.Admission.RevokeGuests();
        if (revoked is not { } done) return null;
        _passes.TryRemove(done.Pass.Id, out _);
        Changed?.Invoke();
        return revoked;
    }

    /// <summary>The room's guest link while it is good, or null.</summary>
    public GuestPass? GuestsOf(string sessionId)
    {
        if (!_sessions.TryGetValue(sessionId, out var entry)) return null;
        lock (entry.Gate)
            return entry.Admission.Guests is { } pass && pass.LiveAt(clock.GetUtcNow()) ? pass : null;
    }

    /// <summary>The room a guest link opens while it is good, or null.</summary>
    public string? RoomOfGuests(string passId) =>
        _passes.TryGetValue(passId, out var room) && GuestsOf(room)?.Id == passId ? room : null;

    /// <summary>
    /// Lets a guest in on the room's link, as the participant given. Null when they are in; otherwise
    /// why not (<see cref="GuestRefusals"/>): the link is no longer the room's, nobody with an account
    /// has been in the room for longer than <paramref name="grace"/>, or it already holds
    /// <paramref name="most"/> guests.
    /// </summary>
    public string? AdmitGuest(string sessionId, string passId, string connectionId, Participant guest, int most, TimeSpan grace)
    {
        if (!_sessions.TryGetValue(sessionId, out var entry)) return GuestRefusals.Ended;
        var now = clock.GetUtcNow();
        lock (entry.Gate)
        {
            if (entry.Admission.Guests is not { } pass || pass.Id != passId || !pass.LiveAt(now))
                return GuestRefusals.Ended;
            if (!Attended(entry, now, grace)) return GuestRefusals.Unattended;
            // A page coming back in on the connection it holds is already counted.
            if (entry.Participants.Count(p => p.Value.Guest && p.Key != connectionId) >= most) return GuestRefusals.Full;
            entry.Participants[connectionId] = guest with { Guest = true };
        }

        entry.LastActivityUtc = now;
        Changed?.Invoke();
        return null;
    }

    /// <summary>The rooms whose guest link has run out.</summary>
    public IReadOnlyList<string> ExpiredGuests()
    {
        var now = clock.GetUtcNow();
        var rooms = new List<string>();
        foreach (var (id, entry) in _sessions)
        {
            lock (entry.Gate)
                if (entry.Admission.Guests is { } pass && !pass.LiveAt(now)) rooms.Add(id);
        }
        return rooms;
    }

    /// <summary>
    /// The rooms with guests in them that nobody with an account has been in for longer than
    /// <paramref name="grace"/>: the guests have nobody watching with them, and are sent away.
    /// </summary>
    public IReadOnlyList<string> Unattended(TimeSpan grace)
    {
        var now = clock.GetUtcNow();
        var rooms = new List<string>();
        foreach (var (id, entry) in _sessions)
        {
            lock (entry.Gate)
                if (entry.Participants.Values.Any(p => p.Guest) && !Attended(entry, now, grace)) rooms.Add(id);
        }
        return rooms;
    }

    /// <summary>Somebody with an account is in the room, or the last of them left within the grace.</summary>
    private static bool Attended(SessionEntry entry, DateTimeOffset now, TimeSpan grace) =>
        entry.Participants.Values.Any(p => !p.Guest)
        || (entry.MembersGoneSince is { } gone && now - gone < grace);

    public SessionState? Get(string sessionId) =>
        _sessions.TryGetValue(sessionId, out var entry) ? WithCurrentHead(entry.State) : null;

    /// <summary>
    /// Puts a film into the room, from the start.
    ///
    /// Whether the room is playing is left as it was. A room that was watching one film and is
    /// handed another goes on playing, from the beginning of the new one, so changing the film
    /// is one act rather than a change followed by everyone waiting for somebody to press play.
    /// A room that was paused stays paused.
    ///
    /// <paramref name="startSeconds"/> puts the room somewhere other than the beginning, which is
    /// how somebody carries on a film they are partway through. It is held to the transcode head
    /// like any seek, judged against the film being loaded.
    /// </summary>
    public MutationResult LoadTitle(string sessionId, string titleId, Actor actor, double? startSeconds = null)
    {
        var entry = Entry(sessionId);
        lock (entry.Gate)
        {
            EndSpan(entry, Viewers(entry));
            var loaded = entry.State with { TitleId = titleId, Preparing = null };
            var (start, clamp) = ClampToHead(loaded, startSeconds ?? 0);
            entry.State = Advance(loaded, actor) with
            {
                PositionSeconds = start,
                AnchorUtc = clock.GetUtcNow()
            };
            return new MutationResult(WithCurrentHead(entry.State), clamp);
        }
    }

    /// <summary>
    /// Puts something that is arriving into the room in place of what it held: the room holds no
    /// title, only <paramref name="preparing"/>, from the beginning, and every viewer is told it as
    /// <paramref name="actor"/> changing the film. Playing or paused stays as it was.
    /// </summary>
    public MutationResult Prepare(string sessionId, RoomPreparing preparing, Actor actor)
    {
        var entry = Entry(sessionId);
        lock (entry.Gate)
        {
            EndSpan(entry, Viewers(entry));
            entry.State = Advance(entry.State with { TitleId = null, Preparing = preparing }, actor) with
            {
                PositionSeconds = 0,
                AnchorUtc = clock.GetUtcNow()
            };
            return new MutationResult(WithCurrentHead(entry.State), null);
        }
    }

    /// <summary>
    /// Says what film the room is waiting on and how far it has got, or that it waits on nothing.
    ///
    /// Nobody did this: the server is reporting on a download, so the state names no actor, which is
    /// also what keeps every player from announcing it as somebody moving the room. Null when
    /// nothing changed, so a pass that finds the same progress pushes nothing.
    /// </summary>
    public MutationResult? SetPreparing(string sessionId, RoomPreparing? preparing)
    {
        var entry = Entry(sessionId);
        lock (entry.Gate)
        {
            if (entry.State.Preparing == preparing) return null;

            entry.LastActivityUtc = clock.GetUtcNow();
            Changed?.Invoke();
            entry.State = entry.State with
            {
                Revision = entry.State.Revision + 1, UpdatedBy = null, Preparing = preparing,
            };
            return new MutationResult(WithCurrentHead(entry.State), null);
        }
    }

    /// <summary>
    /// The rooms holding a film with somebody in them, with who is in each: what is being watched
    /// this instant, which is newer than anything written down. Position is derived from the anchor
    /// at the instant asked, never read as a stored number.
    /// </summary>
    public IReadOnlyList<ViewingRoom> Viewing()
    {
        var now = clock.GetUtcNow();
        var rooms = new List<ViewingRoom>();

        foreach (var (id, entry) in _sessions)
        {
            SessionState state;
            lock (entry.Gate) state = entry.State;

            if (state.TitleId is not { } titleId) continue;
            var viewers = Viewers(entry);
            if (viewers.Count == 0) continue;

            rooms.Add(new ViewingRoom(id, titleId, state.PositionAt(now), !state.Paused, viewers));
        }

        return rooms;
    }

    /// <summary>
    /// Ends every stretch of playing at once, for a server that is stopping: the people in the rooms
    /// are about to be disconnected without leaving one by one.
    /// </summary>
    public void EndAllSpans()
    {
        foreach (var entry in _sessions.Values)
            lock (entry.Gate) EndSpan(entry, Viewers(entry));
    }

    /// <summary>
    /// Starts the room playing.
    /// </summary>
    /// <remarks>
    /// <b>A null <paramref name="atSeconds"/> means "from wherever the room is".</b> A player knows
    /// where its own playhead sits and says so; a caller that is not in the room — the bot, a spoken
    /// command — does not, and has no way to find out that is still true by the time it is acted on.
    /// Resolved in here, under the same lock as the change itself, so the answer cannot go stale
    /// between reading it and using it.
    /// </remarks>
    public MutationResult Play(string sessionId, double? atSeconds, Actor actor)
    {
        var entry = Entry(sessionId);
        lock (entry.Gate)
        {
            EndSpan(entry, Viewers(entry));
            var from = atSeconds ?? entry.State.PositionAt(clock.GetUtcNow());
            var (granted, clamp) = ClampToHead(entry.State, from);
            entry.State = Advance(entry.State, actor) with
            {
                Paused = false,
                PositionSeconds = granted,
                AnchorUtc = clock.GetUtcNow()
            };
            return new MutationResult(WithCurrentHead(entry.State), clamp);
        }
    }

    /// <summary>
    /// Stops the room where it is.
    /// </summary>
    /// <remarks>
    /// <inheritdoc cref="Play" path="/remarks"/>
    /// </remarks>
    public MutationResult Pause(string sessionId, double? atSeconds, Actor actor)
    {
        var entry = Entry(sessionId);
        lock (entry.Gate)
        {
            EndSpan(entry, Viewers(entry));
            var at = atSeconds ?? entry.State.PositionAt(clock.GetUtcNow());
            entry.State = Advance(entry.State, actor) with
            {
                Paused = true,
                PositionSeconds = Math.Max(0, at),
                AnchorUtc = clock.GetUtcNow()
            };
            return new MutationResult(WithCurrentHead(entry.State), null);
        }
    }

    /// <summary>Moves the room to an absolute position.</summary>
    public MutationResult Seek(string sessionId, double toSeconds, Actor actor)
    {
        var entry = Entry(sessionId);
        lock (entry.Gate)
        {
            EndSpan(entry, Viewers(entry));
            var (granted, clamp) = ClampToHead(entry.State, toSeconds);
            entry.State = Advance(entry.State, actor) with
            {
                PositionSeconds = granted,
                AnchorUtc = clock.GetUtcNow()
            };
            return new MutationResult(WithCurrentHead(entry.State), clamp);
        }
    }

    /// <summary>
    /// Moves the room by <paramref name="deltaSeconds"/> from where it is now — back fifteen,
    /// forward a minute.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Relative rather than a position computed by the caller, because the film is moving.</b>
    /// Reading the position and then seeking to it minus fifteen is two acts with a gap between
    /// them, and a playing room has moved on by the time the second one lands — so "back fifteen"
    /// would take back fifteen minus however long the round trip took. Resolved under the lock, the
    /// subtraction happens against the position the change is applied to.
    /// </para>
    /// <para>
    /// Before zero is zero: asking to go back further than the film has run is a request to go to
    /// the beginning, not an error. Past the transcode head is clamped like any other seek.
    /// </para>
    /// </remarks>
    public MutationResult SeekRelative(string sessionId, double deltaSeconds, Actor actor)
    {
        var entry = Entry(sessionId);
        lock (entry.Gate)
        {
            EndSpan(entry, Viewers(entry));
            var from = entry.State.PositionAt(clock.GetUtcNow());
            var (granted, clamp) = ClampToHead(entry.State, from + deltaSeconds);
            entry.State = Advance(entry.State, actor) with
            {
                PositionSeconds = granted,
                AnchorUtc = clock.GetUtcNow()
            };
            return new MutationResult(WithCurrentHead(entry.State), clamp);
        }
    }

    // ---- Participants ---------------------------------------------------------------

    /// <summary>Puts somebody with an account into the room. A guest comes in by <see cref="AdmitGuest"/>.</summary>
    public IReadOnlyList<Participant> Join(string sessionId, string connectionId, Participant participant)
    {
        var entry = Entry(sessionId);
        lock (entry.Gate)
        {
            entry.Participants[connectionId] = participant with { Guest = false };
            entry.MembersGoneSince = null;
        }
        entry.LastActivityUtc = clock.GetUtcNow();

        // Somebody arriving puts the room's idle clock back, which is worth keeping: a room being
        // watched must not be swept on the next start for having last been touched hours ago.
        Changed?.Invoke();
        return entry.Participants.Values.ToList();
    }

    public IReadOnlyList<Participant> Leave(string sessionId, string connectionId)
    {
        if (!_sessions.TryGetValue(sessionId, out var entry)) return new List<Participant>();
        entry.WaitingAt.TryRemove(connectionId, out _);
        var now = clock.GetUtcNow();
        if (entry.Participants.TryRemove(connectionId, out var leaving) && !leaving.Guest)
            lock (entry.Gate)
            {
                EndSpan(entry, [leaving]);
                if (!entry.Participants.Values.Any(p => !p.Guest)) entry.MembersGoneSince = now;
            }
        // The clock on an empty room starts when the last person leaves it.
        entry.LastActivityUtc = now;

        Changed?.Invoke();
        return entry.Participants.Values.ToList();
    }

    /// <summary>
    /// Records whether one connection's player is waiting for the film, and where its playhead
    /// stopped. Answers the roster when that changed anything, and null when it did not, so a player
    /// repeating itself is not broadcast.
    /// </summary>
    public IReadOnlyList<Participant>? SetBuffering(string sessionId, string connectionId, bool buffering, double atSeconds)
    {
        if (!_sessions.TryGetValue(sessionId, out var entry)) return null;
        // A guest's player says nothing to the room, so where it stopped can never hold it.
        if (!entry.Participants.TryGetValue(connectionId, out var participant) || participant.Guest) return null;
        if (buffering) entry.WaitingAt[connectionId] = atSeconds;
        else entry.WaitingAt.TryRemove(connectionId, out _);
        if (participant.Buffering == buffering) return null;
        entry.Participants[connectionId] = participant with { Buffering = buffering };
        return entry.Participants.Values.ToList();
    }

    /// <summary>
    /// Holds a playing room while every player in it is waiting for the film, and lets it go the
    /// moment one is not. Answers the new state when that changed the room, and null when it did not.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Only when every player waits.</b> One viewer on a slow connection never stops the film for
    /// the rest of the room; they fall behind and catch up. A room in which nobody can play is a
    /// different thing: its timeline runs on for nobody, and the moment a player has the film again
    /// it is told the room is somewhere it has not fetched, seeks there, and stalls again.
    /// </para>
    /// <para>
    /// <b>Held where the furthest-behind player stopped</b>, never ahead of where the room is. A
    /// player put back a few seconds seeks into what it already holds; one put forward seeks into
    /// what it has not fetched, which is the stall the hold exists to end.
    /// </para>
    /// <para>
    /// Nobody did this, so the state names no actor and no player announces it, and the revision
    /// moves so every player applies it.
    /// </para>
    /// </remarks>
    public SessionState? Hold(string sessionId)
    {
        if (!_sessions.TryGetValue(sessionId, out var entry)) return null;
        lock (entry.Gate)
        {
            var waiting = EveryoneWaiting(entry);
            if (entry.State.Waiting == waiting) return null;

            var now = clock.GetUtcNow();
            var at = entry.State.PositionAt(now);
            if (waiting && !entry.WaitingAt.IsEmpty) at = Math.Min(at, Math.Max(0, entry.WaitingAt.Values.Min()));

            EndSpan(entry, Viewers(entry));
            entry.LastActivityUtc = now;
            Changed?.Invoke();
            entry.State = entry.State with
            {
                Revision = entry.State.Revision + 1,
                UpdatedBy = null,
                Waiting = waiting,
                PositionSeconds = at,
                AnchorUtc = now
            };
            return WithCurrentHead(entry.State);
        }
    }

    /// <summary>
    /// Somebody with an account is in the room and every one of their players is waiting for the film.
    /// A guest holds nothing: what a guest's player does never moves the room.
    /// </summary>
    private static bool EveryoneWaiting(SessionEntry entry)
    {
        var members = entry.Participants.Values.Where(p => !p.Guest).ToList();
        return members.Count > 0 && members.All(p => p.Buffering);
    }

    public IReadOnlyList<Participant> Participants(string sessionId) =>
        _sessions.TryGetValue(sessionId, out var entry) ? entry.Participants.Values.ToList() : new List<Participant>();

    // ---- Internals ------------------------------------------------------------------

    /// <summary>
    /// Each person in the room once: one person on two devices is one person watching. A guest is
    /// nobody, and is never a viewer anything is recorded for.
    /// </summary>
    private static List<Participant> Viewers(SessionEntry entry) =>
        [.. entry.Participants.Values.Where(p => !p.Guest).GroupBy(p => p.UserId).Select(g => g.First())];

    /// <summary>
    /// Says where a playing film got to, for whoever was watching it, as the state it was playing
    /// under ends. Called under the room's lock, before the state changes.
    /// </summary>
    private void EndSpan(SessionEntry entry, IReadOnlyList<Participant> viewers)
    {
        var state = entry.State;
        if (state.Paused || state.Waiting || state.TitleId is not { } titleId || viewers.Count == 0) return;
        var now = clock.GetUtcNow();
        Watched?.Invoke(new WatchedSpan(titleId, state.PositionAt(now), viewers, now));
    }

    private SessionEntry Entry(string sessionId)
    {
        GetOrCreate(sessionId);
        return _sessions[sessionId];
    }

    /// <summary>
    /// Stamps the next revision and who caused it. Every mutation goes through here. A room whose
    /// players are all waiting stays held through whatever somebody does to it, from wherever that
    /// put it (<see cref="Hold"/>).
    /// </summary>
    private SessionState Advance(SessionState state, Actor actor)
    {
        var waiting = false;
        if (_sessions.TryGetValue(state.SessionId, out var entry))
        {
            entry.LastActivityUtc = clock.GetUtcNow();
            waiting = EveryoneWaiting(entry);
        }

        Changed?.Invoke();
        return state with { Revision = state.Revision + 1, UpdatedBy = actor, Waiting = waiting };
    }

    /// <summary>How busy each room with somebody in it is. No names: this is read by open routes.</summary>
    public IReadOnlyList<(string SessionId, int Participants)> Occupied() =>
        [.. _sessions
            .Where(s => !s.Value.Participants.IsEmpty)
            .Select(s => (s.Key, s.Value.Participants.Count))];

    /// <summary>
    /// Every room with what it is watching and how many are in it, or only the rooms that admit
    /// <paramref name="visibleTo"/> when it names a person: a private room is nobody else's to know
    /// of. Names are left out: this is what tells the rest of the server about rooms, and who is in
    /// one stays inside it.
    /// </summary>
    public IReadOnlyList<RoomSummary> Summaries(string? visibleTo = null)
    {
        var now = clock.GetUtcNow();
        var rooms = new List<RoomSummary>();

        foreach (var (id, entry) in _sessions)
        {
            SessionState state;
            bool open;
            lock (entry.Gate)
            {
                if (visibleTo is not null && !entry.Admission.Admits(visibleTo)) continue;
                state = entry.State;
                open = entry.Admission.Open;
            }

            var manifest = state.TitleId is { } titleId ? library.Get(titleId) : null;
            rooms.Add(new RoomSummary
            {
                SessionId = id,
                TitleId = state.TitleId,
                Name = manifest is null ? null : manifest.Film?.Display ?? manifest.Title,
                DurationSeconds = manifest?.DurationSeconds,
                Paused = state.Paused,
                PositionSeconds = state.PositionAt(now),
                Participants = entry.Participants.Count,
                Open = open
            });
        }

        return rooms;
    }

    /// <summary>Every room as it stands, for writing down.</summary>
    public IReadOnlyList<PersistedSession> Snapshot()
    {
        var rooms = new List<PersistedSession>();

        foreach (var (_, entry) in _sessions)
        {
            lock (entry.Gate)
                rooms.Add(new PersistedSession
                {
                    State = entry.State,
                    LastActivityUtc = entry.LastActivityUtc,
                    Open = entry.Admission.Open,
                    Admitted = [.. entry.Admission.Admitted],
                    Guests = entry.Admission.Guests
                });
        }

        return rooms;
    }

    /// <summary>
    /// Puts back rooms kept from a previous run, before anybody has joined one.
    ///
    /// A restored room keeps its revision and its epoch, so it is the same run of the same room
    /// rather than a new one wearing its name: a client that was connected across the restart
    /// finds the room it already had, and its revision gate never notices. It keeps who may come in:
    /// a restart that opened every private room would be the worst way for one to stop being private.
    ///
    /// Participants are not restored. Membership is a live connection, and every one of them was
    /// dropped when the server stopped; they come back by rejoining, which is what puts the room's
    /// idle clock back where it belongs. A room's guest link is restored with it, and its guests
    /// coming back within the grace find it attended while its people come back too.
    /// </summary>
    public void Restore(IReadOnlyList<PersistedSession> rooms)
    {
        var now = clock.GetUtcNow();
        foreach (var room in rooms)
        {
            if (room.Guests is { } guests && guests.LiveAt(now)) _passes[guests.Id] = room.State.SessionId;
            _sessions[room.State.SessionId] = new SessionEntry
            {
                // What a room holds while it arrives is kept, and its download's tag carries it on.
                // One still being looked for had its search end with the server, so it is said to
                // be unavailable rather than waited on for ever.
                State = room.State.Preparing is { Stage: PreparingStages.Finding } finding
                    ? room.State with { Preparing = finding with { Stage = PreparingStages.Failed, Problem = "unavailable" } }
                    : room.State,
                LastActivityUtc = room.LastActivityUtc,
                Admission = new RoomAdmission(room.Open, room.Admitted,
                    room.Guests is { } kept && kept.LiveAt(now) ? kept : null),
                MembersGoneSince = now
            };
        }
    }

    /// <summary>
    /// Forgets rooms nobody is in and nobody has touched.
    ///
    /// A launch hands out a link, and a link that outlives the evening is a way back into
    /// somebody's film. A room with no one in it and nothing happening for the idle window is
    /// dropped, so the same link opens an empty room rather than resuming what was playing.
    ///
    /// Presence is what keeps a room alive: a film playing to a room full of people is activity
    /// even when hours pass without anyone touching a control.
    /// </summary>
    public IReadOnlyList<string> Sweep(TimeSpan idleFor)
    {
        var cutoff = clock.GetUtcNow() - idleFor;
        var dropped = new List<string>();

        foreach (var (id, entry) in _sessions)
        {
            if (!entry.Participants.IsEmpty) continue;
            if (entry.LastActivityUtc > cutoff) continue;
            if (!_sessions.TryRemove(id, out _)) continue;
            dropped.Add(id);
            lock (entry.Gate)
                if (entry.Admission.Guests is { } guests) _passes.TryRemove(guests.Id, out _);
        }

        if (dropped.Count > 0) Changed?.Invoke();
        return dropped;
    }

    private SessionState WithCurrentHead(SessionState state)
    {
        var manifest = state.TitleId is { } titleId ? library.Get(titleId) : null;
        if (manifest is not { Status: TitleStatus.Transcoding })
            return state with { TranscodeHead = null, RungHeads = null, AudioHead = null };

        return state with
        {
            TranscodeHead = manifest.HeadSeconds,
            RungHeads = TranscodeHeads.RungsOf(manifest),
            AudioHead = TranscodeHeads.AudioOf(manifest)
        };
    }

    /// <summary>
    /// Refuses a position past what has actually been transcoded on the best rung.
    ///
    /// A lagging rung never clamps the room: a viewer is on one rung, and the player steps down
    /// to one that covers where it is.
    ///
    /// This is enforced here and never in the client. A client's idea of the head is always a
    /// little stale, so client-side clamping produces a seek that some of the room accepts and
    /// some rejects — a split timeline that is miserable to reproduce.
    /// </summary>
    private (double Granted, SeekClamped? Clamp) ClampToHead(SessionState state, double requested)
    {
        var requestedPosition = Math.Max(0, requested);
        var head = library.HeadOf(state.TitleId);

        if (head is not { } headSeconds || requestedPosition <= headSeconds)
            return (requestedPosition, null);

        var granted = Math.Max(0, headSeconds - HeadSafetyMargin);
        return (granted, new SeekClamped
        {
            RequestedSeconds = requestedPosition,
            GrantedSeconds = granted,
            HeadSeconds = headSeconds
        });
    }
}
