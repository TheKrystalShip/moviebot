using System.Collections.Concurrent;
using Microsoft.AspNetCore.SignalR;
using TheKrystalShip.MovieBot.Api.Sessions;
using TheKrystalShip.MovieBot.Core;

namespace TheKrystalShip.MovieBot.Api.Library;

/// <summary>
/// Tells every room holding a title when the title changes underneath it.
///
/// A manifest is rewritten throughout a transcode: the head every few seconds, the subtitles and
/// the preview sheet once the main pass is done, and the status at the end. A viewer who opened
/// the film early holds the copy they read, and nothing they do afterwards reads it again. This
/// watches the manifests of the titles that occupied rooms are watching and pushes the fresh copy
/// down the connection every viewer already holds, so what a film gains reaches the people
/// watching it without a reload.
///
/// Subtitles fetched from outside live beside the manifest rather than in it, so the file does
/// not move when one lands; the route that adds one says so here instead.
/// </summary>
public sealed class TitleChanges(
    SessionStore sessions,
    TitleLibrary library,
    IHubContext<SessionHub> hub,
    ILogger<TitleChanges> logger) : BackgroundService
{
    /// <summary>How often the manifests are looked at. The ingest rewrites one every few seconds.</summary>
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(2);

    private readonly Dictionary<string, DateTime> _announced = [];
    private readonly ConcurrentDictionary<string, byte> _nudged = new();

    /// <summary>A change the file's timestamp does not show: a subtitle fetched, confirmed or unconfirmed.</summary>
    public void Announce(string titleId) => _nudged[titleId] = 0;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval);

        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                try
                {
                    await SweepAsync(stoppingToken);
                }
                catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
                {
                    logger.LogWarning(ex, "A title sweep failed.");
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Stopping.
        }
    }

    private async Task SweepAsync(CancellationToken ct)
    {
        var held = sessions.Summaries()
            .Where(room => room.Participants > 0 && room.TitleId is not null)
            .GroupBy(room => room.TitleId!)
            .ToDictionary(g => g.Key, g => g.Select(room => room.SessionId).ToList());

        // A title nobody is watching any more is forgotten, so its next viewer is told once.
        foreach (var forgotten in _announced.Keys.Where(id => !held.ContainsKey(id)).ToList())
            _announced.Remove(forgotten);

        foreach (var (titleId, rooms) in held)
        {
            var written = library.LastWriteOf(titleId);
            if (written is null) continue;

            var nudged = _nudged.TryRemove(titleId, out _);
            if (!nudged && _announced.TryGetValue(titleId, out var last) && last == written) continue;

            if (library.Get(titleId) is not { } manifest) continue;
            _announced[titleId] = written.Value;

            foreach (var sessionId in rooms)
                await hub.Clients.Group(sessionId).SendAsync("TitleChanged", manifest, ct);

            logger.LogDebug("Told {Rooms} room(s) that {TitleId} changed ({Status}, head {Head})",
                rooms.Count, titleId, manifest.Status, manifest.HeadSeconds);
        }
    }
}
