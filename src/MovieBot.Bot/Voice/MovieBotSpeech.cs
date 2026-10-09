using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TheKrystalShip.Discord.Voice;
using TheKrystalShip.Speech;

namespace TheKrystalShip.MovieBot.Bot.Voice;

/// <summary>Where the speech daemon this bot hears through answers.</summary>
public sealed class SpeechSocketOptions
{
    public const string Section = "Speech";

    /// <summary>
    /// The unix socket of the speech daemon. Blank means there is none, and nothing is heard.
    /// </summary>
    public string SocketPath { get; set; } = "";
}

/// <summary>
/// This host's recogniser, as the bot reaches it.
/// </summary>
/// <remarks>
/// <para>
/// <b>The model is in another process and is not this one's to manage.</b> The daemon holds the
/// model, so nothing waits on it in front of a room. The bot connects to a socket and asks.
/// </para>
/// <para>
/// <b>A host without it is still a bot.</b> Everything here answers with an absence rather than a
/// failure: with no socket configured, or none there, the bot joins a voice channel and hears
/// nothing, and every command that does not need ears carries on.
/// </para>
/// </remarks>
public sealed class MovieBotSpeech : ISpeechEngine, IDisposable
{
    private readonly bool _enabled;

    public MovieBotSpeech(
        IOptions<SpeechSocketOptions> socket,
        IOptions<DiscordVoiceOptions> voice,
        ILogger<MovieBotSpeech> logger)
    {
        string path = socket.Value.SocketPath;
        Client = string.IsNullOrWhiteSpace(path) ? null : new SpeechClient(path, logger);
        _enabled = voice.Value.Enabled;
    }

    /// <summary>The client every recognition on this host goes through; null with no socket configured.</summary>
    public SpeechClient? Client { get; }

    /// <summary>Whether listening is switched on and the recogniser's socket is there to ask.</summary>
    public bool Available => _enabled && Client is { IsProvisioned: true };

    /// <remarks>
    /// Sent on joining a channel, so a daemon that idles out between films has its model loading
    /// while the room settles.
    /// </remarks>
    public void Wake()
    {
        if (_enabled) Client?.Wake();
    }

    public void Dispose() => Client?.Dispose();
}
