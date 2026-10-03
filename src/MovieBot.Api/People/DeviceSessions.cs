using System.Text.Json;
using System.Text.Json.Serialization;

namespace TheKrystalShip.MovieBot.Api.People;

/// <summary>
/// One sign-in, as the person sees it in their list of devices. <see cref="Id"/> is the token's
/// <c>jti</c>, so ending the session is refusing that id.
/// </summary>
public sealed record DeviceSession
{
    public required string Id { get; init; }
    public required string Device { get; init; }
    public required DateTimeOffset IssuedAt { get; init; }
    public required DateTimeOffset LastSeenAt { get; init; }

    /// <summary>The session the request asking is itself carrying.</summary>
    public bool Current { get; init; }
}

/// <summary>What is kept for one sign-in. The token's own expiry decides when the entry is forgotten.</summary>
public sealed record StoredDevice
{
    public required string Id { get; init; }
    public required string UserId { get; init; }
    public required string Device { get; init; }
    public required DateTimeOffset IssuedAt { get; init; }
    public required DateTimeOffset LastSeenAt { get; init; }
    public required DateTimeOffset ExpiresAt { get; init; }
    public bool Revoked { get; init; }
}

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingDefault,
    WriteIndented = true)]
[JsonSerializable(typeof(List<StoredDevice>))]
internal partial class DeviceSessionsJson : JsonSerializerContext;

/// <summary>
/// Every sign-in the API issued a token for, in one file in the state directory.
///
/// A token is signed and short-lived, so by itself it cannot be taken back. This list is what makes
/// "sign out elsewhere" possible: a revoked id is refused by the middleware until the token would
/// have expired anyway, at which point the entry has nothing left to say and is dropped.
///
/// <c>lastSeenAt</c> moves on every request, which would be a write per request; it is touched at
/// most once a minute per session, and the file is written a few seconds after the last change
/// through a temporary file and a rename.
/// </summary>
public sealed class DeviceSessions : IDisposable
{
    private static readonly TimeSpan TouchInterval = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan WriteDelay = TimeSpan.FromSeconds(3);

    private readonly string _path;
    private readonly TimeProvider _clock;
    private readonly ILogger<DeviceSessions> _logger;
    private readonly Lock _gate = new();
    private readonly Dictionary<string, StoredDevice> _byId;
    private ITimer? _pending;

    public DeviceSessions(string path, TimeProvider clock, ILogger<DeviceSessions> logger)
    {
        _path = path;
        _clock = clock;
        _logger = logger;
        var now = clock.GetUtcNow();
        _byId = Read().Where(d => d.ExpiresAt > now).ToDictionary(d => d.Id);
    }

    /// <summary>Records a sign-in whose token is being issued, and forgets what has expired.</summary>
    public void Record(string id, string userId, string device, DateTimeOffset expiresAt)
    {
        var now = _clock.GetUtcNow();
        lock (_gate)
        {
            foreach (var expired in _byId.Values.Where(d => d.ExpiresAt <= now).ToList())
                _byId.Remove(expired.Id);

            _byId[id] = new StoredDevice
            {
                Id = id, UserId = userId, Device = device,
                IssuedAt = now, LastSeenAt = now, ExpiresAt = expiresAt,
            };
            ScheduleWrite();
        }
    }

    /// <summary>True for an id somebody ended. An id never recorded is not revoked: a token issued before ids existed has none.</summary>
    public bool IsRevoked(string id)
    {
        lock (_gate) return _byId.TryGetValue(id, out var device) && device.Revoked;
    }

    /// <summary>Notes that a session made a request, at most once a minute.</summary>
    public void Touch(string id)
    {
        var now = _clock.GetUtcNow();
        lock (_gate)
        {
            if (!_byId.TryGetValue(id, out var device) || now - device.LastSeenAt < TouchInterval) return;
            _byId[id] = device with { LastSeenAt = now };
            ScheduleWrite();
        }
    }

    /// <summary>The person's live sessions, most recently seen first. <paramref name="currentId"/> is the one asking.</summary>
    public List<DeviceSession> For(string userId, string? currentId)
    {
        var now = _clock.GetUtcNow();
        lock (_gate)
        {
            return _byId.Values
                .Where(d => d.UserId == userId && !d.Revoked && d.ExpiresAt > now)
                .OrderByDescending(d => d.LastSeenAt)
                .Select(d => new DeviceSession
                {
                    Id = d.Id, Device = d.Device, IssuedAt = d.IssuedAt, LastSeenAt = d.LastSeenAt,
                    Current = d.Id == currentId,
                })
                .ToList();
        }
    }

    /// <summary>Ends one of the person's own sessions. Another person's id, or none, is false.</summary>
    public bool Revoke(string userId, string id)
    {
        lock (_gate)
        {
            if (!_byId.TryGetValue(id, out var device) || device.UserId != userId) return false;
            if (device.Revoked) return true;

            _byId[id] = device with { Revoked = true };
            ScheduleWrite();
            return true;
        }
    }

    /// <summary>Ends every session of the person except the one asking.</summary>
    public void RevokeOthers(string userId, string? currentId)
    {
        lock (_gate)
        {
            var changed = false;
            foreach (var device in _byId.Values.Where(d => d.UserId == userId && !d.Revoked && d.Id != currentId).ToList())
            {
                _byId[device.Id] = device with { Revoked = true };
                changed = true;
            }
            if (changed) ScheduleWrite();
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_pending is null) return;
            _pending.Dispose();
            _pending = null;
            Write();
        }
    }

    private void ScheduleWrite()
    {
        // The first change in a burst arms the write; the rest ride along with it.
        _pending ??= _clock.CreateTimer(_ => Flush(), null, WriteDelay, Timeout.InfiniteTimeSpan);
    }

    private void Flush()
    {
        lock (_gate)
        {
            _pending?.Dispose();
            _pending = null;
            Write();
        }
    }

    private List<StoredDevice> Read()
    {
        try
        {
            if (!File.Exists(_path)) return [];
            using var stream = File.OpenRead(_path);
            return JsonSerializer.Deserialize(stream, DeviceSessionsJson.Default.ListStoredDevice) ?? [];
        }
        catch (Exception ex) when (ex is JsonException or IOException)
        {
            // An unreadable list costs everybody their device list and nothing else; tokens already
            // issued keep working. The next change writes a good file.
            _logger.LogError(ex, "Device sessions at {Path} could not be read; starting with none", _path);
            return [];
        }
    }

    private void Write()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            var temporary = _path + ".tmp";
            File.WriteAllText(temporary, JsonSerializer.Serialize(_byId.Values.ToList(), DeviceSessionsJson.Default.ListStoredDevice));
            File.Move(temporary, _path, overwrite: true);
        }
        catch (IOException ex)
        {
            _logger.LogError(ex, "Device sessions could not be written to {Path}", _path);
        }
    }
}

/// <summary>
/// A short name for what a browser is, from its User-Agent: "Chrome on Android". The Activity is
/// named for what it is, since Discord's embedded browser says little a person would recognise.
/// </summary>
public static class DeviceName
{
    public const string Activity = "Discord Activity";
    public const string Service = "Service";

    public static string FromUserAgent(string? userAgent)
    {
        if (string.IsNullOrWhiteSpace(userAgent)) return "Unknown device";

        // Order matters: most browsers also claim to be Chrome and Safari, and Android claims Linux.
        var browser =
            Has("Edg/") || Has("EdgA/") || Has("EdgiOS/") ? "Edge" :
            Has("OPR/") ? "Opera" :
            Has("Firefox/") || Has("FxiOS/") ? "Firefox" :
            Has("Chrome/") || Has("CriOS/") ? "Chrome" :
            Has("Safari/") ? "Safari" : null;

        var system =
            Has("Android") ? "Android" :
            Has("iPhone") || Has("iPad") ? "iOS" :
            Has("Windows") ? "Windows" :
            Has("Mac OS X") ? "macOS" :
            Has("CrOS") ? "ChromeOS" :
            Has("Linux") ? "Linux" : null;

        return (browser, system) switch
        {
            (not null, not null) => $"{browser} on {system}",
            (not null, null) => browser,
            (null, not null) => system,
            _ => "Unknown device",
        };

        bool Has(string part) => userAgent.Contains(part, StringComparison.Ordinal);
    }
}
