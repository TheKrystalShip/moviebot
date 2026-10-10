using System.Security.Cryptography;
using Microsoft.Data.Sqlite;
using TheKrystalShip.MovieBot.Api.Data;

namespace TheKrystalShip.MovieBot.Api.Lists;

/// <summary>What a list holds: a film or a show, never an episode.</summary>
public static class EntryKinds
{
    public const string Film = "film";
    public const string Show = "show";

    public static bool IsKnown(string? kind) => kind is Film or Show;
}

/// <summary>What a person may do with a list they can see.</summary>
public static class ListRoles
{
    /// <summary>Made the list: renames it, deletes it, and says who else is on it.</summary>
    public const string Owner = "owner";

    /// <summary>Adds to the list and takes off it.</summary>
    public const string Editor = "editor";

    /// <summary>Reads the list.</summary>
    public const string Viewer = "viewer";

    /// <summary>The roles a member can be given; the owner is who made the list and nobody else.</summary>
    public static bool IsMemberRole(string? role) => role is Editor or Viewer;

    public static bool CanEdit(string? role) => role is Owner or Editor;
}

/// <summary>One film or show on a list, and who put it there when.</summary>
public sealed record StoredEntry
{
    /// <summary>The canonical <c>tt0458352</c> form. A list holds a title once.</summary>
    public required string ImdbId { get; init; }

    /// <summary>One of <see cref="EntryKinds"/>.</summary>
    public required string Kind { get; init; }

    public required DateTimeOffset AddedAt { get; init; }

    /// <summary>The account of whoever added it.</summary>
    public required string AddedBy { get; init; }

    public string? Note { get; init; }
}

/// <summary>Somebody the owner put on their list, and what they may do with it.</summary>
public sealed record ListMember
{
    public required string UserId { get; init; }

    /// <summary><see cref="ListRoles.Editor"/> or <see cref="ListRoles.Viewer"/>.</summary>
    public required string Role { get; init; }

    public required DateTimeOffset Since { get; init; }
}

/// <summary>
/// A named list of films and shows. Every person has one Watch later, which is theirs alone and is
/// addressed as <see cref="ListStore.WatchLaterId"/> by its owner; every other list has an id of its
/// own and the people its owner put on it.
/// </summary>
public sealed record StoredList
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required string Owner { get; init; }
    public bool WatchLater { get; init; }
    public required DateTimeOffset CreatedAt { get; init; }
    public required DateTimeOffset UpdatedAt { get; init; }
    public List<ListMember> Members { get; init; } = [];
    public List<StoredEntry> Entries { get; init; } = [];

    /// <summary>What the person may do with the list, or null for somebody it is not shown to.</summary>
    public string? RoleOf(string userId) =>
        Owner == userId ? ListRoles.Owner : Members.FirstOrDefault(m => m.UserId == userId)?.Role;
}

/// <summary>Why a change to a list was turned down.</summary>
public enum ListRefusal
{
    None,

    /// <summary>No list by that id that the person can see. Somebody else's list is not shown to exist.</summary>
    NotFound,

    /// <summary>The person can see the list but not make this change to it.</summary>
    NotAllowed,

    /// <summary>The change would take the list past one of its limits.</summary>
    Full,

    /// <summary>A Watch later is its owner's alone and has nobody else on it.</summary>
    Personal,
}

/// <summary>
/// Every list, with its members and entries. A change is decided and written in one transaction,
/// so two people saving to one list at once each see the other's entry before counting.
/// </summary>
public sealed class ListStore(CinemaDatabase database)
{
    /// <summary>What a person calls their own Watch later in a route.</summary>
    public const string WatchLaterId = "later";

    public const string WatchLaterName = "Watch later";

    public const int NameLength = 80;
    public const int NoteLength = 500;
    public const int ListsPerOwner = 100;
    public const int EntriesPerList = 2000;
    public const int MembersPerList = 50;

    /// <summary>
    /// The lists a person can see, their Watch later first and then the rest most recently changed
    /// first. A Watch later nothing was ever saved to is listed empty without being written.
    /// </summary>
    public List<StoredList> For(string userId, DateTimeOffset now) => database.Read(c =>
    {
        var later = Load(c, WatchLaterOf(userId)) ?? EmptyWatchLater(userId, now);
        var others = c.Query(
            "SELECT id FROM lists WHERE watch_later = 0 AND (owner = $user "
            + "OR id IN (SELECT list_id FROM list_members WHERE user_id = $user)) ORDER BY updated_at DESC, rowid",
            r => r.GetString(0), ("$user", userId));
        return (List<StoredList>)[later, .. others.Select(id => Load(c, id)!)];
    });

    /// <summary>A list by the id its route names, if the person can see it.</summary>
    public StoredList? Get(string userId, string id, DateTimeOffset now) => database.Read(c => Visible(c, userId, id, now));

    /// <summary>Every title on any list, which is what keeps their descriptions warm.</summary>
    public List<StoredEntry> AllEntries() =>
        database.Read(c => c.Query($"SELECT {EntryColumns} FROM list_entries ORDER BY rowid", Entry));

    public (StoredList? List, ListRefusal Refusal) Create(string userId, string name, DateTimeOffset now) => database.Write(c =>
    {
        if (c.Count("SELECT COUNT(*) FROM lists WHERE owner = $user AND watch_later = 0", ("$user", userId)) >= ListsPerOwner)
            return ((StoredList?)null, ListRefusal.Full);

        var list = new StoredList { Id = NewId(c), Name = name, Owner = userId, CreatedAt = now, UpdatedAt = now };
        Insert(c, list);
        return (list, ListRefusal.None);
    });

    public (StoredList? List, ListRefusal Refusal) Rename(string userId, string id, string name, DateTimeOffset now) => database.Write(c =>
    {
        var list = Visible(c, userId, id, now);
        if (list is null) return ((StoredList?)null, ListRefusal.NotFound);
        if (list.WatchLater) return (null, ListRefusal.Personal);
        if (list.RoleOf(userId) != ListRoles.Owner) return (null, ListRefusal.NotAllowed);

        c.Execute("UPDATE lists SET name = $name, updated_at = $now WHERE id = $id", ("$id", list.Id), ("$name", name), ("$now", now));
        return (list with { Name = name, UpdatedAt = now }, ListRefusal.None);
    });

    /// <summary>Deletes a list its owner made. A Watch later is emptied rather than deleted, entry by entry.</summary>
    public ListRefusal Delete(string userId, string id) => database.Write(c =>
    {
        var list = Visible(c, userId, id, DateTimeOffset.MinValue);
        if (list is null) return ListRefusal.NotFound;
        if (list.WatchLater) return ListRefusal.Personal;
        if (list.RoleOf(userId) != ListRoles.Owner) return ListRefusal.NotAllowed;

        c.Execute("DELETE FROM lists WHERE id = $id", ("$id", list.Id));
        return ListRefusal.None;
    });

    /// <summary>
    /// Puts a title on a list, or changes its note when it is already there: an entry keeps the
    /// moment it was first added and who added it, so saving a title twice never moves it. The
    /// person's Watch later is made on the first title saved to it.
    /// </summary>
    public (StoredEntry? Entry, bool Added, ListRefusal Refusal) Save(
        string userId, string id, string imdbId, string kind, string? note, DateTimeOffset now) => database.Write(c =>
    {
        var list = Visible(c, userId, id, now);
        if (list is null) return ((StoredEntry?)null, false, ListRefusal.NotFound);
        if (!ListRoles.CanEdit(list.RoleOf(userId))) return (null, false, ListRefusal.NotAllowed);

        if (list.Entries.FirstOrDefault(e => e.ImdbId == imdbId) is { } existing)
        {
            var saved = note is null ? existing : existing with { Note = note.Length == 0 ? null : note };
            if (saved == existing) return (existing, false, ListRefusal.None);

            c.Execute("UPDATE list_entries SET note = $note WHERE list_id = $id AND imdb_id = $imdb",
                ("$id", list.Id), ("$imdb", imdbId), ("$note", saved.Note));
            Touch(c, list.Id, now);
            return (saved, false, ListRefusal.None);
        }

        if (list.Entries.Count >= EntriesPerList) return (null, false, ListRefusal.Full);

        var entry = new StoredEntry
        {
            ImdbId = imdbId, Kind = kind, AddedAt = now, AddedBy = userId, Note = string.IsNullOrEmpty(note) ? null : note,
        };
        Stored(c, list);
        c.Execute(
            "INSERT INTO list_entries (list_id, imdb_id, kind, added_at, added_by, note) VALUES ($id, $imdb, $kind, $at, $by, $note)",
            ("$id", list.Id), ("$imdb", entry.ImdbId), ("$kind", entry.Kind), ("$at", entry.AddedAt), ("$by", entry.AddedBy),
            ("$note", entry.Note));
        Touch(c, list.Id, now);
        return (entry, true, ListRefusal.None);
    });

    /// <summary>Takes a title off a list; a title that was not on it is the same end state.</summary>
    public ListRefusal Remove(string userId, string id, string imdbId, DateTimeOffset now) => database.Write(c =>
    {
        var list = Visible(c, userId, id, now);
        if (list is null) return ListRefusal.NotFound;
        if (!ListRoles.CanEdit(list.RoleOf(userId))) return ListRefusal.NotAllowed;

        if (c.Execute("DELETE FROM list_entries WHERE list_id = $id AND imdb_id = $imdb", ("$id", list.Id), ("$imdb", imdbId)) > 0)
            Touch(c, list.Id, now);
        return ListRefusal.None;
    });

    /// <summary>Puts somebody on the owner's list, or changes what they may do with it.</summary>
    public (StoredList? List, bool Added, ListRefusal Refusal) SetMember(
        string userId, string id, string memberId, string role, DateTimeOffset now) => database.Write(c =>
    {
        var list = Visible(c, userId, id, now);
        if (list is null) return ((StoredList?)null, false, ListRefusal.NotFound);
        if (list.WatchLater) return (null, false, ListRefusal.Personal);
        if (list.RoleOf(userId) != ListRoles.Owner || memberId == list.Owner) return (null, false, ListRefusal.NotAllowed);

        var added = false;
        if (list.Members.FirstOrDefault(m => m.UserId == memberId) is { } existing)
        {
            if (existing.Role == role) return (list, false, ListRefusal.None);
            c.Execute("UPDATE list_members SET role = $role WHERE list_id = $id AND user_id = $member",
                ("$id", list.Id), ("$member", memberId), ("$role", role));
        }
        else
        {
            if (list.Members.Count >= MembersPerList) return (null, false, ListRefusal.Full);
            c.Execute("INSERT INTO list_members (list_id, user_id, role, since) VALUES ($id, $member, $role, $now)",
                ("$id", list.Id), ("$member", memberId), ("$role", role), ("$now", now));
            added = true;
        }

        Touch(c, list.Id, now);
        return (Load(c, list.Id), added, ListRefusal.None);
    });

    /// <summary>
    /// Takes somebody off a list: the owner takes anybody off, and a member takes themselves off,
    /// which is leaving it. Somebody who was not on it is the same end state.
    /// </summary>
    public ListRefusal RemoveMember(string userId, string id, string memberId, DateTimeOffset now) => database.Write(c =>
    {
        var list = Visible(c, userId, id, now);
        if (list is null) return ListRefusal.NotFound;
        if (list.WatchLater) return ListRefusal.Personal;
        if (list.RoleOf(userId) != ListRoles.Owner && memberId != userId) return ListRefusal.NotAllowed;

        if (c.Execute("DELETE FROM list_members WHERE list_id = $id AND user_id = $member", ("$id", list.Id), ("$member", memberId)) > 0)
            Touch(c, list.Id, now);
        return ListRefusal.None;
    });

    private const string EntryColumns = "imdb_id, kind, added_at, added_by, note";

    /// <summary>The list a route names, if the person can see it; their Watch later as it stands, stored or not.</summary>
    private static StoredList? Visible(SqliteConnection c, string userId, string id, DateTimeOffset now)
    {
        if (id == WatchLaterId) return Load(c, WatchLaterOf(userId)) ?? EmptyWatchLater(userId, now);
        return Load(c, id) is { WatchLater: false } list && list.RoleOf(userId) is not null ? list : null;
    }

    private static StoredList? Load(SqliteConnection c, string id)
    {
        var list = c.First(
            "SELECT id, name, owner, watch_later, created_at, updated_at FROM lists WHERE id = $id",
            r => new StoredList
            {
                Id = r.GetString(0), Name = r.GetString(1), Owner = r.GetString(2), WatchLater = r.GetBool(3),
                CreatedAt = r.GetInstant(4), UpdatedAt = r.GetInstant(5),
            },
            ("$id", id));
        if (list is null) return null;

        list.Members.AddRange(c.Query(
            "SELECT user_id, role, since FROM list_members WHERE list_id = $id ORDER BY rowid",
            r => new ListMember { UserId = r.GetString(0), Role = r.GetString(1), Since = r.GetInstant(2) },
            ("$id", id)));
        list.Entries.AddRange(c.Query($"SELECT {EntryColumns} FROM list_entries WHERE list_id = $id ORDER BY rowid", Entry, ("$id", id)));
        return list;
    }

    private static StoredEntry Entry(SqliteDataReader r) => new()
    {
        ImdbId = r.GetString(0), Kind = r.GetString(1), AddedAt = r.GetInstant(2), AddedBy = r.GetString(3), Note = r.GetStringOrNull(4),
    };

    /// <summary>Writes the list's own row when it is a Watch later not yet stored.</summary>
    private static void Stored(SqliteConnection c, StoredList list)
    {
        if (!c.Exists("SELECT 1 FROM lists WHERE id = $id", ("$id", list.Id))) Insert(c, list);
    }

    private static void Insert(SqliteConnection c, StoredList list) =>
        c.Execute(
            "INSERT INTO lists (id, name, owner, watch_later, created_at, updated_at) VALUES ($id, $name, $owner, $later, $created, $updated)",
            ("$id", list.Id), ("$name", list.Name), ("$owner", list.Owner), ("$later", list.WatchLater),
            ("$created", list.CreatedAt), ("$updated", list.UpdatedAt));

    private static void Touch(SqliteConnection c, string id, DateTimeOffset now) =>
        c.Execute("UPDATE lists SET updated_at = $now WHERE id = $id", ("$id", id), ("$now", now));

    private static string WatchLaterOf(string userId) => $"{WatchLaterId}-{userId}";

    private static StoredList EmptyWatchLater(string userId, DateTimeOffset now) => new()
    {
        Id = WatchLaterOf(userId), Name = WatchLaterName, Owner = userId, WatchLater = true, CreatedAt = now, UpdatedAt = now,
    };

    /// <summary>
    /// A list's id is what a link to it carries, so it says nothing about who made it or how many
    /// lists there are: ten random characters a person can read back.
    /// </summary>
    private static string NewId(SqliteConnection c)
    {
        const string alphabet = "abcdefghijkmnpqrstuvwxyz23456789";
        while (true)
        {
            var id = RandomNumberGenerator.GetString(alphabet, 10);
            if (!c.Exists("SELECT 1 FROM lists WHERE id = $id", ("$id", id))) return id;
        }
    }
}
