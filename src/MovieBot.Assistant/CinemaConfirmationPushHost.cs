using TheKrystalShip.Agent.Confirmations;
using TheKrystalShip.Agent.Service;
using TheKrystalShip.Agent.Service.Push;
using TheKrystalShip.Agent.Service.Turns;

namespace TheKrystalShip.MovieBot.Assistant;

/// <summary>
/// The proposals of a person's own chat, as the organization's confirmation notifications need them:
/// which wait unannounced, whether the person is at a surface, how one reads on a lock screen, and
/// taking one.
/// </summary>
/// <remarks>
/// <para>
/// <b>Only a person's own proposals are announced.</b> A room's are answered in the room by whoever is
/// standing in it, so a phone in somebody's pocket has nothing to add. <b>Presence</b> is the event
/// stream's: a surface open, or closed within the grace.
/// </para>
/// <para>
/// <b>Agreeing from a notification is refused, and cancelling works.</b> A proposal is carried out by
/// the API as whoever agrees, with their access token, and a button tapped on a locked phone presents
/// none: the provider mints a person's token only for a client that holds their sign-in or exchanges
/// their Discord identity, and this service is neither. So Confirm says to agree in the chat, where the
/// proposal still waits (<see cref="ConfirmUnavailable"/>), and nothing here runs one.
/// </para>
/// </remarks>
public sealed class CinemaConfirmationPushHost(StagedActions staged, IConversationEventBus streams) : IConfirmationPushHost<StagedAction>
{
    /// <summary>What a notification's Confirm answers.</summary>
    public const string ConfirmUnavailable =
        "Agreeing needs you signed in, so open MovieBot's chat on Krystal Cinema: it is still waiting there.";

    public IReadOnlyList<WaitingAction<StagedAction>> Unannounced() =>
        [.. staged.Unannounced().Select(a => new WaitingAction<StagedAction>(a.Token, Stager(a), a, a.Until))];

    public void MarkAnnounced(string handle) => staged.MarkAnnounced(handle);

    public bool PresentWithin(string userId, TimeSpan grace) => streams.PresentWithin(userId, grace);

    public PushedActionWords Describe(StagedAction action) => new(Words(action.Verb), action.Subject, action.Subject);

    public bool TryTake(string handle, AssistantCaller caller, out StagedAction action)
    {
        action = staged.Take(handle, caller.OwnerKey)!;
        return action is not null;
    }

    /// <summary>Never: nothing here holds a token to carry a proposal out with.</summary>
    public ValueTask<bool> AllowedAsync(StagedAction action, AssistantCaller caller, CancellationToken ct) =>
        ValueTask.FromResult(false);

    public Task<HeldActionOutcome> ExecuteAsync(
        StagedAction action, AssistantCaller approver, ConfirmationStager stager, IServiceProvider services, CancellationToken ct) =>
        Task.FromResult(new HeldActionOutcome(false, ConfirmUnavailable));

    /// <summary>Who a proposal waits on: the person who asked for it.</summary>
    public static ConfirmationStager Stager(StagedAction action) =>
        new(CinemaCallers.Provider, action.AskedBy, action.AskedByName);

    /// <summary>The verb as a question opens with it, in the words Cinema's chat puts on its buttons.</summary>
    private static string Words(string verb) => verb switch
    {
        "wish" => "get notified about",
        "release" => "let go of",
        "fetch_subtitle" => "fetch subtitles for",
        _ => verb,
    };
}
