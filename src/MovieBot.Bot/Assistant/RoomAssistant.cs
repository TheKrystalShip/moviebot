using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TheKrystalShip.Llm.Agent;
using TheKrystalShip.Llm.Conversation;
using TheKrystalShip.Llm.Interfaces;
using TheKrystalShip.Llm.Models;
using TheKrystalShip.MovieBot.Bot.Launch;

namespace TheKrystalShip.MovieBot.Bot.Assistant;

/// <summary>What one turn came to: the reply, what it proposed, and the launch cards it produced.</summary>
public sealed record RoomAnswer(
    string Text,
    IReadOnlyList<StagedAction> Proposed,
    IReadOnlyList<LaunchReply> Launched,
    bool Failed = false);

/// <summary>
/// Puts a request to the model, with the room and the library in front of it and the room's tools in
/// its hands.
/// </summary>
/// <remarks>
/// <para>
/// <b>One conversation per room.</b> Everyone in a voice channel is in one conversation, each line
/// attributed to whoever said it, so "and after that?" follows whatever the room was just talking
/// about rather than whatever the speaker last said somewhere else.
/// </para>
/// <para>
/// <b>Thinking is off, and says so.</b> This model reasons when a request leaves the template's
/// variable unset, which measured 2.19 s to a tool call against 0.77 s with it off. The loop sends the
/// variable on every request.
/// </para>
/// <para>
/// <b>What the room verbs did goes into the same conversation.</b> A pause the gate carried out never
/// reaches the model, and "why did it stop?" asked a minute later would otherwise be answered by an
/// assistant that has no idea anything happened. The act is recorded as a turn of its own, with no
/// model involved.
/// </para>
/// </remarks>
public sealed class RoomAssistant(
    ILlmClient model,
    IConversationStore conversations,
    IConversationCompactor compactor,
    PromptPack prompts,
    ToolCatalog catalog,
    RoomFacts facts,
    RoomToolbox toolbox,
    TimeProvider clock,
    IOptions<LlmAgentOptions> agentOptions,
    IOptions<ConversationOptions> conversationOptions,
    ILoggerFactory loggers)
{
    private readonly ILogger _logger = loggers.CreateLogger<RoomAssistant>();

    public async Task<RoomAnswer> AskAsync(RoomTurn turn, string said, CancellationToken ct)
    {
        var started = clock.GetTimestamp();
        var prompt = prompts.Read();
        var context = await facts.DescribeAsync(turn, ct);
        var tools = toolbox.For(turn);

        // The loop is built per turn because the tools are: they act on this room, as this person,
        // and hold what this turn proposed until the chat has posted it.
        var agent = new LlmAgent(model, tools, conversations, agentOptions, loggers.CreateLogger<LlmAgent>());
        var wroteNothing = false;

        var result = await agent.RunAsync(new AgentTurn
        {
            ConversationId = turn.ConversationId,
            UserPrompt = said,
            Speaker = turn.SpeakerName,
            UserDisplay = turn.SpeakerName,
            SystemPrompt = prompt.Text,
            SystemPromptHash = prompt.Hash,
            Tools = catalog.All,
            Context = context,
            Think = false,
            ReviewReply = reply =>
            {
                if (!string.IsNullOrWhiteSpace(reply)) return ReplyReview.Accept;

                // This model ends its turn straight after a tool result more often than not. What the
                // tools said is then the answer; a proposal or a card posts its own message and needs
                // none.
                wroteNothing = true;
                return tools.Proposed.Count == 0 && tools.Launched.Count == 0 && tools.Told.Count > 0
                    ? ReplyReview.Amend(tools.Told[^1])
                    : ReplyReview.Accept;
            },
        }, ct);

        if (result.IsFailure)
        {
            _logger.LogWarning("Assistant: the turn for {Speaker} failed: {Error}", turn.SpeakerName, result.Error);
            return new RoomAnswer(
                "I couldn't reach the model just now, so nothing happened. The room verbs still work.",
                tools.Proposed, tools.Launched, Failed: true);
        }

        var answer = result.Value!;
        if (wroteNothing && (tools.Proposed.Count > 0 || tools.Launched.Count > 0))
            answer = answer with { Text = "" };
        _logger.LogInformation(
            "Assistant: answered {Speaker} in {Elapsed:0}ms ({Proposed} proposed, {Launched} launched, {Used}/{Window} tokens)",
            turn.SpeakerName, clock.GetElapsedTime(started).TotalMilliseconds, tools.Proposed.Count,
            tools.Launched.Count, answer.Usage?.UsedTokens, answer.Usage?.ContextWindow);

        await CompactIfFullAsync(turn.ConversationId, answer.Usage);
        return new RoomAnswer(answer.Text, tools.Proposed, tools.Launched);
    }

    /// <summary>
    /// Writes a room verb the gate carried out into the room's conversation, as a turn that called the
    /// tool the model would have called. Best effort: the film has already moved, and a record that
    /// could not be written is not a reason to say otherwise.
    /// </summary>
    public void RecordAct(RoomTurn turn, string said, string tool, IReadOnlyDictionary<string, string?> arguments, string outcome)
    {
        try
        {
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

    /// <summary>
    /// Folds the front of a room's conversation into a summary once it fills most of the window. A
    /// room is one conversation for as long as the channel exists, and nobody talking in it knows a
    /// window exists, so left alone it grows until the model silently loses the start of it.
    /// Measured from what the backend reported for the turn that just ran, never estimated.
    /// </summary>
    private async Task CompactIfFullAsync(string conversationId, LlmUsage? usage)
    {
        var at = conversationOptions.Value.CompactAtPercent;
        if (at <= 0 || usage is null || usage.ContextWindow <= 0) return;
        if (usage.UsedTokens * 100 < usage.ContextWindow * at) return;

        try
        {
            var compacted = await compactor.CompactAsync(conversationId, CancellationToken.None);
            if (compacted.IsFailure)
                _logger.LogWarning("Assistant: could not compact {Conversation}: {Error}", conversationId, compacted.Error);
            else if (compacted.Value!.Compacted)
                _logger.LogInformation(
                    "Assistant: compacted {Conversation} at {Used} of {Window} tokens",
                    conversationId, usage.UsedTokens, usage.ContextWindow);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Assistant: could not compact {Conversation}", conversationId);
        }
    }
}
