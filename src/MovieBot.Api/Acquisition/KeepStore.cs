using System.Text.Json;
using System.Text.Json.Serialization;

namespace TheKrystalShip.MovieBot.Api.Acquisition;

/// <summary>Somebody wants this film to stay in the library, and when they said so.</summary>
public sealed record KeepRecord(string TitleId, string KeeperId, DateTimeOffset KeptAt);

/// <summary>What a keeper is called, or null when nothing knows. A keeper is shown by id where it gives none.</summary>
public delegate string? KeeperNames(string keeperId);

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    WriteIndented = true)]
[JsonSerializable(typeof(List<KeepRecord>))]
internal partial class KeepsJson : JsonSerializerContext;

/// <summary>
/// The films and seasons somebody keeps, by unit — a film's library id, or a season's
/// (<see cref="Library.TitleLibrary.UnitOf(Core.Manifest)"/>) — in one file in the state directory.
///
/// A keep pins a settled film against the library's clock. It lives here and not on the film's
/// torrent because the torrent is let go of once its seeding is done, long before the film leaves
/// the library, and a keep that went with it would protect nothing. Anyone may keep a film and
/// anyone may let it go; the record says who did.
///
/// Written whole on every change, through a rename: a keep is a few bytes and changes when somebody
/// presses a button, and a file written whole is never found half-written.
/// </summary>
public sealed class KeepStore
{
    private readonly string _path;
    private readonly ILogger<KeepStore> _logger;
    private readonly Lock _gate = new();
    private Dictionary<string, KeepRecord> _byTitle;

    public KeepStore(string path, ILogger<KeepStore> logger)
    {
        _path = path;
        _logger = logger;
        _byTitle = Read();
    }

    public KeepRecord? Get(string titleId)
    {
        lock (_gate) return _byTitle.GetValueOrDefault(titleId);
    }

    /// <summary>
    /// Keeps the film as somebody. A film already kept stays its first keeper's, so a second press
    /// is the same answer rather than a change of hands.
    /// </summary>
    public KeepRecord Add(string titleId, string keeperId, DateTimeOffset now)
    {
        lock (_gate)
        {
            if (_byTitle.TryGetValue(titleId, out var existing)) return existing;

            var keep = new KeepRecord(titleId, keeperId, now);
            _byTitle = new Dictionary<string, KeepRecord>(_byTitle) { [titleId] = keep };
            Write(_byTitle);
            return keep;
        }
    }

    /// <summary>Lets the film go back on the clock. True when there was a keep to remove.</summary>
    public bool Remove(string titleId)
    {
        lock (_gate)
        {
            if (!_byTitle.ContainsKey(titleId)) return false;

            var rest = new Dictionary<string, KeepRecord>(_byTitle);
            rest.Remove(titleId);
            _byTitle = rest;
            Write(_byTitle);
            return true;
        }
    }

    private Dictionary<string, KeepRecord> Read()
    {
        try
        {
            if (!File.Exists(_path)) return [];
            using var stream = File.OpenRead(_path);
            var keeps = JsonSerializer.Deserialize(stream, KeepsJson.Default.ListKeepRecord) ?? [];
            return keeps.ToDictionary(k => k.TitleId, StringComparer.Ordinal);
        }
        catch (Exception ex) when (ex is JsonException or IOException or ArgumentException)
        {
            // Not read as "nobody keeps anything": that reading lets every film leave on the next
            // sweep. The store does not start, so the library clock has no answer and nothing is
            // deleted until the file is looked at.
            _logger.LogError(ex, "Keeps at {Path} could not be read.", _path);
            throw;
        }
    }

    private void Write(Dictionary<string, KeepRecord> all)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        var temporary = _path + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize([.. all.Values], KeepsJson.Default.ListKeepRecord));
        File.Move(temporary, _path, overwrite: true);
    }
}
