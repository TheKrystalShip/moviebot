using Discord;
using TheKrystalShip.MovieBot.Core;

namespace TheKrystalShip.MovieBot.Bot.Launch;

/// <summary>
/// What a launch card becomes once it has stopped being a way into anything.
///
/// A card is a door, and a door that no longer opens has to say so where it stands. Left alone
/// it reads exactly as it did the moment it was posted — the same film, the same button, nothing
/// anywhere to explain why pressing it does nothing — and what somebody does next is ask the
/// room whether the bot is broken.
/// </summary>
public static class ExpiredCard
{
    /// <summary>
    /// Discord's own muted grey. The card keeps its poster and its fields, because scrolling back
    /// to what an evening watched is worth being able to do; the colour is what says at a glance
    /// that this one is over.
    /// </summary>
    private static readonly Color Spent = new(0x4F, 0x54, 0x5C);

    /// <summary>
    /// Why this card is no longer a way in, or null while it still is.
    ///
    /// Two ways to stop being one, and they are worth telling apart: the room closed, so the film
    /// is over and starting it again is what is left to do; or the room moved on to another film,
    /// so it is still watching and this card is simply describing the wrong thing.
    ///
    /// A room holding nothing counts as closed. A card is a way into a film, and a room that was
    /// forgotten and opened again by somebody arriving is an empty room wearing the same name.
    /// </summary>
    public static string? Reason(LaunchCard card, IReadOnlyList<RoomSummary> rooms)
    {
        var room = rooms.FirstOrDefault(r => string.Equals(r.SessionId, card.SessionId, StringComparison.Ordinal));

        if (room is null || room.TitleId is null)
            return "This room has closed. Nobody was left watching, so the film was put away. "
                + "Run /watch in a voice channel to start it again.";

        if (string.Equals(room.TitleId, card.TitleId, StringComparison.Ordinal)) return null;

        var now = room.Name is { Length: > 0 } name ? name : "something else";
        return $"The room is watching {now} now. "
            + "Run /watch in a voice channel to start this one again.";
    }

    /// <summary>
    /// The card as it should now stand: what it was, greyed, saying what happened instead of what
    /// is playable. The title stops being a link along with the button, since both led to the
    /// same door.
    /// </summary>
    public static Embed Rebuild(IEmbed posted, string reason)
    {
        var embed = posted.ToEmbedBuilder()
            .WithColor(Spent)
            .WithDescription(reason);

        embed.Url = null;
        return embed.Build();
    }
}
