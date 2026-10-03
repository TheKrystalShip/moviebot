namespace TheKrystalShip.MovieBot.Assistant;

/// <summary>How the assistant answers, and where it keeps what it remembers.</summary>
public sealed class AssistantOptions
{
    public const string Section = "Assistant";

    /// <summary>Where the service listens. Loopback: the bot reaches it there and nginx proxies the site to it.</summary>
    public string Listen { get; set; } = "http://127.0.0.1:8191";

    /// <summary>
    /// The directory holding the instructions and <c>tools.json</c>. Relative to the directory the
    /// service runs from, which is where the build puts them.
    /// </summary>
    public string PromptDirectory { get; set; } = "prompts";

    /// <summary>
    /// How long a proposed download, keep or fetch waits for somebody to agree, in minutes. After it
    /// the handle redeems nothing and says so.
    /// </summary>
    public int OfferMinutes { get; set; } = 10;

    /// <summary>
    /// The most films of the library written into every turn. The rest are one search away, and a
    /// library that outgrows this would otherwise spend the context window on titles nobody named.
    /// </summary>
    public int LibraryInContext { get; set; } = 60;

    /// <summary>
    /// How long a room's conversation may sit silent, in minutes, before the next thing said in it
    /// starts the conversation over. Zero never starts it over. A person's own conversations on the
    /// site are never started over: they choose when to begin a new one.
    /// </summary>
    public int IdleResetMinutes { get; set; } = 15;

    /// <summary>Where the conversations are written. Empty means the state directory systemd hands the service.</summary>
    public string DatabasePath { get; set; } = "";

    public string ResolvePromptDirectory() =>
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, PromptDirectory));

    public string ResolveDatabasePath()
    {
        if (!string.IsNullOrWhiteSpace(DatabasePath)) return Path.GetFullPath(DatabasePath);

        // The directory systemd makes for this service; the variable may name more than one, and
        // the first is the service's own.
        var state = Environment.GetEnvironmentVariable("STATE_DIRECTORY")?.Split(':').FirstOrDefault();
        return Path.GetFullPath(Path.Combine(
            string.IsNullOrWhiteSpace(state) ? Directory.GetCurrentDirectory() : state, "assistant.db"));
    }

    /// <summary>
    /// Where the request that warms the model is written, for the model's own unit to replay when it
    /// starts. Beside the conversations, because it is written by the same process.
    /// </summary>
    public string ResolveWarmupPath() =>
        Path.Combine(Path.GetDirectoryName(ResolveDatabasePath())!, "llm-warmup.json");
}

/// <summary>The keys this service checks callers with and reaches the API with.</summary>
public sealed class AssistantAuthOptions
{
    public const string Section = "Auth";

    /// <summary>The key the API signs room tokens with; a bearer is checked against it.</summary>
    public string SigningKey { get; set; } = "";

    /// <summary>The key a service that acts on somebody's behalf proves itself with.</summary>
    public string ServiceKey { get; set; } = "";
}
