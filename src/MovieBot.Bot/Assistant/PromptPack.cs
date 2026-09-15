using Microsoft.Extensions.Options;
using TheKrystalShip.Llm.Models;

namespace TheKrystalShip.MovieBot.Bot.Assistant;

/// <summary>The standing instructions for one turn, and a short stamp that moves only when they are edited.</summary>
public sealed record SystemPrompt(string Text, string Hash);

/// <summary>
/// <c>system.md</c>, read again on every turn.
/// </summary>
/// <remarks>
/// A prompt is tuned by editing it and asking again, so an edit is picked up by the next thing
/// somebody says rather than by a restart. Nothing live goes in it: the system prompt is rendered
/// ahead of the tool catalog, and a byte that changes there makes the model read the catalog and the
/// whole conversation again. What the room is doing is the turn's context instead.
/// </remarks>
public sealed class PromptPack(IOptions<AssistantOptions> options)
{
    public const string FileName = "system.md";

    public SystemPrompt Read()
    {
        var path = Path.Combine(options.Value.ResolvePromptDirectory(), FileName);
        if (!File.Exists(path))
            throw new InvalidOperationException(
                $"The assistant's instructions are not at {path}. Assistant:PromptDirectory names the "
                + "directory they were installed into.");

        var text = File.ReadAllText(path).Trim();
        return new SystemPrompt(text, PromptHash.Short(text));
    }
}
