using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using TheKrystalShip.MovieBot.Api.Library;
using TheKrystalShip.MovieBot.Core;
using Xunit;

namespace TheKrystalShip.MovieBot.Tests;

/// <summary>
/// A poster is rewritten under its own name, so it is checked at its bare address and kept for good at
/// the versioned one the listing hands out, whose version moves when the file does.
/// </summary>
public sealed class PosterCacheTests(SessionFixture fixture) : IClassFixture<SessionFixture>
{
    private const string Titled = "with-a-poster-2026";

    [Fact]
    public async Task A_listed_poster_is_kept_at_its_version_and_checked_at_its_bare_address()
    {
        var poster = Path.Combine(fixture.MediaRoot, Titled, "poster.jpg");
        WriteTitle(poster, new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc));

        var client = fixture.CreateServiceClient();
        var version = await ListedVersion(client);
        Assert.Equal(new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.Zero).ToUnixTimeMilliseconds().ToString(), version);

        var anybody = fixture.CreateClient();
        using var bare = await anybody.GetAsync($"/media/{Titled}/poster.jpg");
        Assert.Equal(TimeSpan.Zero, bare.Headers.CacheControl!.MaxAge);
        Assert.True(bare.Headers.CacheControl.MustRevalidate);

        using var versioned = await anybody.GetAsync($"/media/{Titled}/poster.jpg?v={version}");
        Assert.Equal(TimeSpan.FromDays(365), versioned.Headers.CacheControl!.MaxAge);
        Assert.Contains(versioned.Headers.CacheControl.Extensions, e => e.Name == "immutable");

        File.SetLastWriteTimeUtc(poster, new DateTime(2026, 2, 3, 4, 5, 6, DateTimeKind.Utc));
        Assert.NotEqual(version, await ListedVersion(client));
    }

    private async Task<string?> ListedVersion(HttpClient client)
    {
        fixture.Services.GetRequiredService<LibraryListing>().Refresh();
        var titles = await client.GetFromJsonAsync<List<Listed>>("/api/titles");
        return titles!.Single(t => t.Id == Titled).PosterVersion;
    }

    private void WriteTitle(string poster, DateTime written)
    {
        ManifestJson.WriteAtomic(Path.Combine(fixture.MediaRoot, Titled, "manifest.json"), new Manifest
        {
            Id = Titled,
            Title = "With a Poster",
            DurationSeconds = 6000,
            Status = TitleStatus.Ready,
            Poster = "poster.jpg",
            Video = new VideoInfo
            {
                Width = 1920, Height = 800, SourceCodec = "h264", SourceHdr = "sdr",
                Renditions = [new Rendition { Name = "800p", BitrateKbps = 9000, Width = 1920, Height = 800, Uri = "v0/index.m3u8" }],
            },
            Audio = [],
            Subtitles = [],
        });
        File.WriteAllBytes(poster, [0xFF, 0xD8, 0xFF, 0xD9]);
        File.SetLastWriteTimeUtc(poster, written);
    }

    private sealed record Listed(string Id, string? PosterVersion);
}
