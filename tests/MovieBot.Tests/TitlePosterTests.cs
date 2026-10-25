using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using TheKrystalShip.MovieBot.Api.Library;
using TheKrystalShip.MovieBot.Bot.Notify;
using TheKrystalShip.MovieBot.Catalogue;
using TheKrystalShip.MovieBot.Core;
using Xunit;

namespace TheKrystalShip.MovieBot.Tests;

/// <summary>
/// A title in the library is shown by the catalogue's poster, the one every list and page shows it by,
/// and by the poster beside the title only where the catalogue keeps none.
/// </summary>
public sealed class TitlePosterTests(SessionFixture fixture) : IClassFixture<SessionFixture>
{
    [Fact]
    public async Task A_library_title_is_served_and_listed_by_the_catalogue_s_poster()
    {
        const string id = "catalogued-poster-2001";
        WriteTitle(id, TestCatalogue.Fast, beside: [0x01, 0x02]);
        var kept = Path.Combine(fixture.CatalogueRoot, "images", ImageUrls.Poster, TestCatalogue.FastPoster);
        File.SetLastWriteTimeUtc(kept, new DateTime(2026, 3, 4, 5, 6, 7, DateTimeKind.Utc));

        using var served = await fixture.CreateClient().GetAsync($"/media/{id}/poster.jpg");
        Assert.Equal(HttpStatusCode.OK, served.StatusCode);
        Assert.Equal(await File.ReadAllBytesAsync(kept), await served.Content.ReadAsByteArrayAsync());

        fixture.Services.GetRequiredService<LibraryListing>().Refresh();
        var listed = (await fixture.CreateServiceClient().GetFromJsonAsync<List<Listed>>("/api/titles"))!.Single(t => t.Id == id);
        Assert.Equal("poster.jpg", listed.Poster);
        Assert.Equal(new DateTimeOffset(2026, 3, 4, 5, 6, 7, TimeSpan.Zero).ToUnixTimeMilliseconds().ToString(), listed.PosterVersion);
    }

    [Fact]
    public async Task The_poster_beside_a_title_is_served_where_the_catalogue_has_none()
    {
        const string id = "beside-only-2001";
        WriteTitle(id, "tt0120001", beside: [0x01, 0x02]);

        using var served = await fixture.CreateClient().GetAsync($"/media/{id}/poster.jpg");

        Assert.Equal(HttpStatusCode.OK, served.StatusCode);
        Assert.Equal([0x01, 0x02], await served.Content.ReadAsByteArrayAsync());
    }

    [Theory]
    [InlineData("/images/w500/heat.jpg", "https://movies.example", "https://movies.example/images/w500/heat.jpg")]
    [InlineData("/images/w500/heat.jpg", null, null)]
    [InlineData("https://m.media-amazon.com/images/M/x._V1_.jpg", null, "https://m.media-amazon.com/images/M/x._V1_.jpg")]
    public void A_wish_s_poster_reaches_discord_only_at_an_address_it_can_fetch(string served, string? publicBase, string? shown)
    {
        var card = new TitleCard { Id = "tt0113277", Kind = "film", Title = "Heat", Year = 1995, Category = "general", Poster = served };
        var view = new WantView { ImdbId = "tt0113277", Kind = "film", Title = "Heat", Card = card, Visible = true, Since = DateTimeOffset.UnixEpoch };

        Assert.Equal(shown, Wish.From(view, publicBase).PosterUrl);
    }

    private void WriteTitle(string id, string imdbId, byte[] beside)
    {
        ManifestJson.WriteAtomic(Path.Combine(fixture.MediaRoot, id, "manifest.json"), new Manifest
        {
            Id = id,
            Title = id,
            DurationSeconds = 6000,
            Status = TitleStatus.Ready,
            Poster = "poster.jpg",
            Film = new FilmIdentity { ImdbId = imdbId, Name = id },
            Video = new VideoInfo
            {
                Width = 1920, Height = 800, SourceCodec = "h264", SourceHdr = "sdr",
                Renditions = [new Rendition { Name = "800p", BitrateKbps = 9000, Width = 1920, Height = 800, Uri = "v0/index.m3u8" }],
            },
            Audio = [],
            Subtitles = [],
        });
        File.WriteAllBytes(Path.Combine(fixture.MediaRoot, id, "poster.jpg"), beside);
    }

    private sealed record Listed(string Id, string? Poster, string? PosterVersion);
}
