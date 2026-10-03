using System.Text.Json;
using System.Text.Json.Serialization;
using TheKrystalShip.MovieBot.Core;

namespace TheKrystalShip.MovieBot.Api.Notifications;

/// <summary>Where a notification's Discord message stands, for one no push reached.</summary>
public static class DirectState
{
    /// <summary>Waiting for the bot to send it.</summary>
    public const string Waiting = "waiting";

    public const string Sent = "sent";

    /// <summary>Discord would not take it: the person shares no server with the bot, or closed their messages.</summary>
    public const string Refused = "refused";
}

/// <summary>One notification as it is kept, with what became of it.</summary>
public sealed record StoredNotification
{
    public required string Id { get; init; }

    public required string Kind { get; init; }

    /// <summary>
    /// What the notification is about, as one string: a person is told about the same thing once,
    /// however many passes notice it.
    /// </summary>
    public required string Key { get; init; }

    public required string Text { get; init; }

    public string? Url { get; init; }

    public string? Action { get; init; }

    public required DateTimeOffset CreatedAt { get; init; }

    public DateTimeOffset? ReadAt { get; init; }

    /// <summary>How many of the person's devices a push service took it for.</summary>
    public int Pushed { get; init; }

    /// <summary>One of <see cref="DirectState"/>, or absent when no Discord message is owed.</summary>
    public string? Direct { get; init; }

    public NotificationView View() => new()
    {
        Id = Id, Kind = Kind, Text = Text, Url = Url, Action = Action, CreatedAt = CreatedAt, Read = ReadAt is not null,
    };
}

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    WriteIndented = true)]
[JsonSerializable(typeof(Dictionary<string, List<StoredNotification>>))]
[JsonSerializable(typeof(List<StoredPushSubscription>))]
[JsonSerializable(typeof(Dictionary<string, NotificationPreferences>))]
[JsonSerializable(typeof(StoredVapidKeys))]
[JsonSerializable(typeof(PushPayload))]
internal partial class NotificationsJson : JsonSerializerContext;

/// <summary>
/// Everybody's notifications, in one file in the state directory, keyed by Discord user id.
///
/// The bell is a short memory, not an archive: a person keeps their newest <see cref="Kept"/>, none
/// older than <see cref="KeptFor"/>, and the key of each is what stops the same thing being said
/// twice while it is kept. Written whole on every change through a rename, like every other store
/// here; a notification is a sentence, and they arrive a few a day.
/// </summary>
public sealed class NotificationStore
{
    public const int Kept = 50;

    public static readonly TimeSpan KeptFor = TimeSpan.FromDays(30);

    private readonly string _path;
    private readonly ILogger<NotificationStore> _logger;
    private readonly Lock _gate = new();
    private Dictionary<string, List<StoredNotification>> _byUser;

    public NotificationStore(string path, ILogger<NotificationStore> logger)
    {
        _path = path;
        _logger = logger;
        _byUser = Read();
    }

    /// <summary>
    /// Keeps a notification for a person. False, and nothing kept, when they already hold one about
    /// the same thing.
    /// </summary>
    public bool Add(string userId, StoredNotification notification)
    {
        lock (_gate)
        {
            var mine = _byUser.GetValueOrDefault(userId) ?? [];
            if (mine.Any(n => n.Key == notification.Key)) return false;

            var cutoff = notification.CreatedAt - KeptFor;
            _byUser[userId] = [.. new[] { notification }.Concat(mine).Where(n => n.CreatedAt >= cutoff).Take(Kept)];
            Write();
            return true;
        }
    }

    /// <summary>A person's notifications, newest first.</summary>
    public IReadOnlyList<StoredNotification> For(string userId)
    {
        lock (_gate) return [.. _byUser.GetValueOrDefault(userId) ?? []];
    }

    /// <summary>Marks the named notifications read, or all of a person's when none are named.</summary>
    public void MarkRead(string userId, IReadOnlyCollection<string>? ids, DateTimeOffset now)
    {
        lock (_gate)
        {
            if (!_byUser.TryGetValue(userId, out var mine)) return;

            var changed = false;
            for (var i = 0; i < mine.Count; i++)
            {
                if (mine[i].ReadAt is not null || (ids is { Count: > 0 } && !ids.Contains(mine[i].Id))) continue;
                mine[i] = mine[i] with { ReadAt = now };
                changed = true;
            }
            if (changed) Write();
        }
    }

    /// <summary>Records what became of one notification.</summary>
    public void Update(string userId, string id, Func<StoredNotification, StoredNotification> change)
    {
        lock (_gate)
        {
            if (!_byUser.TryGetValue(userId, out var mine)) return;
            var index = mine.FindIndex(n => n.Id == id);
            if (index < 0) return;
            mine[index] = change(mine[index]);
            Write();
        }
    }

    /// <summary>Every notification whose Discord message is waiting, oldest first.</summary>
    public IReadOnlyList<(string UserId, StoredNotification Notification)> Waiting()
    {
        lock (_gate)
            return [.. _byUser
                .SelectMany(p => p.Value.Where(n => n.Direct == DirectState.Waiting).Select(n => (p.Key, n)))
                .OrderBy(p => p.n.CreatedAt)];
    }

    /// <summary>Whose notification an id is, or null when nobody holds it.</summary>
    public string? OwnerOf(string id)
    {
        lock (_gate)
            return _byUser.FirstOrDefault(p => p.Value.Any(n => n.Id == id)).Key;
    }

    private Dictionary<string, List<StoredNotification>> Read()
    {
        try
        {
            if (!File.Exists(_path)) return [];
            using var stream = File.OpenRead(_path);
            return JsonSerializer.Deserialize(stream, NotificationsJson.Default.DictionaryStringListStoredNotification) ?? [];
        }
        catch (Exception ex) when (ex is JsonException or IOException)
        {
            // Unreadable notifications cost everybody their bell and nothing else. Its keys are what
            // stop a repeat, so a few things may be said again; the file is left for somebody to
            // look at, and the next notification writes a good one beside it.
            _logger.LogError(ex, "Notifications at {Path} could not be read; everybody starts with none", _path);
            return [];
        }
    }

    private void Write()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            var temporary = _path + ".tmp";
            File.WriteAllText(temporary, JsonSerializer.Serialize(_byUser, NotificationsJson.Default.DictionaryStringListStoredNotification));
            File.Move(temporary, _path, overwrite: true);
        }
        catch (IOException ex)
        {
            // What is in memory is right and the person was told; what is lost is the next restart.
            _logger.LogError(ex, "Notifications at {Path} could not be written", _path);
        }
    }
}
