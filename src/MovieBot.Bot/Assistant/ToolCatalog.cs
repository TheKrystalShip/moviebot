using System.Text.Json;
using System.Text.Json.Serialization;
using TheKrystalShip.Llm.Models;

namespace TheKrystalShip.MovieBot.Bot.Assistant;

/// <summary>One tool as <c>tools.json</c> describes it.</summary>
/// <remarks>
/// A property with a default has a setter rather than an initialiser: the generated reader assigns
/// every init-only property it knows, the absent ones included, so an init default would arrive as
/// null and a parameter left unmarked would read as optional.
/// </remarks>
public sealed record ToolDocument
{
    public required string Description { get; init; }
    public IReadOnlyList<ToolParameterDocument> Params { get; set; } = [];
}

public sealed record ToolParameterDocument
{
    public required string Name { get; init; }
    public required string Description { get; init; }
    public bool Required { get; set; } = true;
    public string Type { get; set; } = "string";
    public IReadOnlyList<string>? Enum { get; set; }
}

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    PropertyNameCaseInsensitive = true,
    ReadCommentHandling = JsonCommentHandling.Skip,
    AllowTrailingCommas = true)]
[JsonSerializable(typeof(Dictionary<string, ToolDocument>))]
internal partial class ToolCatalogJsonContext : JsonSerializerContext;

/// <summary>
/// The tools the model is offered, read from <c>tools.json</c>.
/// </summary>
/// <remarks>
/// <para>
/// <b>The words are a file and the behaviour is code.</b> A description is tuned by reading what the
/// model does with it, which is a loop that should not need a build. What a tool does, and whether it
/// runs at once or waits for somebody to agree, is decided in <see cref="RoomTools"/>, where a change
/// is reviewed as a change to what the bot can do.
/// </para>
/// <para>
/// <b>The two must name the same tools, exactly.</b> A tool the file names and the code does not is
/// offered to the model and fails the turn it is called on; one the code implements and the file does
/// not is a thing the bot can do that the model is never told about. Either is refused when the
/// catalog is read, so the bot does not start with a catalog that disagrees with itself.
/// </para>
/// <para>
/// Read once. The catalog sits in every request ahead of the conversation, so it is the prefix the
/// model's cache is built on, and changing it is a restart in any case.
/// </para>
/// </remarks>
public sealed class ToolCatalog
{
    public const string FileName = "tools.json";

    private static readonly IReadOnlySet<string> KnownTypes =
        new HashSet<string>(StringComparer.Ordinal) { "string", "integer", "number", "boolean" };

    private ToolCatalog(IReadOnlyList<LlmToolDefinition> all) => All = all;

    /// <summary>Every tool, in the order the file lists them, which is the order the model reads them in.</summary>
    public IReadOnlyList<LlmToolDefinition> All { get; }

    public static ToolCatalog Load(string directory, IReadOnlyCollection<string> implemented)
    {
        var path = Path.Combine(directory, FileName);
        if (!File.Exists(path))
            throw new InvalidOperationException(
                $"The assistant's tool catalog is not at {path}. Assistant:PromptDirectory names the "
                + "directory it was installed into.");

        return Parse(File.ReadAllText(path), implemented, path);
    }

    public static ToolCatalog Parse(string json, IReadOnlyCollection<string> implemented, string source = FileName)
    {
        var documents = JsonSerializer.Deserialize(json, ToolCatalogJsonContext.Default.DictionaryStringToolDocument)
                        ?? throw new InvalidOperationException($"{source} is empty.");

        var described = documents.Keys.ToHashSet(StringComparer.Ordinal);
        var missing = implemented.Where(name => !described.Contains(name)).ToList();
        var unknown = described.Where(name => !implemented.Contains(name)).ToList();

        if (missing.Count > 0 || unknown.Count > 0)
            throw new InvalidOperationException(
                $"{source} and the bot disagree about which tools exist."
                + (missing.Count > 0 ? $" Not described: {string.Join(", ", missing)}." : "")
                + (unknown.Count > 0 ? $" Described but not implemented: {string.Join(", ", unknown)}." : ""));

        var tools = new List<LlmToolDefinition>();
        foreach (var (name, document) in documents)
        {
            if (string.IsNullOrWhiteSpace(document.Description))
                throw new InvalidOperationException($"{source}: {name} has no description.");

            foreach (var parameter in document.Params)
            {
                if (!KnownTypes.Contains(parameter.Type))
                    throw new InvalidOperationException(
                        $"{source}: {name}.{parameter.Name} has type '{parameter.Type}', which is not one of "
                        + string.Join(", ", KnownTypes) + ".");
            }

            tools.Add(new LlmToolDefinition(
                new Tool(name),
                document.Description,
                document.Params
                    .Select(p => new LlmToolParameter(p.Name, p.Description, p.Required, p.Type, p.Enum))
                    .ToList()));
        }

        return new ToolCatalog(tools);
    }
}
