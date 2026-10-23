using System.Buffers.Text;
using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using Microsoft.Extensions.DependencyInjection;
using TheKrystalShip.MovieBot.Api.People;
using TheKrystalShip.MovieBot.Core;
using TheKrystalShip.Notifications;
using Xunit;

namespace TheKrystalShip.MovieBot.Tests;

/// <summary>
/// Telling a person something through the shared library: it is kept for their bell and pushed to every
/// browser they subscribed where their choices allow. Everything a person reads or changes is their own.
/// </summary>
public sealed class NotificationTests(SessionFixture fixture) : IClassFixture<SessionFixture>
{
    private HttpClient As(string userId, string name = "Cy")
    {
        fixture.Services.GetRequiredService<PersonDirectory>().Remember(userId, name);
        return fixture.CreatePersonClient(userId, name);
    }

    /// <summary>A subscription as a browser makes one: a P-256 public key and an auth secret, base64url.</summary>
    internal static object Browser(string endpoint)
    {
        using var ecdh = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);
        var point = ecdh.ExportParameters(false).Q;
        return new
        {
            endpoint,
            p256dh = Base64Url.EncodeToString([0x04, .. point.X!, .. point.Y!]),
            auth = Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(16)),
        };
    }

    private static async Task Subscribe(HttpClient client, string endpoint)
    {
        using var response = await client.PostAsJsonAsync("/api/me/push/subscribe", Browser(endpoint));
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    private static async Task<int> Devices(HttpClient client) =>
        (await client.GetFromJsonAsync<DevicesPage>("/api/me/push/devices"))!.Endpoints.Count;

    internal static Task<BellPage> Bell(HttpClient client) =>
        client.GetFromJsonAsync<BellPage>("/api/me/notifications")!;

    [Fact]
    public async Task A_browser_is_registered_to_the_person_who_subscribed_it()
    {
        var mine = As("8101");
        const string endpoint = "https://push.invalid/send/8101-phone";
        await Subscribe(mine, endpoint);

        Assert.Equal([endpoint], (await mine.GetFromJsonAsync<DevicesPage>("/api/me/push/devices"))!.Endpoints);
        Assert.Equal(0, await Devices(As("8102")));

        using var removed = await mine.SendAsync(new HttpRequestMessage(HttpMethod.Delete, "/api/me/push/subscribe")
        {
            Content = JsonContent.Create(new { endpoint }),
        });
        Assert.Equal(HttpStatusCode.NoContent, removed.StatusCode);
        Assert.Equal(0, await Devices(mine));
    }

    [Fact]
    public async Task Anything_but_a_push_service_address_is_refused()
    {
        using var response = await As("8103").PostAsJsonAsync("/api/me/push/subscribe", Browser("http://push.invalid/plain"));

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

        using var read = await guest.PostAsJsonAsync("/api/me/notifications/read", new { });
        Assert.Equal(0, (await Bell(guest)).Unread);
    }

    [Fact]
    public async Task Without_a_push_an_invite_waits_in_the_bell()
    {
        var jo = As("8121", "Jo");
        using var sent = await As("8122", "Alex").PostAsJsonAsync("/api/rooms/quiet-night/invite", new InviteRequest { UserId = "8121" });
        Assert.Equal("bell", (await sent.Content.ReadFromJsonAsync<InviteReply>())!.Delivery);

        var bell = await Bell(jo);
        Assert.Equal("Alex invites you to their room.", Assert.Single(bell.Items).Text);
    }

    [Fact]
    public async Task A_browser_the_push_service_calls_gone_is_forgotten()
    {
        var guest = As("8131");
        await Subscribe(guest, "https://push.invalid/gone/8131");

        using var sent = await As("8132").PostAsJsonAsync("/api/rooms/r/invite", new InviteRequest { UserId = "8131" });

        Assert.Equal("bell", (await sent.Content.ReadFromJsonAsync<InviteReply>())!.Delivery);
        Assert.Equal(0, await Devices(guest));
    }

    [Fact]
    public async Task One_thing_is_said_once_and_a_switched_off_kind_is_neither_kept_nor_pushed()
    {
        var person = As("8141");
        const string endpoint = "https://push.invalid/send/8141";
        await Subscribe(person, endpoint);
        var notifier = fixture.Services.GetRequiredService<INotifier>();
        var draft = new NotificationDraft { Kind = NotificationKinds.Episode, Key = "episode:tt1:1", Text = "Show S1 · E1 is out." };

        using var off = await person.PutAsJsonAsync("/api/me/notifications/preferences", new { kinds = new Dictionary<string, bool> { ["episode"] = false } });
        Assert.Null(await notifier.TellAsync("8141", draft, CancellationToken.None));
        Assert.Equal(0, fixture.PushService.SentTo(endpoint));
        Assert.Empty((await Bell(person)).Items);

        using var on = await person.PutAsJsonAsync("/api/me/notifications/preferences", new { kinds = new Dictionary<string, bool> { ["episode"] = true } });
        Assert.Equal(new Delivery(1), await notifier.TellAsync("8141", draft, CancellationToken.None));
        Assert.Null(await notifier.TellAsync("8141", draft, CancellationToken.None));
        Assert.Single((await Bell(person)).Items);
    }

    [Fact]
    public async Task Somebody_who_chose_nothing_has_every_switchable_kind_at_its_default()
    {
        var reply = await As("8151").GetFromJsonAsync<PreferencesPage>("/api/me/notifications/preferences");

        Assert.Equal(new Dictionary<string, bool>
        {
            [NotificationKinds.Available] = true,
            [NotificationKinds.Ready] = true,
            [NotificationKinds.Episode] = true,
            [NotificationKinds.Together] = false,
        }, reply!.Kinds);
    }

    [Fact]
    public async Task An_invite_and_a_plan_cannot_be_switched_off()
    {
        var kinds = (await As("8161").GetFromJsonAsync<KindsPage>("/api/me/notifications/kinds"))!.Kinds;

        Assert.All(kinds.Where(k => k.Id is NotificationKinds.Invite or NotificationKinds.Plan), k => Assert.False(k.Switchable));
        Assert.All(kinds.Where(k => k.Id is not (NotificationKinds.Invite or NotificationKinds.Plan)), k => Assert.True(k.Switchable));
    }

    [Fact]
    public async Task The_button_route_is_anonymous_and_refuses_a_handle_it_does_not_know()
    {
        using var response = await fixture.CreateClient().PostAsync("/api/push/actions/nothing", null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.False((await response.Content.ReadFromJsonAsync<ActionPage>())!.Ok);
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
        using var response = await As("8181").PostAsJsonAsync("/api/rooms/r-8181/invite", new InviteRequest { UserId = "999999" });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}

/// <summary>A person's notifications as <c>GET /api/me/notifications</c> answers.</summary>
public sealed record BellPage(int Unread, List<Notification> Items, bool More);

public sealed record DevicesPage(List<string> Endpoints);

public sealed record PreferencesPage(Dictionary<string, bool> Kinds);

public sealed record KindsPage(List<KindRow> Kinds);

public sealed record KindRow(string Id, bool Switchable);

public sealed record ActionPage(bool Ok, string Message);
