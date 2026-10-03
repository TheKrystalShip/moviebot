using System.Text.Json;
using TheKrystalShip.MovieBot.Core;

namespace TheKrystalShip.MovieBot.Api.Notifications;

/// <summary>
/// Which notifications each person is pushed, in one file in the state directory, keyed by Discord
/// user id. Somebody who never chose has the defaults, which <see cref="NotificationPreferences"/>
/// spells.
/// </summary>
public sealed class NotificationPreferenceStore
{
    private readonly string _path;
    private readonly ILogger<NotificationPreferenceStore> _logger;
    private readonly Lock _gate = new();
    private Dictionary<string, NotificationPreferences> _byUser;

    public NotificationPreferenceStore(string path, ILogger<NotificationPreferenceStore> logger)
    {
        _path = path;
        _logger = logger;
        _byUser = Read();
    }

    public NotificationPreferences For(string userId)
    {
        lock (_gate) return _byUser.GetValueOrDefault(userId) ?? new NotificationPreferences();
    }

    public NotificationPreferences Set(string userId, NotificationPreferences preferences)
    {
        lock (_gate)
        {
            _byUser = new Dictionary<string, NotificationPreferences>(_byUser) { [userId] = preferences };
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
                var temporary = _path + ".tmp";
                File.WriteAllText(temporary, JsonSerializer.Serialize(_byUser, NotificationsJson.Default.DictionaryStringNotificationPreferences));
                File.Move(temporary, _path, overwrite: true);
            }
            catch (IOException ex)
            {
                _logger.LogError(ex, "Notification preferences at {Path} could not be written", _path);
            }
        }
        return preferences;
    }

    private Dictionary<string, NotificationPreferences> Read()
    {
        try
        {
            if (!File.Exists(_path)) return [];
            using var stream = File.OpenRead(_path);
            return JsonSerializer.Deserialize(stream, NotificationsJson.Default.DictionaryStringNotificationPreferences) ?? [];
        }
        catch (Exception ex) when (ex is JsonException or IOException)
        {
            _logger.LogError(ex, "Notification preferences at {Path} could not be read; everybody has the defaults", _path);
            return [];
        }
    }
}
