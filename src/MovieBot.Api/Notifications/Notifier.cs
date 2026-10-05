using System.Text.Json;
using TheKrystalShip.KGSM.WebPush;
using TheKrystalShip.MovieBot.Core;

namespace TheKrystalShip.MovieBot.Api.Notifications;

/// <summary>Something to tell one person, before it is kept.</summary>
public sealed record NotificationDraft
{
    /// <summary>One of <see cref="NotificationKinds"/>.</summary>
    public required string Kind { get; init; }

    /// <summary>What it is about, as one string; a person is told about one thing once.</summary>
    public required string Key { get; init; }

    public required string Text { get; init; }

    public string? Url { get; init; }

    public string? Action { get; init; }
}

/// <summary>How a notification went out: how many devices a push service took it for.</summary>
public sealed record Delivery(int Pushed);

/// <summary>What a push carries. The worker shows the text and opens the path.</summary>
public sealed record PushPayload(string Title, string Body, string? Url, string Tag);

/// <summary>
/// Tells a person something: it goes in their bell, and, where their choices allow it, to every
/// browser they subscribed.
///
/// Every notification is kept whatever the person chose; the switches hold back the push, not the
/// record. A push service taking a push is all that is ever known about it, so that is all
/// <see cref="Delivery"/> says.
/// </summary>
public sealed class Notifier(
    NotificationStore inbox,
    PushDevices devices,
    NotificationPreferenceStore preferences,
    WebPushSender sender,
    IConfiguration configuration,
    TimeProvider clock,
    ILogger<Notifier> logger)
{
    public const string Title = "Krystal Cinema";

    /// <summary>
    /// Tells a person. Null when they already hold a notification about the same thing, which is
    /// what lets every watcher ask on every pass.
    /// </summary>
    public async Task<Delivery?> TellAsync(string userId, NotificationDraft draft, CancellationToken ct)
    {
        var notification = new StoredNotification
        {
            Id = Guid.NewGuid().ToString("N"),
            Kind = draft.Kind,
            Key = draft.Key,
            Text = draft.Text,
            Url = draft.Url,
            Action = draft.Action,
            CreatedAt = clock.GetUtcNow(),
        };
        if (!inbox.Add(userId, notification)) return null;

        if (draft.Kind != NotificationKinds.Invite && !preferences.For(userId).Allows(draft.Kind))
            return new Delivery(0);

        var pushed = await PushAsync(userId, notification, ct);
        if (pushed > 0)
            inbox.Update(userId, notification.Id, n => n with { Pushed = pushed });

        logger.LogInformation("Told {User} {Kind} ({Key}): {Pushed} push.", userId, draft.Kind, draft.Key, pushed);
        return new Delivery(pushed);
    }

    /// <summary>Pushes a notification to every browser a person subscribed, counting the ones a push service took.</summary>
    private async Task<int> PushAsync(string userId, StoredNotification notification, CancellationToken ct)
    {
        var targets = devices.For(userId);
        if (targets.Count == 0) return 0;

        VapidKeyPair keys;
        try
        {
            keys = devices.Keys();
        }
        catch (Exception ex) when (ex is IOException or JsonException or InvalidDataException or UnauthorizedAccessException)
        {
            logger.LogError(ex, "This server's push keys could not be read, so nothing is pushed.");
            return 0;
        }

        var payload = JsonSerializer.SerializeToUtf8Bytes(
            new PushPayload(Title, notification.Text, notification.Url, $"{notification.Kind}:{notification.Key}"),
            NotificationsJson.Default.PushPayload);

        var accepted = 0;
        foreach (var target in targets)
        {
            PushResult result;
            try
            {
                result = await sender.SendAsync(target.Subscription, payload, keys, Subject(), ct);
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
            {
                logger.LogWarning(ex, "A push to one of {User}'s devices could not be sent.", userId);
                continue;
            }

            switch (result.Outcome)
            {
                case PushOutcome.Accepted:
                    accepted++;
                    break;
                case PushOutcome.Expired:
                    // The browser gave the subscription up; it never works again.
                    devices.Expire(target.Endpoint);
                    logger.LogInformation("One of {User}'s devices is no longer subscribed and is forgotten.", userId);
                    break;
                default:
                    logger.LogWarning("A push service refused a push to one of {User}'s devices: {Status} {Error}",
                        userId, result.Status, result.Error);
                    break;
            }
        }
        return accepted;
    }

    /// <summary>How a push service reaches whoever runs this server: its public address.</summary>
    private string Subject() =>
        configuration["Api:PublicBaseUrl"] is { Length: > 0 } url ? url.TrimEnd('/') : "mailto:moviebot@localhost";
}
