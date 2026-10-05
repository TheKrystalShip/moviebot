using System.Text;
using Microsoft.Extensions.Options;
using TheKrystalShip.Llm.Interfaces;
using TheKrystalShip.Llm.Models;
using TheKrystalShip.MovieBot.Client;
using TheKrystalShip.MovieBot.Client.Assistant;
using TheKrystalShip.MovieBot.Core;

namespace TheKrystalShip.MovieBot.Assistant;

/// <summary>
/// What is true at the moment somebody asks — the room they are in, when they are in one, what the
/// library holds, and on a person's own chat what they asked to be remembered — written for the model
/// to read before the request.
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
public sealed class TurnFacts(
    MovieBotApiClient api,
    IMemoryStore memories,
    TimeProvider clock,
    IOptions<AssistantOptions> options,
    ILogger<TurnFacts> logger)
{
    public async Task<string> DescribeAsync(AssistantTurn turn, CancellationToken ct)
    {
        var text = new StringBuilder();
        text.AppendLine("What is true right now. Read it before answering; it is newer than anything said earlier.");
        text.AppendLine($"Speaking: {turn.SpeakerName}.");

        // Read as the person, like everything else the turn asks of the API.
        var asPerson = api.As(turn.AccessToken);

        IReadOnlyList<LibraryTitle>? library = null;
        try
        {
            library = await asPerson.ListTitlesAsync(ct);
        }
        catch (MovieBotApiException ex)
        {
            logger.LogWarning(ex, "Assistant: the library could not be read for the turn");
        }

        if (turn.Room is { } room) text.AppendLine(await RoomLineAsync(asPerson, room, library, ct));
        text.Append(LibraryLines(library));
        text.Append(MemoryLines(turn));
        return text.ToString().TrimEnd();
    }

    /// <summary>
    /// What the person asked to be remembered, one summary a line; <c>recall</c> reads the rest. A turn
    /// with no memory owner, a room's, has none, and a store that cannot be read leaves them out: they
    /// are what the person said, not what is true now.
    /// </summary>
    private string MemoryLines(AssistantTurn turn)
    {
        if (turn.MemoryOwner is not { } owner) return "";

        IReadOnlyList<MemoryRecord> remembered;
        try
        {
            remembered = memories.List(owner);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Assistant: what {Speaker} asked to be remembered could not be read", turn.SpeakerName);
            return "";
        }

        if (remembered.Count == 0) return "";

        var text = new StringBuilder($"What {turn.SpeakerName} asked you to remember:\n");
        foreach (var memory in remembered)
            text.AppendLine($"- {memory.Key}: {memory.Summary}");
        return text.ToString();
    }

    private async Task<string> RoomLineAsync(MovieBotApiClient api, TurnRoom room, IReadOnlyList<LibraryTitle>? library, CancellationToken ct)
    {
        try
        {
            var state = await api.ReadRoomAsync(room.SessionId, ct);
            return Describe(room.Name, state, library, clock.GetUtcNow());
        }
        catch (MovieBotApiException ex)
        {
            logger.LogWarning(ex, "Assistant: room {Session} could not be read for the turn", room.SessionId);
            return $"Room {room.Name}: could not be read just now.";
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
            text.AppendLine($"({library.Count - shown} more; watch_film finds them.)");

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
