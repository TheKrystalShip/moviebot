using System.Net;
using Xunit;

namespace TheKrystalShip.MovieBot.Tests;

/// <summary>
/// A playlist still being written is replaced by a fresh copy renamed over it, so it is served whole
/// from the copy that was opened, never stored, and answers HEAD.
/// </summary>
public sealed class GrowingPlaylistTests(SessionFixture fixture) : IClassFixture<SessionFixture>
{
    [Fact]
    public async Task A_growing_playlist_is_served_whole_and_never_stored()
    {
        var directory = Path.Combine(fixture.MediaRoot, SessionFixture.TranscodingTitle, "v0");
        Directory.CreateDirectory(directory);
        var playlist = Path.Combine(directory, "index.m3u8");
        var text = "#EXTM3U\n#EXT-X-PLAYLIST-TYPE:EVENT\n#EXTINF:2.000000,\nseg00000.m4s\n";
        await File.WriteAllTextAsync(playlist, text);

        var client = fixture.CreatePersonClient("grower");
        using var response = await client.GetAsync($"/media/{SessionFixture.TranscodingTitle}/v0/index.m3u8");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(text, await response.Content.ReadAsStringAsync());
        Assert.Equal(text.Length, response.Content.Headers.ContentLength);
        Assert.Equal("application/vnd.apple.mpegurl", response.Content.Headers.ContentType!.MediaType);
        Assert.True(response.Headers.CacheControl!.NoStore);

        using var head = await client.SendAsync(new HttpRequestMessage(HttpMethod.Head,
            $"/media/{SessionFixture.TranscodingTitle}/v0/index.m3u8"));
        Assert.Equal(HttpStatusCode.OK, head.StatusCode);
        Assert.Equal(text.Length, head.Content.Headers.ContentLength);
    }
}
