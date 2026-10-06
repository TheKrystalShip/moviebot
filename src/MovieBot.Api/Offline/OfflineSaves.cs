using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;

namespace TheKrystalShip.MovieBot.Api.Offline;

/// <summary>One save of one title onto one of a person's devices, as it is kept.</summary>
public sealed record StoredSave
{
    public required string Id { get; init; }
    public required string TitleId { get; init; }

    /// <summary>The video rendition saved, by its directory.</summary>
    public required string Rung { get; init; }

    public required List<string> Audio { get; init; }
    public required List<string> Subtitles { get; init; }

    /// <summary>The name the device goes by, as it gave it.</summary>
    public required string Device { get; init; }

    public required DateTimeOffset CreatedAt { get; init; }

    /// <summary>When the device said it holds every byte; absent while it is still being made.</summary>
    public DateTimeOffset? CompletedAt { get; init; }
}

/// <summary>Everything the saves keep: each person's, by account.</summary>
public sealed record OfflineData
{
    public Dictionary<string, List<StoredSave>> People { get; init; } = [];
}

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    WriteIndented = true)]
[JsonSerializable(typeof(OfflineData))]
internal partial class OfflineJson : JsonSerializerContext;

/// <summary>The titles a save still being made keeps in the library and in place.</summary>
public interface ISaveHolds
{
    IReadOnlySet<string> Holding(DateTimeOffset now);
}

/// <summary>
/// What each person saved onto which device, in one file in the state directory, keyed by account and
/// written whole through a rename on every change, like every other store here.
///
/// A save still being made holds its title: the device is copying the title's files one track after
/// another, and a title that left the library, moved between disks or was encoded again under it
/// would leave the device holding half of one film and half of another. The hold ends when the device
/// says it has every byte, when the save is forgotten, or <see cref="OfflineOptions.HoldHours"/> after
/// the save was made, so a device that walked away mid-save holds nothing for long.
/// </summary>
public sealed class OfflineSaves : ISaveHolds
{
    private readonly string _path;
    private readonly IOptionsMonitor<OfflineOptions> _options;
    private readonly ILogger<OfflineSaves> _logger;
    private readonly Lock _gate = new();
    private OfflineData _data;

    public OfflineSaves(string path, IOptionsMonitor<OfflineOptions> options, ILogger<OfflineSaves> logger)
    {
        _path = path;
        _options = options;
        _logger = logger;
        _data = Read();
    }

    /// <summary>A person's saves, newest first.</summary>
    public List<StoredSave> For(string userId)
    {
        lock (_gate)
            return [.. (_data.People.GetValueOrDefault(userId) ?? []).OrderByDescending(s => s.CreatedAt)];
    }

    /// <summary>Whether the person has a save of the title on any device, which is what opens its tracks to them.</summary>
    public bool Has(string userId, string titleId)
    {
        lock (_gate) return _data.People.GetValueOrDefault(userId)?.Any(s => s.TitleId == titleId) ?? false;
    }

    /// <summary>
    /// Records a save. One of the same title on the same device is that save made again: it keeps its
    /// id, starts its hold over and is no longer complete.
    /// </summary>
    public StoredSave Save(string userId, string titleId, string rung, List<string> audio, List<string> subtitles,
        string device, DateTimeOffset now)
    {
        lock (_gate)
        {
            var mine = _data.People.GetValueOrDefault(userId) ?? [];
            var existing = mine.FirstOrDefault(s => s.TitleId == titleId && s.Device == device);
            var save = new StoredSave
            {
                Id = existing?.Id ?? NewId(),
                TitleId = titleId,
                Rung = rung,
                Audio = audio,
                Subtitles = subtitles,
                Device = device,
                CreatedAt = now,
            };

            Commit(userId, [.. mine.Where(s => s.Id != save.Id), save]);
            return save;
        }
    }

    /// <summary>Marks a save complete. Null when the person has no save by that id.</summary>
    public StoredSave? Complete(string userId, string id, DateTimeOffset now)
    {
        lock (_gate)
        {
            var mine = _data.People.GetValueOrDefault(userId) ?? [];
            if (mine.FirstOrDefault(s => s.Id == id) is not { } save) return null;
            if (save.CompletedAt is not null) return save;

            var done = save with { CompletedAt = now };
            Commit(userId, [.. mine.Select(s => s.Id == id ? done : s)]);
            return done;
        }
    }

    /// <summary>Forgets a save. False when the person had none by that id.</summary>
    public bool Forget(string userId, string id)
    {
        lock (_gate)
        {
            var mine = _data.People.GetValueOrDefault(userId) ?? [];
            if (!mine.Any(s => s.Id == id)) return false;
            Commit(userId, [.. mine.Where(s => s.Id != id)]);
            return true;
        }
    }

    /// <summary>When the save stops holding its title, or null when it holds nothing.</summary>
    public DateTimeOffset? HoldsUntil(StoredSave save, DateTimeOffset now)
    {
        if (save.CompletedAt is not null) return null;
        var until = save.CreatedAt + _options.CurrentValue.Hold;
        return until > now ? until : null;
    }

    public IReadOnlySet<string> Holding(DateTimeOffset now)
    {
        lock (_gate)
            return _data.People.Values.SelectMany(p => p)
                .Where(s => HoldsUntil(s, now) is not null)
                .Select(s => s.TitleId)
                .ToHashSet(StringComparer.Ordinal);
    }

    /// <summary>Called with the gate held.</summary>
    private void Commit(string userId, List<StoredSave> saves)
    {
        var people = new Dictionary<string, List<StoredSave>>(_data.People);
        if (saves.Count == 0) people.Remove(userId); else people[userId] = saves;
        _data = _data with { People = people };

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            var temporary = _path + ".tmp";
            File.WriteAllText(temporary, JsonSerializer.Serialize(_data, OfflineJson.Default.OfflineData));
            File.Move(temporary, _path, overwrite: true);
        }
        catch (IOException ex)
        {
            _logger.LogError(ex, "Saves at {Path} could not be written", _path);
        }
    }

    private static string NewId() => Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(8));

    private OfflineData Read()
    {
        try
        {
            if (!File.Exists(_path)) return new OfflineData();
            using var stream = File.OpenRead(_path);
            return JsonSerializer.Deserialize(stream, OfflineJson.Default.OfflineData) ?? new OfflineData();
        }
        catch (Exception ex) when (ex is JsonException or IOException)
        {
            // An unreadable file costs every save its hold and its listing, and nothing on any device.
            _logger.LogError(ex, "Saves at {Path} could not be read; nobody has any", _path);
            return new OfflineData();
        }
    }
}
