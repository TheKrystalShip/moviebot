using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TheKrystalShip.Discord.Voice;
using TheKrystalShip.Speech;

namespace TheKrystalShip.MovieBot.Bot.Voice;

/// <summary>
/// Recognition, as the voice surface asks for it: an utterance in, words out.
/// </summary>
/// <remarks>
/// <para>
/// <b>What a transcript means is decided by the speech package, not here.</b> Which parts are speech
/// rather than notes about the audio, and whether a transcript is really the priming coming back, are
/// facts about the recogniser, and every surface that listens reads them the same way. What this owns
/// is the one thing only this bot knows: what to prime the recogniser with.
/// </para>
/// <para>
/// <b>Primed with the wake phrases.</b> "MovieBot" is not a word a general recogniser has any reason to
/// produce, and a request whose trigger was misheard is not a request at all — so the phrases are named
/// to it as though they had just been said.
/// </para>
/// </remarks>
public sealed class MovieBotSpeechToText : ISpeechToText
{
    private readonly MovieBotSpeech _speech;
    private readonly IVoiceTally _tally;
    private readonly ILogger<MovieBotSpeechToText> _logger;
    private readonly string _vocabulary;

    public MovieBotSpeechToText(
        MovieBotSpeech speech,
        IOptions<DiscordVoiceOptions> voice,
        IVoiceTally tally,
        ILogger<MovieBotSpeechToText> logger)
    {
        _speech = speech;
        _tally = tally;
        _logger = logger;

        string[] triggers = voice.Value.Triggers
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        _vocabulary = SpokenVocabulary.Compose(triggers, [], []);
    }

    public bool IsAvailable => _speech.Available;

    public Task<string?> TranscribeAsync(VoiceUtterance utterance, CancellationToken ct = default) =>
        RecogniseAsync(utterance, ifIdle: false, ct);

    public Task<string?> TranscribeIfIdleAsync(VoiceUtterance utterance, CancellationToken ct = default) =>
        RecogniseAsync(utterance, ifIdle: true, ct);

    private async Task<string?> RecogniseAsync(VoiceUtterance utterance, bool ifIdle, CancellationToken ct)
    {
        if (!IsAvailable) return null;

        var timer = Stopwatch.StartNew();
        (SpeechProtocol.Outcome outcome, string text) =
            await _speech.Client.TranscribeAsync(utterance.Audio, _vocabulary, ifIdle, ct);
        timer.Stop();

        if (outcome == SpeechProtocol.Outcome.Busy)
        {
            // Invisible from inside a channel: being addressed while the recogniser is occupied goes
            // unnoticed until the sentence ends. Logged so contention can be told apart from a trigger
            // that is not matching.
            _logger.LogDebug("Voice: skipped reading {Speaker} early, the recogniser was busy", utterance.SpeakerName);
            return null;
        }

        if (outcome != SpeechProtocol.Outcome.Done) return null;

        string transcript = SpokenTranscript.Clean(text);

        _logger.LogDebug(
            "Voice: recognised {Spoken:F1}s{Partial} from {Speaker} in {Elapsed}ms",
            utterance.Duration.TotalSeconds, utterance.Partial ? " so far" : string.Empty,
            utterance.SpeakerName, timer.ElapsedMilliseconds);

        if (SpokenVocabulary.IsEchoOf(transcript, _vocabulary))
        {
            // Whisper continuing the priming instead of admitting it heard nothing. Not counted for a
            // partial, which comes back complete a moment later and would be counted twice.
            if (!utterance.Partial) _tally.Echoed();
            _logger.LogDebug("Voice: discarded a transcript from {Speaker} that was the priming coming back", utterance.SpeakerName);
            return null;
        }

        return transcript.Length == 0 ? null : transcript;
    }
}
