using System.Text.Json;
using System.Threading.Channels;
using TheKrystalShip.Agent.Service;
using TheKrystalShip.Agent.Service.Turns;
using TheKrystalShip.Llm.Interfaces;
using TheKrystalShip.MovieBot.Client.Assistant;

namespace TheKrystalShip.MovieBot.Assistant.Wire;

/// <summary>
/// A turn as this service runs it: who asked and about what, and what it proposed and the films it put on
/// or found, kept for the whole answer the bot is given.
/// </summary>
public sealed record CinemaTurn(AssistantTurn Turn)
{
    private readonly List<CommandProposed> _proposed = [];
    private readonly List<LaunchFrame> _launched = [];

    public IReadOnlyList<CommandProposed> Proposed { get { lock (_proposed) return [.. _proposed]; } }

    public IReadOnlyList<LaunchFrame> Launched { get { lock (_launched) return [.. _launched]; } }

    internal void Saw(Frame frame)
    {
        switch (frame)
        {
            case CommandProposed proposed: lock (_proposed) _proposed.Add(proposed); break;
            case LaunchFrame launch: lock (_launched) _launched.Add(launch); break;
        }
    }
}

/// <summary>
/// A turn of MovieBot's, as the frames every surface watching it receives: the assistant's own frames in
/// the contract's shapes, and what is this product's — a proposal, a film's card, <c>launch</c> — as this
/// service writes it, an absent field left out.
/// </summary>
public sealed class CinemaTurnRunner(CinemaAssistant assistant, ILogger<CinemaTurnRunner> logger) : IAssistantTurnRunner
{
    public IAsyncEnumerable<TurnFrame> RunAsync(AssistantTurnContext turn, CancellationToken ct)
    {
        var cinema = (CinemaTurn)turn.Run.Host!;
        var frames = Channel.CreateUnbounded<TurnFrame>(new UnboundedChannelOptions { SingleReader = true });

        _ = Task.Run(async () =>
        {
            try
            {
                await assistant.RunAsync(cinema.Turn, turn.Run.Prompt, frame =>
                {
                    cinema.Saw(frame);
                    frames.Writer.TryWrite(Standard(frame));
                }, ct);
                frames.Writer.TryComplete();
            }
            catch (OperationCanceledException ex) when (ct.IsCancellationRequested)
            {
                frames.Writer.TryComplete(ex);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Assistant: the turn in {Conversation} failed", turn.Conversation.Key);
                frames.Writer.TryWrite(new TurnFrame(
                    TurnStream.Error, new StreamErrorEvent("assistant_failed", CinemaAssistant.FailedReply)));
                frames.Writer.TryComplete();
            }
        }, CancellationToken.None);

        return frames.Reader.ReadAllAsync(ct);
    }

    /// <summary>One of the assistant's frames in the contract's shape, or one of this product's as it writes it.</summary>
    private static TurnFrame Standard(Frame frame) => frame switch
    {
        TextDelta delta => new TurnFrame(TurnStream.TextDelta, new TokenEvent(delta.Text)),
        ToolStartFrame start => new TurnFrame(TurnStream.ToolStart, new ToolStartEvent(start.Id, start.Tool, start.Arguments)),
        ToolResultFrame result => new TurnFrame(TurnStream.ToolResult, new ToolResultEvent(
            result.Id, result.Tool, result.Summary,
            result.Result is { } card ? JsonSerializer.SerializeToElement(card, AssistantJsonContext.Default.ToolCard) : null)),
        DoneFrame done => new TurnFrame(TurnStream.Done, new DoneEvent(
            done.Text, done.CompletedAt,
            done.Usage is { } u ? new UsageDto(u.PromptTokens, u.ResponseTokens, u.UsedTokens, u.ContextWindow, u.RemainingTokens) : null,
            done.TurnId)),
        ErrorFrame error => new TurnFrame(TurnStream.Error, new StreamErrorEvent(error.Code, error.Message)),
        _ => new TurnFrame(frame.Type, Written(frame)),
    };

    /// <summary>A frame of this product's own, as this service writes it.</summary>
    internal static JsonElement Written(Frame frame) => JsonSerializer.SerializeToElement(frame, AssistantJsonContext.Default.Frame);
}

/// <summary>
/// How this service reads <c>POST /turn</c>, and the whole answer the bot is given.
/// </summary>
/// <remarks>
/// <para>
/// <b>Only a client relaying a person names a room</b>, in the body's <c>room</c>: a room is what lets a
/// turn move the film for everyone, and the bot is what knows which voice channel somebody is standing in.
/// A person's own turn names none, whatever the body says, and continues the chat it names — <c>main</c>
/// when it names none. A chat named for the first time is made, and the person's other surfaces told.
/// </para>
/// <para>
/// <b>A buffered turn runs through the engine like any other</b>, so a room's turns wait their turn, and is
/// answered with what it said, proposed and put on.
/// </para>
/// </remarks>
public sealed class CinemaTurnRequests(
    IAssistantCallers callers, ITurnRegistry turns, IConversationEventBus bus, IConversationStore store)
    : IAssistantTurnRequests
{
    public ValueTask<TurnPreparation> PrepareAsync(
        HttpContext http, AssistantCaller caller, TurnRequestBody request, JsonElement body, CancellationToken ct)
    {
        var person = (Caller)caller.Principal!;
        var room = body.TryGetProperty("room", out var named) && named.ValueKind == JsonValueKind.Object
            ? named.Deserialize(AssistantJsonContext.Default.TurnRoom)
            : null;
        var spoken = string.Equals(request.Style, "voice", StringComparison.OrdinalIgnoreCase);

        if (person.Relayed && room is { SessionId.Length: > 0 })
        {
            var inRoom = AssistantTurn.InRoom(room, person.UserId, person.DisplayName, spoken) with { AccessToken = person.AccessToken };
            return ValueTask.FromResult(new TurnPreparation(
                new AssistantConversation(inRoom.ConversationId, "", null, Shared: true),
                new TurnRun(request.Prompt!, request.Style, request.Speak ?? false, new CinemaTurn(inRoom))));
        }

        var chat = request.ConversationId is { Length: > 0 } id ? id : AssistantTurn.MainChat;
        var own = callers.OwnChat(caller, chat);
        var turn = AssistantTurn.ForPerson(person.UserId, person.DisplayName, chat)
            with { Spoken = spoken, AccessToken = person.AccessToken };

        if (store.CreateConversation(own.Key))
            bus.Publish(caller.OwnerKey, new ConversationEvent(
                ConversationStream.Started, new ConversationChanged(chat, ConversationEventsExtensions.OriginOf(http))));

        return ValueTask.FromResult(new TurnPreparation(
            own, new TurnRun(request.Prompt!, request.Style, request.Speak ?? false, new CinemaTurn(turn))));
    }

    public async Task<IResult> BufferedAsync(
        HttpContext http, AssistantCaller caller, TurnPreparation turn, CancellationToken ct)
    {
        var admission = turns.Admit(caller, turn.Conversation, turn.Run);
        if (admission.Outcome == TurnAdmission.QueueFull)
            return Results.Json(new AssistantError("queue_full"), AssistantJsonContext.Default.AssistantError,
                statusCode: StatusCodes.Status409Conflict);

        var session = admission.Session!;
        await session.Finished.WaitAsync(ct);
        bus.PublishActivity(caller, turn.Conversation, ConversationEventsExtensions.OriginOf(http));

        var failed = session.Error is not null || session.Done is null;
        return Results.Json(new TurnReply(
                failed ? CinemaAssistant.FailedReply : session.Done!.Text,
                session.Done?.TurnId ?? 0,
                failed,
                ((CinemaTurn)turn.Run.Host!).Proposed,
                ((CinemaTurn)turn.Run.Host!).Launched),
            AssistantJsonContext.Default.TurnReply);
    }
}

/// <summary>The proposals still waiting in one of a person's chats, as the frame that proposed them.</summary>
public sealed class CinemaPendingProposals(StagedActions staged) : IAssistantPendingProposals
{
    public IReadOnlyList<object> PendingIn(AssistantCaller caller, AssistantConversation conversation) =>
        [.. staged.Pending(conversation.Key).Select(a => (object)CinemaTurnRunner.Written(a.Frame()))];
}

/// <summary>
/// Agreeing to a proposal: carried out by the API as whoever agrees, with their token.
/// </summary>
/// <remarks>
/// An unknown handle, a spent one, an expired one and somebody else's are refused alike, and a refused
/// handle stays where it was: a guess cannot cancel what its owner is about to approve. In a room anybody
/// may agree, so a room's proposal has no owner to check.
/// </remarks>
public sealed class CinemaConfirmations(
    StagedActions staged, Client.MovieBotApiClient api, ILogger<CinemaConfirmations> logger) : IAssistantConfirmations
{
    public ValueTask<ConfirmPreparation> PrepareAsync(
        HttpContext http, AssistantCaller caller, JsonElement body, CancellationToken ct)
    {
        var person = (Caller)caller.Principal!;
        var token = body.ValueKind == JsonValueKind.Object && body.TryGetProperty("token", out var t)
            && t.ValueKind == JsonValueKind.String ? t.GetString() : null;

        if (staged.Take(token ?? "", person.UserId) is not { } action)
            return ValueTask.FromResult(ConfirmPreparation.Refused(Results.Json(
                new AssistantError("That has already been answered, has expired, or is not yours to answer."),
                AssistantJsonContext.Default.AssistantError, statusCode: StatusCodes.Status404NotFound)));

        logger.LogInformation("Assistant: {Approver} agreed to {What}, asked for by {Asker}",
            person.DisplayName, action.Describes, action.AskedByName);

        return ValueTask.FromResult(ConfirmPreparation.Run(async (_, c) =>
        {
            ConfirmOutcome outcome;
            try
            {
                outcome = await action.Run(new Agreement(api.As(person.AccessToken), person.DisplayName), c);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Assistant: carrying out {What} failed", action.Describes);
                outcome = new ConfirmOutcome
                {
                    Text = "That did not work: the API did not answer.", Success = false, Verdict = "failed", Kind = action.Kind,
                };
            }

            return JsonSerializer.SerializeToElement(
                new ConfirmReply(outcome.Text, outcome.Success, new ConfirmVerdict(outcome.Verdict, action.Verb), outcome),
                AssistantJsonContext.Default.ConfirmReply);
        }));
    }
}
