using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TheKrystalShip.Discord.Voice;
using TheKrystalShip.Speech;

namespace TheKrystalShip.MovieBot.Bot.Voice;

/// <summary>Where moviebot-speech answers.</summary>
public sealed class SpeechSocketOptions
{
    public const string Section = "Speech";

    /// <summary>
    /// The socket moviebot-speech listens on. The same key the speech host reads, so both ends of the
    /// socket are configured by one name and cannot drift apart.
    /// </summary>
    public string SocketPath { get; set; } = "/run/moviebot-speech/speech.sock";
}

/// <summary>
/// This host's recogniser, as the bot reaches it.
/// </summary>
/// <remarks>
/// <para>
/// <b>The model is in another process and is not this one's to manage.</b> moviebot-speech holds
/// whisper on the card, loads it at startup and never lets it go, so nothing waits on a model in
/// front of a room. The bot connects to a socket and asks.
/// </para>
/// <para>
/// <b>A host without it is still a bot.</b> Everything here answers with an absence rather than a
/// failure: without the socket the bot joins a voice channel and hears nothing, and every command
/// that does not need ears carries on.
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
        Client = new SpeechClient(socket.Value.SocketPath, logger);
        _enabled = voice.Value.Enabled;
    }

    /// <summary>The client every recognition on this host goes through.</summary>
    public SpeechClient Client { get; }

    /// <summary>Whether listening is switched on and the recogniser's socket is there to ask.</summary>
    public bool Available => _enabled && Client.IsProvisioned;

    /// <remarks>
    /// Sent on joining a channel. moviebot-speech is resident and already warm, so this costs a
    /// message and changes nothing — it is sent anyway because whether the recogniser idles is the
    /// recogniser's business, and a bot that assumed it never does would be wrong the day it does.
    /// </remarks>
    public void Wake()
    {
        if (_enabled) Client.Wake();
    }

    public void Dispose() => Client.Dispose();
}
