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

/// <summary>A position in a film, the way a person reads one.</summary>
public static class FilmClock
{
    public static string Format(double seconds)
    {
        var span = TimeSpan.FromSeconds(Math.Max(0, Math.Round(seconds)));
        return span.TotalHours >= 1
            ? $"{(int)span.TotalHours}:{span.Minutes:00}:{span.Seconds:00}"
            : $"{span.Minutes}:{span.Seconds:00}";
    }

    /// <summary>
    /// Reads "1:02:03", "62:03", "90" or "90.5" as seconds. Null for anything else, which is refused
    /// rather than guessed at: a position read wrongly moves the film for everybody.
    /// </summary>
    public static double? Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var trimmed = text.Trim();

        if (double.TryParse(trimmed, NumberStyles.Float, CultureInfo.InvariantCulture, out var plain))
            return plain >= 0 ? plain : null;

        var parts = trimmed.Split(':');
        if (parts.Length is < 2 or > 3) return null;

        double total = 0;
        for (var i = 0; i < parts.Length; i++)
        {
            if (!int.TryParse(parts[i], NumberStyles.None, CultureInfo.InvariantCulture, out var value)) return null;
            if (i > 0 && value >= 60) return null;
            total = total * 60 + value;
        }

        return total;
    }
}
