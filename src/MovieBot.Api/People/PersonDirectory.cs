using System.Text.Json;
using System.Text.Json.Serialization;

namespace TheKrystalShip.MovieBot.Api.People;

[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(Dictionary<string, string>))]
internal partial class PersonDirectoryJson : JsonSerializerContext;

/// <summary>
/// Who a Discord id is called. Everything else this service keeps about a person is keyed by id,
/// and an id is no use to somebody reading "last played by" or "kept by"; this is what turns one
/// into a name.
///
/// Told whenever the API issues a token and whenever somebody joins a room, which are the only two
/// moments it hears a name at all. A name is overwritten by the latest one heard, so a person who
/// renames themselves is called by the new name from their next visit.
///
/// Written whole through a rename when a name is new or changed. That is rare next to how often
/// somebody joins a room, and a join that heard nothing new writes nothing.
/// </summary>
public sealed class PersonDirectory
{
    private readonly string _path;
    private readonly ILogger<PersonDirectory> _logger;
    private readonly Lock _gate = new();
    private Dictionary<string, string> _names;

    public PersonDirectory(string path, ILogger<PersonDirectory> logger)
    {
        _path = path;
        _logger = logger;
        _names = Read();
    }

    /// <summary>What the person is called, or null when this service has never heard their name.</summary>
    public string? NameOf(string userId)
    {
        lock (_gate) return _names.GetValueOrDefault(userId);
    }

    /// <summary>Everybody this service has heard of, by id, with what they are called.</summary>
    public IReadOnlyDictionary<string, string> All()
    {
        lock (_gate) return _names;
    }

    /// <summary>Notes a name. A blank one says nothing about who the person is and is ignored.</summary>
    public void Remember(string userId, string displayName)
    {
        if (string.IsNullOrWhiteSpace(userId) || string.IsNullOrWhiteSpace(displayName)) return;
        var name = displayName.Trim();

        lock (_gate)
        {
            if (_names.TryGetValue(userId, out var known) && known == name) return;
            _names = new Dictionary<string, string>(_names) { [userId] = name };
            Write(_names);
        }
    }

    private Dictionary<string, string> Read()
    {
        try
        {
            if (!File.Exists(_path)) return [];
            using var stream = File.OpenRead(_path);
            return JsonSerializer.Deserialize(stream, PersonDirectoryJson.Default.DictionaryStringString) ?? [];
        }
        catch (Exception ex) when (ex is JsonException or IOException)
        {
            // Names are heard again the next time each person signs in or joins a room, so
            // starting empty costs a few "someone"s for a while and nothing else.
            _logger.LogError(ex, "The names at {Path} could not be read; they are learned again as people arrive", _path);
            return [];
        }
    }

    private void Write(Dictionary<string, string> all)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            var temporary = _path + ".tmp";
            File.WriteAllText(temporary, JsonSerializer.Serialize(all, PersonDirectoryJson.Default.DictionaryStringString));
            File.Move(temporary, _path, overwrite: true);
        }
        catch (IOException ex)
        {
            // A name that could not be written down is still held, and heard again on the next visit.
            _logger.LogError(ex, "The names could not be written to {Path}", _path);
        }
    }
}
