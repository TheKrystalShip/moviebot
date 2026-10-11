using System.Security.Claims;
using Microsoft.AspNetCore.SignalR;
using TheKrystalShip.MovieBot.Api.Auth;
using TheKrystalShip.MovieBot.Api.Library;

namespace TheKrystalShip.MovieBot.Api.Sessions;

/// <summary>
/// The hub groups a person's own connections are in. A group is named by the account the token names,
/// so a message sent to one reaches that person and nobody else.
/// </summary>
public static class PersonGroups
{
    /// <summary>Every connection of the person's that has an account behind it: screens and the rest.</summary>
    public static string Everything(string userId) => "person:" + userId;

    /// <summary>The person's connections that are not screens: where their screens' presence is told.</summary>
    public static string Viewers(string userId) => "person-viewers:" + userId;
}

/// <summary>A screen as a person's other connections see it.</summary>
/// <param name="Id">The screen's anchor session (<c>sid</c>), stable across token refresh.</param>
/// <param name="Name">The name its owner left on the device session, absent when it has none.</param>
/// <param name="Connected">Whether a hub connection of the screen is open right now.</param>
/// <param name="Room">The room it is in, absent while it is in none.</param>
/// <param name="TitleId">What that room holds, absent when the room holds nothing.</param>
/// <param name="Title">The held title's name as the library lists it.</param>
/// <param name="Paused">Whether that room is paused.</param>
public sealed record ScreenView(
    string Id, string? Name, bool Connected, string? Room, string? TitleId, string? Title, bool? Paused);

/// <summary>
/// What the token of a hub connection says it is: a screen when it was minted for the television
/// client and carries an anchor session. Decided from the validated token and nothing the page sends.
/// </summary>
public readonly record struct ScreenIdentity(string Id, string? Name)
{
    public static ScreenIdentity? Of(ClaimsIdentity identity, string tvClientId) =>
        identity.FindFirst("client_id")?.Value == tvClientId
        && identity.FindFirst("sid")?.Value is { Length: > 0 } sid
            ? new ScreenIdentity(sid, identity.FindFirst("tks_device_name")?.Value is { Length: > 0 } name ? name : null)
            : null;
}

/// <summary>
/// Which screens each person has, whether they are connected, and what room each is in.
/// </summary>
/// <remarks>
/// <para>
/// <b>Screens live in memory.</b> A screen is a live connection, so a restart drops every one and each
/// reappears when its television reconnects. Nothing here is a status the hub connection did not
/// measure: connected is whether a connection of the screen is open, the room is the room its newest
/// connection joined, and what the room holds is read from the room when it is asked for.
/// </para>
/// <para>
/// <b>A screen is told apart by its anchor session.</b> A television signed in again is a new session
/// and so a new screen. The old one stays as not connected, and is forgotten when a screen of the same
/// name arrives for the person, or after <see cref="Retention"/> unseen; forgetting loses nothing, since
/// a screen that connects again is known again.
/// </para>
/// <para>
/// <b>Only the owner is told.</b> Every message goes to the group named by the screen's own owner
/// (<see cref="PersonGroups.Viewers"/>), and the list a person asks for is the one filed under their
/// own token's account, so a screen is never reachable under anyone else's.
/// </para>
/// </remarks>
public sealed class ScreenPresence(
    SessionStore sessions, TitleLibrary library, IHubContext<SessionHub> hub, TimeProvider clock)
{
    /// <summary>How long a screen that is not connected is remembered.</summary>
    public static readonly TimeSpan Retention = TimeSpan.FromDays(7);

    private sealed class Entry(string userId, string id)
    {
        public string UserId { get; } = userId;
        public string Id { get; } = id;
        public string? Name;
        public DateTimeOffset LastSeen;
        public ScreenView? Told;

        /// <summary>The open connections, with the room each joined and the order it did so in.</summary>
        public Dictionary<string, (string? Room, long Order)> Connections { get; } = [];
    }

    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Dictionary<(string UserId, string Id), Entry> _screens = [];
    private readonly Dictionary<string, Entry> _byConnection = [];
    private long _order;

    /// <summary>A connection of a screen opened. Tells the person's other connections.</summary>
    public async Task ConnectedAsync(string userId, ScreenIdentity screen, string connectionId)
    {
        await _gate.WaitAsync();
        try
        {
            var now = clock.GetUtcNow();
            var forgotten = Forget(userId, screen, now);

            if (!_screens.TryGetValue((userId, screen.Id), out var entry))
                _screens[(userId, screen.Id)] = entry = new Entry(userId, screen.Id);
            entry.Name = screen.Name ?? entry.Name;
            entry.LastSeen = now;
            entry.Connections[connectionId] = (null, ++_order);
            _byConnection[connectionId] = entry;

            foreach (var id in forgotten) await SendAsync(userId, "ScreenLeft", id);
            await AnnounceAsync(entry, arrived: true);
        }
        finally { _gate.Release(); }
    }

    /// <summary>A connection of a screen joined a room.</summary>
    public async Task JoinedAsync(string connectionId, string sessionId)
    {
        await _gate.WaitAsync();
        try
        {
            if (!_byConnection.TryGetValue(connectionId, out var entry)) return;
            entry.Connections[connectionId] = (sessionId, ++_order);
            await AnnounceAsync(entry, arrived: false);
        }
        finally { _gate.Release(); }
    }

    /// <summary>A connection closed. A screen with none left is not connected, and is not in a room.</summary>
    public async Task DisconnectedAsync(string connectionId)
    {
        await _gate.WaitAsync();
        try
        {
            if (!_byConnection.Remove(connectionId, out var entry)) return;
            entry.Connections.Remove(connectionId);
            entry.LastSeen = clock.GetUtcNow();
            await AnnounceAsync(entry, arrived: false);
        }
        finally { _gate.Release(); }
    }

    /// <summary>
    /// A room's state changed. Every screen in it is told again when what it shows of the room is not
    /// what the person was last told.
    /// </summary>
    public async Task RoomChangedAsync(string sessionId)
    {
        await _gate.WaitAsync();
        try
        {
            foreach (var entry in _screens.Values.ToList())
                if (RoomOf(entry) == sessionId)
                    await AnnounceAsync(entry, arrived: false);
        }
        finally { _gate.Release(); }
    }

    /// <summary>The screens of one person, as they stand. A person is only ever handed their own.</summary>
    public async Task<List<ScreenView>> OfAsync(string userId)
    {
        await _gate.WaitAsync();
        try
        {
            foreach (var id in Forget(userId, null, clock.GetUtcNow())) await SendAsync(userId, "ScreenLeft", id);
            var views = new List<ScreenView>();
            foreach (var entry in _screens.Values.Where(e => e.UserId == userId).ToList())
                views.Add(ViewOf(entry));
            return views;
        }
        finally { _gate.Release(); }
    }

    /// <summary>
    /// Drops what the person's screens no longer need to be shown: those unseen for <see cref="Retention"/>,
    /// and those not connected that carry the name of a screen arriving under another session.
    /// </summary>
    private List<string> Forget(string userId, ScreenIdentity? arriving, DateTimeOffset now)
    {
        var gone = new List<string>();
        foreach (var entry in _screens.Values.Where(e => e.UserId == userId && e.Connections.Count == 0 && e.Id != arriving?.Id).ToList())
        {
            var superseded = arriving?.Name is { } name
                && string.Equals(entry.Name, name, StringComparison.OrdinalIgnoreCase);
            if (!superseded && now - entry.LastSeen < Retention) continue;
            _screens.Remove((userId, entry.Id));
            gone.Add(entry.Id);
        }
        return gone;
    }

    private static string? RoomOf(Entry entry)
    {
        string? room = null;
        long newest = 0;
        foreach (var (joined, order) in entry.Connections.Values)
            if (joined is not null && order > newest) (room, newest) = (joined, order);
        return room;
    }

    private ScreenView ViewOf(Entry entry)
    {
        var room = RoomOf(entry);
        var state = room is null ? null : sessions.Get(room);
        var manifest = state?.TitleId is { } titleId ? library.Get(titleId) : null;
        return new ScreenView(
            entry.Id, entry.Name, entry.Connections.Count > 0, room, state?.TitleId,
            manifest is null ? null : manifest.Film?.Display ?? manifest.Title, state?.Paused);
    }

    private async Task AnnounceAsync(Entry entry, bool arrived)
    {
        var view = ViewOf(entry);
        var message = arrived && entry.Told is not { Connected: true } ? "ScreenArrived" : "ScreenChanged";
        if (view == entry.Told) return;
        entry.Told = view;
        await SendAsync(entry.UserId, message, view);
    }

    private Task SendAsync(string userId, string method, object payload) =>
        hub.Clients.Group(PersonGroups.Viewers(userId)).SendAsync(method, payload);
}
