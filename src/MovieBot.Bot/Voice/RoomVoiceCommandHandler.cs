using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TheKrystalShip.Discord.Voice;
using TheKrystalShip.MovieBot.Bot.Api;
using TheKrystalShip.MovieBot.Bot.Sessions;
using TheKrystalShip.MovieBot.Core;

namespace TheKrystalShip.MovieBot.Bot.Voice;

/// <summary>What became of one spoken request.</summary>
public enum VoiceOutcome
{
    /// <summary>The trigger and nothing after it.</summary>
    Nothing,

    /// <summary>A room verb, carried out.</summary>
    Acted,

    /// <summary>A room verb in a voice channel whose room holds no film, so there was nothing to move.</summary>
    NoFilm,

    /// <summary>Not a room verb the gate could read. Nothing on this host answers these.</summary>
    NotHandled,

    /// <summary>A room verb the API refused or could not be reached for.</summary>
    Failed,
}

/// <summary>
/// Turns what somebody said to the room into what happens to it.
/// </summary>
/// <remarks>
/// <para>
/// <b>The gate first, and only the gate.</b> <see cref="RoomVerbs"/> decides whether an utterance is
/// one of the room's own verbs. One that is gets carried out; anything else is left alone, because
/// nothing on this host understands language — a question put to the bot out loud is heard, recorded
/// as unhandled, and answered by nothing rather than by a guess.
/// </para>
/// <para>
/// <b>The act is silent.</b> The film stopping is the acknowledgement, and the player already tells
/// everyone who did it. Saying "paused" over the film would cost a round trip to a synthesiser and a
/// second of audio to announce what the whole room just watched happen.
/// </para>
/// <para>
/// <b>The room is the voice channel, and it has to hold a film.</b> Every write to a room creates it,
/// so a pause said in a channel nobody is watching anything in would conjure an empty room with a
/// stranger's name on its last change. The room is looked up first, which does not create one.
/// </para>
/// <para>
/// <b>Whoever spoke is who did it.</b> The change is recorded under the speaker's own account, so the
/// bubble the player draws names the person, and a question later about why the film stopped has an
/// answer in the room itself.
/// </para>
/// </remarks>
public sealed class RoomVoiceCommandHandler(
    MovieBotApiClient api,
    TimeProvider clock,
    IOptions<DiscordVoiceOptions> voice,
    ILogger<RoomVoiceCommandHandler> logger) : IVoiceCommandHandler
{
    async ValueTask IVoiceCommandHandler.HandleAsync(VoiceCommand command, CancellationToken ct) =>
        await HandleAsync(command, ct);

    /// <summary>Handles one spoken request and says what became of it. Never throws.</summary>
    public async Task<VoiceOutcome> HandleAsync(VoiceCommand command, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(command.Text)) return VoiceOutcome.Nothing;

        var reading = RoomVerbs.Read(command.Text);
        if (reading.Match != RoomVerbMatch.Match)
        {
            // What was said is only written down when the operator has asked for transcripts: a voice
            // channel is full of things nobody said to the bot.
            if (voice.Value.LogTranscripts)
                logger.LogInformation(
                    "Voice: {Speaker} said \"{Text}\", which is not a room verb ({Reading})",
                    command.SpeakerName, command.Text, reading.Match);
            else
                logger.LogInformation(
                    "Voice: {Speaker} asked for something that is not a room verb ({Reading})",
                    command.SpeakerName, reading.Match);

            return VoiceOutcome.NotHandled;
        }

        var session = RoomSession.IdFor(command.ChannelId);

        try
        {
            var rooms = await api.ListRoomsAsync(ct);
            if (rooms.FirstOrDefault(r => r.SessionId == session) is not { TitleId: not null })
            {
                logger.LogInformation(
                    "Voice: {Speaker} asked to {Verb} in a channel whose room holds no film",
                    command.SpeakerName, Describe(reading));
                return VoiceOutcome.NoFilm;
            }

            var userId = command.SpeakerId.ToString(System.Globalization.CultureInfo.InvariantCulture);
            RoomChanged change = reading.Kind switch
            {
                RoomVerbKind.Pause => await api.PauseAsync(session, userId, command.SpeakerName, ct),
                RoomVerbKind.Play => await api.PlayAsync(session, userId, command.SpeakerName, ct),
                _ => await api.NudgeAsync(session, reading.Seconds, userId, command.SpeakerName, ct),
            };

            // The number this surface is judged on: how long from the moment somebody stopped talking to
            // the moment the room changed. The silence that ends a sentence is inside it, on purpose —
            // that wait is part of what the person experiences.
            if (command.EndedAt is { } ended)
                logger.LogInformation(
                    "Voice: {Speaker} {Verb} -> {Position:0.0}s, {Elapsed:0}ms after they stopped talking",
                    command.SpeakerName, Describe(reading), change.Push.State.PositionSeconds,
                    (clock.GetUtcNow() - ended).TotalMilliseconds);
            else
                logger.LogInformation(
                    "Voice: {Speaker} {Verb} -> {Position:0.0}s",
                    command.SpeakerName, Describe(reading), change.Push.State.PositionSeconds);

            return VoiceOutcome.Acted;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            return VoiceOutcome.Failed;
        }
        catch (Exception ex)
        {
            // One request failing is not the surface failing: the next thing said is heard as usual.
            logger.LogWarning(ex, "Voice: could not {Verb} for {Speaker}", Describe(reading), command.SpeakerName);
            return VoiceOutcome.Failed;
        }
    }

    private static string Describe(RoomVerbReading reading) => reading.Kind switch
    {
        RoomVerbKind.Pause => "pause",
        RoomVerbKind.Play => "play",
        _ when reading.Seconds < 0 => $"go back {-reading.Seconds:0}s",
        _ => $"go forward {reading.Seconds:0}s",
    };
}
