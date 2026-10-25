using System.Text.Json;

namespace TheKrystalShip.MovieBot.Api.Sessions;

/// <summary>
/// What a viewer's stats panel shows, sent when they press Send, written to the log and to nothing
/// else.
///
/// The panel measures what only the browser can: how far the picture is from the sound, the output
/// device's latency, the player's drift from the room with every correction it made, the buffers and the
/// fetches. A person describing trouble in words leaves the operator guessing; the snapshot is the
/// numbers themselves, under the viewer's name, the room and the playback its requests carry.
///
/// A snapshot is a JSON object of at most <see cref="MaxChars"/> characters, written as the page sent
/// it once it parses. Each connection may send one every <see cref="Every"/>; one sooner is not
/// written, and the player is told so.
/// </summary>
public sealed class PlayerStats(TimeProvider clock, ILogger<PlayerStats> logger)
{
    public const int MaxChars = 8192;
    public static readonly TimeSpan Every = TimeSpan.FromSeconds(10);

    private readonly Lock _gate = new();
    private readonly Dictionary<string, DateTimeOffset> _sent = new();

    /// <summary>Writes a snapshot. False when it was not written: too soon, too large, or not a JSON object.</summary>
    public bool Report(string connectionId, string who, string sessionId, string? titleId, string? snapshot)
    {
        if (string.IsNullOrEmpty(snapshot) || snapshot.Length > MaxChars || !IsObject(snapshot)) return false;

        var now = clock.GetUtcNow();
        lock (_gate)
        {
            if (_sent.TryGetValue(connectionId, out var last) && now - last < Every) return false;
            _sent[connectionId] = now;
        }

        // A parsed JSON text holds no raw control characters, so the line is one line however it was sent.
        logger.LogInformation("{Who}'s player in {SessionId} sent its playback stats for {TitleId}: {Stats}",
            who, sessionId, titleId ?? "nothing", snapshot);
        return true;
    }

    /// <summary>Forgets a connection that left.</summary>
    public void Leave(string connectionId)
    {
        lock (_gate) _sent.Remove(connectionId);
    }

    private static bool IsObject(string text)
    {
        try
        {
            using var document = JsonDocument.Parse(text);
            return document.RootElement.ValueKind == JsonValueKind.Object;
        }
        catch (JsonException)
        {
            return false;
        }
    }
}
