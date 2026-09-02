using System.Text.Json;
using System.Text.Json.Serialization;
using TheKrystalShip.MovieBot.Core;

namespace TheKrystalShip.MovieBot.Api.Sessions;

/// <summary>One room as it is kept between runs of the server.</summary>
public sealed record PersistedSession
{
    public required SessionState State { get; init; }

    /// <summary>
    /// When somebody last did something in this room. Kept because it is what decides whether a
    /// room is still worth having: a room nobody has touched since before the idle window is one
    /// the reaper would have forgotten anyway, and restoring it would resurrect a link that was
    /// supposed to have stopped working.
    /// </summary>
    public required DateTimeOffset LastActivityUtc { get; init; }
}

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    UseStringEnumConverter = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    WriteIndented = true)]
[JsonSerializable(typeof(List<PersistedSession>))]
internal partial class SessionJournalJson : JsonSerializerContext;

/// <summary>
/// Keeps the rooms across a restart.
///
/// A room is a few small fields and it is the only thing in the system that exists nowhere else:
/// the films are on disk, the subtitles are on disk, and what a room is watching and where it has
/// got to lives in memory and nowhere at all. Restarting the server — a deploy, a crash — used to
/// take a film out from under everybody watching it, leaving a player that carried on playing
/// against a server that no longer knew what it was.
///
/// What is written is the state itself, revision and epoch included. A restored room is the same
/// run of the same room rather than a new one that happens to share its name, so a client's
/// revision gate goes on working and the resync it does on reconnecting hands it back the room it
/// already had. Nothing about the restart reaches a person watching.
///
/// Position survives for free. It is held as a position true at an instant rather than as a
/// ticking number, so a room that was playing when the server stopped is still playing, at the
/// right place, when it starts again — however long that took.
/// </summary>
public sealed class SessionJournal(string path, ILogger<SessionJournal> logger)
{
    private readonly SemaphoreSlim _gate = new(1, 1);

    public string Path { get; } = path;

    /// <summary>
    /// The rooms worth restoring. Never throws: a journal that cannot be read is a server that
    /// starts with no rooms, which is exactly where it would have been without one.
    /// </summary>
    public IReadOnlyList<PersistedSession> Read(DateTimeOffset now, TimeSpan idleFor)
    {
        if (!File.Exists(Path)) return [];

        try
        {
            var kept = JsonSerializer.Deserialize(
                File.ReadAllText(Path), SessionJournalJson.Default.ListPersistedSession) ?? [];

            var cutoff = now - idleFor;
            var live = kept.Where(s => s.LastActivityUtc > cutoff).ToList();

            if (live.Count < kept.Count)
                logger.LogInformation(
                    "{Dropped} room(s) in the journal had been idle past the window and were not restored.",
                    kept.Count - live.Count);

            return live;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "The room journal at {Path} could not be read; starting with none.", Path);
            return [];
        }
    }

    /// <summary>
    /// Replaces the journal with what is held now. Written whole and moved into place, because a
    /// half-written journal read at the next start is worse than no journal at all.
    /// </summary>
    public async Task WriteAsync(IReadOnlyList<PersistedSession> sessions, CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);

            var temporary = Path + ".tmp";
            await File.WriteAllTextAsync(
                temporary,
                JsonSerializer.Serialize(sessions, SessionJournalJson.Default.ListPersistedSession),
                ct);

            File.Move(temporary, Path, overwrite: true);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // A room that cannot be written down is a room that will not survive the next restart.
            // That is worth saying and is not worth failing a seek over.
            logger.LogError(ex, "The room journal at {Path} could not be written.", Path);
        }
        finally
        {
            _gate.Release();
        }
    }
}
