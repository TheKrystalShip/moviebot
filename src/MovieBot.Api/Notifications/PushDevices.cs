using TheKrystalShip.MovieBot.Api.Data;
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

/// <summary>
/// The browsers each person has subscribed, and this server's VAPID key pair.
///
/// A subscription is a capability to put text on somebody's phone, so the routes that touch one
/// answer for the person the token names and nobody else. A browser that subscribes again replaces
/// its own row rather than adding a second, and it belongs to whoever subscribed it last: one
/// browser is one person's at a time.
///
/// The key pair is made the first time it is asked for and never again. Its public half is inside
/// every subscription a browser makes, so a new pair silently orphans every device already
/// registered. Its private half is a secret, and the database it is kept in is readable by this
/// service's user alone.
/// </summary>
public sealed class PushDevices(CinemaDatabase database, ILogger<PushDevices> logger)
{
    private const string PrivateKey = "vapid_private";
    private const string PublicKey = "vapid_public";

    private VapidKeyPair? _keys;

    /// <summary>This server's key pair, made and kept the first time it is asked for.</summary>
    public VapidKeyPair Keys()
    {
        if (_keys is { } held) return held;

        return _keys = database.Write(c =>
        {
            var kept = c.Query("SELECT key, value FROM meta WHERE key IN ($private, $public)",
                r => KeyValuePair.Create(r.GetString(0), r.GetString(1)), ("$private", PrivateKey), ("$public", PublicKey))
                .ToDictionary();
            if (kept.TryGetValue(PrivateKey, out var privateKey) && kept.TryGetValue(PublicKey, out var publicKey))
                return new VapidKeyPair(privateKey, publicKey);

            var keys = VapidKeyPair.Generate();
            c.Execute("INSERT INTO meta (key, value) VALUES ($private, $privateValue), ($public, $publicValue)",
                ("$private", PrivateKey), ("$privateValue", keys.PrivateKey), ("$public", PublicKey), ("$publicValue", keys.PublicKey));
            logger.LogInformation("Made this server's push keys.");
            return keys;
        });
    }

    public IReadOnlyList<StoredPushSubscription> For(string userId) =>
        database.Read(c => c.Query(
            "SELECT endpoint, p256dh, auth, user_id, subscribed_at FROM push_subscriptions WHERE user_id = $user ORDER BY rowid",
            r => new StoredPushSubscription
            {
                Endpoint = r.GetString(0), P256dh = r.GetString(1), Auth = r.GetString(2), UserId = r.GetString(3),
                SubscribedAt = r.GetInstant(4),
            },
            ("$user", userId)));

    public bool Holds(string userId, string endpoint) =>
        database.Read(c => c.Exists("SELECT 1 FROM push_subscriptions WHERE user_id = $user AND endpoint = $endpoint",
            ("$user", userId), ("$endpoint", endpoint)));

    /// <summary>Registers a browser for a person, replacing whatever that browser was registered as.</summary>
    public void Add(StoredPushSubscription subscription) =>
        database.Write(c => c.Execute(
            "INSERT OR REPLACE INTO push_subscriptions (endpoint, user_id, p256dh, auth, subscribed_at) "
            + "VALUES ($endpoint, $user, $p256dh, $auth, $at)",
            ("$endpoint", subscription.Endpoint), ("$user", subscription.UserId), ("$p256dh", subscription.P256dh),
            ("$auth", subscription.Auth), ("$at", subscription.SubscribedAt)));

    /// <summary>Forgets one of a person's browsers. False when it was not theirs to forget.</summary>
    public bool Remove(string userId, string endpoint) =>
        database.Write(c => c.Execute("DELETE FROM push_subscriptions WHERE user_id = $user AND endpoint = $endpoint",
            ("$user", userId), ("$endpoint", endpoint)) > 0);

    /// <summary>Forgets a browser the push service says is gone for good.</summary>
    public void Expire(string endpoint) =>
        database.Write(c => c.Execute("DELETE FROM push_subscriptions WHERE endpoint = $endpoint", ("$endpoint", endpoint)));
}
