using Microsoft.Extensions.Options;
using TheKrystalShip.Agent.Prompts;
using TheKrystalShip.Agent.Tools;
using TheKrystalShip.Llm.Models;

namespace TheKrystalShip.MovieBot.Assistant;

/// <summary>The standing instructions for one turn, and a short stamp that moves only when they are edited.</summary>
public sealed record SystemPrompt(string Text, string Hash);

/// <summary>
/// The instructions, read again on every turn: <c>system.md</c> for a room, whose people speak and are
/// transcribed, and <c>site.md</c> for a person typing on the site.
/// </summary>
/// <remarks>
/// A prompt is tuned by editing it and asking again, so an edit is picked up by the next thing
/// somebody says rather than by a restart. Nothing live goes in it: the system prompt is rendered
/// ahead of the tool catalog, and a byte that changes there makes the model read the catalog and the
/// whole conversation again. What the room is doing is the turn's context instead.
/// </remarks>
public sealed class PromptPack(IOptions<AssistantOptions> options)
{
    public const string RoomFile = "system.md";
    public const string SiteFile = "site.md";

    private readonly PromptDirectory _directory = new(options.Value.ResolvePromptDirectory());

    public SystemPrompt Read(AssistantTurn turn) => Read(turn.Room is null ? SiteFile : RoomFile);

    public SystemPrompt Read(string file)
    {
        var text = _directory.Require(file);
        return new SystemPrompt(text, PromptHash.Short(text));
    }
}

/// <summary>
/// The tools the model is offered, read from <c>tools.json</c> by <see cref="ToolCatalogFile"/>.
/// </summary>
/// <remarks>
/// <para>
/// <b>The words are a file and the behaviour is code.</b> A description is tuned by reading what the
/// model does with it, which is a loop that should not need a build. What a tool does, and whether it
/// runs at once or waits for somebody to agree, is decided in <see cref="RoomTools"/>, where a change
/// is reviewed as a change to what the assistant can do.
/// </para>
/// <para>
/// <b>The two must name the same tools, exactly.</b> Tools are bound by name here: the file's keys are
/// held to <see cref="RoomTools.Names"/>, and a catalog that disagrees in either direction stops the
/// service starting rather than offering a tool that fails the turn it is called on.
/// </para>
/// <para>
/// Read once. The catalog sits in every request ahead of the conversation, so it is the prefix the
/// model's cache is built on, and changing it is a restart in any case.
/// </para>
/// </remarks>
public sealed class ToolCatalog
{
    public const string FileName = ToolCatalogFile.FileName;

    private ToolCatalog(IReadOnlyList<LlmToolDefinition> all)
    {
        All = all;
        Site = [.. all.Where(t => !RoomTools.RoomOnly.Contains(t.Name))];
    }

    /// <summary>Every tool, in the order the file lists them, which is the order the model reads them in.</summary>
    public IReadOnlyList<LlmToolDefinition> All { get; }

    /// <summary>The tools a turn that names no room is offered: everything that does not act on a room.</summary>
    public IReadOnlyList<LlmToolDefinition> Site { get; }

    public IReadOnlyList<LlmToolDefinition> For(AssistantTurn turn) => turn.Room is null ? Site : All;

    public static ToolCatalog Load(string directory, IReadOnlyCollection<string> implemented) =>
        Bind(ToolCatalogFile.Read(directory), implemented, Path.Combine(directory, FileName));

    public static ToolCatalog Parse(string json, IReadOnlyCollection<string> implemented, string source = FileName) =>
        Bind(ToolCatalogFile.Parse(json, source), implemented, source);

    private static ToolCatalog Bind(IReadOnlyList<ToolEntry> entries, IReadOnlyCollection<string> implemented, string source)
    {
        ToolCatalogFile.RequireAgreement(source, entries.Select(e => e.Name), implemented, "tool");
        return new ToolCatalog(entries.Select(e => e.Definition).ToList());
    }
}
