using TheKrystalShip.MovieBot.Api.Library;
using TheKrystalShip.MovieBot.Api.Transcode;

namespace TheKrystalShip.MovieBot.Api.Sessions;

/// <summary>
/// What one viewer's player says about quality: the rung height it plays, and the rung height its
/// own adaptive choice would pick with nothing holding it back. Null in both is a player that
/// knows neither, which is how a browser that plays HLS itself reads.
/// </summary>
public sealed record ViewerQualityReport(int? PlayingHeight, int? WantedHeight);

/// <summary>
/// What each connected viewer would watch at, kept in memory for the transcode planner to read.
///
/// A viewer's quality is never shared with the room: it is held per connection, appears in no
/// room state and no broadcast, and is dropped the moment the connection leaves. Segment requests
/// cannot say this, because the cache in front of the API answers a whole room from one fetch.
/// </summary>
public sealed class ViewerQuality(TitleLibrary library, SessionStore sessions) : IViewerQuality
{
    private readonly Lock _gate = new();
    private readonly Dictionary<string, Dictionary<string, ViewerQualityReport>> _rooms = new();

    /// <summary>A viewer arrives knowing nothing, which counts as wanting the best rung there is.</summary>
    public void Join(string sessionId, string connectionId)
    {
        lock (_gate) RoomOf(sessionId)[connectionId] = new ViewerQualityReport(null, null);
    }

    /// <summary>Takes what a connected viewer's player says. A report from a connection that never joined is ignored.</summary>
    public void Report(string sessionId, string connectionId, ViewerQualityReport report)
    {
        lock (_gate)
        {
            if (_rooms.TryGetValue(sessionId, out var room) && room.ContainsKey(connectionId))
                room[connectionId] = report;
        }
    }

    public void Leave(string sessionId, string connectionId)
    {
        lock (_gate)
        {
            if (!_rooms.TryGetValue(sessionId, out var room)) return;
            room.Remove(connectionId);
            if (room.Count == 0) _rooms.Remove(sessionId);
        }
    }

    /// <summary>
    /// The rung height each connected viewer of a room wants, one per connection. A viewer whose
    /// player says nothing wants the tallest rung the room's film has. Empty when nobody is
    /// connected and when the room holds no film.
    /// </summary>
    public IReadOnlyList<int> WantedHeights(string sessionId)
    {
        ViewerQualityReport[] reports;
        lock (_gate)
            reports = _rooms.TryGetValue(sessionId, out var room) ? [.. room.Values] : [];

        if (reports.Length == 0 || TopRungOf(sessionId) is not { } top) return [];
        return [.. reports.Select(r => r.WantedHeight ?? top)];
    }

    /// <summary>Every room somebody is connected to, with the heights its viewers want.</summary>
    public IReadOnlyDictionary<string, IReadOnlyList<int>> WantedByRoom()
    {
        string[] rooms;
        lock (_gate) rooms = [.. _rooms.Keys];

        var wanted = new Dictionary<string, IReadOnlyList<int>>();
        foreach (var sessionId in rooms)
        {
            var heights = WantedHeights(sessionId);
            if (heights.Count > 0) wanted[sessionId] = heights;
        }

        return wanted;
    }

    private Dictionary<string, ViewerQualityReport> RoomOf(string sessionId)
    {
        if (!_rooms.TryGetValue(sessionId, out var room))
            _rooms[sessionId] = room = new Dictionary<string, ViewerQualityReport>();
        return room;
    }

    private int? TopRungOf(string sessionId)
    {
        if (sessions.Get(sessionId)?.TitleId is not { } titleId || library.Get(titleId) is not { } manifest)
            return null;

        var renditions = manifest.Video.Renditions;
        return renditions.Count == 0 ? null : renditions.Max(r => r.SizeIn(manifest.Video).Height);
    }
}
