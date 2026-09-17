using TheKrystalShip.Discord.Voice;

namespace TheKrystalShip.MovieBot.Bot.Voice;

/// <summary>
/// Calls a spoken request whole the moment it reads as one of the room's verbs, so the film stops
/// while the room keeps talking.
/// </summary>
/// <remarks>
/// <para>
/// <b>People watching together do not go quiet after "hey MovieBot, pause".</b> They carry on talking
/// about the film, and waiting for a pause leaves it playing over the moment somebody wanted it
/// stopped. The voice pipeline reads a request as it grows and asks this whether it is already whole.
/// </para>
/// <para>
/// <b>The same gate the handler acts on, and only its unmistakable answer.</b> A request that is a
/// verb here is exactly one <see cref="RoomVerbs"/> would carry out; anything ambiguous, and anything
/// that is not a verb at all, runs until the speaker stops, and the handler reads it then. The
/// pipeline asks only once two readings in a row have agreed, so a word cut off mid-syllable is not
/// taken for the verb it starts like.
/// </para>
/// </remarks>
public sealed class RoomVerbCompleteness : IVoiceCommandCompleteness
{
    public bool IsComplete(string request) => RoomVerbs.Read(request).Match == RoomVerbMatch.Match;
}
