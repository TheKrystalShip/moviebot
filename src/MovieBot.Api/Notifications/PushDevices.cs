using System.Text.Json;
using TheKrystalShip.WebPush;

namespace TheKrystalShip.MovieBot.Api.Notifications;

/// <summary>One browser a person subscribed to pushes from this server.</summary>
public sealed record StoredPushSubscription
{
    /// <summary>The push service's address for the browser. It is the browser's, and the key.</summary>
    public required string Endpoint { get; init; }

    public required string P256dh { get; init; }

    public required string Auth { get; init; }

    public required string UserId { get; init; }

    public required DateTimeOffset SubscribedAt { get; init; }

    public PushSubscription Subscription => new(Endpoint, P256dh, Auth);
}

/// <summary>This server's VAPID pair, as it is kept.</summary>
public sealed record StoredVapidKeys(string PrivateKey, string PublicKey);

/// <summary>
/// The browsers each person has subscribed, in one file in the state directory, and this server's
/// VAPID key pair beside it.
///
/// A subscription is a capability to put text on somebody's phone, so the routes that touch one
/// answer for the person the token names and nobody else. A browser that subscribes again replaces
/// its own row rather than adding a second, and it belongs to whoever subscribed it last: one
/// browser is one person's at a time.
///
/// The key pair is made the first time it is asked for and never again. Its public half is inside
/// every subscription a browser makes, so a new pair silently orphans every device already
/// registered. Its private half is a secret: the file is readable by this service's user alone.
/// </summary>
public sealed class PushDevices
{
    private readonly string _path;
    private readonly string _keysPath;
    private readonly ILogger<PushDevices> _logger;
    private readonly Lock _gate = new();
    private List<StoredPushSubscription> _all;
    private VapidKeyPair? _keys;

    public PushDevices(string path, string keysPath, ILogger<PushDevices> logger)
    {
        _path = path;
        _keysPath = keysPath;
        _logger = logger;
        _all = Read();
    }

    /// <summary>This server's key pair, made and written down the first time it is asked for.</summary>
    public VapidKeyPair Keys()
    {
        lock (_gate)
        {
            if (_keys is not null) return _keys;

            if (File.Exists(_keysPath))
            {
                // A file that is there and unreadable is not replaced: a new pair would orphan every
                // device, and the old one may still be recoverable by hand. Nothing is pushed until then.
                var stored = JsonSerializer.Deserialize(File.ReadAllText(_keysPath), NotificationsJson.Default.StoredVapidKeys)
                    ?? throw new InvalidDataException($"The push keys at {_keysPath} are empty.");
                return _keys = new VapidKeyPair(stored.PrivateKey, stored.PublicKey);
            }

            var keys = VapidKeyPair.Generate();
            Directory.CreateDirectory(Path.GetDirectoryName(_keysPath)!);
            var temporary = _keysPath + ".tmp";
            File.WriteAllText(temporary, "");
            if (!OperatingSystem.IsWindows())
                File.SetUnixFileMode(temporary, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            File.WriteAllText(temporary, JsonSerializer.Serialize(
                new StoredVapidKeys(keys.PrivateKey, keys.PublicKey), NotificationsJson.Default.StoredVapidKeys));
            File.Move(temporary, _keysPath, overwrite: false);
            _logger.LogInformation("Made this server's push keys at {Path}.", _keysPath);
            return _keys = keys;
        }
    }

    public IReadOnlyList<StoredPushSubscription> For(string userId)
    {
        lock (_gate) return [.. _all.Where(s => s.UserId == userId)];
    }

    public bool Holds(string userId, string endpoint)
    {
        lock (_gate) return _all.Any(s => s.UserId == userId && s.Endpoint == endpoint);
    }

    /// <summary>Registers a browser for a person, replacing whatever that browser was registered as.</summary>
    public void Add(StoredPushSubscription subscription)
    {
        lock (_gate)
        {
            _all = [.. _all.Where(s => s.Endpoint != subscription.Endpoint), subscription];
            Write();
        }
    }

    /// <summary>Forgets one of a person's browsers. False when it was not theirs to forget.</summary>
    public bool Remove(string userId, string endpoint)
    {
        lock (_gate)
        {
            var before = _all.Count;
            _all = [.. _all.Where(s => !(s.UserId == userId && s.Endpoint == endpoint))];
            if (_all.Count == before) return false;
            Write();
            return true;
        }
    }

    /// <summary>Forgets a browser the push service says is gone for good.</summary>
    public void Expire(string endpoint)
    {
        lock (_gate)
        {
            var before = _all.Count;
            _all = [.. _all.Where(s => s.Endpoint != endpoint)];
            if (_all.Count != before) Write();
        }
    }

    private List<StoredPushSubscription> Read()
    {
        try
        {
            if (!File.Exists(_path)) return [];
            using var stream = File.OpenRead(_path);
            return JsonSerializer.Deserialize(stream, NotificationsJson.Default.ListStoredPushSubscription) ?? [];
        }
        catch (Exception ex) when (ex is JsonException or IOException)
        {
            // Every device would have to subscribe again; the file is left for somebody to look at.
            _logger.LogError(ex, "Push subscriptions at {Path} could not be read; nobody is subscribed", _path);
            return [];
        }
    }

    private void Write()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            var temporary = _path + ".tmp";
            File.WriteAllText(temporary, JsonSerializer.Serialize(_all, NotificationsJson.Default.ListStoredPushSubscription));
            File.Move(temporary, _path, overwrite: true);
        }
        catch (IOException ex)
        {
            _logger.LogError(ex, "Push subscriptions at {Path} could not be written", _path);
        }
    }
}
