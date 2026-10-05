using TheKrystalShip.Agent.Service;
using TheKrystalShip.Agent.Service.Commands;
using TheKrystalShip.Llm.Interfaces;
using TheKrystalShip.MovieBot.Client.Assistant;

namespace TheKrystalShip.MovieBot.Assistant.Wire;

/// <summary>
/// The tools <c>GET /tools</c> and <c>/tools</c> list: what a person's own chat is offered, since that is
/// where somebody reads a list.
/// </summary>
public sealed class CinemaToolOffer(ToolCatalog catalog) : IAssistantToolOffer
{
    public Task<IReadOnlyList<ToolDto>> OfferedAsync(AssistantCaller caller, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<ToolDto>>([.. catalog.Site.Select(ToolDto.From)]);
}

/// <summary>
/// What a command changed in a person's chats, told to their other open surfaces on <c>GET /events</c>,
/// and a started chat's row read back from the listing so the two cannot disagree.
/// </summary>
/// <remarks>
/// A room's conversation is in nobody's listing, so nothing done to one is announced.
/// </remarks>
public sealed class CinemaConversationEvents(EventStreams streams, IConversationStore conversations) : IAssistantConversationEvents
{
    public object? Started(AssistantCaller caller, AssistantConversation conversation, string? origin)
    {
        var scope = AssistantTurn.PersonScope(caller.OwnerKey);
        streams.ToScope(scope, new ConversationFrame("started", conversation.ChatId ?? AssistantTurn.MainChat, origin));

        var row = conversations.ListConversations(conversation.Key).FirstOrDefault(c => c.ConversationId == conversation.Key);
        return row is null ? null : AssistantEndpoints.Row(scope, row);
    }

    public void Changed(AssistantCaller caller, AssistantConversation conversation, string? origin)
    {
        if (conversation.Shared) return;
        streams.ToScope(AssistantTurn.PersonScope(caller.OwnerKey),
            new ConversationFrame("activity", conversation.ChatId ?? AssistantTurn.MainChat, origin));
    }

    /// <summary>A conversation here carries no switches.</summary>
    public void SwitchesChanged(AssistantCaller caller, AssistantConversation conversation, string? origin)
    {
    }
}
