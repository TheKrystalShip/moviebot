using System.Text.RegularExpressions;
using TheKrystalShip.MovieBot.Core;

namespace TheKrystalShip.MovieBot.Bot.Presence;

/// <summary>
/// The words for what rooms are watching, in the two places Discord shows them.
///
/// Pure, so the sentences can be checked without a gateway: what the bot says beside its own
/// name, and the line it writes under a voice channel.
/// </summary>
public static partial class RoomStatusText
{
    /// <summary>
    /// What the bot's own status names, for the rooms that are being watched.
    ///
    /// There is one status for the whole bot, so it can name a film only while exactly one room
    /// holds one. With more it counts them, and with none it says nothing at all: a placeholder
    /// beside the name is a status that never changes, which reads as one that never worked.
    /// </summary>
    public static string? BotActivity(IReadOnlyList<RoomSummary> rooms)
    {
        var watched = rooms.Where(IsWatched).ToList();

        return watched.Count switch
        {
            0 => null,
            1 => Truncate(watched[0].Name!, 128),
            var count => $"{count} films"
        };
    }

    /// <summary>
    /// The line under the voice channel a room lives in, or null when the channel should carry
    /// nothing of ours.
    ///
    /// The position is written to the minute. Every distinct line is one request to Discord, so
    /// the granularity of the clock is the pace of the updates: a film playing changes the line
    /// once a minute, and a paused one never does.
    /// </summary>
    public static string? VoiceChannelLine(RoomSummary room)
    {
        if (!IsWatched(room)) return null;

        var name = Truncate(room.Name!, 400);
        var position = Clock(room.PositionSeconds);

        if (room.Paused) return $"{name} · paused at {position}";

        return room.DurationSeconds is { } duration and > 0
            ? $"{name} · {position} / {Clock(duration)}"
            : $"{name} · {position}";
    }

    /// <summary>
    /// Whether a line under a voice channel is one this bot wrote.
    ///
    /// The bot keeps no record of the channels it has written to across a restart, and a line
    /// left standing after the room behind it ended would otherwise stay until the next film in
    /// that channel. A person's own channel status is theirs, so nothing is cleared that does not
    /// carry exactly this shape.
    /// </summary>
    public static bool IsOwnLine(string? status) =>
        status is { Length: > 0 } && OwnLine().IsMatch(status);

    private static bool IsWatched(RoomSummary room) =>
        room.Participants > 0 && room.Name is { Length: > 0 };

    private static string Clock(double seconds)
    {
        var whole = TimeSpan.FromSeconds(Math.Max(0, Math.Floor(seconds)));
        return whole.TotalHours >= 1
            ? $"{(int)whole.TotalHours}:{whole.Minutes:00}"
            : $"{whole.Minutes}:{whole.Seconds:00}";
    }

    private static string Truncate(string value, int max) =>
        value.Length <= max ? value : value[..(max - 1)] + "…";

    [GeneratedRegex(@" · (paused at )?\d+:\d\d( / \d+:\d\d)?$")]
    private static partial Regex OwnLine();
}
