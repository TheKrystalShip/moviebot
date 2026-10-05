using TheKrystalShip.Agent.Confirmations;
using TheKrystalShip.Agent.Service;

namespace TheKrystalShip.MovieBot.Assistant;

/// <summary>
/// Who a caller of the organization's memory, command and notification routes is, and what they may
/// do, answered from the access token <see cref="Callers"/> reads.
/// </summary>
/// <remarks>
/// <para>
/// <b>The caller</b> is the person the token names, keyed by the provider's <c>sub</c>, which is what
/// their devices and the proposals they staged are filed under. What they hold is the token's actions:
/// the provider evaluated them when it minted it, and this service holds nothing to evaluate them by.
/// </para>
/// <para>
/// <b>The conversation</b> is the one a turn would address. A client relaying the person names a room
/// in <see cref="RoomHeader"/>, and the room's conversation is the shared one every turn there
/// continues; anybody else addresses their own chat, <c>person:&lt;userId&gt;:&lt;chatId&gt;</c>.
/// </para>
/// <para>
/// <b>Memory belongs to a person's own chats.</b> A room is shared by whoever is in the channel and
/// starts over after a silence, so nothing said there is anybody's to keep: a room's conversation has
/// no memory owner, and a person's chats share theirs, <c>person:&lt;userId&gt;</c>.
/// </para>
/// <para>
/// <b>A recorded stager is who they were</b>, because nothing here can ask who they are now without
/// their token. That is enough for what a notification's button can do on this host, which is cancel:
/// approving one is refused (<see cref="CinemaConfirmationPushHost"/>).
/// </para>
/// </remarks>
public sealed class CinemaCallers(Callers callers) : IAssistantCallers
{
    /// <summary>The header a relaying client names the room a command is typed in with: the room's session id.</summary>
    public const string RoomHeader = "X-Assistant-Room";

    /// <summary>The provider a recorded stager is named by: the application whose tokens name them.</summary>
    public const string Provider = "cinema";

    public AssistantCaller? Caller(HttpContext http) =>
        callers.Resolve(http.Request) is { } caller ? new AssistantCaller(caller.UserId, caller.DisplayName, caller) : null;

    public AssistantConversation Conversation(HttpContext http, AssistantCaller caller, string? chatId)
    {
        var chat = string.IsNullOrWhiteSpace(chatId) ? null : chatId.Trim();
        if (caller.Principal is Caller { Relayed: true } && http.Request.Headers[RoomHeader].ToString() is { Length: > 0 } room)
            return new AssistantConversation(AssistantTurn.RoomConversation(room), "", chat, Shared: true);

        return new AssistantConversation(
            AssistantTurn.PersonConversation(caller.OwnerKey, chat ?? AssistantTurn.MainChat),
            AssistantTurn.PersonScope(caller.OwnerKey), chat, Shared: false);
    }

    public ValueTask<Func<string, bool>> HeldAsync(AssistantCaller caller, CancellationToken ct)
    {
        var held = caller.Principal is Caller person ? person.Actions : new HashSet<string>();
        return ValueTask.FromResult<Func<string, bool>>(held.Contains);
    }

    public ValueTask<AssistantCaller?> ResolveRecordedAsync(ConfirmationStager stager, CancellationToken ct) =>
        ValueTask.FromResult<AssistantCaller?>(new AssistantCaller(stager.UserId, stager.DisplayName));
}
