using System.Collections.Concurrent;
using TheKrystalShip.MovieBot.Api.Library;
using TheKrystalShip.MovieBot.Core;

namespace TheKrystalShip.MovieBot.Api.Sessions;

/// <summary>The outcome of a mutation: the new state, plus a clamp notice when one was applied.</summary>
public sealed record MutationResult(SessionState State, SeekClamped? Clamped);

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

        /// <summary>When somebody last did something here. Opening the room to look does not count.</summary>
        public DateTimeOffset LastActivityUtc;
    }

    public SessionState GetOrCreate(string sessionId)
    {
        var entry = _sessions.GetOrAdd(sessionId, id => new SessionEntry
        {
            State = new SessionState
            {
                SessionId = id,
                Paused = true,
                PositionSeconds = 0,
                AnchorUtc = clock.GetUtcNow(),
                Revision = 0,
                // Minted with the room, carried by every state it ever produces, and different
                // from the one the room before it carried.
                Epoch = Guid.NewGuid().ToString("n")
            },
            LastActivityUtc = clock.GetUtcNow()
        });

        lock (entry.Gate)
            return WithCurrentHead(entry.State);
    }

    public SessionState? Get(string sessionId) =>
        _sessions.TryGetValue(sessionId, out var entry) ? WithCurrentHead(entry.State) : null;

    /// <summary>
    /// Puts a film into the room, from the start.
    ///
    /// Whether the room is playing is left as it was. A room that was watching one film and is
    /// handed another goes on playing, from the beginning of the new one, so changing the film
    /// is one act rather than a change followed by everyone waiting for somebody to press play.
    /// A room that was paused stays paused.
    /// </summary>
    public MutationResult LoadTitle(string sessionId, string titleId, Actor actor)
    {
        var entry = Entry(sessionId);
        lock (entry.Gate)
        {
            entry.State = Advance(entry.State, actor) with
            {
                TitleId = titleId,
                PositionSeconds = 0,
                AnchorUtc = clock.GetUtcNow()
            };
            return new MutationResult(WithCurrentHead(entry.State), null);
        }
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

    public IReadOnlyList<Participant> Join(string sessionId, string connectionId, Participant participant)
    {
        var entry = Entry(sessionId);
        entry.Participants[connectionId] = participant;
        entry.LastActivityUtc = clock.GetUtcNow();

        // Somebody arriving puts the room's idle clock back, which is worth keeping: a room being
        // watched must not be swept on the next start for having last been touched hours ago.
        Changed?.Invoke();
        return entry.Participants.Values.ToList();
    }

    public IReadOnlyList<Participant> Leave(string sessionId, string connectionId)
    {
        if (!_sessions.TryGetValue(sessionId, out var entry)) return new List<Participant>();
        entry.Participants.TryRemove(connectionId, out _);
        // The clock on an empty room starts when the last person leaves it.
        entry.LastActivityUtc = clock.GetUtcNow();

        Changed?.Invoke();
        return entry.Participants.Values.ToList();
    }

    public IReadOnlyList<Participant> Participants(string sessionId) =>
        _sessions.TryGetValue(sessionId, out var entry) ? entry.Participants.Values.ToList() : new List<Participant>();

    // ---- Internals ------------------------------------------------------------------

    private SessionEntry Entry(string sessionId)
    {
        GetOrCreate(sessionId);
        return _sessions[sessionId];
    }

    /// <summary>Stamps the next revision and who caused it. Every mutation goes through here.</summary>
    private SessionState Advance(SessionState state, Actor actor)
    {
        if (_sessions.TryGetValue(state.SessionId, out var entry))
            entry.LastActivityUtc = clock.GetUtcNow();

        Changed?.Invoke();
        return state with { Revision = state.Revision + 1, UpdatedBy = actor };
    }

    /// <summary>How busy each room with somebody in it is. No names: this is read by open routes.</summary>
    public IReadOnlyList<(string SessionId, int Participants)> Occupied() =>
        [.. _sessions
            .Where(s => !s.Value.Participants.IsEmpty)
            .Select(s => (s.Key, s.Value.Participants.Count))];

    /// <summary>
    /// Every room with what it is watching and how many are in it. Names are left out: this is
    /// what tells the rest of the server about rooms, and who is in one stays inside it.
    /// </summary>
    public IReadOnlyList<RoomSummary> Summaries()
    {
        var now = clock.GetUtcNow();
        var rooms = new List<RoomSummary>();

        foreach (var (id, entry) in _sessions)
        {
            SessionState state;
            lock (entry.Gate) state = entry.State;

            var manifest = state.TitleId is { } titleId ? library.Get(titleId) : null;
            rooms.Add(new RoomSummary
            {
                SessionId = id,
                TitleId = state.TitleId,
                Name = manifest is null ? null : manifest.Film?.Display ?? manifest.Title,
                DurationSeconds = manifest?.DurationSeconds,
                Paused = state.Paused,
                PositionSeconds = state.PositionAt(now),
                Participants = entry.Participants.Count
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
                    LastActivityUtc = entry.LastActivityUtc
                });
        }

        return rooms;
    }

    /// <summary>
    /// Puts back rooms kept from a previous run, before anybody has joined one.
    ///
    /// A restored room keeps its revision and its epoch, so it is the same run of the same room
    /// rather than a new one wearing its name: a client that was connected across the restart
    /// finds the room it already had, and its revision gate never notices.
    ///
    /// Participants are not restored. Membership is a live connection, and every one of them was
    /// dropped when the server stopped; they come back by rejoining, which is what puts the room's
    /// idle clock back where it belongs.
    /// </summary>
    public void Restore(IReadOnlyList<PersistedSession> rooms)
    {
        foreach (var room in rooms)
        {
            _sessions[room.State.SessionId] = new SessionEntry
            {
                State = room.State,
                LastActivityUtc = room.LastActivityUtc
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
            if (_sessions.TryRemove(id, out _)) dropped.Add(id);
        }

        if (dropped.Count > 0) Changed?.Invoke();
        return dropped;
    }

    private SessionState WithCurrentHead(SessionState state) =>
        state with { TranscodeHead = library.HeadOf(state.TitleId) };

    /// <summary>
    /// Refuses a position past what has actually been transcoded.
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
