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
    public async Task Skipping_intros_is_off_until_switched_on_and_follows_the_person()
    {
        Assert.False((await Read(As("7005", member: true)))?.SkipIntros);

        using var put = await As("7005", member: true).PutAsJsonAsync("/api/me/preferences",
            new ViewerPreferences { SkipIntros = true });
        put.EnsureSuccessStatusCode();

        Assert.True((await Read(As("7005", member: false)))?.SkipIntros);
        Assert.False((await Read(As("7006", member: true)))?.SkipIntros);
    }

    [Fact]
    public async Task A_subtitle_style_is_kept_in_range_and_follows_the_person()
    {
        Assert.Null((await Read(As("7007", member: true)))?.SubtitleStyle);

        using var put = await As("7007", member: true).PutAsJsonAsync("/api/me/preferences", new ViewerPreferences
        {
            SubtitleStyle = new SubtitleStyle
            {
                Size = 400, Font = "papyrus", Bold = true, TextColor = "#FFE14D", Edge = "outline",
                EdgeColor = "black", BackgroundOpacity = -2, Position = 12.5,
            },
        });
        put.EnsureSuccessStatusCode();

        var style = (await Read(As("7007", member: false)))?.SubtitleStyle;
        Assert.NotNull(style);
        Assert.Equal(200, style.Size);
        Assert.Null(style.Font);
        Assert.True(style.Bold);
        Assert.Equal("#ffe14d", style.TextColor);
        Assert.Equal("outline", style.Edge);
        Assert.Null(style.EdgeColor);
        Assert.Equal(0, style.BackgroundOpacity);
        Assert.Equal(12.5, style.Position);
    }

    [Fact]
    public async Task A_subtitle_style_with_nothing_in_range_is_no_style()
    {
        using var put = await As("7008", member: true).PutAsJsonAsync("/api/me/preferences", new ViewerPreferences
        {
            SubtitleStyle = new SubtitleStyle { Font = "papyrus", TextColor = "red" },
        });
        put.EnsureSuccessStatusCode();

        Assert.Null((await Read(As("7008", member: true)))?.SubtitleStyle);
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
