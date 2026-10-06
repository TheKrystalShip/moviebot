using System.Text.Json.Serialization;
using TheKrystalShip.MovieBot.Core;

namespace TheKrystalShip.MovieBot.Client.Assistant;

// What moviebot-assistant says and answers that is MovieBot's own, beside the assistant wire contract
// (tks-assistant/docs/wire-contract.md, version 2.1) the organization's service writes: the frames the
// assistant produces — written onto the stream in the contract's shapes, with `launch`, this product's
// own, as it is — and the bodies of a relayed turn, a confirmation and a room's record. Here rather than
// in the service so the bot compiles against it without the service: every frame carries its own
// `type`, and a client ignores what it does not know.

/// <summary>
/// The room-verb tools the bot records into a room's conversation when it carries one out itself.
/// They are tools of the assistant's catalog, so the names are the contract.
/// </summary>
public static class RoomVerbTools
{
    public const string Resume = "resume";
    public const string Pause = "pause";
    public const string SeekRelative = "seek_relative";
}

/// <summary>A room a turn is about: the session it is, and what people call it.</summary>
/// <param name="ChannelId">The Discord channel announcements for the room go to, when it has one.</param>
public sealed record TurnRoom(string SessionId, string Name, string? ChannelId = null);

/// <summary>
/// What carrying out a proposal came to: a sentence, whether it may be presented as done, and the
/// API's own answer, which a client words its own message from.
/// </summary>
/// <param name="Verdict">
/// <c>accepted</c> when the API took the act, <c>refused</c> when nothing ran, <c>failed</c> when it
/// ran and said no.
/// </param>
public sealed record ConfirmOutcome
{
    public required string Text { get; init; }

    public required bool Success { get; init; }

    public required string Verdict { get; init; }

    /// <summary>The tool that proposed it: <c>download_film</c> is a download into the room it was proposed in.</summary>
    public string? Kind { get; init; }

    public StartDownloadReply? Download { get; init; }

    public AddWishReply? Wish { get; init; }

    public SubtitleAdded? Subtitle { get; init; }

    public static ConfirmOutcome Refused(string text) => new() { Text = text, Success = false, Verdict = "refused" };
}

/// <summary>One frame on a turn or event stream. <see cref="Type"/> is also the SSE event name.</summary>
[JsonDerivedType(typeof(TextDelta))]
[JsonDerivedType(typeof(ToolStartFrame))]
[JsonDerivedType(typeof(ToolResultFrame))]
[JsonDerivedType(typeof(CommandProposed))]
[JsonDerivedType(typeof(LaunchFrame))]
[JsonDerivedType(typeof(DoneFrame))]
[JsonDerivedType(typeof(ErrorFrame))]
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

/// <summary><see cref="Result"/> is present only when the tool has something a client draws, never hollow.</summary>
public sealed record ToolResultFrame(string Id, string Tool, string Summary, ToolCard? Result = null) : Frame
{
    public override string Type => "tool.result";
}

/// <summary>
/// A tool's structured result, which a client draws as a card and keys on <see cref="Tool"/>. The one
/// this service sends is a film to open: a film put on in a room, or found for somebody to watch.
/// </summary>
public sealed record ToolCard(string Tool, string Confidence, CommandSubject Subject, FilmCardData Data);

/// <summary>A film to open, and the room it is on in when it was put on in one.</summary>
public sealed record FilmCardData(string TitleId, string Name, string? SessionId = null);

/// <summary>What a proposal is done to.</summary>
public sealed record CommandSubject(string Resource, string Id);

/// <summary>
/// An act staged for somebody to agree to. <see cref="Token"/> is what <c>POST /confirm</c> takes.
/// <see cref="Describes"/> is the act in the words somebody would use and <see cref="AskedBy"/> who
/// asked for it, so a client can say what was agreed to without parsing <see cref="Confirm"/>.
/// </summary>
public sealed record CommandProposed(
    string Id, string Verb, CommandSubject Subject, string Confirm, string Token,
    string? Describes = null, string? AskedBy = null, string? Reason = null) : Frame
{
    public override string Type => "command.proposed";
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

/// <summary>
/// What <c>POST /confirm</c> answers whole, and what its streamed <c>result</c> frame carries: the
/// contract's form, with the facts as <see cref="Result"/>.
/// </summary>
public sealed record ConfirmReply(string Text, bool Success, ConfirmVerdict Outcome, ConfirmOutcome? Result = null);

public sealed record ConfirmVerdict(string Verdict, string? Verb = null, string? Reason = null);

/// <summary>A room verb carried out without the model, written into the room's conversation.</summary>
public sealed record RecordRequest(
    TurnRoom Room, string Said, string Tool, IReadOnlyDictionary<string, string?> Arguments, string Outcome);

public sealed record MeReply(string UserId, string DisplayName, bool Service);

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
[JsonSerializable(typeof(ToolCard))]
[JsonSerializable(typeof(TurnRoom))]
[JsonSerializable(typeof(MeReply))]
[JsonSerializable(typeof(AssistantError))]
public partial class AssistantJsonContext : JsonSerializerContext;
