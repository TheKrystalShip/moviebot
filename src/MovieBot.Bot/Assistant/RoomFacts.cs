using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TheKrystalShip.MovieBot.Bot.Api;
using TheKrystalShip.MovieBot.Core;

namespace TheKrystalShip.MovieBot.Bot.Assistant;

/// <summary>
/// What is true in the room at the moment somebody speaks, and what the library holds, written for
/// the model to read before the request.
/// </summary>
/// <remarks>
/// <para>
/// <b>Without it the model grounds itself first.</b> "Carry on" means play only if the film is paused,
/// and "back to the start of it" means nothing without knowing which film. A model that has not been
/// told spends a tool call finding out, or asks — measured on this model, "carry on" with nothing
/// said about the room came back as a question.
/// </para>
/// <para>
/// <b>Read fresh every turn and never remembered.</b> It goes into the turn's context, after the
/// conversation and before the request, which is also where it costs least: everything ahead of it
/// is the same as the turn before.
/// </para>
/// <para>
/// A room or a library that cannot be read is said to be unreadable rather than left out. Left out,
/// it reads as a room holding nothing.
/// </para>
/// </remarks>
public sealed class RoomFacts(
    MovieBotApiClient api,
    TimeProvider clock,
    IOptions<AssistantOptions> options,
    ILogger<RoomFacts> logger)
{
    public async Task<string> DescribeAsync(RoomTurn turn, CancellationToken ct)
    {
        var text = new StringBuilder();
        text.AppendLine("What is true right now. Read it before answering; it is newer than anything said earlier.");
        text.AppendLine($"Speaking: {turn.SpeakerName}.");

        IReadOnlyList<LibraryTitle>? library = null;
        try
        {
            library = await api.ListTitlesAsync(ct);
        }
        catch (MovieBotApiException ex)
        {
            logger.LogWarning(ex, "Assistant: the library could not be read for the turn");
        }

        text.AppendLine(await RoomLineAsync(turn, library, ct));
        text.Append(LibraryLines(library));
        return text.ToString().TrimEnd();
    }

    private async Task<string> RoomLineAsync(RoomTurn turn, IReadOnlyList<LibraryTitle>? library, CancellationToken ct)
    {
        try
        {
            var state = await api.ReadRoomAsync(turn.SessionId, ct);
            return Describe(turn.ChannelName, state, library, clock.GetUtcNow());
        }
        catch (MovieBotApiException ex)
        {
            logger.LogWarning(ex, "Assistant: room {Session} could not be read for the turn", turn.SessionId);
            return $"Room {turn.ChannelName}: could not be read just now.";
        }
    }

    /// <summary>One room in a sentence: the film, where it has got to, whether it is moving, and who last moved it.</summary>
    public static string Describe(
        string roomName, SessionState? state, IReadOnlyList<LibraryTitle>? library, DateTimeOffset now)
    {
        if (state?.TitleId is not { } titleId)
            return $"Room {roomName}: no film is loaded.";

        var title = library?.FirstOrDefault(t => t.Id == titleId);
        var name = title?.Name ?? titleId;
        var position = FilmClock.Format(state.PositionAt(now));
        var length = title is null ? "" : $" of {FilmClock.Format(title.DurationSeconds)}";

        // "Waiting to be played" rather than a bare "paused": measured on this model, "carry on",
        // "keep going" and "resume" against a room described as paused came back as a pause or a
        // question, and against one waiting to be played came back as a play.
        var line = new StringBuilder($"Room {roomName}: {name} (id {titleId}) at {position}{length}, "
                                     + (state.Paused ? "paused, waiting to be played" : "playing"));

        if (state.TranscodeHead is { } head)
            line.Append($", playable up to {FilmClock.Format(head)} while it transcodes");

        if (state.UpdatedBy is { } by)
            line.Append($". Last changed by {by.DisplayName}");

        return line.Append('.').ToString();
    }

    private string LibraryLines(IReadOnlyList<LibraryTitle>? library)
    {
        if (library is null) return "The library could not be read just now.\n";
        if (library.Count == 0) return "The library is empty.\n";

        var shown = Math.Max(1, options.Value.LibraryInContext);
        var text = new StringBuilder($"The library ({library.Count} films, id then name):\n");

        foreach (var title in library.OrderBy(t => t.Name, StringComparer.OrdinalIgnoreCase).Take(shown))
            text.AppendLine($"- {title.Id}: {title.Name}{StatusNote(title)}");

        if (library.Count > shown)
            text.AppendLine($"({library.Count - shown} more; search_library finds them.)");

        return text.ToString();
    }

    /// <summary>Said only when a film cannot simply be played from anywhere, which is the exception.</summary>
    public static string StatusNote(LibraryTitle title) => title.Status switch
    {
        TitleStatus.Failed => " (its transcode failed; it cannot be played)",
        TitleStatus.Transcoding when title.HeadSeconds is { } head =>
            $" (still transcoding, playable up to {FilmClock.Format(head)})",
        TitleStatus.Transcoding => " (still transcoding)",
        _ => "",
    };
}
