using System.Text.Json.Serialization;
using TheKrystalShip.MovieBot.Core;

namespace TheKrystalShip.MovieBot.Assistant.Wire;

// The frames of the assistant wire contract (kgsm-llm/docs/wire-contract.md, version 2.1), in the
// shapes this service sends, plus the one frame this product adds: `launch`. Every frame carries its
// own `type`, an absent optional field is omitted rather than null, and a client ignores what it does
// not know.

/// <summary>One frame on a turn or event stream. <see cref="Type"/> is also the SSE event name.</summary>
[JsonDerivedType(typeof(TextDelta))]
[JsonDerivedType(typeof(ToolStartFrame))]
[JsonDerivedType(typeof(ToolResultFrame))]
[JsonDerivedType(typeof(CommandProposed))]
[JsonDerivedType(typeof(LaunchFrame))]
[JsonDerivedType(typeof(DoneFrame))]
[JsonDerivedType(typeof(ErrorFrame))]
[JsonDerivedType(typeof(HelloFrame))]
[JsonDerivedType(typeof(ConversationFrame))]
[JsonDerivedType(typeof(TurnAttach))]
[JsonDerivedType(typeof(TurnQueue))]
[JsonDerivedType(typeof(ConfirmResult))]
public abstract record Frame
{
    [JsonPropertyOrder(-1)]
    public abstract string Type { get; }
}

public sealed record TextDelta(string Text) : Frame
{
    public override string Type => "text.delta";
}

public sealed record ToolStartFrame(string Id, string Tool, IReadOnlyDictionary<string, string?> Arguments) : Frame
{
    public override string Type => "tool.start";
}

public sealed record ToolResultFrame(string Id, string Tool, string Summary) : Frame
{
    public override string Type => "tool.result";
}

/// <summary>What a proposal is done to.</summary>
public sealed record CommandSubject(string Resource, string Id);

/// <summary>An act staged for somebody to agree to. <see cref="Token"/> is what <c>POST /confirm</c> takes.</summary>
public sealed record CommandProposed(
    string Id, string Verb, CommandSubject Subject, string Confirm, string Token, string? Reason = null) : Frame
{
    public override string Type => "command.proposed";

    public static CommandProposed From(StagedAction action) =>
        new(action.Id, action.Verb, new CommandSubject("film", action.Subject), action.Confirm, action.Token);
}

/// <summary>
/// A film put on, or found, for the person to open. Each client presents it its own way: the bot posts
/// a launch card, the site opens the player. <see cref="SessionId"/> is absent when no room was named,
/// and <see cref="Replaced"/> names the film the room was watching before, when it was another.
/// </summary>
public sealed record LaunchFrame(
    string TitleId,
    string Name,
    string RequestedBy,
    string? SessionId = null,
    string? Replaced = null,
    bool AlreadyWatching = false) : Frame
{
    public override string Type => "launch";
}

public sealed record TurnUsage(int PromptTokens, int ResponseTokens, int UsedTokens, int ContextWindow, int RemainingTokens);

public sealed record DoneFrame(string Text, DateTimeOffset CompletedAt, long TurnId, TurnUsage? Usage = null) : Frame
{
    public override string Type => "done";
}

public sealed record ErrorFrame(string Code, string Message) : Frame
{
    public override string Type => "error";
}

public sealed record HelloFrame(string StreamId) : Frame
{
    public override string Type => "hello";
}

/// <summary><c>conversation.started</c>, <c>.deleted</c> or <c>.activity</c>.</summary>
public sealed record ConversationFrame(string Kind, string ConversationId, string? Origin) : Frame
{
    public override string Type => $"conversation.{Kind}";
}

public sealed record ToolState(string Id, string Name, string State, string? Summary);

/// <summary>The whole of a turn so far, for a stream arriving after it started.</summary>
public sealed record TurnAttach(
    string TurnId,
    string ConversationId,
    string Prompt,
    string State,
    string Text,
    IReadOnlyList<ToolState> Tools,
    IReadOnlyList<CommandProposed> Proposals,
    IReadOnlyList<LaunchFrame> Launches,
    DoneFrame? Done,
    ErrorFrame? Error) : Frame
{
    public override string Type => "turn.attach";
}

public sealed record QueuedTurn(string TurnId, string Prompt);

public sealed record TurnQueue(string ConversationId, string? RunningTurnId, IReadOnlyList<QueuedTurn> Queued) : Frame
{
    public override string Type => "turn.queue";
}

// ── Requests and answers ─────────────────────────────────────────────────────────────────────

/// <summary>
/// A turn. <see cref="Room"/> is accepted from a service alone: it is what gives a turn the room's
/// tools and the room's conversation, and a person on the site names none.
/// </summary>
public sealed record TurnRequest
{
    public required string Prompt { get; init; }

    public string? ConversationId { get; init; }

    /// <summary><c>voice</c> for a reply that is spoken; anything else is the written answer.</summary>
    public string? Style { get; init; }

    public TurnRoom? Room { get; init; }
}

/// <summary>A turn answered whole, for a caller that did not ask for a stream.</summary>
public sealed record TurnReply(
    string Text,
    long TurnId,
    bool Failed,
    IReadOnlyList<CommandProposed> Proposals,
    IReadOnlyList<LaunchFrame> Launches);

public sealed record ConfirmRequest(string Token);

/// <summary>What <c>POST /confirm</c> answers: the contract's buffered form, with the facts as <see cref="Result"/>.</summary>
public sealed record ConfirmReply(string Text, bool Success, ConfirmVerdict Outcome, ConfirmOutcome? Result = null);

public sealed record ConfirmVerdict(string Verdict, string? Verb = null, string? Reason = null);

/// <summary>The confirm channel's terminal frame when it streams: the whole buffered answer.</summary>
public sealed record ConfirmResult(string Text, bool Success, ConfirmVerdict Outcome, ConfirmOutcome? Result = null) : Frame
{
    public override string Type => "result";
}

/// <summary>A room verb carried out without the model, written into the room's conversation.</summary>
public sealed record RecordRequest(
    TurnRoom Room, string Said, string Tool, IReadOnlyDictionary<string, string?> Arguments, string Outcome);

public sealed record ConversationRow(
    string ConversationId, string Title, DateTimeOffset CreatedAt, DateTimeOffset LastActivityAt, int TurnCount,
    bool Think, bool Autorun);

public sealed record HistoryTool(string Id, string Name, IReadOnlyDictionary<string, string?> Arguments, string Summary);

public sealed record HistoryTurn(
    long TurnId, string Prompt, string? Speaker, DateTimeOffset StartedAt, DateTimeOffset CompletedAt,
    string Outcome, string? Text, IReadOnlyList<HistoryTool> Tools, string? Rating);

public sealed record ConversationDetail(
    string ConversationId, string Title, IReadOnlyList<HistoryTurn> Turns, IReadOnlyList<CommandProposed> Pending,
    bool Think, bool Autorun);

public sealed record FeedbackRequest(string? Rating, string? Note);

public sealed record AttachRequest(string? ConversationId);

public sealed record MeReply(string UserId, string DisplayName, bool Service);

public sealed record SpeechReply(bool Hear, bool Speak);

public sealed record CommandOption(string Name, string Description);

public sealed record CommandEntry(string Name, string Description, string Action, bool Mutates, IReadOnlyList<CommandOption> Options);

public sealed record CommandRequest(string? ConversationId, string? Argument);

public sealed record CommandReply(
    string Message, string? ConversationId = null, ConversationRow? Conversation = null,
    IReadOnlyList<CommandEntry>? Commands = null);

public sealed record AssistantError(string Error);

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    UseStringEnumConverter = true)]
[JsonSerializable(typeof(Frame))]
[JsonSerializable(typeof(TurnRequest))]
[JsonSerializable(typeof(TurnReply))]
[JsonSerializable(typeof(ConfirmRequest))]
[JsonSerializable(typeof(ConfirmReply))]
[JsonSerializable(typeof(RecordRequest))]
[JsonSerializable(typeof(List<ConversationRow>))]
[JsonSerializable(typeof(ConversationDetail))]
[JsonSerializable(typeof(FeedbackRequest))]
[JsonSerializable(typeof(AttachRequest))]
[JsonSerializable(typeof(MeReply))]
[JsonSerializable(typeof(SpeechReply))]
[JsonSerializable(typeof(List<CommandEntry>))]
[JsonSerializable(typeof(CommandRequest))]
[JsonSerializable(typeof(CommandReply))]
[JsonSerializable(typeof(AssistantError))]
public partial class AssistantJsonContext : JsonSerializerContext;
