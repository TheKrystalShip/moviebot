using Discord;
using TheKrystalShip.MovieBot.Bot.Api;

namespace TheKrystalShip.MovieBot.Bot.Launch;

/// <summary>
/// Everything a launch needs to know about the room and the film. All of it is read from
/// Discord and from the API at the moment the command runs; none of it is remembered.
/// </summary>
public sealed record LaunchRequest
{
    /// <summary>The room's session, derived from the voice channel.</summary>
    public required string SessionId { get; init; }

    public required LibraryTitle Title { get; init; }

    public required ulong VoiceChannelId { get; init; }
    public required string VoiceChannelName { get; init; }

    /// <summary>Who asked, as the room sees them.</summary>
    public required string RequestedBy { get; init; }

    /// <summary>
    /// The film the room was watching before this one, when it was watching a different one.
    /// The switch has already happened for everyone in the room by the time this is presented,
    /// so the message says what was replaced rather than asking.
    /// </summary>
    public LibraryTitle? Replaced { get; init; }

    /// <summary>
    /// Whether the room already held this very film. It was left exactly where it was, and the
    /// message is a way in rather than an announcement.
    /// </summary>
    public bool AlreadyWatching { get; init; }

    /// <summary>
    /// How long the film stays on disk, or null when no download stands behind it. It is on the
    /// launch because the launch is where somebody who wants the film around is looking when they
    /// find out it will not be.
    /// </summary>
    public string? OnDisk { get; init; }

    /// <summary>The one sentence above the card, worded by what the command did to the room.</summary>
    public string Headline =>
        AlreadyWatching
            ? $"{VoiceChannelName} is already watching {Title.Name}. {RequestedBy} asked for it again."
            : Replaced is { } replaced
                ? $"{RequestedBy} switched {VoiceChannelName} from {replaced.Name} to {Title.Name}."
                : $"{RequestedBy} started {Title.Name} in {VoiceChannelName}.";
}

/// <summary>
/// The way into a room that a launch handed out, and what it was handed out for.
///
/// Carried back with the reply because the message and the invite are one thing: the card is
/// only a way in for as long as the room behind it is watching that film, and both halves are
/// needed to say so once it is not. A launch that opens a browser link carries none, since a
/// link into the player neither expires nor has to be taken away.
/// </summary>
public sealed record LaunchInvite
{
    /// <summary>The invite's code, which is the whole of the URL that is not the host.</summary>
    public required string Code { get; init; }

    /// <summary>The room it opens.</summary>
    public required string SessionId { get; init; }

    /// <summary>The film the card announces, which is what the room is checked against.</summary>
    public required string TitleId { get; init; }
}

/// <summary>What the bot posts back to the channel.</summary>
public sealed record LaunchReply(
    string Text, Embed Embed, MessageComponent? Components, LaunchInvite? Invite = null);

/// <summary>
/// How a person gets from a Discord message into the player.
///
/// It is the only part of the command that knows the answer, so the command resolves the film,
/// opens the session and hands over — a presenter that opens the player as an Activity inside
/// the voice channel replaces one that links to it, and nothing above this line changes.
/// </summary>
public interface ILaunchPresenter
{
    Task<LaunchReply> PresentAsync(LaunchRequest request, CancellationToken ct);
}
