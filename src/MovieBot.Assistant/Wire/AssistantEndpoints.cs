using System.Text.Json;
using TheKrystalShip.Agent.Service.Commands;
using TheKrystalShip.Agent.Service.Memory;
using TheKrystalShip.Agent.Service.Push;
using TheKrystalShip.Llm.Interfaces;
using TheKrystalShip.Llm.Models;
using TheKrystalShip.MovieBot.Client.Assistant;

namespace TheKrystalShip.MovieBot.Assistant.Wire;

/// <summary>
/// The assistant wire contract (<c>tks-agent/docs/wire-contract.md</c>, version 2.1) as this service
/// serves it: the turn stream, the confirm channel, a person's conversations and the event stream that
/// keeps their other surfaces current, the commands, memory and notification routes of
/// <c>TheKrystalShip.Agent.Service</c>, plus the room verbs the bot writes into a room's conversation.
/// </summary>
/// <remarks>
/// <para>
/// <b>A turn streams when asked to and answers whole otherwise.</b> With <c>Accept:
/// text/event-stream</c> the response is the turn's first watcher and commits <c>200</c> on its first
/// frame; any failure after that is an <c>error</c> frame. Without it, one JSON answer once the turn
/// has finished.
/// </para>
/// <para>
/// <b>Only a client relaying a person names a room.</b> A room on a turn is what lets it move the film
/// for everyone, and the bot is what knows which voice channel somebody is standing in: its token for
/// the person names it as the client acting for them. A person on the site asks about films; their
/// turn names none, whatever the body says.
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
                ? Json(new MeReply(caller.UserId, caller.DisplayName, caller.Relayed))
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
            if (callers.Resolve(request) is not { Relayed: true } caller) return Refuse();
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

            // A chat the client has named and nobody has spoken in yet is empty, not missing: the
            // client mints the id when somebody presses New chat, before the first turn makes it.
            if (summary is null)
                return Json(new ConversationDetail(id, ConversationTitle.NewConversation, [], [], false, false));

            var entries = store.GetHistory(full)
                .Select(e => e.Kind == ConversationEntryKind.Checkpoint || e.Turn is null
                    ? new HistoryEntry("checkpoint", e.Id, e.CreatedAt)
                    : new HistoryEntry("turn", e.Id, e.CreatedAt, e.Turn.StartedAt,
                        new HistoryTurn(
                            e.Turn.UserPrompt, e.Turn.Final, e.Turn.Outcome.ToString().ToLowerInvariant(),
                            [.. e.Turn.Tools.Select(t => new HistoryTool(t.Name.Name, t.Summary, StoredCard(t.Card)))],
                            e.Turn.Usage is { } usage
                                ? new TurnUsage(usage.PromptTokens, usage.ResponseTokens, usage.UsedTokens, usage.ContextWindow, usage.RemainingTokens)
                                : null),
                        e.Feedback is { } feedback
                            ? new HistoryFeedback(feedback.Rating.ToString().ToLowerInvariant(), feedback.Note)
                            : null))
                .ToList();

            return Json(new ConversationDetail(id, summary.Title, entries,
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

        // What every TheKrystalShip assistant serves around the conversation, from TheKrystalShip.Agent.Service
        // over CinemaCallers: the commands, a person's memory, and their notification devices. A
        // notification's button arrives with no token, so its route is on the app with nothing in front.
        app.MapAssistantCommands();
        app.MapAssistantMemory();
        app.MapAssistantPush<StagedAction>(open: app);

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

    private static async Task TurnAsync(
        HttpContext context, Callers callers, TurnHub hub, EventStreams streams, IConversationStore store)
    {
        var request = context.Request;
        if (callers.Resolve(request) is not { } caller)
        {
            await Write(context, Refuse());
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
        if (caller.Relayed && body.Room is { SessionId.Length: > 0 } room)
        {
            turn = AssistantTurn.InRoom(room, caller.UserId, caller.DisplayName, spoken) with { AccessToken = caller.AccessToken };
            scope = turn.ConversationId;
        }
        else
        {
            var chat = body.ConversationId is { Length: > 0 } id ? id : AssistantTurn.MainChat;
            turn = AssistantTurn.ForPerson(caller.UserId, caller.DisplayName, chat) with { Spoken = spoken, AccessToken = caller.AccessToken };
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

    private static async Task ConfirmAsync(
        HttpContext context, Callers callers, StagedActions staged, Client.MovieBotApiClient api, ILoggerFactory loggers)
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
            outcome = await action.Run(new Agreement(api.As(caller.AccessToken), caller.DisplayName), context.RequestAborted);
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

    /// <summary>The person asking, whoever carried the request.</summary>
    private static Caller? Person(HttpRequest request, Callers callers) =>
        callers.Resolve(request) is { UserId.Length: > 0 } caller ? caller : null;

    /// <summary>
    /// A tool's card as the store holds it: written as the card itself and read back as JSON, which is
    /// exactly what the client is sent.
    /// </summary>
    private static JsonElement? StoredCard(object? card) => card switch
    {
        JsonElement element when element.ValueKind == JsonValueKind.Object => element,
        ToolCard toolCard => JsonSerializer.SerializeToElement(toolCard, AssistantJsonContext.Default.ToolCard),
        _ => null,
    };

    private static ConversationRow Row(Caller caller, ConversationSummary summary) => Row(caller.Scope, summary);

    /// <summary>One of a person's conversations as the listing states it, named by the id the client holds.</summary>
    public static ConversationRow Row(string scope, ConversationSummary summary) =>
        new(summary.ConversationId[(scope.Length + 1)..], summary.Title, summary.CreatedAt,
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
        Results.Json(new AssistantError("Sign in to Krystal Cinema."),
            AssistantJsonContext.Default.AssistantError, statusCode: StatusCodes.Status401Unauthorized);

    private static IResult Bad(string why) =>
        Results.Json(new AssistantError(why), AssistantJsonContext.Default.AssistantError, statusCode: StatusCodes.Status400BadRequest);

    private static Task Write(HttpContext context, IResult result) => result.ExecuteAsync(context);
}
