using TheKrystalShip.MovieBot.Api.Data;
using TheKrystalShip.MovieBot.Core;

namespace TheKrystalShip.MovieBot.Api.Notifications;

/// <summary>
/// Which notifications each person is pushed, one row per account. Somebody who never chose has no
/// row and has the defaults, which <see cref="NotificationPreferences"/> spells.
/// </summary>
public sealed class NotificationPreferenceStore(CinemaDatabase database)
{
    public NotificationPreferences For(string userId) =>
        database.Read(c => c.First(
            "SELECT available, ready, episode, together FROM notification_preferences WHERE user_id = $user",
            r => new NotificationPreferences { Available = r.GetBool(0), Ready = r.GetBool(1), Episode = r.GetBool(2), Together = r.GetBool(3) },
            ("$user", userId)))
        ?? new NotificationPreferences();

    public NotificationPreferences Set(string userId, NotificationPreferences preferences)
    {
        database.Write(c => c.Execute(
            "INSERT OR REPLACE INTO notification_preferences (user_id, available, ready, episode, together) "
            + "VALUES ($user, $available, $ready, $episode, $together)",
            ("$user", userId), ("$available", preferences.Available), ("$ready", preferences.Ready),
            ("$episode", preferences.Episode), ("$together", preferences.Together)));
        return preferences;
    }
}
