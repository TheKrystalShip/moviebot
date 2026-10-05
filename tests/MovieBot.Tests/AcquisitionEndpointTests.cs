using System.Net;
using System.Net.Http.Json;
using TheKrystalShip.MovieBot.Api.Auth;
using TheKrystalShip.MovieBot.Core;
using Xunit;

namespace TheKrystalShip.MovieBot.Tests;

/// <summary>
/// Fetching films over HTTP, as the site, the bot and the assistant reach it: the tracker and the title
/// index answer from the test (<see cref="AssistantHost.OneRelease"/>), and the torrent client is not there.
/// </summary>
public sealed class AcquisitionEndpointTests(SessionFixture fixture) : IClassFixture<SessionFixture>
{
    [Fact]
    public async Task Somebody_who_may_only_watch_spends_nothing()
    {
        var viewer = fixture.CreatePersonClient("31", "Vi", [CinemaActions.Watch]);

        using var search = await viewer.GetAsync("/api/releases?q=inception");
        using var start = await viewer.PostAsJsonAsync("/api/downloads", new StartDownloadRequest { TorrentId = 41 },
            ManifestJsonContext.Default.StartDownloadRequest);
        using var wishes = await viewer.GetAsync("/api/wishes");

        Assert.Equal(HttpStatusCode.Forbidden, search.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, start.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, wishes.StatusCode);
    }

    [Fact]
    public async Task Somebody_who_may_download_searches_and_starts_one()
    {
        var downloader = fixture.CreatePersonClient("32", "Di", [CinemaActions.Watch, CinemaActions.Download]);

        var ranked = await downloader.GetFromJsonAsync("/api/releases?q=inception", ManifestJsonContext.Default.RankedOffers);
        Assert.Equal(41, Assert.Single(ranked!.Candidates).TorrentId);

        // Offered, and then the torrent client, which is not there, is what answers.
        using var start = await downloader.PostAsJsonAsync("/api/downloads", new StartDownloadRequest { TorrentId = 41 },
            ManifestJsonContext.Default.StartDownloadRequest);
        Assert.Equal(HttpStatusCode.OK, start.StatusCode);
        Assert.NotEqual("noLongerOffered", (await start.Content.ReadFromJsonAsync(ManifestJsonContext.Default.StartDownloadReply))?.Outcome);
    }

    [Fact]
    public async Task The_service_key_starts_no_download()
    {
        using var response = await fixture.CreateServiceClient().PostAsJsonAsync("/api/downloads",
            new StartDownloadRequest { TorrentId = 41 }, ManifestJsonContext.Default.StartDownloadRequest);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Nobody_without_a_token_or_the_key_gets_in()
    {
        using var response = await fixture.CreateClient().GetAsync("/api/releases?q=inception");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task A_release_a_search_offered_can_then_be_picked_by_its_id()
    {
        var client = fixture.CreatePersonClient("33", "Ed");

        var ranked = await client.GetFromJsonAsync("/api/releases?q=inception", ManifestJsonContext.Default.RankedOffers);
        var offer = Assert.Single(ranked!.Candidates);
        Assert.Equal(41, offer.TorrentId);
        Assert.Equal("Inception (2010)", offer.Display);

        var picked = await client.GetFromJsonAsync($"/api/releases/{offer.TorrentId}", ManifestJsonContext.Default.ReleaseOffer);
        Assert.Equal(offer.ReleaseName, picked?.ReleaseName);
    }

    [Fact]
    public async Task An_id_no_search_offered_is_not_a_release()
    {
        using var response = await fixture.CreatePersonClient("34", "Flo").GetAsync("/api/releases/987654");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task A_download_of_something_never_offered_is_refused_before_the_client_is_asked()
    {
        using var response = await fixture.CreatePersonClient("35", "Gus").PostAsJsonAsync("/api/downloads",
            new StartDownloadRequest { TorrentId = 987654 }, ManifestJsonContext.Default.StartDownloadRequest);

        var reply = await response.Content.ReadFromJsonAsync(ManifestJsonContext.Default.StartDownloadReply);
        Assert.Equal("noLongerOffered", reply?.Outcome);
    }

    [Fact]
    public async Task A_torrent_client_that_is_not_there_is_said_rather_than_read_as_nothing()
    {
        using var response = await fixture.CreateServiceClient().GetAsync("/api/downloads");

        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
    }

    /// <summary>A wish is the person's the token names, whoever carried it, and is theirs to read and cancel.</summary>
    [Fact]
    public async Task A_wish_is_held_for_the_person_who_made_it()
    {
        const string film = "tt0449088";   // At World's End: in the title index, with no release on the tracker.
        var bea = fixture.CreatePersonClient("77", "Bea", [CinemaActions.Wish]);

        using var asked = await bea.PostAsJsonAsync("/api/wishes", new AddWishRequest { Chosen = film },
            ManifestJsonContext.Default.AddWishRequest);
        var reply = await asked.Content.ReadFromJsonAsync(ManifestJsonContext.Default.AddWishReply);
        Assert.Equal("subscribed", reply?.Outcome);
        Assert.Equal("77", Assert.Single(reply!.Wish!.Subscribers).UserId);

        var held = await bea.GetFromJsonAsync("/api/wishes", ManifestJsonContext.Default.IReadOnlyListWishView);
        Assert.Equal(film, Assert.Single(held!).ImdbId);
        Assert.Null(held![0].Release);

        var others = await fixture.CreatePersonClient("78", "Cy").GetFromJsonAsync("/api/wishes", ManifestJsonContext.Default.IReadOnlyListWishView);
        Assert.Empty(others!);

        using var cancelled = await bea.DeleteAsync($"/api/wishes/{film}");
        Assert.Equal("cancelled", (await cancelled.Content.ReadFromJsonAsync(ManifestJsonContext.Default.CancelWishReply))?.Outcome);
        Assert.Empty((await bea.GetFromJsonAsync("/api/wishes", ManifestJsonContext.Default.IReadOnlyListWishView))!);
    }
}
