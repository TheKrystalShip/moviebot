using Microsoft.Extensions.Logging;
using TheKrystalShip.MovieBot.Bot.Api;
using TheKrystalShip.MovieBot.Bot.Launch;
using TheKrystalShip.MovieBot.Bot.Library;
using TheKrystalShip.MovieBot.Bot.Sessions;
using TheKrystalShip.MovieBot.Core;

namespace TheKrystalShip.MovieBot.Bot.Watch;

/// <summary>What the command was asked, reduced to the facts it acts on.</summary>
public sealed record WatchRequest
{
    /// <summary>The voice channel the requester is in, or null when they are in none.</summary>
    public required ulong? VoiceChannelId { get; init; }

    public required string? VoiceChannelName { get; init; }

    /// <summary>What was typed into the command's title option.</summary>
    public required string Query { get; init; }

    public required string RequestedBy { get; init; }
}

public enum WatchStatus
{
    Launched,
    NotInVoiceChannel,
    LibraryEmpty,
    TitleNotFound,
    TitleAmbiguous,
    TitleFailed,
    BackendUnavailable
}

/// <summary>
/// The command's answer. <see cref="Reply"/> is set only when there is something to launch;
/// everything else carries a message that names what to do instead.
/// </summary>
public sealed record WatchResult(WatchStatus Status, string Message, LaunchReply? Reply = null);

/// <summary>
/// Resolves a film, opens the room's session, and hands the launch to the presenter.
///
/// Deliberately free of Discord's types: what makes this command wrong is picking the wrong
/// film or launching into the wrong room, and neither needs a gateway to reproduce.
/// </summary>
public sealed class WatchCommand(
    MovieBotApiClient api,
    ILaunchPresenter presenter,
    ILogger<WatchCommand> logger)
{
    public async Task<WatchResult> ExecuteAsync(WatchRequest request, CancellationToken ct)
    {
        // The room is the voice channel, so there is no room to launch into without one.
        if (request.VoiceChannelId is not { } voiceChannelId)
            return new WatchResult(WatchStatus.NotInVoiceChannel,
                "Join a voice channel first, then run the command again. The channel you are in is "
                + "the room everyone watches in.");

        IReadOnlyList<LibraryTitle> library;
        try
        {
            library = await api.ListTitlesAsync(ct);
        }
        catch (MovieBotApiException ex)
        {
            logger.LogWarning(ex, "The library could not be read");
            return new WatchResult(WatchStatus.BackendUnavailable,
                "The library is not answering. Start the API, then run the command again.");
        }

        if (library.Count == 0)
            return new WatchResult(WatchStatus.LibraryEmpty,
                "There is nothing in the library yet. Ingest a film, then run the command again.");

        var match = TitleMatcher.Resolve(library, request.Query);
        switch (match.Kind)
        {
            case TitleMatchKind.NotFound:
                return new WatchResult(WatchStatus.TitleNotFound,
                    $"Nothing in the library matches \"{request.Query}\". Ask for one of these: "
                    + Names(match.Candidates));

            case TitleMatchKind.Ambiguous:
                return new WatchResult(WatchStatus.TitleAmbiguous,
                    $"\"{request.Query}\" matches several films. Name the one you want: "
                    + Names(match.Candidates));
        }

        var title = match.Title!;
        if (title.Status == TitleStatus.Failed)
            return new WatchResult(WatchStatus.TitleFailed,
                $"The transcode for \"{title.Name}\" failed, so there is nothing to play. "
                + "Ingest it again, then run the command again.");

        var sessionId = RoomSession.IdFor(voiceChannelId);

        SessionState session;
        try
        {
            session = await api.OpenSessionAsync(sessionId, ct);
        }
        catch (MovieBotApiException ex)
        {
            logger.LogWarning(ex, "Session {SessionId} could not be opened", sessionId);
            return new WatchResult(WatchStatus.BackendUnavailable,
                "The session could not be opened. Start the API, then run the command again.");
        }

        // The room is told which film before anyone opens it. An Activity arrives at a URL the bot
        // never wrote, so this is the only way the choice reaches the player; the browser link
        // carries it in a query string too, and both end up in the same session either way.
        var alreadyLoaded = session.TitleId;
        try
        {
            session = await api.SetSessionTitleAsync(session.SessionId, title.Id, request.RequestedBy, ct);
        }
        catch (MovieBotApiException ex)
        {
            logger.LogWarning(ex, "Could not load {TitleId} into session {SessionId}", title.Id, session.SessionId);
            return new WatchResult(WatchStatus.BackendUnavailable,
                "The film could not be loaded into the room. Try the command again.");
        }

        var reply = await presenter.PresentAsync(new LaunchRequest
        {
            SessionId = session.SessionId,
            Title = title,
            VoiceChannelId = voiceChannelId,
            VoiceChannelName = request.VoiceChannelName ?? "the voice channel",
            RequestedBy = request.RequestedBy,
            LoadedTitleId = alreadyLoaded is { } loaded && loaded != title.Id ? loaded : null
        }, ct);

        logger.LogInformation("{RequestedBy} launched {TitleId} into session {SessionId}",
            request.RequestedBy, title.Id, session.SessionId);

        return new WatchResult(WatchStatus.Launched, reply.Text, reply);
    }

    /// <summary>
    /// The films a refused request could have meant. Ids come along because the id is what a
    /// second attempt can be typed exactly, where a title with punctuation in it invites another
    /// near miss.
    /// </summary>
    private static string Names(IReadOnlyList<LibraryTitle> titles) =>
        string.Join(", ", titles.Take(5).Select(t => $"{t.Name} ({t.Id})"));
}
