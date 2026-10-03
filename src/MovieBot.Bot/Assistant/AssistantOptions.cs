namespace TheKrystalShip.MovieBot.Bot.Assistant;

/// <summary>How the bot reaches moviebot-assistant for what the room verbs do not cover.</summary>
public sealed class AssistantOptions
{
    public const string Section = "Assistant";

    /// <summary>
    /// Whether a spoken request that is not a room verb is put to the assistant. Off, those requests
    /// are heard and answered by nothing, and the room verbs work exactly the same.
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>Where moviebot-assistant answers. Loopback is the normal answer.</summary>
    public string BaseUrl { get; set; } = "http://127.0.0.1:8191";

    /// <summary>
    /// How long the bot listens for a spoken yes or no to what the assistant proposed, without the
    /// trigger, in seconds. Zero leaves the buttons as the only way to agree.
    /// </summary>
    public int ConfirmWindowSeconds { get; set; } = 20;
}
