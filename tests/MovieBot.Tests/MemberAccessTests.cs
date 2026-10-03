using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using TheKrystalShip.MovieBot.Api.Discord;
using TheKrystalShip.MovieBot.Core;
using Xunit;

namespace TheKrystalShip.MovieBot.Tests;

/// <summary>
/// The site's door: signing in through the redirect is for members of the server, and a member may
/// fetch films as themselves and as nobody else.
/// </summary>
public sealed class MemberAccessTests(SessionFixture fixture) : IClassFixture<SessionFixture>
{
    private const string Guild = "385730677141929985";

    private HttpClient Member(string userId = "5150", string name = "Bea")
    {
        var client = fixture.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", RoomToken.Issue(new RoomTokenPayload
        {
            UserId = userId, DisplayName = name, RoomId = "", Member = true,
            ExpiresAtUnix = DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeSeconds(),
        }, SessionFixture.SigningKey));
        return client;
    }

    [Fact]
    public async Task A_member_searches_the_tracker()
    {
        var ranked = await Member().GetFromJsonAsync("/api/releases?q=inception", ManifestJsonContext.Default.RankedOffers);

        Assert.Equal(41, Assert.Single(ranked!.Candidates).TorrentId);
    }

    [Fact]
    public async Task A_member_s_wish_is_their_own_whoever_the_body_names_and_is_announced_in_no_channel()
    {
        var bea = Member("5151", "Bea");

        using var asked = await bea.PostAsJsonAsync("/api/wishes",
            new AddWishRequest { Chosen = "tt0449088", RequestedBy = new Actor("1", "Somebody else"), ChannelId = "9" },
            ManifestJsonContext.Default.AddWishRequest);
        var reply = await asked.Content.ReadFromJsonAsync(ManifestJsonContext.Default.AddWishReply);
        Assert.Equal("subscribed", reply?.Outcome);

        var subscriber = Assert.Single(reply!.Wish!.Subscribers, s => s.UserId == "5151");
        Assert.Null(subscriber.ChannelId);
        Assert.DoesNotContain(reply.Wish.Subscribers, s => s.UserId == "1");

        // The list is read as the token's person, whatever the query names.
        var mine = await bea.GetFromJsonAsync("/api/wishes?userId=1", ManifestJsonContext.Default.IReadOnlyListWishView);
        Assert.Contains(mine!, w => w.ImdbId == "tt0449088");

        using var cancelled = await bea.DeleteAsync("/api/wishes/tt0449088?userId=1");
        Assert.Equal("cancelled",
            (await cancelled.Content.ReadFromJsonAsync(ManifestJsonContext.Default.CancelWishReply))?.Outcome);
    }

    [Fact]
    public async Task Announcing_wishes_is_the_bot_s_alone()
    {
        using var response = await Member().GetAsync("/api/wishes/available");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task A_member_signs_in_on_the_site_and_the_token_says_so()
    {
        using var api = SignIn();

        using var response = await api.CreateClient().PostAsJsonAsync("/api/auth/discord/callback",
            new { code = "member", redirectUri = "https://movies.example/signin" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var token = (await response.Content.ReadFromJsonAsync<SignInBody>())!.RoomToken;
        var payload = RoomToken.Validate(token, SessionFixture.SigningKey, DateTimeOffset.UtcNow);
        Assert.True(payload?.Member);
        Assert.Equal("555", payload!.UserId);
    }

    [Fact]
    public async Task Somebody_outside_the_server_is_refused_on_the_site()
    {
        using var api = SignIn();

        using var response = await api.CreateClient().PostAsJsonAsync("/api/auth/discord/callback",
            new { code = "stranger", redirectUri = "https://movies.example/signin" });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    /// <summary>The Activity only opens inside the server, sends no redirect, and is asked nothing more.</summary>
    [Fact]
    public async Task The_activity_s_sign_in_is_not_a_member_s()
    {
        using var api = SignIn();

        using var response = await api.CreateClient().PostAsJsonAsync("/api/auth/discord/callback",
            new { code = "stranger", sessionId = "room-1" });

        var token = (await response.Content.ReadFromJsonAsync<SignInBody>())!.RoomToken;
        Assert.False(RoomToken.Validate(token, SessionFixture.SigningKey, DateTimeOffset.UtcNow)?.Member);
    }

    private sealed record SignInBody(string AccessToken, string RoomToken);

    private WebApplicationFactory<Program> SignIn() => fixture.WithWebHostBuilder(builder =>
    {
        builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Discord:ClientId"] = "1",
            ["Discord:ClientSecret"] = "secret",
            ["Discord:GuildIds:0"] = Guild,
        }));
        builder.ConfigureServices(services =>
            services.AddHttpClient<DiscordAuthClient>().ConfigurePrimaryHttpMessageHandler(() => new DiscordStub()));
    });

    /// <summary>Discord, as far as signing in goes: a code names who it is for, and only "member" is in the server.</summary>
    private sealed class DiscordStub : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path.EndsWith("/oauth2/token", StringComparison.Ordinal))
            {
                var form = await request.Content!.ReadAsStringAsync(ct);
                var who = form.Contains("code=member", StringComparison.Ordinal) ? "member" : "stranger";
                return Json($$"""{"access_token":"{{who}}-at","token_type":"Bearer","expires_in":3600}""");
            }

            if (path.EndsWith("/users/@me", StringComparison.Ordinal))
                return Json("""{"id":"555","username":"bea","global_name":"Bea"}""");

            if (path.EndsWith($"/guilds/{Guild}/member", StringComparison.Ordinal))
                return request.Headers.Authorization?.Parameter == "member-at"
                    ? Json("""{"nick":null}""")
                    : new HttpResponseMessage(HttpStatusCode.NotFound);

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        }

        private static HttpResponseMessage Json(string body) =>
            new(HttpStatusCode.OK) { Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json") };
    }
}
