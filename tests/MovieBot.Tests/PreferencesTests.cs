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
    /// <summary>The site and the Activity both arrive with a token the provider minted, by different clients.</summary>
    private HttpClient As(string userId, bool site) =>
        fixture.CreatePersonClient(userId, "Cy", via: site ? null : "cinema-activity");

    private static async Task<ViewerPreferences?> Read(HttpClient client) =>
        await client.GetFromJsonAsync<ViewerPreferences>("/api/me/preferences");

    [Fact]
    public async Task Somebody_who_set_nothing_has_the_defaults()
    {
        var preferences = await Read(As("7001", site: true));

        Assert.Equal(new ViewerPreferences(), preferences);
    }

    [Fact]
    public async Task What_the_site_sets_the_activity_reads()
    {
        using var put = await As("7002", site: true).PutAsJsonAsync("/api/me/preferences",
            new ViewerPreferences { SubtitleLanguage = "es", QualityHeight = 720, OfflineQualityHeight = 480 });
        put.EnsureSuccessStatusCode();

        var seen = await Read(As("7002", site: false));
        Assert.Equal("es", seen?.SubtitleLanguage);
        Assert.Equal(720, seen?.QualityHeight);
        Assert.Equal(480, seen?.OfflineQualityHeight);
        Assert.Null(seen?.AudioLanguage);

        // Somebody else reads their own.
        Assert.Null((await Read(As("7003", site: true)))?.SubtitleLanguage);
    }

    [Fact]
    public async Task Skipping_intros_is_off_until_switched_on_and_follows_the_person()
    {
        Assert.False((await Read(As("7005", site: true)))?.SkipIntros);

        using var put = await As("7005", site: true).PutAsJsonAsync("/api/me/preferences",
            new ViewerPreferences { SkipIntros = true });
        put.EnsureSuccessStatusCode();

        Assert.True((await Read(As("7005", site: false)))?.SkipIntros);
        Assert.False((await Read(As("7006", site: true)))?.SkipIntros);
    }

    [Fact]
    public async Task A_subtitle_style_in_range_is_kept_and_follows_the_person()
    {
        Assert.Null((await Read(As("7007", site: true)))?.SubtitleStyle);

        using var put = await As("7007", site: true).PutAsJsonAsync("/api/me/preferences", new ViewerPreferences
        {
            SubtitleStyle = new SubtitleStyle
            {
                Size = 200, Font = "serif", Bold = true, TextColor = "#FFE14D", Edge = "outline",
                EdgeColor = "#000000", BackgroundOpacity = 0, Position = 12.5,
            },
        });
        put.EnsureSuccessStatusCode();

        var style = (await Read(As("7007", site: false)))?.SubtitleStyle;
        Assert.NotNull(style);
        Assert.Equal(200, style.Size);
        Assert.Equal("serif", style.Font);
        Assert.True(style.Bold);
        Assert.Equal("#ffe14d", style.TextColor);
        Assert.Equal("outline", style.Edge);
        Assert.Equal(0, style.BackgroundOpacity);
        Assert.Equal(12.5, style.Position);
    }

    [Theory]
    [InlineData(400, null, null, null, null)]
    [InlineData(49, null, null, null, null)]
    [InlineData(null, "papyrus", null, null, null)]
    [InlineData(null, null, "red", null, null)]
    [InlineData(null, null, null, -2.0, null)]
    [InlineData(null, null, null, 1.5, null)]
    [InlineData(null, null, null, null, 31.0)]
    [InlineData(null, null, null, null, -1.0)]
    public async Task A_subtitle_style_outside_the_ranges_is_refused_and_keeps_what_was_there(
        int? size, string? font, string? colour, double? opacity, double? position)
    {
        var client = As("7007" + size + font + colour + opacity + position, site: true);
        using var kept = await client.PutAsJsonAsync("/api/me/preferences",
            new ViewerPreferences { SubtitleStyle = new SubtitleStyle { Size = 120 } });
        kept.EnsureSuccessStatusCode();

        using var put = await client.PutAsJsonAsync("/api/me/preferences", new ViewerPreferences
        {
            SubtitleStyle = new SubtitleStyle { Size = size, Font = font, TextColor = colour, BackgroundOpacity = opacity, Position = position },
        });

        Assert.Equal(HttpStatusCode.BadRequest, put.StatusCode);
        Assert.Equal(120, (await Read(client))?.SubtitleStyle?.Size);
    }

    [Fact]
    public async Task A_subtitle_style_with_nothing_in_it_is_no_style()
    {
        using var put = await As("7008", site: true).PutAsJsonAsync("/api/me/preferences", new ViewerPreferences
        {
            SubtitleStyle = new SubtitleStyle(),
        });
        put.EnsureSuccessStatusCode();

        Assert.Null((await Read(As("7008", site: true)))?.SubtitleStyle);
    }

    [Theory]
    [InlineData(-1, null)]
    [InlineData(0, null)]
    [InlineData(4321, null)]
    [InlineData(null, 0)]
    [InlineData(null, -480)]
    public async Task A_quality_that_is_neither_auto_nor_a_height_is_refused(int? quality, int? offline)
    {
        using var put = await As("7004", site: true).PutAsJsonAsync("/api/me/preferences",
            new ViewerPreferences { QualityHeight = quality, OfflineQualityHeight = offline });

        Assert.Equal(HttpStatusCode.BadRequest, put.StatusCode);
    }

    [Fact]
    public async Task Auto_and_a_height_are_kept_and_whitespace_around_a_language_is_not_part_of_it()
    {
        var client = As("7009", site: true);
        using var fixedHeight = await client.PutAsJsonAsync("/api/me/preferences",
            new ViewerPreferences { SubtitleLanguage = "  es ", QualityHeight = 720 });
        var stored = await fixedHeight.Content.ReadFromJsonAsync<ViewerPreferences>();
        Assert.Equal("es", stored?.SubtitleLanguage);
        Assert.Equal(720, stored?.QualityHeight);

        using var auto = await client.PutAsJsonAsync("/api/me/preferences", new ViewerPreferences { QualityHeight = null });
        auto.EnsureSuccessStatusCode();
        Assert.Null((await Read(client))?.QualityHeight);
    }

    [Fact]
    public async Task The_service_key_names_nobody()
    {
        using var response = await fixture.CreateServiceClient().GetAsync("/api/me/preferences");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
