using System.Collections.Concurrent;
using System.Security.Claims;
using Microsoft.AspNetCore.SignalR;
using TheKrystalShip.MovieBot.Api.Auth;
using TheKrystalShip.MovieBot.Api.Library;
using TheKrystalShip.MovieBot.Core;

namespace TheKrystalShip.MovieBot.Api.Sessions;

/// <summary>
/// The hub groups a person's own connections are in. A group is named by the account the token names,
/// and a session's group by the account and the session, so a message sent to one reaches that person
/// and nobody else.
/// </summary>
public static class PersonGroups
{
    /// <summary>Every connection of the person's that has an account behind it: screens and the rest.</summary>
    public static string Everything(string userId) => "person:" + userId;

    /// <summary>
    /// The connections of one of the person's sessions that is not a screen: a phone, where the screens
    /// paired with it are told.
    /// </summary>
    public static string Phone(string userId, string sid) => $"phone:{userId}:{sid}";

    /// <summary>The connections of one of the person's screens, where the phones paired with it are told.</summary>
    public static string Screen(string userId, string sid) => $"screen:{userId}:{sid}";
}

/// <summary>
/// What a phone is told when it becomes a remote of a screen: the room the screen is in now, absent while
/// it is in none.
/// </summary>
public sealed record RemoteJoin(string? Room, SessionStatePush? Push);

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
/// Which screens each person has, whether they are connected, what room each is in, and the phones
/// paired with each.
/// </summary>
/// <remarks>
/// <para>
/// <b>Screens live in memory; pairings are kept.</b> A screen is a live connection, so a restart drops
/// every one and each reappears when its television reconnects. Nothing here is a status the hub
/// connection did not measure: connected is whether a connection of the screen is open, the room is the
/// room its newest connection joined, and what the room holds is read from the room when it is asked
/// for. Which phones are paired with a screen is <see cref="ScreenPairings"/>, in the database.
/// </para>
/// <para>
/// <b>A screen is told apart by its anchor session.</b> A television signed in again is a new session
/// and so a new screen. The old one stays as not connected, and is forgotten when a screen of the same
/// name arrives for the person, or after <see cref="Retention"/> unseen; forgetting loses nothing, since
/// a screen that connects again is known again.
/// </para>
/// <para>
/// <b>Only the phones paired with a screen are told about it, and only its owner's.</b> Every message
/// about a screen goes to the group of each phone the screen's own person paired with it
/// (<see cref="PersonGroups.Phone"/>), and every message about its phones to the screen's own group
/// (<see cref="PersonGroups.Screen"/>), so a screen is never reachable under anyone else's account and
/// never heard by a phone nobody paired.
/// </para>
/// <para>
/// <b>A pairing is made by a code only the screen was shown</b> (<see cref="PairingCodes"/>), checked
/// against the screens known now: a code names its screen by a tag and is signed for the screen's
/// person, so a phone of anybody else is refused, and a code that ran out pairs nothing.
/// </para>
/// </remarks>
public sealed class ScreenPresence(
    SessionStore sessions, TitleLibrary library, ScreenPairings pairings, PairingCodes codes,
    IHubContext<SessionHub> hub, TimeProvider clock)
{
    /// <summary>How long a screen that is not connected is remembered.</summary>
    public static readonly TimeSpan Retention = TimeSpan.FromDays(7);

    private sealed class Entry(string userId, string id, byte[] tag)
    {
        public string UserId { get; } = userId;
        public string Id { get; } = id;
        public byte[] Tag { get; } = tag;
        public string? Name;
        public DateTimeOffset LastSeen;
        public ScreenView? Told;

        /// <summary>The open connections, with the room each joined and the order it did so in.</summary>
        public Dictionary<string, (string? Room, long Order)> Connections { get; } = [];
    }

    /// <summary>A phone's room connection that drives a screen's room, and the room it is in now.</summary>
    private sealed record Remote(string UserId, string PhoneId, string ScreenId, string? Room);

    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly ConcurrentDictionary<string, Remote> _remotes = new(StringComparer.Ordinal);
    private readonly Dictionary<(string UserId, string Id), Entry> _screens = [];
    private readonly Dictionary<string, Entry> _byConnection = [];
    private long _order;

    /// <summary>
    /// A connection of a screen opened. Ends the pairings of a television signed in again under this
    /// one's name, and tells the phones paired with it.
    /// </summary>
    public async Task ConnectedAsync(string userId, ScreenIdentity screen, string connectionId)
    {
        await _gate.WaitAsync();
        try
        {
            var now = clock.GetUtcNow();
            Forget(userId, screen, now);

            if (!_screens.TryGetValue((userId, screen.Id), out var entry))
                _screens[(userId, screen.Id)] = entry = new Entry(userId, screen.Id, codes.Tag(screen.Id));
            entry.Name = screen.Name ?? entry.Name;
            entry.LastSeen = now;
            entry.Connections[connectionId] = (null, ++_order);
            _byConnection[connectionId] = entry;

            foreach (var ended in pairings.ScreenSeen(userId, screen.Id, screen.Name, Connected(userId)))
            {
                await DropRemotesAsync(userId, ended.PhoneId, ended.ScreenId);
                await hub.Clients.Group(PersonGroups.Phone(userId, ended.PhoneId)).SendAsync("ScreenLeft", ended.ScreenId);
            }
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
            _remotes.TryRemove(connectionId, out _);
            if (!_byConnection.Remove(connectionId, out var entry)) return;
            entry.Connections.Remove(connectionId);
            entry.LastSeen = clock.GetUtcNow();
            pairings.ScreenSeen(entry.UserId, entry.Id, null, Connected(entry.UserId));
            await AnnounceAsync(entry, arrived: false);
        }
        finally { _gate.Release(); }
    }

    /// <summary>
    /// A room's state changed. The phones paired with every screen in it are told again when what the
    /// screen shows of the room is not what they were last told.
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

    /// <summary>
    /// Makes a phone's connection a remote of one of the person's screens: it hears the screen's room and
    /// moves with the screen from room to room. Null when the phone is not paired with the screen. A
    /// screen in no room, or not connected, leaves the remote in none until it is in one.
    /// </summary>
    public async Task<RemoteJoin?> JoinRemoteAsync(string userId, string phoneId, string screenId, string connectionId)
    {
        await _gate.WaitAsync();
        try
        {
            if (!pairings.Paired(userId, phoneId, screenId)) return null;
            if (_remotes.TryGetValue(connectionId, out var before) && before.Room is not null)
                await hub.Groups.RemoveFromGroupAsync(connectionId, RoomGroups.Everyone(before.Room));

            var room = _screens.TryGetValue((userId, screenId), out var entry) ? RoomOf(entry) : null;
            _remotes[connectionId] = new Remote(userId, phoneId, screenId, room);
            if (room is not null) await hub.Groups.AddToGroupAsync(connectionId, RoomGroups.Everyone(room));
            pairings.PhoneSeen(userId, phoneId);
            return new RemoteJoin(room, StateOf(room));
        }
        finally { _gate.Release(); }
    }

    /// <summary>
    /// The room a remote's screen is in now, checked against the pairing every time it is asked: a
    /// remote whose pairing has ended, or that is no remote, has none. Null with the reason when the
    /// screen is in no room.
    /// </summary>
    public (string? Room, string? Refusal) RoomOfRemote(string connectionId)
    {
        if (!_remotes.TryGetValue(connectionId, out var remote)) return (null, "Join a screen's room first.");
        if (!pairings.Paired(remote.UserId, remote.PhoneId, remote.ScreenId)) return (null, "This phone is no longer paired with that screen.");
        return remote.Room is { } room ? (room, null) : (null, "The screen is in no room.");
    }

    /// <summary>
    /// Ends every remote a phone holds of a screen whose pairing ended: out of the screen's room, and
    /// forgotten, so what it asks for next is refused.
    /// </summary>
    private async Task DropRemotesAsync(string userId, string phoneId, string screenId)
    {
        foreach (var (connectionId, remote) in _remotes.ToList())
        {
            if (remote.UserId != userId || remote.PhoneId != phoneId || remote.ScreenId != screenId) continue;
            _remotes.TryRemove(connectionId, out _);
            if (remote.Room is not null)
                await hub.Groups.RemoveFromGroupAsync(connectionId, RoomGroups.Everyone(remote.Room));
        }
    }

    /// <summary>
    /// Moves the remotes of a screen to the room it is in now, and tells each where the room is: out of
    /// the room they were in, into the new one, or into none when the screen has left its rooms.
    /// </summary>
    private async Task FollowAsync(Entry entry)
    {
        var room = RoomOf(entry);
        foreach (var (connectionId, remote) in _remotes.ToList())
        {
            if (remote.UserId != entry.UserId || remote.ScreenId != entry.Id || remote.Room == room) continue;
            if (remote.Room is not null)
                await hub.Groups.RemoveFromGroupAsync(connectionId, RoomGroups.Everyone(remote.Room));
            if (room is not null)
                await hub.Groups.AddToGroupAsync(connectionId, RoomGroups.Everyone(room));
            _remotes[connectionId] = remote with { Room = room };
            await hub.Clients.Client(connectionId).SendAsync("RemoteMoved", new JoinReply { Admitted = true, Push = StateOf(room) });
        }
    }

    private SessionStatePush? StateOf(string? room) =>
        room is not null && sessions.Get(room) is { } state
            ? new SessionStatePush { State = state, ServerTime = DateTimeOffset.UtcNow }
            : null;

    /// <summary>
    /// The screens a phone is paired with, as they stand: those not known now as not connected, under the
    /// name they last had. A phone is only ever handed its own person's.
    /// </summary>
    public async Task<List<ScreenView>> PairedWithAsync(string userId, string phoneId)
    {
        await _gate.WaitAsync();
        try
        {
            Forget(userId, null, clock.GetUtcNow());
            pairings.PhoneSeen(userId, phoneId);
            var views = new List<ScreenView>();
            foreach (var row in pairings.OfPhone(userId, phoneId, Connected(userId)))
                views.Add(_screens.TryGetValue((userId, row.ScreenId), out var entry)
                    ? ViewOf(entry)
                    : new ScreenView(row.ScreenId, row.ScreenName, false, null, null, null, null));
            return views;
        }
        finally { _gate.Release(); }
    }

    /// <summary>The phones paired with one of the person's screens, oldest first.</summary>
    public async Task<List<PairedPhone>> PhonesOfAsync(string userId, string screenId)
    {
        await _gate.WaitAsync();
        try { return Phones(userId, screenId); }
        finally { _gate.Release(); }
    }

    /// <summary>A code for one of the person's screens to show.</summary>
    public PairingCodeView CodeFor(string userId, string screenId) => codes.Issue(userId, screenId);

    /// <summary>
    /// Pairs a phone with the screen that showed <paramref name="code"/>, and answers the screen as the
    /// phone now sees it, or why the code paired nothing. The phone's other connections hear the screen
    /// arrive, and the screen hears its phones change.
    /// </summary>
    public async Task<(ScreenView? Screen, PairingRefusal? Refused)> PairAsync(
        string userId, string phoneId, string phoneName, string code)
    {
        if (PairingCodes.Open(code) is not { } opened) return (null, PairingRefusal.Unknown);

        await _gate.WaitAsync();
        try
        {
            var entry = _screens.Values.FirstOrDefault(e => e.Tag.AsSpan().SequenceEqual(opened.Tag)
                && codes.Signed(opened, e.UserId, e.Id));
            if (entry is null) return (null, PairingRefusal.Unknown);
            if (codes.Expired(opened)) return (null, PairingRefusal.Expired);
            if (entry.UserId != userId) return (null, PairingRefusal.AnotherPerson);

            pairings.Pair(userId, phoneId, phoneName, entry.Id, entry.Name);
            var view = ViewOf(entry);
            await hub.Clients.Group(PersonGroups.Phone(userId, phoneId)).SendAsync("ScreenArrived", view);
            await hub.Clients.Group(PersonGroups.Screen(userId, entry.Id)).SendAsync("PhonesChanged", Phones(userId, entry.Id));
            return (view, null);
        }
        finally { _gate.Release(); }
    }

    /// <summary>
    /// Ends a pairing, from either side. The phone hears the screen leave, and the screen hears its
    /// phones change. Ending one there is none of does nothing.
    /// </summary>
    public async Task UnpairAsync(string userId, string phoneId, string screenId)
    {
        await _gate.WaitAsync();
        try
        {
            if (!pairings.Unpair(userId, phoneId, screenId)) return;
            await DropRemotesAsync(userId, phoneId, screenId);
            await hub.Clients.Group(PersonGroups.Phone(userId, phoneId)).SendAsync("ScreenLeft", screenId);
            await hub.Clients.Group(PersonGroups.Screen(userId, screenId)).SendAsync("PhonesChanged", Phones(userId, screenId));
        }
        finally { _gate.Release(); }
    }

    private List<PairedPhone> Phones(string userId, string screenId) =>
        [.. pairings.OfScreen(userId, screenId, Connected(userId)).Select(row => new PairedPhone(row.PhoneId, row.PhoneName, row.PairedAt))];

    /// <summary>The person's screens with a connection open now.</summary>
    private HashSet<string> Connected(string userId) =>
        [.. _screens.Values.Where(e => e.UserId == userId && e.Connections.Count > 0).Select(e => e.Id)];

    /// <summary>
    /// Drops what the person's screens no longer need to be shown: those unseen for <see cref="Retention"/>,
    /// and those not connected that carry the name of a screen arriving under another session.
    /// </summary>
    private void Forget(string userId, ScreenIdentity? arriving, DateTimeOffset now)
    {
        foreach (var entry in _screens.Values.Where(e => e.UserId == userId && e.Connections.Count == 0 && e.Id != arriving?.Id).ToList())
        {
            var superseded = arriving?.Name is { } name
                && string.Equals(entry.Name, name, StringComparison.OrdinalIgnoreCase);
            if (!superseded && now - entry.LastSeen < Retention) continue;
            _screens.Remove((userId, entry.Id));
        }
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

    /// <summary>Tells the phones paired with a screen what it shows now, when that is not what they were last told.</summary>
    private async Task AnnounceAsync(Entry entry, bool arrived)
    {
        await FollowAsync(entry);
        var view = ViewOf(entry);
        var message = arrived && entry.Told is not { Connected: true } ? "ScreenArrived" : "ScreenChanged";
        if (view == entry.Told) return;
        entry.Told = view;
        foreach (var phone in pairings.OfScreen(entry.UserId, entry.Id, Connected(entry.UserId)))
            await hub.Clients.Group(PersonGroups.Phone(entry.UserId, phone.PhoneId)).SendAsync(message, view);
    }
}
