using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using TheKrystalShip.MovieBot.Core;

namespace TheKrystalShip.MovieBot.Handoff;

/// <summary>
/// Which films the rooms hold right now, read from the API at the moment of asking.
///
/// A room that holds a film may be opened into it at any moment, whether or not anybody is in it
/// this second; the API forgets an idle room on its own, so a room blocks a prune for at most
/// that idle window past its last viewer.
/// </summary>
public sealed class OccupiedRooms(HttpClient http, ILogger<OccupiedRooms> logger)
{
    /// <summary>
    /// The ids of every title a room holds, or null when the API could not say. Null is the
    /// answer that stops a pass acting, because a film deleted from under a room is the one
    /// outcome worse than a film kept a while longer, and a library that cannot be read is
    /// indistinguishable from an empty one.
    /// </summary>
    public async Task<IReadOnlySet<string>?> HeldTitlesAsync(CancellationToken ct)
    {
        try
        {
            var rooms = await http.GetFromJsonAsync(
                "api/sessions", ManifestJsonContext.Default.IReadOnlyListRoomSummary, ct);

            return rooms?
                .Select(r => r.TitleId)
                .OfType<string>()
                .ToHashSet(StringComparer.Ordinal) ?? [];
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or TaskCanceledException
                                   && !ct.IsCancellationRequested)
        {
            logger.LogWarning(ex, "The rooms could not be read, so this pass moves and removes nothing.");
            return null;
        }
    }
}
