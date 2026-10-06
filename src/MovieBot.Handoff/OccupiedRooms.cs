using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using TheKrystalShip.MovieBot.Core;

namespace TheKrystalShip.MovieBot.Handoff;

/// <summary>
/// Which films the rooms and the saves hold, and which of them somebody is reading, asked of the API
/// at the moment of asking.
///
/// A room that holds a film may be opened into it at any moment, whether or not anybody is in it
/// this second; the API forgets an idle room on its own, so a room blocks a removal for at most
/// that idle window past its last viewer. A room somebody is connected to is playing its film,
/// paused or not, and that is the narrower question settling asks. A save still being made
/// (<c>/api/library/saving</c>) is a device copying the film file by file, so it counts as both: the
/// film is neither removed, nor moved, nor encoded again under it.
/// </summary>
public sealed class OccupiedRooms(HttpClient http, ILogger<OccupiedRooms> logger)
{
    /// <summary>
    /// The ids of every title a room or a save holds, or null when the API could not say. Null is the
    /// answer that stops a pass acting, because a film deleted from under a room is the one
    /// outcome worse than a film kept a while longer, and a library that cannot be read is
    /// indistinguishable from an empty one.
    /// </summary>
    public async Task<IReadOnlySet<string>?> HeldTitlesAsync(CancellationToken ct) =>
        await ReadAsync(Held, ct);

    /// <summary>
    /// The ids of the titles a room is playing, one with somebody connected whether it is running or
    /// paused, and of those a save is copying. Null when the API could not say, which stops a pass
    /// moving anything, because a copy that saturates the disk a viewer is reading from is only ever
    /// worth making when it is known that nobody is.
    /// </summary>
    public async Task<IReadOnlySet<string>?> PlayingTitlesAsync(CancellationToken ct) =>
        await ReadAsync(Playing, ct);

    /// <summary>Every title a room holds, occupied or not.</summary>
    public static IReadOnlySet<string> Held(IEnumerable<RoomSummary> rooms) =>
        rooms.Select(r => r.TitleId).OfType<string>().ToHashSet(StringComparer.Ordinal);

    /// <summary>The titles of the rooms with somebody connected.</summary>
    public static IReadOnlySet<string> Playing(IEnumerable<RoomSummary> rooms) =>
        Held(rooms.Where(r => r.Participants > 0));

    private async Task<IReadOnlySet<string>?> ReadAsync(
        Func<IEnumerable<RoomSummary>, IReadOnlySet<string>> select, CancellationToken ct)
    {
        try
        {
            var rooms = await http.GetFromJsonAsync(
                "api/sessions", ManifestJsonContext.Default.IReadOnlyListRoomSummary, ct);
            var saving = await http.GetFromJsonAsync(
                "api/library/saving", ManifestJsonContext.Default.StringArray, ct);

            return select(rooms ?? []).Concat(saving ?? []).ToHashSet(StringComparer.Ordinal);
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or TaskCanceledException
                                   && !ct.IsCancellationRequested)
        {
            logger.LogWarning(ex, "The rooms could not be read, so this pass moves and removes nothing.");
            return null;
        }
    }
}
