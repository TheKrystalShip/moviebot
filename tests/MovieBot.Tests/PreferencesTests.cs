using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using TheKrystalShip.MovieBot.Api.People;
using TheKrystalShip.MovieBot.Core;
using Xunit;

namespace TheKrystalShip.MovieBot.Tests;

/// <summary>
/// How a person watches follows the person: whatever token names them, from the site or from the
/// Activity, reads and writes the same preferences, and nobody else's.
/// </summary>
public sealed class PreferencesTests(SessionFixture fixture) : IClassFixture<SessionFixture>
{
    private HttpClient As(string userId, bool member)
    {
        var client = fixture.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", RoomToken.Issue(new RoomTokenPayload
        {
            UserId = userId, DisplayName = "Cy", RoomId = member ? "" : "room-1", Member = member,
            ExpiresAtUnix = DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeSeconds(),
        }, SessionFixture.SigningKey));
        return client;
    }

    private static async Task<ViewerPreferences?> Read(HttpClient client) =>
        await client.GetFromJsonAsync<ViewerPreferences>("/api/me/preferences");

    [Fact]
    public async Task Somebody_who_set_nothing_has_the_defaults()
    {
        var preferences = await Read(As("7001", member: true));

        Assert.Equal(new ViewerPreferences(), preferences);
    }

    [Fact]
    public async Task What_the_site_sets_the_activity_reads()
    {
        using var put = await As("7002", member: true).PutAsJsonAsync("/api/me/preferences",
            new ViewerPreferences { SubtitleLanguage = "es", QualityHeight = 720 });
        put.EnsureSuccessStatusCode();

        var seen = await Read(As("7002", member: false));
        Assert.Equal("es", seen?.SubtitleLanguage);
        Assert.Equal(720, seen?.QualityHeight);
        Assert.Null(seen?.AudioLanguage);

        // Somebody else reads their own.
        Assert.Null((await Read(As("7003", member: true)))?.SubtitleLanguage);
    }

    [Fact]
    public async Task A_meaningless_value_is_the_setting_left_alone()
    {
        using var put = await As("7004", member: true).PutAsJsonAsync("/api/me/preferences",
            new ViewerPreferences { SubtitleLanguage = "  ", QualityHeight = -1 });
        var stored = await put.Content.ReadFromJsonAsync<ViewerPreferences>();

        Assert.Null(stored?.SubtitleLanguage);
        Assert.Null(stored?.QualityHeight);
    }

    [Fact]
    public async Task The_service_key_names_nobody()
    {
        var client = fixture.CreateClient();
        client.DefaultRequestHeaders.Add("X-MovieBot-Service", SessionFixture.ServiceKey);

        using var response = await client.GetAsync("/api/me/preferences");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }
}
