using TheKrystalShip.Agent.Contracts;
using TheKrystalShip.Agent.Plugin;

namespace TheKrystalShip.MovieBot.Assistant;

/// <summary>
/// The live state sent with every turn, read after the person's message so the prompt prefix stays
/// byte-identical and its cache holds: the person's name, for address. What they watched and saved is
/// read by <c>my_taste</c> when a recommendation needs it, and the date is the harness's.
/// </summary>
public sealed class CinemaTurnContext : IPluginTurnContext
{
    public ValueTask<IReadOnlyList<ContextBlock>> BuildAsync(
        PluginPerson person, string conversationId, CancellationToken cancellationToken) =>
        ValueTask.FromResult<IReadOnlyList<ContextBlock>>(
            [new ContextBlock { Name = "person", Text = person.DisplayName }]);
}
