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
