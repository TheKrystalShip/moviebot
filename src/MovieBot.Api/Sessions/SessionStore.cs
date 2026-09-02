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

    public MutationResult LoadTitle(string sessionId, string titleId, Actor actor)
    {
        var entry = Entry(sessionId);
        lock (entry.Gate)
        {
            entry.State = Advance(entry.State, actor) with
            {
                TitleId = titleId,
                Paused = true,
                PositionSeconds = 0,
                AnchorUtc = clock.GetUtcNow()
            };
            return new MutationResult(WithCurrentHead(entry.State), null);
        }
    }

    public MutationResult Play(string sessionId, double atSeconds, Actor actor)
    {
        var entry = Entry(sessionId);
        lock (entry.Gate)
        {
            var (granted, clamp) = ClampToHead(entry.State, atSeconds);
            entry.State = Advance(entry.State, actor) with
            {
                Paused = false,
                PositionSeconds = granted,
                AnchorUtc = clock.GetUtcNow()
            };
            return new MutationResult(WithCurrentHead(entry.State), clamp);
        }
    }

    public MutationResult Pause(string sessionId, double atSeconds, Actor actor)
    {
        var entry = Entry(sessionId);
        lock (entry.Gate)
        {
            entry.State = Advance(entry.State, actor) with
            {
                Paused = true,
                PositionSeconds = Math.Max(0, atSeconds),
                AnchorUtc = clock.GetUtcNow()
            };
            return new MutationResult(WithCurrentHead(entry.State), null);
        }
    }

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

    // ---- Participants ---------------------------------------------------------------

    public IReadOnlyList<Participant> Join(string sessionId, string connectionId, Participant participant)
    {
        var entry = Entry(sessionId);
        entry.Participants[connectionId] = participant;
        entry.LastActivityUtc = clock.GetUtcNow();

        // Somebody arriving puts the room's idle clock back, which is worth keeping: a room being
        // watched must not be swept on the next start for having last been touched hours ago.
        Changed?.Invoke();
        return [.. entry.Participants.Values];
    }

    public IReadOnlyList<Participant> Leave(string sessionId, string connectionId)
    {
        if (!_sessions.TryGetValue(sessionId, out var entry)) return [];
        entry.Participants.TryRemove(connectionId, out _);
        // The clock on an empty room starts when the last person leaves it.
        entry.LastActivityUtc = clock.GetUtcNow();

        Changed?.Invoke();
        return [.. entry.Participants.Values];
    }

    public IReadOnlyList<Participant> Participants(string sessionId) =>
        _sessions.TryGetValue(sessionId, out var entry) ? [.. entry.Participants.Values] : [];

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
