using System.Collections.Concurrent;
using System.Text;
using System.Threading.Channels;

namespace TheKrystalShip.MovieBot.Assistant.Wire;

/// <summary>
/// One turn with a life of its own: what it has said so far, and everybody watching it.
/// </summary>
/// <remarks>
/// A turn is not the request that started it. The request is its first watcher, other surfaces of the
/// same conversation attach to it, and the request going away ends nothing; a stop does. Whatever
/// arrives late is handed the turn so far in one <see cref="TurnAttach"/> and then the frames that
/// follow, so no watcher sees a reply with a hole in it.
/// </remarks>
public sealed class LiveTurn
{
    private readonly object _gate = new();
    private readonly List<Channel<Frame>> _watchers = [];
    private readonly StringBuilder _text = new();
    private readonly List<ToolState> _tools = [];
    private readonly List<CommandProposed> _proposals = [];
    private readonly List<LaunchFrame> _launches = [];
    private readonly TaskCompletionSource<TurnAnswer> _finished = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private DoneFrame? _done;
    private ErrorFrame? _error;

    public LiveTurn(AssistantTurn turn, string prompt, string scope)
    {
        Turn = turn;
        Prompt = prompt;
        Scope = scope;
    }

    public string TurnId { get; } = "t_" + Guid.NewGuid().ToString("N");

    public AssistantTurn Turn { get; }

    public string ConversationId => Turn.ConversationId;

    public string Prompt { get; }

    /// <summary>Whose turn it is to watch and to stop: the person's scope, or the room's conversation.</summary>
    public string Scope { get; }

    public string State { get; private set; } = "queued";

    public CancellationTokenSource Stop { get; } = new();

    public Task<TurnAnswer> Finished => _finished.Task;

    /// <summary>Called with every frame after the turn's own watchers have it, for the event streams attached to its conversation.</summary>
    public Action<Frame>? Relay { get; init; }

    /// <summary>A watcher from now on, with everything that happened before it arrived.</summary>
    public (TurnAttach SoFar, ChannelReader<Frame> Frames) Watch()
    {
        var channel = Channel.CreateUnbounded<Frame>(new UnboundedChannelOptions { SingleReader = true });
        lock (_gate)
        {
            if (_done is not null || _error is not null) channel.Writer.TryComplete();
            else _watchers.Add(channel);
            return (Snapshot(), channel.Reader);
        }
    }

    public TurnAttach Snapshot()
    {
        lock (_gate)
        {
            return new TurnAttach(TurnId, ConversationId, Prompt, State, _text.ToString(), [.. _tools],
                [.. _proposals], [.. _launches], _done, _error);
        }
    }

    public void Running()
    {
        lock (_gate) State = "running";
    }

    public void Publish(Frame frame)
    {
        lock (_gate)
        {
            switch (frame)
            {
                case TextDelta delta: _text.Append(delta.Text); break;
                case ToolStartFrame start: _tools.Add(new ToolState(start.Id, start.Tool, "running", null)); break;
                case ToolResultFrame result:
                    var at = _tools.FindIndex(t => t.Id == result.Id);
                    if (at >= 0) _tools[at] = _tools[at] with { State = "done", Summary = result.Summary };
                    break;
                case CommandProposed proposed: _proposals.Add(proposed); break;
                case LaunchFrame launch: _launches.Add(launch); break;
                case DoneFrame done: _done = done; State = "done"; break;
                case ErrorFrame error: _error = error; State = "error"; break;
            }

            foreach (var watcher in _watchers) watcher.Writer.TryWrite(frame);
            if (frame is DoneFrame or ErrorFrame)
            {
                foreach (var watcher in _watchers) watcher.Writer.TryComplete();
                _watchers.Clear();
            }
        }

        Relay?.Invoke(frame);
    }

    public void Complete(TurnAnswer answer) => _finished.TrySetResult(answer);

    public void Fail(Exception ex) => _finished.TrySetException(ex);
}

/// <summary>
/// Runs turns one at a time per conversation, and knows which is running where.
/// </summary>
/// <remarks>
/// A conversation's turns share its history, so a second prompt waits for the first rather than both
/// reading a history neither has finished writing. Up to three wait; a fourth is refused, because a
/// queue that grows without bound is a room asking the same thing over and over while the model is
/// slow. The queue is in memory and dies with the service.
/// </remarks>
public sealed class TurnHub(CinemaAssistant assistant, EventStreams streams, ILogger<TurnHub> logger)
{
    public const int MostWaiting = 3;

    private readonly ConcurrentDictionary<string, Line> _lines = new();
    private readonly ConcurrentDictionary<string, LiveTurn> _turns = new();

    private sealed class Line
    {
        public SemaphoreSlim Gate { get; } = new(1, 1);
        public List<LiveTurn> Waiting { get; } = [];
        public LiveTurn? Running { get; set; }
    }

    /// <summary>
    /// Queues a turn and returns it before it runs, with <paramref name="watch"/> already watching it, so
    /// the first watcher misses nothing. Null when the conversation's queue is full.
    /// </summary>
    public (LiveTurn Turn, TurnAttach SoFar, ChannelReader<Frame> Frames)? Start(AssistantTurn turn, string prompt, string scope)
    {
        var line = _lines.GetOrAdd(turn.ConversationId, _ => new Line());
        var live = new LiveTurn(turn, prompt, scope) { Relay = frame => streams.ToConversation(turn.ConversationId, frame) };

        lock (line)
        {
            if (line.Waiting.Count >= MostWaiting) return null;
            line.Waiting.Add(live);
        }

        var (soFar, frames) = live.Watch();
        _turns[live.TurnId] = live;
        Announce(turn.ConversationId, line);
        _ = Task.Run(() => RunAsync(line, live));
        return (live, soFar, frames);
    }

    private async Task RunAsync(Line line, LiveTurn live)
    {
        try
        {
            await line.Gate.WaitAsync(live.Stop.Token);
        }
        catch (OperationCanceledException)
        {
            lock (line) line.Waiting.Remove(live);
            live.Publish(new ErrorFrame("assistant_failed", "Stopped before it started."));
            live.Complete(new TurnAnswer("", [], [], Failed: true));
            Forget(live, line);
            return;
        }

        try
        {
            lock (line)
            {
                line.Waiting.Remove(live);
                line.Running = live;
            }

            live.Running();
            Announce(live.ConversationId, line);
            var answer = await assistant.RunAsync(live.Turn, live.Prompt, live.Publish, live.Stop.Token);
            live.Complete(answer);
        }
        catch (OperationCanceledException) when (live.Stop.IsCancellationRequested)
        {
            live.Publish(new ErrorFrame("assistant_failed", "Stopped."));
            live.Complete(new TurnAnswer("", [], [], Failed: true));
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Assistant: the turn in {Conversation} failed", live.ConversationId);
            live.Publish(new ErrorFrame("assistant_failed", CinemaAssistant.FailedReply));
            live.Fail(ex);
        }
        finally
        {
            lock (line) line.Running = null;
            line.Gate.Release();
            streams.ToScope(live.Scope, new ConversationFrame("activity", live.ConversationId, null));
            Forget(live, line);
        }
    }

    private void Forget(LiveTurn live, Line line)
    {
        _turns.TryRemove(live.TurnId, out _);
        Announce(live.ConversationId, line);
    }

    private void Announce(string conversationId, Line line)
    {
        TurnQueue queue;
        lock (line)
            queue = new TurnQueue(conversationId, line.Running?.TurnId,
                [.. line.Waiting.Select(w => new QueuedTurn(w.TurnId, w.Prompt))]);
        streams.ToConversation(conversationId, queue);
    }

    /// <summary>What is running in a conversation and what waits behind it, as one stream is told on attaching.</summary>
    public (LiveTurn? Running, TurnQueue Queue) InConversation(string conversationId)
    {
        if (!_lines.TryGetValue(conversationId, out var line))
            return (null, new TurnQueue(conversationId, null, []));

        lock (line)
            return (line.Running, new TurnQueue(conversationId, line.Running?.TurnId,
                [.. line.Waiting.Select(w => new QueuedTurn(w.TurnId, w.Prompt))]));
    }

    /// <summary>Stops a turn, running or waiting. False for one that is unknown or not in this scope.</summary>
    public bool Stop(string turnId, string scope)
    {
        if (!_turns.TryGetValue(turnId, out var live) || live.Scope != scope) return false;
        live.Stop.Cancel();
        return true;
    }
}

/// <summary>
/// The open event streams: one per surface a person has open, each scoped to that person and
/// attached to at most one conversation.
/// </summary>
/// <remarks>
/// Nothing is buffered for a stream that is not connected, and there is no replay: a surface that
/// reconnects re-reads the listing. That keeps the stream an optimisation rather than a second way of
/// getting a transcript that could drift from the first.
/// </remarks>
public sealed class EventStreams
{
    private readonly ConcurrentDictionary<string, EventStream> _open = new();

    public sealed class EventStream(string id, string scope)
    {
        public string Id { get; } = id;
        public string Scope { get; } = scope;

        /// <summary>The conversation this stream watches turns in, by its full id.</summary>
        public string? Attached { get; set; }

        public Channel<Frame> Frames { get; } = Channel.CreateUnbounded<Frame>(new UnboundedChannelOptions { SingleReader = true });

        /// <summary>
        /// Queues a frame, naming its conversation the way the client does: the id it holds, without
        /// the scope every one of its conversations sits under.
        /// </summary>
        public void Send(Frame frame) => Frames.Writer.TryWrite(frame switch
        {
            ConversationFrame f => f with { ConversationId = Local(f.ConversationId) },
            TurnAttach f => f with { ConversationId = Local(f.ConversationId) },
            TurnQueue f => f with { ConversationId = Local(f.ConversationId) },
            _ => frame,
        });

        private string Local(string conversationId) =>
            conversationId.StartsWith(Scope + ":", StringComparison.Ordinal) ? conversationId[(Scope.Length + 1)..] : conversationId;
    }

    public EventStream Open(string scope)
    {
        var stream = new EventStream(Guid.NewGuid().ToString("N"), scope);
        _open[stream.Id] = stream;
        return stream;
    }

    public void Close(string id)
    {
        if (_open.TryRemove(id, out var stream)) stream.Frames.Writer.TryComplete();
    }

    /// <summary>The stream with this id, when it belongs to this scope.</summary>
    public EventStream? Find(string? id, string scope) =>
        id is not null && _open.TryGetValue(id, out var stream) && stream.Scope == scope ? stream : null;

    /// <summary>A change to the chat list, for every surface of that person.</summary>
    public void ToScope(string scope, Frame frame)
    {
        foreach (var stream in _open.Values)
            if (stream.Scope == scope) stream.Send(frame);
    }

    /// <summary>A turn's frame, for the surfaces attached to its conversation.</summary>
    public void ToConversation(string conversationId, Frame frame)
    {
        foreach (var stream in _open.Values)
            if (stream.Attached == conversationId) stream.Send(frame);
    }
}
