using System.Text.Json;
using TheKrystalShip.Llm.Interfaces;
using TheKrystalShip.Llm.Models;
using TheKrystalShip.MovieBot.Client.Assistant;

namespace TheKrystalShip.MovieBot.Assistant.Wire;

/// <summary>
/// The assistant wire contract (<c>kgsm-llm/docs/wire-contract.md</c>, version 2.1) as this service
/// serves it: the turn stream, the confirm channel, a person's conversations and the event stream that
/// keeps their other surfaces current, plus the room verbs the bot writes into a room's conversation.
/// </summary>
/// <remarks>
/// <para>
/// <b>A turn streams when asked to and answers whole otherwise.</b> With <c>Accept:
/// text/event-stream</c> the response is the turn's first watcher and commits <c>200</c> on its first
/// frame; any failure after that is an <c>error</c> frame. Without it, one JSON answer once the turn
/// has finished.
/// </para>
/// <para>
/// <b>Only a service names a room.</b> A room on a turn is what lets it move the film for everyone, and
/// the bot is what knows which voice channel somebody is standing in. A person on the site asks about
/// films; their turn names none, whatever the body says.
/// </para>
/// </remarks>
public static class AssistantEndpoints
{
    private const string EventStream = "text/event-stream";
    private const string OriginHeader = "X-Assistant-Origin";

    public static void MapAssistant(this WebApplication app)
    {
        app.MapGet("/health", () => Results.Ok());

        app.MapGet("/me", (HttpRequest request, Callers callers) =>
            callers.Resolve(request) is { } caller
                ? Json(new MeReply(caller.UserId, caller.DisplayName, caller.Service))
                : Refuse());

        // Speech is the bot's, in a voice channel. The site has neither a microphone nor a voice here.
        app.MapGet("/speech", (HttpRequest request, Callers callers) =>
            callers.Resolve(request) is null ? Refuse() : Json(new SpeechReply(false, false)));

        app.MapPost("/turn", TurnAsync);
        app.MapDelete("/turns/{turnId}", (string turnId, HttpRequest request, Callers callers, TurnHub hub) =>
            callers.Resolve(request) is not { } caller ? Refuse()
            : hub.Stop(turnId, caller.Scope) ? Results.NoContent() : Results.NotFound());

        app.MapPost("/confirm", ConfirmAsync);

        // A room verb the bot carried out without the model, written into the room's conversation so
        // a question about it a minute later is answered by an assistant that knows.
        app.MapPost("/rooms/record", async (HttpRequest request, Callers callers, CinemaAssistant assistant) =>
        {
            if (callers.Resolve(request) is not { Service: true } caller || caller.UserId.Length == 0) return Refuse();
            if (await Read(request, AssistantJsonContext.Default.RecordRequest) is not { } body) return Bad("A record names a room, a tool and an outcome.");

            assistant.RecordAct(AssistantTurn.InRoom(body.Room, caller.UserId, caller.DisplayName),
                body.Said, body.Tool, body.Arguments, body.Outcome);
            return Results.NoContent();
        });

        app.MapGet("/conversations", (HttpRequest request, Callers callers, IConversationStore store) =>
        {
            if (Person(request, callers) is not { } caller) return Refuse();
            return Json(store.ListConversations(caller.Scope)
                .Where(c => c.ConversationId.StartsWith(caller.Scope + ":", StringComparison.Ordinal))
                .OrderByDescending(c => c.LastActivityAt)
                .Select(c => Row(caller, c))
                .ToList());
        });

        app.MapGet("/conversations/{id}", (string id, HttpRequest request, Callers callers, IConversationStore store, StagedActions staged) =>
        {
            if (Person(request, callers) is not { } caller) return Refuse();
            var full = AssistantTurn.PersonConversation(caller.UserId, id);
            var summary = store.ListConversations(full).FirstOrDefault(c => c.ConversationId == full);
            if (summary is null) return Results.NotFound();

            var turns = store.GetHistory(full)
                .Where(e => e.Kind == ConversationEntryKind.Turn && e.Turn is not null)
                .Select(e => new HistoryTurn(
                    e.Id, e.Turn!.UserPrompt, e.Turn.UserDisplay, e.Turn.StartedAt, e.Turn.CompletedAt,
                    e.Turn.Outcome.ToString().ToLowerInvariant(), e.Turn.Final,
                    [.. e.Turn.Tools.Select((t, i) => new HistoryTool($"tc_{i}", t.Name.Name, t.Arguments, t.Summary))],
                    e.Feedback?.Rating.ToString().ToLowerInvariant()))
                .ToList();

            return Json(new ConversationDetail(id, summary.Title, turns,
                [.. staged.Pending(full).Select(a => a.Frame())], false, false));
        });

        app.MapDelete("/conversations/{id}", (string id, HttpRequest request, Callers callers, IConversationStore store, EventStreams streams) =>
        {
            if (Person(request, callers) is not { } caller) return Refuse();
            store.SoftDelete(AssistantTurn.PersonConversation(caller.UserId, id));
            streams.ToScope(caller.Scope, new ConversationFrame("deleted", id, Origin(request)));
            return Results.NoContent();
        });

        app.MapPost("/conversations/{id}/turns/{turnId:long}/feedback", async (
            string id, long turnId, HttpRequest request, Callers callers, IConversationStore store) =>
        {
            if (Person(request, callers) is not { } caller) return Refuse();
            var body = await Read(request, AssistantJsonContext.Default.FeedbackRequest);
            TurnFeedbackRating? rating = body?.Rating switch
            {
                "up" => TurnFeedbackRating.Up,
                "down" => TurnFeedbackRating.Down,
                _ => null,
            };

            return store.SetTurnFeedback(AssistantTurn.PersonConversation(caller.UserId, id), turnId, rating, body?.Note)
                ? Results.NoContent()
                : Results.NotFound();
        });

        app.MapGet("/commands", (HttpRequest request, Callers callers) =>
            callers.Resolve(request) is null ? Refuse() : Json(Commands));

        app.MapPost("/commands/{name}", async (string name, HttpRequest request, Callers callers, IConversationStore store, EventStreams streams) =>
        {
            if (Person(request, callers) is not { } caller) return Refuse();
            var body = await Read(request, AssistantJsonContext.Default.CommandRequest);

            switch (name)
            {
                case "help":
                    return Json(new CommandReply("These are the commands.", Commands: Commands));

                case "new":
                    // The offered id is taken up only while it holds nothing: "start a fresh
                    // conversation" cannot mean the one it was typed in.
                    var offered = body?.ConversationId;
                    var fresh = offered is { Length: > 0 }
                                && store.GetHistory(AssistantTurn.PersonConversation(caller.UserId, offered)).Count == 0
                        ? offered
                        : Guid.NewGuid().ToString("N");
                    var full = AssistantTurn.PersonConversation(caller.UserId, fresh);
                    store.CreateConversation(full);
                    streams.ToScope(caller.Scope, new ConversationFrame("started", fresh, Origin(request)));

                    var row = store.ListConversations(full).FirstOrDefault(c => c.ConversationId == full);
                    return Json(new CommandReply("Started a new conversation.", fresh,
                        row is null ? null : Row(caller, row)));

                default:
                    return Results.NotFound();
            }
        });

        app.MapGet("/events", EventsAsync);

        app.MapPost("/events/attach", async (HttpRequest request, Callers callers, EventStreams streams, TurnHub hub) =>
        {
            if (Person(request, callers) is not { } caller) return Refuse();
            if (streams.Find(request.Headers[OriginHeader].ToString(), caller.Scope) is not { } stream)
                return Bad("Attach names the stream it is made from, in X-Assistant-Origin.");

            var body = await Read(request, AssistantJsonContext.Default.AttachRequest);
            var conversation = body?.ConversationId is { Length: > 0 } id
                ? AssistantTurn.PersonConversation(caller.UserId, id)
                : null;
            stream.Attached = conversation;

            // Attaching answers on the stream, so a surface renders from one source.
            if (conversation is not null)
            {
                var (running, queue) = hub.InConversation(conversation);
                stream.Send(running is not null ? running.Snapshot() : queue);
            }

            return Results.NoContent();
        });
    }

    private static readonly List<CommandEntry> Commands =
    [
        new("new", "Starts a new conversation.", "assistant:chat", false, []),
        new("help", "Lists the commands.", "assistant:chat", false, []),
    ];

    private static async Task TurnAsync(
        HttpContext context, Callers callers, TurnHub hub, EventStreams streams, IConversationStore store)
    {
        var request = context.Request;
        if (callers.Resolve(request) is not { } caller)
        {
            await Write(context, Refuse());
            return;
        }

        if (caller.UserId.Length == 0)
        {
            await Write(context, Bad($"A service names the person it speaks for in {Callers.ActingHeader}."));
            return;
        }

        if (await Read(request, AssistantJsonContext.Default.TurnRequest) is not { Prompt.Length: > 0 } body)
        {
            await Write(context, Bad("A turn needs a prompt."));
            return;
        }

        var spoken = string.Equals(body.Style, "voice", StringComparison.OrdinalIgnoreCase);
        AssistantTurn turn;
        string scope;
        if (caller.Service && body.Room is { SessionId.Length: > 0 } room)
        {
            turn = AssistantTurn.InRoom(room, caller.UserId, caller.DisplayName, spoken);
            scope = turn.ConversationId;
        }
        else
        {
            var chat = body.ConversationId is { Length: > 0 } id ? id : "main";
            turn = AssistantTurn.ForPerson(caller.UserId, caller.DisplayName, chat) with { Spoken = spoken };
            scope = caller.Scope;

            if (store.CreateConversation(turn.ConversationId))
                streams.ToScope(scope, new ConversationFrame("started", chat, Origin(request)));
        }

        if (hub.Start(turn, body.Prompt, scope) is not { } started)
        {
            await Write(context, Results.Json(new AssistantError("queue_full"), AssistantJsonContext.Default.AssistantError,
                statusCode: StatusCodes.Status409Conflict));
            return;
        }

        var (live, _, frames) = started;
        if (!Streams(request))
        {
            var answer = await live.Finished;
            await Write(context, Json(new TurnReply(answer.Text, answer.TurnId, answer.Failed,
                [.. answer.Proposed.Select(a => a.Frame())], answer.Launched)));
            return;
        }

        // The response is one watcher of the turn. Leaving stops watching; the turn runs on.
        await OpenStreamAsync(context);
        await PumpAsync(context, frames, context.RequestAborted);
    }

    private static async Task ConfirmAsync(HttpContext context, Callers callers, StagedActions staged, ILoggerFactory loggers)
    {
        var request = context.Request;
        if (callers.Resolve(request) is not { } caller)
        {
            await Write(context, Refuse());
            return;
        }

        var body = await Read(request, AssistantJsonContext.Default.ConfirmRequest);

        // An unknown handle, a spent one, an expired one and somebody else's are refused alike, and a
        // refused handle stays where it was: a guess cannot cancel what its owner is about to approve.
        // In a room anybody may agree, so a room's proposal has no owner to check.
        if (staged.Take(body?.Token ?? "", caller.UserId) is not { } action)
        {
            await Write(context, Results.Json(
                new AssistantError("That has already been answered, has expired, or is not yours to answer."),
                AssistantJsonContext.Default.AssistantError, statusCode: StatusCodes.Status404NotFound));
            return;
        }

        var logger = loggers.CreateLogger("TheKrystalShip.MovieBot.Assistant.Confirm");
        logger.LogInformation("Assistant: {Approver} agreed to {What}, asked for by {Asker}",
            caller.DisplayName, action.Describes, action.AskedByName);

        var streaming = Streams(request);
        if (streaming) await OpenStreamAsync(context);

        ConfirmOutcome outcome;
        try
        {
            outcome = await action.Run(context.RequestAborted);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Assistant: carrying out {What} failed", action.Describes);
            outcome = new ConfirmOutcome { Text = "That did not work: the API did not answer.", Success = false, Verdict = "failed", Kind = action.Kind };
        }

        var verdict = new ConfirmVerdict(outcome.Verdict, action.Verb);
        if (streaming)
            await WriteFrameAsync(context, new ConfirmResult(outcome.Text, outcome.Success, verdict, outcome), context.RequestAborted);
        else
            await Write(context, Json(new ConfirmReply(outcome.Text, outcome.Success, verdict, outcome)));
    }

    private static async Task EventsAsync(HttpContext context, Callers callers, EventStreams streams)
    {
        if (Person(context.Request, callers) is not { } caller)
        {
            await Write(context, Refuse());
            return;
        }

        var stream = streams.Open(caller.Scope);
        try
        {
            await OpenStreamAsync(context);
            await WriteFrameAsync(context, new HelloFrame(stream.Id), context.RequestAborted);
            await PumpAsync(context, stream.Frames.Reader, context.RequestAborted, untilDone: false);
        }
        finally
        {
            streams.Close(stream.Id);
        }
    }

    /// <summary>
    /// Writes frames until the turn ends or the caller leaves, with a comment every fifteen seconds so
    /// a proxy does not take a quiet wait for a dead socket.
    /// </summary>
    private static async Task PumpAsync(
        HttpContext context, System.Threading.Channels.ChannelReader<Frame> frames, CancellationToken ct, bool untilDone = true)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                using var beat = CancellationTokenSource.CreateLinkedTokenSource(ct);
                beat.CancelAfter(TimeSpan.FromSeconds(15));
                bool more;
                try
                {
                    more = await frames.WaitToReadAsync(beat.Token);
                }
                catch (OperationCanceledException) when (!ct.IsCancellationRequested)
                {
                    await context.Response.WriteAsync(": ping\n\n", ct);
                    await context.Response.Body.FlushAsync(ct);
                    continue;
                }

                if (!more) return;
                while (frames.TryRead(out var frame))
                {
                    await WriteFrameAsync(context, frame, ct);
                    if (untilDone && frame is DoneFrame or ErrorFrame) return;
                }
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
        }
    }

    private static async Task OpenStreamAsync(HttpContext context)
    {
        context.Response.StatusCode = StatusCodes.Status200OK;
        context.Response.ContentType = EventStream;
        context.Response.Headers.CacheControl = "no-cache";
        context.Response.Headers["X-Accel-Buffering"] = "no";
        await context.Response.Body.FlushAsync(context.RequestAborted);
    }

    private static async Task WriteFrameAsync(HttpContext context, Frame frame, CancellationToken ct)
    {
        var data = JsonSerializer.Serialize(frame, AssistantJsonContext.Default.Frame);
        await context.Response.WriteAsync($"event: {frame.Type}\ndata: {data}\n\n", ct);
        await context.Response.Body.FlushAsync(ct);
    }

    /// <summary>A person, or a service speaking for one. A service naming nobody has no conversations.</summary>
    private static Caller? Person(HttpRequest request, Callers callers) =>
        callers.Resolve(request) is { UserId.Length: > 0 } caller ? caller : null;

    private static ConversationRow Row(Caller caller, ConversationSummary summary) =>
        new(summary.ConversationId[(caller.Scope.Length + 1)..], summary.Title, summary.CreatedAt,
            summary.LastActivityAt, summary.TurnCount, false, false);

    private static bool Streams(HttpRequest request) =>
        request.Headers.Accept.ToString().Contains(EventStream, StringComparison.OrdinalIgnoreCase);

    private static string? Origin(HttpRequest request) =>
        request.Headers[OriginHeader].ToString() is { Length: > 0 } origin ? origin : null;

    private static async Task<T?> Read<T>(HttpRequest request, System.Text.Json.Serialization.Metadata.JsonTypeInfo<T> type)
    {
        try
        {
            return await request.ReadFromJsonAsync(type, request.HttpContext.RequestAborted);
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException)
        {
            return default;
        }
    }

    private static IResult Json<T>(T value) =>
        Results.Json(value, (System.Text.Json.Serialization.Metadata.JsonTypeInfo<T>)AssistantJsonContext.Default.GetTypeInfo(typeof(T))!);

    private static IResult Refuse() =>
        Results.Json(new AssistantError("Sign in through Discord, or speak for somebody with the service key."),
            AssistantJsonContext.Default.AssistantError, statusCode: StatusCodes.Status401Unauthorized);

    private static IResult Bad(string why) =>
        Results.Json(new AssistantError(why), AssistantJsonContext.Default.AssistantError, statusCode: StatusCodes.Status400BadRequest);

    private static Task Write(HttpContext context, IResult result) => result.ExecuteAsync(context);
}
