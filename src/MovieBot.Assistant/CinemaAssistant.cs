using System.Text;
using Microsoft.Extensions.Options;
using TheKrystalShip.Agent.Conversation;
using TheKrystalShip.Agent.Replies;
using TheKrystalShip.Llm.Agent;
using TheKrystalShip.Llm.Conversation;
using TheKrystalShip.Llm.Interfaces;
using TheKrystalShip.Llm.Models;
using TheKrystalShip.MovieBot.Client.Assistant;

namespace TheKrystalShip.MovieBot.Assistant;

/// <summary>What one turn came to: the reply, what it proposed, the films it put on, and the turn it was recorded as.</summary>
public sealed record TurnAnswer(
    string Text,
    IReadOnlyList<StagedAction> Proposed,
    IReadOnlyList<LaunchFrame> Launched,
    long TurnId = 0,
    bool Failed = false,
    LlmUsage? Usage = null);

/// <summary>
/// Puts a request to the model, with the room and the library in front of it and the turn's tools in
/// its hands, and hands every step to whoever is watching as it happens.
/// </summary>
/// <remarks>
/// <para>
/// <b>One conversation per room, one per chat on the site.</b> Everyone in a voice channel is in one
/// conversation, each line attributed to whoever said it, so "and after that?" follows whatever the
/// room was just talking about. A person on the site has their own, as many as they start.
/// </para>
/// <para>
/// <b>A room that has gone quiet starts over.</b> Requests to a room are mostly unrelated to each
/// other, and this model copies its own earlier replies: last night's "I need the IMDb ID" comes back
/// as this morning's answer to a different request. A follow-up comes within minutes of what it
/// follows, so once the room has been silent for <see cref="AssistantOptions.IdleResetMinutes"/> the
/// next turn, or the next room verb, replays nothing that came before. The history stays in the store.
/// A person's chat on the site is never started over behind their back: starting a new one is theirs.
/// </para>
/// <para>
/// <b>Thinking is off, and says so.</b> This model reasons when a request leaves the template's
/// variable unset, which measured 2.19 s to a tool call against 0.77 s with it off. The loop sends the
/// variable on every request.
/// </para>
/// <para>
/// <b>What the room verbs did goes into the same conversation.</b> A pause the bot carried out never
/// reaches the model, and "why did it stop?" asked a minute later would otherwise be answered by an
/// assistant that has no idea anything happened. The act is recorded as a turn of its own, with no
/// model involved.
/// </para>
/// <para>
/// <b>A reply never lacks the sentence about what waits.</b> A turn that proposed something and wrote
/// a reply that does not say so has the pending note appended, and a turn that wrote nothing at all
/// answers with that note, or with what its tools said. The note is written here rather than by the
/// model, so it is in the text every client renders.
/// </para>
/// </remarks>
public sealed class CinemaAssistant(
    ILlmClient model,
    IConversationStore conversations,
    IConversationCompactor compactor,
    PromptPack prompts,
    ToolCatalog catalog,
    TurnFacts facts,
    RoomToolbox toolbox,
    TimeProvider clock,
    IOptions<LlmAgentOptions> agentOptions,
    IOptions<ConversationOptions> conversationOptions,
    IOptions<AssistantOptions> assistantOptions,
    ILoggerFactory loggers)
{
    public const string FailedReply = "I couldn't reach the model just now, so nothing happened.";

    private readonly ILogger _logger = loggers.CreateLogger<CinemaAssistant>();

    /// <summary>A whole turn, for a caller that wants the answer and not the steps.</summary>
    public Task<TurnAnswer> AskAsync(AssistantTurn turn, string said, CancellationToken ct) =>
        RunAsync(turn, said, _ => { }, ct);

    /// <summary>
    /// Runs one turn, handing every frame to <paramref name="emit"/> as it happens and ending with
    /// exactly one <see cref="DoneFrame"/> or <see cref="ErrorFrame"/>.
    /// </summary>
    public async Task<TurnAnswer> RunAsync(AssistantTurn turn, string said, Action<Frame> emit, CancellationToken ct)
    {
        var started = clock.GetTimestamp();
        StartOverIfIdle(turn);
        var prompt = prompts.Read(turn);
        var context = await facts.DescribeAsync(turn, ct);
        var tools = toolbox.For(turn, said);

        // The loop is built per turn because the tools are: they act on this room, as this person,
        // and hold what this turn proposed until it has been handed over.
        var agent = new LlmAgent(model, tools, conversations, agentOptions, loggers.CreateLogger<LlmAgent>());

        // Everything the turn is given, for the figure check: what was said, the instructions and the
        // room, and then every tool's answer as the tools note it.
        using var given = MeasuredValues.BeginTurn(said, prompt.Text, context);
        var review = ReplyGuard.Review(
            [
                ReplyChecks.FabricatedFigures(),
                ReplyChecks.UnbackedAction(RoomActionClaim.Check, said, () => tools.Acted),
                RoomIdRequest.Check(said),
                RoomLoadingClaim.Check(said, () => tools.Acted),
            ],
            (check, fault, resolution) => _logger.LogWarning(
                "Assistant: {Speaker}'s reply failed the {Check} check ({Detail}); {Resolution}",
                turn.SpeakerName, check.Name, fault.Detail ?? "no detail", resolution));

        var agentTurn = new AgentTurn
        {
            ConversationId = turn.ConversationId,
            UserPrompt = said,
            Speaker = turn.SpeakerName,
            UserDisplay = turn.SpeakerName,
            SystemPrompt = prompt.Text,
            SystemPromptHash = prompt.Hash,
            Tools = catalog.For(turn),
            Context = context,
            Think = false,
            ReviewReply = reply =>
            {
                // This model ends its turn straight after a tool result more often than not. What
                // waits, or what the tools said, is then the answer.
                if (string.IsNullOrWhiteSpace(reply))
                {
                    if (tools.Proposed.Count > 0)
                        return ReplyReview.Amend(PendingConfirmationNote.For(tools.Proposed.Count, turn.Spoken).TrimStart());
                    return tools.Told.Count > 0 ? ReplyReview.Amend(tools.Told[^1]) : ReplyReview.Accept;
                }

                var verdict = review(reply);
                if (verdict.WantsRetry || verdict.HasAmendment) return verdict;

                return tools.Proposed.Count > 0 && !PendingConfirmationNote.IsPresentIn(reply)
                    ? ReplyReview.Amend(PendingConfirmationNote.For(tools.Proposed.Count, turn.Spoken))
                    : ReplyReview.Accept;
            },
        };

        var text = new StringBuilder();
        var handedProposals = 0;
        var handedLaunches = 0;
        string? final = null;
        string? error = null;
        LlmUsage? usage = null;
        long turnId = 0;

        void HandOver()
        {
            for (; handedProposals < tools.Proposed.Count; handedProposals++)
                emit(tools.Proposed[handedProposals].Frame());
            for (; handedLaunches < tools.Launched.Count; handedLaunches++)
                emit(tools.Launched[handedLaunches]);
        }

        await foreach (var step in agent.RunStreamAsync(agentTurn, ct))
        {
            switch (step.Kind)
            {
                case AgentEventKind.Token when step.Text is { Length: > 0 } delta:
                    text.Append(delta);
                    emit(new TextDelta(delta));
                    break;

                case AgentEventKind.ToolStart:
                    emit(new ToolStartFrame(step.ToolCallId ?? "", step.ToolName?.Name ?? "",
                        step.ToolArguments ?? new Dictionary<string, string?>()));
                    break;

                case AgentEventKind.ToolResult:
                    emit(new ToolResultFrame(step.ToolCallId ?? "", step.ToolName?.Name ?? "", step.ToolSummary ?? ""));
                    HandOver();
                    break;

                case AgentEventKind.Final:
                    final = step.Text ?? text.ToString();
                    usage = step.Usage;
                    turnId = step.TurnId;
                    break;

                case AgentEventKind.Error:
                    error = step.ErrorMessage ?? "The turn failed.";
                    break;
            }
        }

        HandOver();

        if (final is null)
        {
            _logger.LogWarning("Assistant: the turn for {Speaker} failed: {Error}", turn.SpeakerName, error);
            emit(new ErrorFrame("assistant_failed", FailedReply));
            return new TurnAnswer(FailedReply, tools.Proposed, tools.Launched, Failed: true);
        }

        emit(new DoneFrame(final, clock.GetUtcNow(), turnId, usage is null
            ? null
            : new TurnUsage(usage.PromptTokens, usage.ResponseTokens, usage.UsedTokens, usage.ContextWindow, usage.RemainingTokens)));

        _logger.LogInformation(
            "Assistant: answered {Speaker} in {Elapsed:0}ms ({Proposed} proposed, {Launched} launched, {Used}/{Window} tokens)",
            turn.SpeakerName, clock.GetElapsedTime(started).TotalMilliseconds, tools.Proposed.Count,
            tools.Launched.Count, usage?.UsedTokens, usage?.ContextWindow);

        await ContextCompaction.CompactIfFullAsync(
            compactor, turn.ConversationId, usage, conversationOptions.Value.CompactAtPercent, _logger);
        return new TurnAnswer(final, tools.Proposed, tools.Launched, turnId, Usage: usage);
    }

    /// <summary>
    /// Starts a room's conversation over when nothing has been written to it for the idle window.
    /// A room verb counts as something written, so "why did it stop?" after a pause still sees the pause.
    /// </summary>
    private void StartOverIfIdle(AssistantTurn turn)
    {
        var idle = assistantOptions.Value.IdleResetMinutes;
        if (idle <= 0 || turn.Room is null) return;

        var room = conversations.ListConversations(turn.ConversationId)
            .FirstOrDefault(c => c.ConversationId == turn.ConversationId);
        if (room is null || clock.GetUtcNow() - room.LastActivityAt < TimeSpan.FromMinutes(idle)) return;

        conversations.Reset(turn.ConversationId);
        _logger.LogInformation(
            "Assistant: {Room} was quiet since {LastActivity:u}, so {Speaker} starts its conversation over",
            turn.ConversationId, room.LastActivityAt, turn.SpeakerName);
    }

    /// <summary>
    /// Writes a room verb the bot carried out into the room's conversation, as a turn that called the
    /// tool the model would have called. Best effort: the film has already moved, and a record that
    /// could not be written is not a reason to say otherwise.
    /// </summary>
    public void RecordAct(AssistantTurn turn, string said, string tool, IReadOnlyDictionary<string, string?> arguments, string outcome)
    {
        try
        {
            StartOverIfIdle(turn);
            var now = clock.GetUtcNow();
            conversations.AppendTurn(new ConversationTurnRecord
            {
                ConversationId = turn.ConversationId,
                UserDisplay = turn.SpeakerName,
                StartedAt = now,
                CompletedAt = now,
                UserPrompt = said,
                SystemPromptHash = "room-verb",
                Tools = [new RecordedToolCall(new Tool(tool), arguments, outcome, 0)],
                Iterations = 0,
                Outcome = TurnOutcome.Ok,
                Think = false,
                Final = outcome,
            });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Assistant: could not record {Speaker}'s {Tool}", turn.SpeakerName, tool);
        }
    }
}
