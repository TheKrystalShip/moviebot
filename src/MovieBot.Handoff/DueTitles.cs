using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using TheKrystalShip.MovieBot.Core;

namespace TheKrystalShip.MovieBot.Handoff;

/// <summary>
/// Which settled films the library's clock says are due to leave, asked of the API at the moment of
/// acting. The rule is the API's alone; this process deletes what it names.
/// </summary>
public sealed class DueTitles(HttpClient http, ILogger<DueTitles> logger)
{
    /// <summary>
    /// The ids of the films that are due, or null when the API could not say. Null stops a pass
    /// acting: an empty answer is a fact and a missing one is not, and a film deleted on a guess is
    /// the one outcome that cannot be taken back.
    /// </summary>
    public async Task<IReadOnlyList<string>?> AskAsync(CancellationToken ct)
    {
        try
        {
            return await http.GetFromJsonAsync("api/library/due", ManifestJsonContext.Default.StringArray, ct);
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or TaskCanceledException
                                   && !ct.IsCancellationRequested)
        {
            logger.LogWarning(ex, "The library's clock could not be read, so this pass removes nothing.");
            return null;
        }
    }
}
