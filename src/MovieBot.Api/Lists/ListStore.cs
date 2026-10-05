using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

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

public sealed record ListData
{
    public List<StoredList> Lists { get; init; } = [];
}

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    WriteIndented = true)]
[JsonSerializable(typeof(ListData))]
internal partial class ListJson : JsonSerializerContext;

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
/// Every list, in one file in the state directory, written whole through a rename on every change
/// like every other store here. A list changes when somebody saves something, which is a few times a
/// day; there is nothing to batch.
/// </summary>
public sealed class ListStore
{
    /// <summary>What a person calls their own Watch later in a route.</summary>
    public const string WatchLaterId = "later";

    public const string WatchLaterName = "Watch later";

    public const int NameLength = 80;
    public const int NoteLength = 500;
    public const int ListsPerOwner = 100;
    public const int EntriesPerList = 2000;
    public const int MembersPerList = 50;

    private readonly string _path;
    private readonly ILogger<ListStore> _logger;
    private readonly Lock _gate = new();
    private ListData _data;

    public ListStore(string path, ILogger<ListStore> logger)
    {
        _path = path;
        _logger = logger;
        _data = Read();
    }

    /// <summary>
    /// The lists a person can see, their Watch later first and then the rest most recently changed
    /// first. A Watch later nothing was ever saved to is listed empty without being written.
    /// </summary>
    public List<StoredList> For(string userId, DateTimeOffset now)
    {
        lock (_gate)
        {
            var visible = _data.Lists.Where(l => l.RoleOf(userId) is not null).ToList();
            var later = visible.FirstOrDefault(l => l.WatchLater && l.Owner == userId) ?? EmptyWatchLater(userId, now);
            return [later, .. visible.Where(l => !(l.WatchLater && l.Owner == userId)).OrderByDescending(l => l.UpdatedAt)];
        }
    }

    /// <summary>A list by the id its route names, if the person can see it.</summary>
    public StoredList? Get(string userId, string id, DateTimeOffset now)
    {
        lock (_gate)
        {
            if (id == WatchLaterId)
                return _data.Lists.FirstOrDefault(l => l.WatchLater && l.Owner == userId) ?? EmptyWatchLater(userId, now);
            return _data.Lists.FirstOrDefault(l => l.Id == id && !l.WatchLater && l.RoleOf(userId) is not null);
        }
    }

    /// <summary>Every title on any list, which is what keeps their descriptions warm.</summary>
    public List<StoredEntry> AllEntries()
    {
        lock (_gate) return [.. _data.Lists.SelectMany(l => l.Entries)];
    }

    public (StoredList? List, ListRefusal Refusal) Create(string userId, string name, DateTimeOffset now)
    {
        lock (_gate)
        {
            if (_data.Lists.Count(l => l.Owner == userId && !l.WatchLater) >= ListsPerOwner) return (null, ListRefusal.Full);

            var list = new StoredList
            {
                Id = NewId(), Name = name, Owner = userId, CreatedAt = now, UpdatedAt = now,
            };
            Commit(_data with { Lists = [.. _data.Lists, list] });
            return (list, ListRefusal.None);
        }
    }

    public (StoredList? List, ListRefusal Refusal) Rename(string userId, string id, string name, DateTimeOffset now) =>
        Change(userId, id, now, list =>
            list.WatchLater ? (null, ListRefusal.Personal)
            : list.RoleOf(userId) != ListRoles.Owner ? (null, ListRefusal.NotAllowed)
            : (list with { Name = name }, ListRefusal.None));

    /// <summary>Deletes a list its owner made. A Watch later is emptied rather than deleted, entry by entry.</summary>
    public ListRefusal Delete(string userId, string id)
    {
        lock (_gate)
        {
            var list = Get(userId, id, DateTimeOffset.MinValue);
            if (list is null) return ListRefusal.NotFound;
            if (list.WatchLater) return ListRefusal.Personal;
            if (list.RoleOf(userId) != ListRoles.Owner) return ListRefusal.NotAllowed;

            Commit(_data with { Lists = [.. _data.Lists.Where(l => l.Id != list.Id)] });
            return ListRefusal.None;
        }
    }

    /// <summary>
    /// Puts a title on a list, or changes its note when it is already there: an entry keeps the
    /// moment it was first added and who added it, so saving a title twice never moves it.
    /// </summary>
    public (StoredEntry? Entry, bool Added, ListRefusal Refusal) Save(
        string userId, string id, string imdbId, string kind, string? note, DateTimeOffset now)
    {
        lock (_gate)
        {
            StoredEntry? saved = null;
            var added = false;
            var (_, refusal) = Change(userId, id, now, list =>
            {
                if (!ListRoles.CanEdit(list.RoleOf(userId))) return (null, ListRefusal.NotAllowed);

                if (list.Entries.FirstOrDefault(e => e.ImdbId == imdbId) is { } existing)
                {
                    saved = note is null ? existing : existing with { Note = note.Length == 0 ? null : note };
                    if (saved == existing) return (list, ListRefusal.None);
                    return (list with { Entries = [.. list.Entries.Select(e => e.ImdbId == imdbId ? saved : e)] }, ListRefusal.None);
                }

                if (list.Entries.Count >= EntriesPerList) return (null, ListRefusal.Full);
                saved = new StoredEntry
                {
                    ImdbId = imdbId, Kind = kind, AddedAt = now, AddedBy = userId,
                    Note = string.IsNullOrEmpty(note) ? null : note,
                };
                added = true;
                return (list with { Entries = [.. list.Entries, saved] }, ListRefusal.None);
            });
            return (refusal == ListRefusal.None ? saved : null, added, refusal);
        }
    }

    /// <summary>Takes a title off a list; a title that was not on it is the same end state.</summary>
    public ListRefusal Remove(string userId, string id, string imdbId, DateTimeOffset now) =>
        Change(userId, id, now, list =>
            !ListRoles.CanEdit(list.RoleOf(userId)) ? (null, ListRefusal.NotAllowed)
            : list.Entries.All(e => e.ImdbId != imdbId) ? (list, ListRefusal.None)
            : (list with { Entries = [.. list.Entries.Where(e => e.ImdbId != imdbId)] }, ListRefusal.None)).Refusal;

    /// <summary>Puts somebody on the owner's list, or changes what they may do with it.</summary>
    public (StoredList? List, bool Added, ListRefusal Refusal) SetMember(
        string userId, string id, string memberId, string role, DateTimeOffset now)
    {
        var added = false;
        var (list, refusal) = Change(userId, id, now, list =>
        {
            if (list.WatchLater) return (null, ListRefusal.Personal);
            if (list.RoleOf(userId) != ListRoles.Owner || memberId == list.Owner) return (null, ListRefusal.NotAllowed);

            if (list.Members.FirstOrDefault(m => m.UserId == memberId) is { } existing)
                return existing.Role == role
                    ? (list, ListRefusal.None)
                    : (list with { Members = [.. list.Members.Select(m => m.UserId == memberId ? m with { Role = role } : m)] }, ListRefusal.None);

            if (list.Members.Count >= MembersPerList) return (null, ListRefusal.Full);
            added = true;
            return (list with { Members = [.. list.Members, new ListMember { UserId = memberId, Role = role, Since = now }] }, ListRefusal.None);
        });
        return (list, added, refusal);
    }

    /// <summary>
    /// Takes somebody off a list: the owner takes anybody off, and a member takes themselves off,
    /// which is leaving it. Somebody who was not on it is the same end state.
    /// </summary>
    public ListRefusal RemoveMember(string userId, string id, string memberId, DateTimeOffset now) =>
        Change(userId, id, now, list =>
            list.WatchLater ? (null, ListRefusal.Personal)
            : list.RoleOf(userId) != ListRoles.Owner && memberId != userId ? (null, ListRefusal.NotAllowed)
            : list.Members.All(m => m.UserId != memberId) ? (list, ListRefusal.None)
            : (list with { Members = [.. list.Members.Where(m => m.UserId != memberId)] }, ListRefusal.None)).Refusal;

    /// <summary>
    /// Applies a change to a list the person can see and writes it when it changed anything. The
    /// person's Watch later is made on the first change to it.
    /// </summary>
    private (StoredList? List, ListRefusal Refusal) Change(
        string userId, string id, DateTimeOffset now, Func<StoredList, (StoredList? List, ListRefusal Refusal)> change)
    {
        lock (_gate)
        {
            var list = Get(userId, id, now);
            if (list is null) return (null, ListRefusal.NotFound);

            var (changed, refusal) = change(list);
            if (refusal != ListRefusal.None || changed is null) return (null, refusal);
            if (ReferenceEquals(changed, list)) return (list, ListRefusal.None);

            changed = changed with { UpdatedAt = now };
            var stored = _data.Lists.Any(l => l.Id == list.Id)
                ? _data.Lists.Select(l => l.Id == list.Id ? changed : l).ToList()
                : [.. _data.Lists, changed];
            Commit(_data with { Lists = stored });
            return (changed, ListRefusal.None);
        }
    }

    private StoredList EmptyWatchLater(string userId, DateTimeOffset now) => new()
    {
        Id = $"{WatchLaterId}-{userId}", Name = WatchLaterName, Owner = userId, WatchLater = true,
        CreatedAt = now, UpdatedAt = now,
    };

    /// <summary>
    /// A list's id is what a link to it carries, so it says nothing about who made it or how many
    /// lists there are: ten random characters a person can read back.
    /// </summary>
    private string NewId()
    {
        const string alphabet = "abcdefghijkmnpqrstuvwxyz23456789";
        while (true)
        {
            var id = RandomNumberGenerator.GetString(alphabet, 10);
            if (_data.Lists.All(l => l.Id != id)) return id;
        }
    }

    private void Commit(ListData data)
    {
        _data = data;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            var temporary = _path + ".tmp";
            File.WriteAllText(temporary, JsonSerializer.Serialize(_data, ListJson.Default.ListData));
            File.Move(temporary, _path, overwrite: true);
        }
        catch (IOException ex)
        {
            _logger.LogError(ex, "Lists at {Path} could not be written", _path);
        }
    }

    private ListData Read()
    {
        try
        {
            if (!File.Exists(_path)) return new ListData();
            using var stream = File.OpenRead(_path);
            return JsonSerializer.Deserialize(stream, ListJson.Default.ListData) ?? new ListData();
        }
        catch (Exception ex) when (ex is JsonException or IOException)
        {
            _logger.LogError(ex, "Lists at {Path} could not be read; nobody has any", _path);
            return new ListData();
        }
    }
}
