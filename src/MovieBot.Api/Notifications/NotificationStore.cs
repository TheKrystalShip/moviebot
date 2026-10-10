using System.Text.Json.Serialization;
using TheKrystalShip.MovieBot.Api.Data;
using TheKrystalShip.MovieBot.Core;

namespace TheKrystalShip.MovieBot.Api.Notifications;

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

    public NotificationView View() => new()
    {
        Id = Id, Kind = Kind, Text = Text, Url = Url, Action = Action, CreatedAt = CreatedAt, Read = ReadAt is not null,
    };
}

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    WriteIndented = true)]
[JsonSerializable(typeof(PushPayload))]
internal partial class NotificationsJson : JsonSerializerContext;

/// <summary>
/// Everybody's notifications, one row each.
///
/// The bell is a short memory, not an archive: a person keeps their newest <see cref="Kept"/>, none
/// older than <see cref="KeptFor"/>, and the key of each is what stops the same thing being said
/// twice while it is kept.
/// </summary>
public sealed class NotificationStore(CinemaDatabase database)
{
    public const int Kept = 50;

    public static readonly TimeSpan KeptFor = TimeSpan.FromDays(30);

    private const string Columns = "id, kind, key, text, url, action, created_at, read_at, pushed";

    /// <summary>
    /// Keeps a notification for a person. False, and nothing kept, when they already hold one about
    /// the same thing.
    /// </summary>
    public bool Add(string userId, StoredNotification notification) => database.Write(c =>
    {
        if (c.Exists("SELECT 1 FROM notifications WHERE user_id = $user AND key = $key",
                ("$user", userId), ("$key", notification.Key)))
            return false;

        c.Execute(
            $"INSERT INTO notifications (user_id, {Columns}) VALUES "
            + "($user, $id, $kind, $key, $text, $url, $action, $created, $read, $pushed)",
            ("$user", userId), ("$id", notification.Id), ("$kind", notification.Kind), ("$key", notification.Key),
            ("$text", notification.Text), ("$url", notification.Url), ("$action", notification.Action),
            ("$created", notification.CreatedAt), ("$read", notification.ReadAt), ("$pushed", notification.Pushed));
        c.Execute(
            "DELETE FROM notifications WHERE user_id = $user AND (created_at < $cutoff OR rowid NOT IN "
            + "(SELECT rowid FROM notifications WHERE user_id = $user ORDER BY rowid DESC LIMIT $kept))",
            ("$user", userId), ("$cutoff", notification.CreatedAt - KeptFor), ("$kept", Kept));
        return true;
    });

    /// <summary>A person's notifications, newest first.</summary>
    public IReadOnlyList<StoredNotification> For(string userId) =>
        database.Read(c => c.Query(
            $"SELECT {Columns} FROM notifications WHERE user_id = $user ORDER BY rowid DESC",
            r => new StoredNotification
            {
                Id = r.GetString(0),
                Kind = r.GetString(1),
                Key = r.GetString(2),
                Text = r.GetString(3),
                Url = r.GetStringOrNull(4),
                Action = r.GetStringOrNull(5),
                CreatedAt = r.GetInstant(6),
                ReadAt = r.GetInstantOrNull(7),
                Pushed = r.GetInt32(8),
            },
            ("$user", userId)));

    /// <summary>Marks the named notifications read, or all of a person's when none are named.</summary>
    public void MarkRead(string userId, IReadOnlyCollection<string>? ids, DateTimeOffset now) => database.Write(c =>
    {
        if (ids is not { Count: > 0 })
        {
            c.Execute("UPDATE notifications SET read_at = $now WHERE user_id = $user AND read_at IS NULL",
                ("$user", userId), ("$now", now));
            return;
        }
        foreach (var id in ids)
            c.Execute("UPDATE notifications SET read_at = $now WHERE user_id = $user AND id = $id AND read_at IS NULL",
                ("$user", userId), ("$id", id), ("$now", now));
    });

    /// <summary>Records how many of the person's devices a push service took a notification for.</summary>
    public void SetPushed(string userId, string id, int pushed) =>
        database.Write(c => c.Execute("UPDATE notifications SET pushed = $pushed WHERE user_id = $user AND id = $id",
            ("$user", userId), ("$id", id), ("$pushed", pushed)));
}
