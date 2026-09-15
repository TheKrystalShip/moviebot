using TheKrystalShip.Agent.Tools;
using TheKrystalShip.Llm.Models;

namespace TheKrystalShip.MovieBot.Bot.Assistant;

/// <summary>
/// The tools the model is offered, read from <c>tools.json</c> by <see cref="ToolCatalogFile"/>.
/// </summary>
/// <remarks>
/// <para>
/// <b>The words are a file and the behaviour is code.</b> A description is tuned by reading what the
/// model does with it, which is a loop that should not need a build. What a tool does, and whether it
/// runs at once or waits for somebody to agree, is decided in <see cref="RoomTools"/>, where a change
/// is reviewed as a change to what the bot can do.
/// </para>
/// <para>
/// <b>The two must name the same tools, exactly.</b> Tools are bound by name here: the file's keys are
/// held to <see cref="RoomTools.Names"/>, and a catalog that disagrees in either direction stops the bot
/// starting rather than offering a tool that fails the turn it is called on.
/// </para>
/// <para>
/// Read once. The catalog sits in every request ahead of the conversation, so it is the prefix the
/// model's cache is built on, and changing it is a restart in any case.
/// </para>
/// </remarks>
public sealed class ToolCatalog
{
    public const string FileName = ToolCatalogFile.FileName;

    private ToolCatalog(IReadOnlyList<LlmToolDefinition> all) => All = all;

    /// <summary>Every tool, in the order the file lists them, which is the order the model reads them in.</summary>
    public IReadOnlyList<LlmToolDefinition> All { get; }

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
