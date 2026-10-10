using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using TheKrystalShip.MovieBot.Api.Data;
using TheKrystalShip.MovieBot.Api.Films;
using TheKrystalShip.MovieBot.Api.Library;
using TheKrystalShip.MovieBot.Bot.Notify;
using TheKrystalShip.MovieBot.Core;
using Xunit;

namespace TheKrystalShip.MovieBot.Tests;

/// <summary>
/// A title keeps one poster from the search that found it to the shelf that holds it: the film
/// database's, pinned to the first one shown, and the poster beside the title only where the database
/// has none.
/// </summary>
public sealed class TitlePosterTests(SessionFixture fixture) : IClassFixture<SessionFixture>, IDisposable
{
    private readonly string _pins = Path.Combine(Path.GetTempPath(), "moviebot-tests", "pins-" + Guid.NewGuid().ToString("N"));

    private CinemaDatabase Database => field ??= new CinemaDatabase(Path.Combine(_pins, "moviebot.db"));

    public void Dispose()
    {
        if (Directory.Exists(_pins)) Directory.Delete(_pins, recursive: true);
    }

    [Fact]
    public void The_first_poster_a_title_is_shown_with_is_the_one_it_keeps()
    {
        var pins = new PosterPins(Database);

        Assert.Equal("/first.jpg", pins.Pin("tt0113277", shown: null, offered: "/first.jpg"));
        Assert.Equal("/first.jpg", pins.Pin("tt0113277", shown: null, offered: "/second.jpg"));
        Assert.Equal("/first.jpg", new PosterPins(Database).Pin("tt0113277", shown: "/kept.jpg", offered: "/third.jpg"));
    }

    [Fact]
    public void A_poster_already_on_show_is_pinned_ahead_of_a_new_answer()
    {
        var pins = new PosterPins(Database);

        Assert.Equal("/kept.jpg", pins.Pin("tt0133093", shown: "/kept.jpg", offered: "/new.jpg"));
        Assert.Null(pins.Pin("tt0076759", shown: null, offered: null));
    }

    [Fact]
    public async Task A_description_fetches_the_pinned_poster_rather_than_the_one_it_names()
    {
        fixture.Services.GetRequiredService<PosterPins>().Pin("tt0113277", shown: null, offered: "/p949.jpg");

        await Signed().GetFromJsonAsync<TheKrystalShip.MovieBot.Api.Films.FilmView>("/api/films/tt0113277");

        var films = Path.Combine(fixture.StateRoot, "films");
        Assert.Equal("/p949.jpg", fixture.Services.GetRequiredService<KeptImages>().Source("films", films, $"tt0113277/{FilmStore.Poster}"));
    }

    [Fact]
    public async Task A_library_title_is_served_and_listed_by_the_film_database_s_poster()
    {
        const string id = "pinned-poster-1999";
        const string imdbId = "tt0120000";
        WriteTitle(id, imdbId, beside: [0x01, 0x02]);
        var kept = Path.Combine(fixture.StateRoot, "films", imdbId, FilmStore.Poster);
        Directory.CreateDirectory(Path.GetDirectoryName(kept)!);
        File.WriteAllBytes(kept, [0xFF, 0xD8, 0xFF, 0xD9]);
        File.SetLastWriteTimeUtc(kept, new DateTime(2026, 3, 4, 5, 6, 7, DateTimeKind.Utc));

        using var served = await fixture.CreateClient().GetAsync($"/media/{id}/poster.jpg");
        Assert.Equal(HttpStatusCode.OK, served.StatusCode);
        Assert.Equal([0xFF, 0xD8, 0xFF, 0xD9], await served.Content.ReadAsByteArrayAsync());

        fixture.Services.GetRequiredService<LibraryListing>().Refresh();
        var listed = (await fixture.CreateServiceClient().GetFromJsonAsync<List<Listed>>("/api/titles"))!.Single(t => t.Id == id);
        Assert.Equal("poster.jpg", listed.Poster);
        Assert.Equal(new DateTimeOffset(2026, 3, 4, 5, 6, 7, TimeSpan.Zero).ToUnixTimeMilliseconds().ToString(), listed.PosterVersion);
    }

    [Fact]
    public async Task The_poster_beside_a_title_is_served_where_the_database_has_none()
    {
        const string id = "beside-only-2001";
        WriteTitle(id, "tt0120001", beside: [0x01, 0x02]);

        using var served = await fixture.CreateClient().GetAsync($"/media/{id}/poster.jpg");

        Assert.Equal(HttpStatusCode.OK, served.StatusCode);
        Assert.Equal([0x01, 0x02], await served.Content.ReadAsByteArrayAsync());
    }

    [Theory]
    [InlineData("/films/tt0113277/poster.jpg", "https://movies.example", "https://movies.example/films/tt0113277/poster.jpg")]
    [InlineData("/films/tt0113277/poster.jpg", null, null)]
    [InlineData("https://m.media-amazon.com/images/M/x._V1_.jpg", null, "https://m.media-amazon.com/images/M/x._V1_.jpg")]
    public void A_wish_s_poster_reaches_discord_only_at_an_address_it_can_fetch(string served, string? publicBase, string? shown)
    {
        var view = new WishView { ImdbId = "tt0113277", Title = "Heat", Display = "Heat (1995)", PosterUrl = served };

        Assert.Equal(shown, Wish.From(view, publicBase).PosterUrl);
    }

    private HttpClient Signed()
    {
        var client = fixture.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", fixture.TokenFor("u7", "Gil"));
        return client;
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
