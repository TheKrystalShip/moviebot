using System.Globalization;

namespace TheKrystalShip.MovieBot.Bot.Sessions;

/// <summary>
/// The room is the voice channel, so the session is named by the channel and by nothing else.
///
/// This is what lets the bot hold no state. Two people asking for a film in the same channel
/// reach the same session without anything remembering that the first one asked, a restarted
/// bot lands the next request in the room that is already playing, and there is no table
/// mapping channels to sessions that could disagree with reality.
/// </summary>
public static class RoomSession
{
    /// <summary>
    /// The channel's snowflake, which travels in a URL path and names a SignalR group as it is.
    /// Never anything a person typed.
    /// </summary>
    public static string IdFor(ulong voiceChannelId) =>
        voiceChannelId.ToString(CultureInfo.InvariantCulture);
}
