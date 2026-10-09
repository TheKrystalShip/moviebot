namespace TheKrystalShip.MovieBot.Assistant;

/// <summary>Where the plugin listens, where the harness is, and the one key it presents there.</summary>
public sealed class AssistantOptions
{
    public const string Section = "Assistant";

    /// <summary>Where the service listens. Loopback: nginx proxies the site to it.</summary>
    public string Listen { get; set; } = "http://127.0.0.1:8191";

    /// <summary>Where the assistant harness listens.</summary>
    public string HarnessAddress { get; set; } = "http://127.0.0.1:8082";

    /// <summary>This plugin's key at the harness. A secret: set only in the environment, never logged.</summary>
    public string PluginKey { get; set; } = "";

    /// <summary>
    /// A file every answer of the API is appended to, as a recording a test replays. Blank records
    /// nothing. A recording holds whatever the API said to the person who asked, history and lists
    /// included, so it is not committed.
    /// </summary>
    public string RecordApiTo { get; set; } = "";
}
