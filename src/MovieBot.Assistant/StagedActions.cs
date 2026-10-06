using System.Collections.Concurrent;
using Microsoft.Extensions.Options;
using TheKrystalShip.Agent.Confirmations;
using TheKrystalShip.MovieBot.Client;
using TheKrystalShip.MovieBot.Client.Assistant;

namespace TheKrystalShip.MovieBot.Assistant;

/// <summary>Whoever agreed to a proposal: the API as them, and what they are called.</summary>
public sealed record Agreement(MovieBotApiClient Api, string Name);

/// <summary>
/// Something the assistant proposed that spends disk, a daily allowance or a film's place on the
/// disk, and so waits for somebody to agree.
/// </summary>
public sealed class StagedAction
{
    public required string Token { get; init; }

    /// <summary>The turn-stable display handle, <c>cmd_n</c>. Not security-bearing.</summary>
    public required string Id { get; init; }

    /// <summary>The tool that proposed it.</summary>
    public required string Kind { get; init; }

    /// <summary>The act in the words somebody would use, beginning with a verb: "download Collateral (2004) · 1080p".</summary>
    public required string Describes { get; init; }

    /// <summary>What the act is done to: a release, a film in the library, a film on the wish list.</summary>
    public required string Subject { get; init; }

    public required string ConversationId { get; init; }

    /// <summary>The room it was proposed in, when it was.</summary>
    public TurnRoom? Room { get; init; }

    /// <summary>Whoever asked, which is whose proposal on the site it is to agree to.</summary>
    public required string AskedBy { get; init; }

    public required string AskedByName { get; init; }

    public required DateTimeOffset Until { get; init; }

    /// <summary>The act, carried out as whoever agrees: the API decides with their token whether they may.</summary>
    public required Func<Agreement, CancellationToken, Task<ConfirmOutcome>> Run { get; init; }

    /// <summary>Whether a notification has told the person who asked that it waits, which it does once.</summary>
    public bool Announced { get; set; }

    /// <summary>The verb a client shows, from the tool that proposed it.</summary>
    public string Verb => Kind switch
    {
        RoomTools.DownloadFilm or RoomTools.DownloadOnly or RoomTools.DownloadShow => "download",
        RoomTools.FetchSubtitle => "fetch_subtitle",
        RoomTools.AddWish => "wish",
        _ => Kind,
    };

    /// <summary>The prompt a client puts beside its buttons.</summary>
    public string Confirm => $"{AskedByName} asked me to {Describes}. Go ahead?";

    /// <summary>The frame that hands it to a client.</summary>
    public CommandProposed Frame() =>
        new(Id, Verb, new CommandSubject("film", Subject), Confirm, Token, Describes, AskedByName);
}

/// <summary>
/// Proposals waiting for somebody to agree, held by <see cref="HeldActions{T}"/>.
/// </summary>
/// <remarks>
/// <para>
/// <b>A token is a grant, spent once.</b> Every way of agreeing redeems the same token, so whichever
/// arrives first carries the act out and the others find nothing to redeem. It rides in a button
/// anyone in a channel can press, so it is a <see cref="ConfirmationHandle"/>.
/// </para>
/// <para>
/// <b>In a room anybody may agree; on the site only the person who asked.</b> Anyone may act on a
/// room and the record says who did, which is the rule everywhere else in MovieBot, so a room's
/// proposal is held with no owner. A person's own conversation is theirs, and so is what it proposed.
/// The act is done as whoever agrees, with their token: they are the one spending it, so the API asks
/// whether they may, records them, and tells them when the download can be watched.
/// </para>
/// <para>
/// <b>Held in memory.</b> A proposal is minutes old at most, and one that outlived a restart would be
/// a button carrying out a decision nobody remembers asking for. After a restart its token redeems
/// nothing.
/// </para>
/// </remarks>
public sealed class StagedActions(IOptions<AssistantOptions> options, TimeProvider clock)
{
    private readonly HeldActions<StagedAction> _waiting = new(clock);
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, byte>> _byConversation = new();
    private int _ids;

    public StagedAction Stage(
        string kind, string describes, string subject, AssistantTurn turn, Func<Agreement, CancellationToken, Task<ConfirmOutcome>> run)
    {
        var action = new StagedAction
        {
            Token = ConfirmationHandle.New(),
            Id = $"cmd_{Interlocked.Increment(ref _ids)}",
            Kind = kind,
            Describes = describes,
            Subject = subject,
            ConversationId = turn.ConversationId,
            Room = turn.Room,
            AskedBy = turn.SpeakerUserId,
            AskedByName = turn.SpeakerName,
            Until = clock.GetUtcNow() + TimeSpan.FromMinutes(Math.Max(1, options.Value.OfferMinutes)),
            Run = run,
        };

        _waiting.Hold(action.Token, action, action.Until, turn.Room is null ? turn.SpeakerUserId : null);
        _byConversation.GetOrAdd(turn.ConversationId, _ => new()).TryAdd(action.Token, 0);
        return action;
    }

    /// <summary>
    /// The proposal, removed so nothing can redeem it again, or null when it is spent, expired, never
    /// was, or is somebody else's to agree to. A refused token is not spent.
    /// </summary>
    public StagedAction? Take(string token, string? redeemer = null)
    {
        if (!_waiting.TryTake(token, redeemer, out var action)) return null;
        if (_byConversation.TryGetValue(action.ConversationId, out var tokens)) tokens.TryRemove(token, out _);
        return action;
    }

    /// <summary>What still waits in a conversation, oldest first.</summary>
    public IReadOnlyList<StagedAction> Pending(string conversationId)
    {
        if (!_byConversation.TryGetValue(conversationId, out var tokens)) return [];

        var pending = new List<StagedAction>();
        foreach (var token in tokens.Keys)
        {
            if (_waiting.TryPeek(token, out var action)) pending.Add(action);
            else tokens.TryRemove(token, out _);
        }

        return [.. pending.OrderBy(a => a.Until)];
    }

    /// <summary>
    /// What waits in a person's own chats and no notification has announced, oldest first. A room's
    /// proposals are answered in the room, by whoever is there, so none of them is announced.
    /// </summary>
    public IReadOnlyList<StagedAction> Unannounced() =>
        [.. _byConversation.Keys.SelectMany(Pending).Where(a => a.Room is null && !a.Announced).OrderBy(a => a.Until)];

    /// <summary>Records that a notification announced <paramref name="token"/>'s proposal.</summary>
    public void MarkAnnounced(string token)
    {
        if (_waiting.TryPeek(token, out var action)) action.Announced = true;
    }
}
