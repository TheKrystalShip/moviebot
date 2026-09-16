namespace TheKrystalShip.MovieBot.Speech;

/// <summary>
/// What this host tells the engine: where the model is, where the socket goes, and that it never
/// answers out loud.
/// </summary>
public sealed class SpeechSettings
{
    public const string Section = "Speech";

    /// <summary>The whisper model used to recognise speech.</summary>
    public string ModelPath { get; set; } = "/opt/moviebot/whisper/models/ggml-small.en.bin";

    /// <summary>The unix socket the bot reaches this on.</summary>
    public string SocketPath { get; set; } = "/run/moviebot-speech/speech.sock";

    /// <summary>Whether to recognise on the card, falling back to the processor.</summary>
    public bool UseGpu { get; set; } = true;

    /// <summary>
    /// The whisper library to load: this machine's own build, not a prebuilt runtime.
    /// </summary>
    /// <remarks>
    /// Every prebuilt linux-x64 runtime Whisper.net ships is wrong for this host, and both are
    /// wrong quietly. The CUDA one carries no PTX for a Pascal card, so it would drop to the
    /// processor without saying so. The others compile the CPU backend for AVX2, which this
    /// machine's Athlon cannot execute — and that does not fail at load, it fails on the first
    /// request that reaches one of those instructions. The build beside the model is made for this
    /// processor and links Vulkan as a hard dependency, so it either loads or the service does not
    /// start. See deploy/vulkan.
    /// </remarks>
    public string NativeLibraryPath { get; set; } = "/opt/moviebot/whisper/lib/libwhisper.so";

    /// <summary>Which whisper runtimes to try, in order.</summary>
    /// <remarks>
    /// Vulkan and nothing else. There is no processor fallback on purpose: on this machine's four
    /// Athlon cores recognition takes about three seconds against a fifth of a second on the card,
    /// which is far too slow to be a fallback anybody would want silently — better a host that says
    /// it cannot hear than one that answers a spoken command fifteen times slower with nothing
    /// saying why.
    /// </remarks>
    public string[] Accelerators { get; set; } = ["vulkan"];

    /// <summary>How much of whisper's thirty-second window to encode, in encoder frames.</summary>
    /// <remarks>
    /// Eight seconds, at roughly fifty frames to the second. The whole thirty seconds costs 870 ms an
    /// utterance on this card and eight costs 220 ms. Four costs 114 ms and is too short: a request to
    /// the assistant, with the trigger in front of it, runs past four seconds, and whisper given a
    /// clip longer than its window repeats phrases rather than stopping ("Pirates of the First
    /// Pirates of the First"). Whatever runs past the window is still lost, so the number is a
    /// ceiling on what can be said in one breath.
    /// </remarks>
    public int AudioContextFrames { get; set; } = 400;
}
