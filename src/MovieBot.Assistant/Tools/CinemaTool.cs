using System.Text.Json;
using TheKrystalShip.Agent.Plugin;
using TheKrystalShip.MovieBot.Assistant.Api;

namespace TheKrystalShip.MovieBot.Assistant.Tools;

/// <summary>
/// The shape every tool of this plugin shares: it runs for a signed-in person, as that person, against
/// the API with their token, and a failure it did not foresee becomes a result the model can read
/// rather than a failed turn.
/// </summary>
internal abstract class CinemaTool(CinemaApi api, ILogger logger) : IPluginTool
{
    public abstract string Name { get; }

    public async ValueTask<PluginToolResult> ExecuteAsync(JsonElement arguments, PluginToolContext context)
    {
        if (context.PrincipalAs<Caller>() is not { } caller)
            return PluginToolResult.Error("Nobody is signed in for this turn, so nothing was read.");

        var call = new ToolCall(api.As(caller.AccessToken), caller, arguments, context.CancellationToken);
        try
        {
            return await RunAsync(call);
        }
        catch (ApiRefusal refusal)
        {
            return PluginToolResult.Error(refusal.Message);
        }
        catch (OperationCanceledException) when (context.CancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "{Tool} failed", Name);
            return PluginToolResult.Error($"{Name} could not finish: {ex.Message}");
        }
    }

    protected abstract ValueTask<PluginToolResult> RunAsync(ToolCall call);
}

/// <summary>One call of one tool by one person: the arguments the model wrote, and the API as that person.</summary>
internal sealed class ToolCall(CinemaApi.PersonApi api, Caller caller, JsonElement arguments, CancellationToken cancellation)
{
    public CinemaApi.PersonApi Api => api;
    public Caller Person => caller;
    public CancellationToken Cancellation => cancellation;

    /// <summary>A string argument; null when absent, null or blank.</summary>
    public string? Text(string name) =>
        Property(name) is { } value
            ? value.ValueKind switch
            {
                JsonValueKind.String => string.IsNullOrWhiteSpace(value.GetString()) ? null : value.GetString()!.Trim(),
                JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False => value.ToString(),
                _ => null,
            }
            : null;

    /// <summary>An integer argument; null when absent or not a whole number.</summary>
    public int? Number(string name) =>
        Property(name) is { } value
            ? value.ValueKind switch
            {
                JsonValueKind.Number when value.TryGetInt32(out int n) => n,
                JsonValueKind.String when int.TryParse(value.GetString(), out int n) => n,
                _ => null,
            }
            : null;

    /// <summary>A boolean argument; <paramref name="otherwise"/> when absent or not one.</summary>
    public bool Flag(string name, bool otherwise) =>
        Property(name) is { } value
            ? value.ValueKind switch
            {
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                JsonValueKind.String when bool.TryParse(value.GetString(), out bool b) => b,
                _ => otherwise,
            }
            : otherwise;

    /// <summary>A list of strings: an array, or one string the model wrote with commas.</summary>
    public IReadOnlyList<string> List(string name) =>
        Property(name) is { } value
            ? value.ValueKind switch
            {
                JsonValueKind.Array =>
                [
                    .. value.EnumerateArray()
                        .Where(v => v.ValueKind == JsonValueKind.String)
                        .Select(v => v.GetString()!.Trim()).Where(s => s.Length > 0),
                ],
                JsonValueKind.String =>
                [
                    .. value.GetString()!.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries),
                ],
                _ => [],
            }
            : [];

    /// <summary>A string argument that must be one of <paramref name="allowed"/>; null when absent. A value outside the set throws.</summary>
    public string? OneOf(string name, params string[] allowed)
    {
        string? text = Text(name)?.ToLowerInvariant();
        if (text is null) return null;
        if (!allowed.Contains(text))
            throw new ApiRefusal(System.Net.HttpStatusCode.BadRequest,
                $"{name} is '{text}'; it has to be one of {string.Join(", ", allowed)}.");
        return text;
    }

    private JsonElement? Property(string name) =>
        arguments.ValueKind == JsonValueKind.Object && arguments.TryGetProperty(name, out JsonElement value)
        && value.ValueKind != JsonValueKind.Null
            ? value
            : null;
}
