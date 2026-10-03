using System.Buffers.Text;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using Microsoft.Extensions.DependencyInjection;
using TheKrystalShip.MovieBot.Api.Notifications;
using TheKrystalShip.MovieBot.Api.People;
using TheKrystalShip.MovieBot.Core;
using Xunit;

namespace TheKrystalShip.MovieBot.Tests;

/// <summary>
/// Telling a person something: it is kept for their bell, pushed to every browser they subscribed
/// where their choices allow, and owed to them on Discord when no push reached them. Everything a
/// person reads or changes is their own.
/// </summary>
public sealed class NotificationTests(SessionFixture fixture) : IClassFixture<SessionFixture>
{
    private HttpClient As(string userId, string name = "Cy")
    {
        fixture.Services.GetRequiredService<PersonDirectory>().Remember(userId, name);
        var client = fixture.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", RoomToken.Issue(new RoomTokenPayload
        {
            UserId = userId, DisplayName = name, RoomId = "", Member = true,
            ExpiresAtUnix = DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeSeconds(),
        }, SessionFixture.SigningKey));
        return client;
    }

    /// <summary>A subscription as a browser makes one: a P-256 public key and an auth secret, base64url.</summary>
    private static PushSubscribeRequest Browser(string endpoint)
    {
        using var ecdh = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);
        var point = ecdh.ExportParameters(false).Q;
        return new PushSubscribeRequest
        {
            Endpoint = endpoint,
            Keys = new PushSubscriptionKeys
            {
                P256dh = Base64Url.EncodeToString([0x04, .. point.X!, .. point.Y!]),
                Auth = Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(16)),
            },
        };
    }

    private static async Task Subscribe(HttpClient client, string endpoint)
    {
        using var response = await client.PostAsJsonAsync("/api/me/push", Browser(endpoint));
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    private static async Task<NotificationsReply> Bell(HttpClient client) =>
        (await client.GetFromJsonAsync<NotificationsReply>("/api/me/notifications"))!;

    [Fact]
    public async Task A_browser_is_registered_to_the_person_who_subscribed_it()
    {
        var mine = As("8101");
        const string endpoint = "https://push.invalid/send/8101-phone";
        await Subscribe(mine, endpoint);

        var status = await mine.GetFromJsonAsync<PushStatusReply>("/api/me/push?endpoint=" + Uri.EscapeDataString(endpoint));
        Assert.True(status!.Subscribed);
        Assert.Equal(1, status.Devices);

        // Somebody else asking about the same browser hears no, and cannot unsubscribe it.
        var theirs = As("8102");
        Assert.False((await theirs.GetFromJsonAsync<PushStatusReply>("/api/me/push?endpoint=" + Uri.EscapeDataString(endpoint)))!.Subscribed);
        using var refused = await theirs.DeleteAsync("/api/me/push?endpoint=" + Uri.EscapeDataString(endpoint));
        Assert.Equal(HttpStatusCode.NotFound, refused.StatusCode);

        using var removed = await mine.DeleteAsync("/api/me/push?endpoint=" + Uri.EscapeDataString(endpoint));
        Assert.Equal(HttpStatusCode.NoContent, removed.StatusCode);
    }

    [Fact]
    public async Task Anything_but_a_push_service_address_is_refused()
    {
        using var response = await As("8103").PostAsJsonAsync("/api/me/push", Browser("http://push.invalid/plain"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task An_invite_is_pushed_encrypted_and_lands_in_the_bell()
    {
        var guest = As("8111", "Jo");
        const string endpoint = "https://push.invalid/send/8111-phone";
        await Subscribe(guest, endpoint);

        using var sent = await As("8112", "Alex").PostAsJsonAsync("/api/rooms/movie-night/invite", new InviteRequest { UserId = "8111" });
        var reply = await sent.Content.ReadFromJsonAsync<InviteReply>();

        Assert.Equal("push", reply!.Delivery);
        var push = Assert.Single(fixture.PushService.Received, r => r.Endpoint.ToString() == endpoint);
        Assert.StartsWith("vapid t=", push.Authorization);
        Assert.Equal("aes128gcm", push.Encoding);

        var bell = await Bell(guest);
        Assert.Equal(1, bell.Unread);
        var invite = Assert.Single(bell.Items);
        Assert.Equal(NotificationKinds.Invite, invite.Kind);
        Assert.Equal("Alex invites you to their room.", invite.Text);
        Assert.Equal("/r/movie-night", invite.Url);

        using var read = await guest.PostAsJsonAsync("/api/me/notifications/read", new MarkReadRequest());
        Assert.Equal(0, (await Bell(guest)).Unread);
    }

    [Fact]
    public async Task Without_a_push_the_bot_is_asked_to_send_it_on_Discord()
    {
        As("8121", "Jo");
        using var sent = await As("8122", "Alex").PostAsJsonAsync("/api/rooms/movie-night/invite", new InviteRequest { UserId = "8121" });
        Assert.Equal("discord", (await sent.Content.ReadFromJsonAsync<InviteReply>())!.Delivery);

        var bot = fixture.CreateServiceClient();
        var waiting = await bot.GetFromJsonAsync<List<DirectMessage>>("/api/notifications/direct");
        var message = Assert.Single(waiting!, m => m.UserId == "8121");
        Assert.Equal("Alex invites you to their room.", message.Text);

        using var told = await bot.PostAsJsonAsync($"/api/notifications/direct/{message.Id}", new DirectMessageReport(true));
        Assert.Equal(HttpStatusCode.NoContent, told.StatusCode);
        Assert.DoesNotContain(await bot.GetFromJsonAsync<List<DirectMessage>>("/api/notifications/direct") ?? [], m => m.Id == message.Id);
    }

    [Fact]
    public async Task A_browser_the_push_service_calls_gone_is_forgotten()
    {
        var guest = As("8131");
        await Subscribe(guest, "https://push.invalid/gone/8131");

        using var sent = await As("8132").PostAsJsonAsync("/api/rooms/r/invite", new InviteRequest { UserId = "8131" });

        Assert.Equal("discord", (await sent.Content.ReadFromJsonAsync<InviteReply>())!.Delivery);
        Assert.Equal(0, (await guest.GetFromJsonAsync<PushStatusReply>("/api/me/push"))!.Devices);
    }

    [Fact]
    public async Task One_thing_is_said_once_and_a_switched_off_kind_reaches_only_the_bell()
    {
        var person = As("8141");
        const string endpoint = "https://push.invalid/send/8141";
        await Subscribe(person, endpoint);
        var notifier = fixture.Services.GetRequiredService<Notifier>();
        var draft = new NotificationDraft { Kind = NotificationKinds.Kept, Key = "kept:heat:1", Text = "Alex kept Heat (1995)." };

        // Kept is off unless asked for.
        var first = await notifier.TellAsync("8141", draft, CancellationToken.None);
        Assert.Equal(new Delivery(0, false), first);
        Assert.Null(await notifier.TellAsync("8141", draft, CancellationToken.None));
        Assert.Equal(0, fixture.PushService.SentTo(endpoint));
        Assert.Single((await Bell(person)).Items);

        using var put = await person.PutAsJsonAsync("/api/me/notifications/preferences", new NotificationPreferences { Kept = true });
        var pushed = await notifier.TellAsync("8141", draft with { Key = "kept:heat:2" }, CancellationToken.None);
        Assert.Equal(new Delivery(1, false), pushed);
    }

    [Fact]
    public async Task Somebody_who_chose_nothing_has_every_kind_but_keeps()
    {
        var preferences = await As("8151").GetFromJsonAsync<NotificationPreferences>("/api/me/notifications/preferences");

        Assert.Equal(new NotificationPreferences { Available = true, Ready = true, Leaving = true, Kept = false }, preferences);
    }

    [Fact]
    public async Task The_Discord_queue_answers_the_service_key_alone()
    {
        using var response = await As("8161").GetAsync("/api/notifications/direct");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task The_invite_list_is_everybody_known_but_the_person_asking()
    {
        As("8172", "Mika");
        var people = await As("8171", "Sam").GetFromJsonAsync<List<PersonView>>("/api/people");

        Assert.Contains(people!, p => p.UserId == "8172" && p.Name == "Mika");
        Assert.DoesNotContain(people!, p => p.UserId == "8171");
    }

    [Fact]
    public async Task Nobody_unknown_is_invited()
    {
        using var response = await As("8181").PostAsJsonAsync("/api/rooms/r/invite", new InviteRequest { UserId = "999999" });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
