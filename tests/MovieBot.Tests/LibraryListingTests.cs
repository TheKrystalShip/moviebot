using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using TheKrystalShip.MovieBot.Api.Library;
using TheKrystalShip.MovieBot.Core;
using Xunit;

namespace TheKrystalShip.MovieBot.Tests;

/// <summary>
/// The shelf is listed from memory, rebuilt off the request path: a title written to disk appears once
/// the listing is next built, and a request in between is answered from the list already held.
/// </summary>
public sealed class LibraryListingTests(SessionFixture fixture) : IClassFixture<SessionFixture>
{
    private const string Arrival = "late-arrival-2026";

    [Fact]
    public async Task A_title_written_to_disk_is_listed_once_the_listing_is_rebuilt()
    {
        var client = fixture.CreateServiceClient();
        await client.GetFromJsonAsync<List<Listed>>("/api/titles");

        ManifestJson.WriteAtomic(Path.Combine(fixture.MediaRoot, Arrival, "manifest.json"), new Manifest
        {
            Id = Arrival,
            Title = "Late Arrival",
            DurationSeconds = 6000,
            Status = TitleStatus.Ready,
            Video = new VideoInfo
            {
                Width = 1920, Height = 800, SourceCodec = "h264", SourceHdr = "sdr",
                Renditions = [new Rendition { Name = "800p", BitrateKbps = 9000, Width = 1920, Height = 800, Uri = "v0/index.m3u8" }],
            },
            Audio = [],
            Subtitles = [],
        });

        var held = await client.GetFromJsonAsync<List<Listed>>("/api/titles");
        Assert.DoesNotContain(held!, t => t.Id == Arrival);

        fixture.Services.GetRequiredService<LibraryListing>().Refresh();

        var rebuilt = await client.GetFromJsonAsync<List<Listed>>("/api/titles");
        Assert.Contains(rebuilt!, t => t.Id == Arrival);
    }

    private sealed record Listed(string Id);
}
