using TheKrystalShip.MovieBot.Bot.Configuration;

namespace TheKrystalShip.MovieBot.Bot.Assistant;

/// <summary>How the bot answers what the room verbs do not cover.</summary>
public sealed class AssistantOptions
{
    public const string Section = "Assistant";

    /// <summary>
    /// Whether a spoken request that is not a room verb is put to the model. Off, those requests are
    /// heard and answered by nothing, and the room verbs work exactly the same.
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// The directory holding <c>system.md</c> and <c>tools.json</c>. Relative to the directory the bot
    /// runs from, which is where the build puts them.
    /// </summary>
    public string PromptDirectory { get; set; } = "prompts";

    /// <summary>
    /// How long a proposed download, keep or fetch waits for somebody to agree, in minutes. After it
    /// the buttons do nothing and say so.
    /// </summary>
    public int OfferMinutes { get; set; } = 10;

    /// <summary>
    /// How long the bot listens for a spoken yes or no to what it proposed, without the trigger, in
    /// seconds. Zero leaves the buttons as the only way to agree.
    /// </summary>
    public int ConfirmWindowSeconds { get; set; } = 20;

    /// <summary>
    /// The most films of the library written into every turn. The rest are one search away, and a
    /// library that outgrows this would otherwise spend the context window on titles nobody named.
    /// </summary>
    public int LibraryInContext { get; set; } = 60;

    /// <summary>
    /// How long a room's conversation may sit silent, in minutes, before the next thing said in it
    /// starts the conversation over. Zero never starts it over.
    /// </summary>
    public int IdleResetMinutes { get; set; } = 15;

    /// <summary>
    /// Where the conversation is written. Empty means the state directory systemd hands the service.
    /// </summary>
    public string DatabasePath { get; set; } = "";

    public string ResolvePromptDirectory() =>
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, PromptDirectory));

    public string ResolveDatabasePath() => StatePath.Resolve(DatabasePath, "assistant.db");

    /// <summary>
    /// Where the request that warms the model is written, for the model's own unit to replay when it
    /// starts. Beside the conversation, because it is written by the same process.
    /// </summary>
    public string ResolveWarmupPath() =>
        Path.Combine(Path.GetDirectoryName(ResolveDatabasePath())!, "llm-warmup.json");
}
