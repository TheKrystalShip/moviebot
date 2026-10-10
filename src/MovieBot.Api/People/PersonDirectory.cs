using System.Collections.Concurrent;
using TheKrystalShip.MovieBot.Api.Data;

namespace TheKrystalShip.MovieBot.Api.People;

/// <summary>
/// What each account is called. Everything else this service keeps about a person is keyed by the
/// account the identity provider names them by, and an id is no use to somebody reading "last played
/// by" or "kept by"; this is what turns one into a name.
///
/// Told on every request that carries a token, which names the person and what they are called. A
/// name is overwritten by the latest one heard, so a person who renames themselves is called by the
/// new name from their next visit.
///
/// Kept in the database and held in memory beside it, since a listing names somebody on every row:
/// a request that heard nothing new reads nothing and writes nothing. When each person was last seen
/// is held in memory alone: it says who is around, and a restart honestly knows nobody.
/// </summary>
public sealed class PersonDirectory
{
    private readonly CinemaDatabase _database;
    private readonly ConcurrentDictionary<string, string> _names;
    private readonly ConcurrentDictionary<string, DateTimeOffset> _seen = new(StringComparer.Ordinal);

    public PersonDirectory(CinemaDatabase database)
    {
        _database = database;
        _names = new ConcurrentDictionary<string, string>(
            database.Read(c => c.Query("SELECT user_id, display_name FROM people",
                r => KeyValuePair.Create(r.GetString(0), r.GetString(1)))),
            StringComparer.Ordinal);
    }

    /// <summary>What the person is called, or null when this service has never heard their name.</summary>
    public string? NameOf(string userId) => _names.GetValueOrDefault(userId);

    /// <summary>Everybody this service has heard of, by id, with what they are called.</summary>
    public IReadOnlyDictionary<string, string> All() => _names;

    /// <summary>Notes a name. A blank one says nothing about who the person is and is ignored.</summary>
    public void Remember(string userId, string displayName)
    {
        if (string.IsNullOrWhiteSpace(userId) || string.IsNullOrWhiteSpace(displayName)) return;
        var name = displayName.Trim();
        if (_names.TryGetValue(userId, out var known) && known == name) return;

        _database.Write(c => c.Execute(
            "INSERT INTO people (user_id, display_name) VALUES ($user, $name) "
            + "ON CONFLICT (user_id) DO UPDATE SET display_name = excluded.display_name",
            ("$user", userId), ("$name", name)));
        _names[userId] = name;
    }

    /// <summary>Notes that the person called the API themselves just now.</summary>
    public void Seen(string userId, DateTimeOffset at) => _seen[userId] = at;

    /// <summary>When each person was last seen, of those seen since the service started.</summary>
    public IReadOnlyDictionary<string, DateTimeOffset> LastSeen() => _seen;
}
