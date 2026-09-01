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
    /// What the session already had loaded, when it is a different film. Reported as a fact so
    /// a room that is already watching something is not surprised by the switch.
    /// </summary>
    public string? LoadedTitleId { get; init; }
}

/// <summary>What the bot posts back to the channel.</summary>
public sealed record LaunchReply(string Text, Embed Embed, MessageComponent? Components);

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
