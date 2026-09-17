using System.Diagnostics;
using Microsoft.Extensions.Logging;
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
/// <b>Primed with the name, and never with the trigger.</b> "MovieBot" is not a word a general
/// recogniser has any reason to produce, and a request whose trigger was misheard is not a request at
/// all — so the name is put to it as though it had just been said. The trigger phrase itself is kept
/// out: given a moment of breath, hiss or a keyboard, whisper hands back whichever sentence it was
/// primed with, and a priming that is the trigger turns every such noise into somebody addressing the
/// bot. Measured on hotbox's recogniser against synthetic noise, priming with the trigger phrases came
/// back as "Hey moviebot." for 21 clips in 72 and priming with the name for none, while both heard all
/// 30 spoken requests.
/// </para>
/// </remarks>
public sealed class MovieBotSpeechToText : ISpeechToText
{
    /// <summary>
    /// What the recogniser is primed with. Whatever it echoes of this must not address the bot.
    /// </summary>
    public const string Priming = "MovieBot.";

    private readonly MovieBotSpeech _speech;
    private readonly IVoiceTally _tally;
    private readonly ILogger<MovieBotSpeechToText> _logger;

    public MovieBotSpeechToText(
        MovieBotSpeech speech,
        IVoiceTally tally,
        ILogger<MovieBotSpeechToText> logger)
    {
        _speech = speech;
        _tally = tally;
        _logger = logger;
    }

    public bool IsAvailable => _speech.Available;

    public async Task<string?> TranscribeAsync(VoiceUtterance utterance, CancellationToken ct = default)
    {
        if (!IsAvailable) return null;

        var timer = Stopwatch.StartNew();
        (SpeechProtocol.Outcome outcome, string text) =
            await _speech.Client.TranscribeAsync(utterance.Audio, Priming, ifIdle: false, ct);
        timer.Stop();

        if (outcome != SpeechProtocol.Outcome.Done) return null;

        string transcript = SpokenTranscript.Clean(text);

        _logger.LogDebug(
            "Voice: recognised {Spoken:F1}s{Partial} from {Speaker} in {Elapsed}ms",
            utterance.Duration.TotalSeconds, utterance.Partial ? " so far" : string.Empty,
            utterance.SpeakerName, timer.ElapsedMilliseconds);

        if (SpokenVocabulary.IsEchoOf(transcript, Priming))
        {
            // Whisper continuing the priming instead of admitting it heard nothing. Not counted for a
            // partial, which comes back complete a moment later and would be counted twice.
            if (!utterance.Partial) _tally.Echoed();
            _logger.LogDebug("Voice: discarded a transcript from {Speaker} that was the priming coming back", utterance.SpeakerName);
            return null;
        }

        return transcript.Length == 0 ? null : transcript;
    }

    public async Task<VoiceScan> ScanAsync(VoiceUtterance window, CancellationToken ct = default)
    {
        if (!IsAvailable) return VoiceScan.Failed;

        // Unprimed, and so not echo-checked: a scan looks for the trigger in everything everybody says,
        // and a recogniser told the bot's name hears it in breath and hiss.
        (SpeechProtocol.Outcome outcome, SpeechScan scan) = await _speech.Client.ScanAsync(window.Audio, ct);

        return outcome switch
        {
            SpeechProtocol.Outcome.Done => VoiceScan.Of(scan),
            SpeechProtocol.Outcome.Busy => VoiceScan.Busy,
            _ => VoiceScan.Failed,
        };
    }
}
