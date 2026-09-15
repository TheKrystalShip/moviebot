using System.Collections.Concurrent;
using System.Security.Cryptography;
using Discord;
using Microsoft.Extensions.Options;

namespace TheKrystalShip.MovieBot.Bot.Assistant;

/// <summary>Whoever agreed to a proposal.</summary>
public sealed record Approval(ulong UserId, string DisplayName);

/// <summary>
/// What carrying out a proposal came to, as the channel is told it.
/// </summary>
/// <param name="AfterPosted">
/// Anything that needs the message once it exists — a download records which message shows its
/// progress, and there is no message to record until this one has been sent.
/// </param>
public sealed record StagedOutcome(
    string Message, Embed? Embed = null, Func<IUserMessage, Task>? AfterPosted = null);

/// <summary>
/// Something the assistant proposed that spends disk, a daily allowance or a film's place on the
/// disk, and so waits for somebody to agree.
/// </summary>
public sealed class StagedAction
{
    public required string Token { get; init; }

    /// <summary>The tool that proposed it.</summary>
    public required string Kind { get; init; }

    /// <summary>The act in the words somebody would use, beginning with a verb: "download Collateral (2004) · 1080p".</summary>
    public required string Describes { get; init; }

    /// <summary>The room it was proposed in, whose chat the prompt and the outcome go to.</summary>
    public required ulong ChannelId { get; init; }

    /// <summary>Whoever asked. The act is carried out as them, whoever agrees to it.</summary>
    public required ulong AskedBy { get; init; }

    public required string AskedByName { get; init; }

    public required DateTimeOffset Until { get; init; }

    public required Func<CancellationToken, Task<StagedOutcome>> Run { get; init; }

    /// <summary>The message carrying the buttons, once it has been posted, so every way of answering closes the same prompt.</summary>
    public ulong? PromptMessageId { get; set; }
}

/// <summary>
/// Proposals waiting for somebody to agree.
/// </summary>
/// <remarks>
/// <para>
/// <b>A token is a grant, spent once.</b> The buttons and a spoken yes redeem the same token, so
/// whichever arrives first carries the act out and the other finds nothing to redeem. A token is
/// 32 hex characters from a cryptographic source, because it rides in a button anyone in the channel
/// can press and a guessable one is a way to act on somebody else's proposal.
/// </para>
/// <para>
/// <b>Anybody may agree.</b> Anyone may act on a room and the record says who did, which is the rule
/// everywhere else in MovieBot; the act is still done as the person who asked for it, so the download
/// pings them and the wish is theirs.
/// </para>
/// <para>
/// <b>Held in memory.</b> A proposal is minutes old at most, and one that outlived a restart would be
/// a button carrying out a decision nobody remembers asking for. After a restart its buttons answer
/// that it has expired.
/// </para>
/// </remarks>
public sealed class StagedActions(IOptions<AssistantOptions> options, TimeProvider clock)
{
    private readonly ConcurrentDictionary<string, StagedAction> _waiting = new(StringComparer.Ordinal);

    public StagedAction Stage(
        string kind, string describes, RoomTurn turn, Func<CancellationToken, Task<StagedOutcome>> run)
    {
        var now = clock.GetUtcNow();
        foreach (var (token, expired) in _waiting)
        {
            if (expired.Until <= now) _waiting.TryRemove(token, out _);
        }

        var action = new StagedAction
        {
            Token = RandomNumberGenerator.GetHexString(32, lowercase: true),
            Kind = kind,
            Describes = describes,
            ChannelId = turn.ChannelId,
            AskedBy = turn.SpeakerId,
            AskedByName = turn.SpeakerName,
            Until = now + TimeSpan.FromMinutes(Math.Max(1, options.Value.OfferMinutes)),
            Run = run,
        };

        _waiting[action.Token] = action;
        return action;
    }

    /// <summary>The proposal, removed so nothing can redeem it again, or null when it is spent, expired or never was.</summary>
    public StagedAction? Take(string token)
    {
        if (!_waiting.TryRemove(token, out var action)) return null;
        return action.Until > clock.GetUtcNow() ? action : null;
    }

    /// <summary>A proposal still waiting, left waiting.</summary>
    public StagedAction? Peek(string token) =>
        _waiting.TryGetValue(token, out var action) && action.Until > clock.GetUtcNow() ? action : null;
}
