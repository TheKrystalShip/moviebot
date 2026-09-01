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
                Revision = 0
            }
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
        return [.. entry.Participants.Values];
    }

    public IReadOnlyList<Participant> Leave(string sessionId, string connectionId)
    {
        if (!_sessions.TryGetValue(sessionId, out var entry)) return [];
        entry.Participants.TryRemove(connectionId, out _);
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
    private static SessionState Advance(SessionState state, Actor actor) =>
        state with { Revision = state.Revision + 1, UpdatedBy = actor };

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
