using System.Globalization;
using TheKrystalShip.MovieBot.Bot.Sessions;

namespace TheKrystalShip.MovieBot.Bot.Assistant;

/// <summary>
/// Who asked, and in which room. Everything a tool acts on is this room and everything it does is
/// done as this person.
/// </summary>
/// <param name="ChannelId">The voice channel, which is the room, and whose chat the answer goes to.</param>
public sealed record RoomTurn(
    ulong GuildId,
    ulong ChannelId,
    string ChannelName,
    ulong SpeakerId,
    string SpeakerName)
{
    public string SessionId => RoomSession.IdFor(ChannelId);

    /// <summary>
    /// One conversation per room, for the life of the room: people talking in one are having a
    /// single conversation, and each line still carries who said it.
    /// </summary>
    public string ConversationId => $"room:{ChannelId.ToString(CultureInfo.InvariantCulture)}";

    public string SpeakerUserId => SpeakerId.ToString(CultureInfo.InvariantCulture);
}
