using System.Globalization;
using TheKrystalShip.Agent.Contracts;
using TheKrystalShip.Agent.Plugin;

namespace TheKrystalShip.MovieBot.Assistant;

/// <summary>
/// The live state sent with every turn, read after the person's message so the prompt prefix stays
/// byte-identical and its cache holds: the person's name, for address, and, when their browser names a time
/// zone whose clock is not the harness's, the time it is for them. The harness's reminder carries its own
/// clock; the tools read and write times in the person's. What they watched and saved is read by
/// <c>my_taste</c> when a recommendation needs it.
/// </summary>
public sealed class CinemaTurnContext(TimeProvider clock) : IPluginTurnContext
{
    public ValueTask<IReadOnlyList<ContextBlock>> BuildAsync(
        PluginPerson person, string conversationId, CancellationToken cancellationToken)
    {
        var blocks = new List<ContextBlock> { new() { Name = "person", Text = person.DisplayName } };

        DateTimeOffset now = clock.GetUtcNow();
        if (person.Principal is Caller { TimeZone: { } id } && TimeZoneInfo.TryFindSystemTimeZoneById(id, out var zone)
            && zone.GetUtcOffset(now) != clock.LocalTimeZone.GetUtcOffset(now))
        {
            DateTimeOffset theirs = TimeZoneInfo.ConvertTime(now, zone);
            blocks.Add(new ContextBlock
            {
                Name = "clock",
                Text = $"The person's time zone is {id}: for them it is {theirs.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)} "
                       + $"({theirs.ToString("dddd", CultureInfo.InvariantCulture)}). Times the tools read and write are in their clock.",
            });
        }

        return ValueTask.FromResult<IReadOnlyList<ContextBlock>>(blocks);
    }
}
