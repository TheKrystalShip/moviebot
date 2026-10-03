using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using TheKrystalShip.MovieBot.Api.People;
using TheKrystalShip.MovieBot.Core;
using Xunit;

namespace TheKrystalShip.MovieBot.Tests;

/// <summary>
/// A token cannot be taken back, so the devices a person is signed in on are what lets them sign
/// out of one: the list names each, and a session they end is refused from then on.
/// </summary>
public sealed class DeviceSessionsTests(SessionFixture fixture) : IClassFixture<SessionFixture>
{
    private sealed record Signed(string Id, string Token);

    /// <summary>A sign-in as the API records one: a token carrying an id, and the id in the list.</summary>
    private Signed SignIn(string userId, string device = "Firefox on Linux", bool withId = true)
    {
        var id = withId ? RoomToken.NewTokenId() : null;
        var expires = DateTimeOffset.UtcNow.AddHours(1);
        if (id is not null)
            fixture.Services.GetRequiredService<DeviceSessions>().Record(id, userId, device, expires);

        return new Signed(id ?? "", RoomToken.Issue(new RoomTokenPayload
        {
            UserId = userId, DisplayName = "Cy", RoomId = "", Member = true, TokenId = id,
            ExpiresAtUnix = expires.ToUnixTimeSeconds(),
        }, SessionFixture.SigningKey));
    }

    private HttpClient With(Signed signIn)
    {
        var client = fixture.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", signIn.Token);
        return client;
    }

    private static async Task<List<DeviceSession>> List(HttpClient client) =>
        await client.GetFromJsonAsync<List<DeviceSession>>("/api/me/sessions", Json) ?? [];

    private static readonly System.Text.Json.JsonSerializerOptions Json = new(System.Text.Json.JsonSerializerDefaults.Web);

    [Fact]
    public async Task A_sign_in_is_listed_and_the_one_asking_is_current()
    {
        var phone = SignIn("8001", "Chrome on Android");
        var laptop = SignIn("8001", "Firefox on Linux");

        var seen = await List(With(phone));

        Assert.Equal(2, seen.Count);
        Assert.True(Assert.Single(seen, s => s.Id == phone.Id).Current);
        var other = Assert.Single(seen, s => s.Id == laptop.Id);
        Assert.False(other.Current);
        Assert.Equal("Firefox on Linux", other.Device);
    }

    [Fact]
    public async Task An_ended_session_is_refused_on_the_api_and_at_the_hub()
    {
        var phone = SignIn("8002");
        var laptop = SignIn("8002");

        using var ended = await With(laptop).DeleteAsync($"/api/me/sessions/{phone.Id}");
        Assert.Equal(HttpStatusCode.NoContent, ended.StatusCode);

        using var api = await With(phone).GetAsync("/api/titles");
        Assert.Equal(HttpStatusCode.Unauthorized, api.StatusCode);

        using var hub = await fixture.CreateClient().PostAsync(
            $"/hub/session/negotiate?negotiateVersion=1&access_token={phone.Token}", null);
        Assert.Equal(HttpStatusCode.Unauthorized, hub.StatusCode);

        using var still = await With(laptop).GetAsync("/api/titles");
        Assert.Equal(HttpStatusCode.OK, still.StatusCode);
        Assert.DoesNotContain(await List(With(laptop)), s => s.Id == phone.Id);
    }

    [Fact]
    public async Task Ending_the_rest_keeps_the_one_asking()
    {
        var phone = SignIn("8003");
        var laptop = SignIn("8003");
        var tablet = SignIn("8003");

        using var ended = await With(phone).DeleteAsync("/api/me/sessions");
        Assert.Equal(HttpStatusCode.NoContent, ended.StatusCode);

        var left = Assert.Single(await List(With(phone)));
        Assert.Equal(phone.Id, left.Id);
        Assert.True(left.Current);

        foreach (var gone in new[] { laptop, tablet })
        {
            using var refused = await With(gone).GetAsync("/api/titles");
            Assert.Equal(HttpStatusCode.Unauthorized, refused.StatusCode);
        }
    }

    [Fact]
    public async Task Another_person_s_session_is_not_there_to_end()
    {
        var mine = SignIn("8004");
        var theirs = SignIn("8005");

        using var refused = await With(mine).DeleteAsync($"/api/me/sessions/{theirs.Id}");
        Assert.Equal(HttpStatusCode.NotFound, refused.StatusCode);

        using var untouched = await With(theirs).GetAsync("/api/titles");
        Assert.Equal(HttpStatusCode.OK, untouched.StatusCode);

        using var nobody = await With(mine).DeleteAsync("/api/me/sessions/no-such-id");
        Assert.Equal(HttpStatusCode.NotFound, nobody.StatusCode);
    }

    [Fact]
    public async Task A_token_without_an_id_works_and_is_not_listed()
    {
        var legacy = SignIn("8006", withId: false);
        var listed = SignIn("8006");

        using var response = await With(legacy).GetAsync("/api/titles");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var seen = await List(With(legacy));
        Assert.Equal(listed.Id, Assert.Single(seen).Id);
        Assert.False(seen[0].Current);

        using var ended = await With(legacy).DeleteAsync("/api/me/sessions");
        Assert.Equal(HttpStatusCode.NoContent, ended.StatusCode);
        using var after = await With(legacy).GetAsync("/api/titles");
        Assert.Equal(HttpStatusCode.OK, after.StatusCode);
    }

    [Fact]
    public async Task The_service_key_names_nobody()
    {
        var client = fixture.CreateServiceClient();

        foreach (var response in new[]
        {
            await client.GetAsync("/api/me/sessions"),
            await client.DeleteAsync("/api/me/sessions"),
            await client.DeleteAsync("/api/me/sessions/x"),
        })
        {
            using (response) Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        }
    }

    [Fact]
    public async Task A_token_the_service_issues_carries_an_id_the_person_can_end()
    {
        var reply = await (await fixture.CreateServiceClient().PostAsJsonAsync("/api/auth/service-token",
            new { roomId = "", userId = "8007", displayName = "Cy", member = true })).Content
            .ReadFromJsonAsync<Dictionary<string, string>>();
        var token = reply!["roomToken"];

        var payload = RoomToken.Validate(token, SessionFixture.SigningKey, DateTimeOffset.UtcNow);
        Assert.NotNull(payload?.TokenId);

        var client = fixture.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        Assert.Equal(payload!.TokenId, Assert.Single(await List(client)).Id);
    }

    [Theory]
    [InlineData("Mozilla/5.0 (X11; Linux x86_64; rv:130.0) Gecko/20100101 Firefox/130.0", "Firefox on Linux")]
    [InlineData("Mozilla/5.0 (Linux; Android 14; Pixel 8) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/128.0.0.0 Mobile Safari/537.36", "Chrome on Android")]
    [InlineData("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/128.0.0.0 Safari/537.36 Edg/128.0.0.0", "Edge on Windows")]
    [InlineData("Mozilla/5.0 (iPhone; CPU iPhone OS 17_5 like Mac OS X) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/17.5 Mobile/15E148 Safari/604.1", "Safari on iOS")]
    [InlineData("", "Unknown device")]
    public void A_user_agent_is_summarised(string userAgent, string expected) =>
        Assert.Equal(expected, DeviceName.FromUserAgent(userAgent));
}
