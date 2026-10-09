using System.Collections.Concurrent;
using System.Globalization;
using System.Text;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Options;
using TheKrystalShip.MovieBot.Api.Auth;
using TheKrystalShip.MovieBot.Core;

namespace TheKrystalShip.MovieBot.Api.Sessions;

/// <summary>
/// The hub's groups for a room. Everybody in it hears the room; only the people with an account in it
/// hear its door, since the door names whoever is asking to come in. Each name is the room's id under
/// a prefix of its own, so no room's id is ever another room's group.
/// </summary>
public static class RoomGroups
{
    /// <summary>Everybody watching: the room's state, its film and who is in it.</summary>
    public static string Everyone(string sessionId) => "room:" + sessionId;

    /// <summary>The people with an account in the room: its door.</summary>
    public static string Members(string sessionId) => "members:" + sessionId;
}

/// <summary>Marks a hub method a guest may call. Every other method refuses a guest.</summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class GuestAllowedAttribute : Attribute;

/// <summary>
/// Refuses a guest any hub method not marked <see cref="GuestAllowedAttribute"/>. A method added to the
/// hub is closed to guests until somebody opens it on purpose.
/// </summary>
public sealed class GuestGate : IHubFilter
{
    public ValueTask<object?> InvokeMethodAsync(
        HubInvocationContext invocation, Func<HubInvocationContext, ValueTask<object?>> next)
    {
        if (GuestViewer.Of(invocation.Context.User) is not null
            && !invocation.HubMethod.IsDefined(typeof(GuestAllowedAttribute), inherit: true))
            throw new HubException("A guest watches; the room is moved by the people in it.");
        return next(invocation);
    }
}

/// <summary>
/// The guests connected to each room, and sending them away.
/// </summary>
/// <remarks>
/// A guest is a connection and nothing more, so sending one away is closing it: the page is told why
/// first (<c>GuestAccessEnded</c>, with a <see cref="GuestRefusals"/> reason), and its connection is
/// then aborted whether or not it listened.
/// </remarks>
public sealed class RoomGuests(IHubContext<SessionHub> hub, ILogger<RoomGuests> logger)
{
    private sealed record Connected(string SessionId, string PassId, HubCallerContext Context);

    private readonly ConcurrentDictionary<string, Connected> _connections = new(StringComparer.Ordinal);

    public void Add(HubCallerContext context, GuestViewer guest) =>
        _connections[context.ConnectionId] = new Connected(guest.SessionId, guest.PassId, context);

    public void Remove(string connectionId) => _connections.TryRemove(connectionId, out _);

    /// <summary>Every guest connection, with its room and the link it came in on.</summary>
    public IReadOnlyList<(string ConnectionId, string SessionId, string PassId)> All() =>
        [.. _connections.Select(c => (c.Key, c.Value.SessionId, c.Value.PassId))];

    /// <summary>Sends away every guest in a room.</summary>
    public Task EndRoomAsync(string sessionId, string reason, CancellationToken ct = default) =>
        EndAsync([.. _connections.Where(c => c.Value.SessionId == sessionId).Select(c => c.Key)], reason, ct);

    /// <summary>Sends away the guests on these connections, telling each page why.</summary>
    public async Task EndAsync(IReadOnlyCollection<string> connectionIds, string reason, CancellationToken ct = default)
    {
        foreach (var id in connectionIds)
        {
            if (!_connections.TryRemove(id, out var guest)) continue;
            try
            {
                await hub.Clients.Client(id).SendAsync("GuestAccessEnded", reason, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogDebug(ex, "A guest on {Connection} could not be told the room was closed to them.", id[..6]);
            }
            guest.Context.Abort();
            logger.LogInformation("A guest was sent away from room {SessionId} ({Reason})", guest.SessionId, reason);
        }
    }

    /// <summary>
    /// The name a guest is shown by: what they typed, without control or formatting characters (a
    /// direction override among them) or runs of space, at most <paramref name="longest"/> characters,
    /// and "Guest" when that leaves nothing.
    /// </summary>
    public static string NameOf(string? typed, int longest = 32)
    {
        var name = new StringBuilder();
        foreach (var c in (typed ?? "").Trim())
        {
            if (char.IsControl(c) || char.GetUnicodeCategory(c) == UnicodeCategory.Format) continue;
            if (char.IsWhiteSpace(c))
            {
                if (name.Length > 0 && name[^1] != ' ') name.Append(' ');
                continue;
            }
            name.Append(c);
        }
        var shown = name.ToString().Trim();
        if (shown.Length > longest)
        {
            var cut = char.IsHighSurrogate(shown[longest - 1]) ? longest - 1 : longest;
            shown = shown[..cut].TrimEnd();
        }
        return shown.Length == 0 ? "Guest" : shown;
    }
}

/// <summary>
/// Sends guests away when the room stops being theirs to watch: its link ran out, or nobody with an
/// account has been in it for longer than <see cref="RoomOptions.GuestGrace"/>.
/// </summary>
public sealed class GuestKeeper(
    SessionStore sessions, RoomDoors doors, RoomGuests guests, IOptions<RoomOptions> options) : BackgroundService
{
    private static readonly TimeSpan Every = TimeSpan.FromSeconds(10);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Every);
        while (await timer.WaitForNextTickAsync(stoppingToken))
            await PassAsync(stoppingToken);
    }

    /// <summary>One look at every room's guests.</summary>
    public async Task PassAsync(CancellationToken ct = default)
    {
        foreach (var room in sessions.ExpiredGuests())
            await doors.RevokeGuestsAsync(room, by: null, ct);

        foreach (var room in sessions.Unattended(options.Value.GuestGrace))
            await guests.EndRoomAsync(room, GuestRefusals.Unattended, ct);

        // A guest on a link that is no longer the room's: it was replaced, or the room was forgotten.
        var stale = guests.All().Where(g => sessions.RoomOfGuests(g.PassId) != g.SessionId).Select(g => g.ConnectionId).ToList();
        if (stale.Count > 0) await guests.EndAsync(stale, GuestRefusals.Ended, ct);
    }
}
