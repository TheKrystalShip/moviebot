using System.Text.Json;
using TheKrystalShip.MovieBot.Api.Data;
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

    /// <summary>Anybody who may watch can come in.</summary>
    public required bool Open { get; init; }

    /// <summary>Everybody let in, by account. Who is asking to come in is a live page and is not kept.</summary>
    public required IReadOnlyList<string> Admitted { get; init; }

    /// <summary>The room's guest link, while it has one. Its guests are live connections and are not kept.</summary>
    public GuestPass? Guests { get; init; }
}

/// <summary>
/// Keeps the rooms across a restart.
///
/// A room is a few small fields and it is the only thing in the system that exists nowhere else:
/// the films are on disk, the subtitles are kept, and what a room is watching and where it has got
/// to lives in memory and nowhere at all.
///
/// What is kept is the state itself, revision and epoch included, in the shape it is pushed to the
/// room. A restored room is the same run of the same room rather than a new one that happens to share
/// its name, so a client's revision gate goes on working and the resync it does on reconnecting hands
/// it back the room it already had. Nothing about the restart reaches a person watching.
///
/// Position survives for free. It is held as a position true at an instant rather than as a
/// ticking number, so a room that was playing when the server stopped is still playing, at the
/// right place, when it starts again, however long that took.
/// </summary>
public sealed class RoomStore(CinemaDatabase database, ILogger<RoomStore> logger)
{
    /// <summary>The rooms worth restoring: those somebody did something in within the idle window.</summary>
    public IReadOnlyList<PersistedSession> Read(DateTimeOffset now, TimeSpan idleFor) => database.Read(c =>
    {
        var cutoff = now - idleFor;
        var idle = c.Count("SELECT COUNT(*) FROM rooms WHERE last_activity_at <= $cutoff", ("$cutoff", cutoff));
        if (idle > 0)
            logger.LogInformation("{Dropped} room(s) had been idle past the window and were not restored.", idle);

        var rooms = c.Query(
            "SELECT id, state, last_activity_at, open, guest_pass, guest_created_by, guest_created_at, guest_expires_at "
            + "FROM rooms WHERE last_activity_at > $cutoff ORDER BY id",
            r => (Id: r.GetString(0), Room: State(r.GetString(0), r.GetString(1)) is not { } state ? null : new PersistedSession
            {
                State = state,
                LastActivityUtc = r.GetInstant(2),
                Open = r.GetBool(3),
                Admitted = [],
                Guests = r.IsDBNull(4) ? null : new GuestPass
                {
                    Id = r.GetString(4), CreatedBy = r.GetString(5), CreatedAt = r.GetInstant(6), ExpiresAt = r.GetInstant(7),
                },
            }),
            ("$cutoff", cutoff));

        return (IReadOnlyList<PersistedSession>)[.. rooms.Where(room => room.Room is not null).Select(room => room.Room! with
        {
            Admitted = c.Query("SELECT user_id FROM room_admitted WHERE room_id = $id ORDER BY rowid",
                r => r.GetString(0), ("$id", room.Id)),
        })];
    });

    /// <summary>
    /// A kept room's state, or null for one that no longer reads as a room's state: a room that cannot
    /// be restored is one that starts again empty, which is where it would be without being kept.
    /// </summary>
    private SessionState? State(string id, string json)
    {
        try
        {
            return JsonSerializer.Deserialize(json, ManifestJsonContext.Default.SessionState);
        }
        catch (JsonException ex)
        {
            logger.LogError(ex, "The kept state of room {Room} could not be read; it is not restored.", id);
            return null;
        }
    }

    /// <summary>Replaces the kept rooms with what is held now, in one transaction.</summary>
    public void Write(IReadOnlyList<PersistedSession> sessions) => database.Write(c =>
    {
        c.Execute("DELETE FROM rooms");
        foreach (var room in sessions)
        {
            var id = room.State.SessionId;
            c.Execute(
                "INSERT INTO rooms (id, state, last_activity_at, open, guest_pass, guest_created_by, guest_created_at, guest_expires_at) "
                + "VALUES ($id, $state, $activity, $open, $pass, $by, $created, $expires)",
                ("$id", id), ("$state", JsonSerializer.Serialize(room.State, ManifestJsonContext.Default.SessionState)),
                ("$activity", room.LastActivityUtc), ("$open", room.Open), ("$pass", room.Guests?.Id),
                ("$by", room.Guests?.CreatedBy), ("$created", room.Guests?.CreatedAt), ("$expires", room.Guests?.ExpiresAt));
            foreach (var userId in room.Admitted.Distinct(StringComparer.Ordinal))
                c.Execute("INSERT INTO room_admitted (room_id, user_id) VALUES ($id, $user)", ("$id", id), ("$user", userId));
        }
    });
}
