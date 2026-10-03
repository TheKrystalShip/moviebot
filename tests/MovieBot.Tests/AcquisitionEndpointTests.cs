using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using TheKrystalShip.MovieBot.Core;
using Xunit;

namespace TheKrystalShip.MovieBot.Tests;

/// <summary>
/// Fetching films over HTTP, as the bot and the assistant reach it: the tracker and the title index
/// answer from the test (<see cref="AssistantHost.OneRelease"/>), and the torrent client is not there.
/// </summary>
public sealed class AcquisitionEndpointTests(SessionFixture fixture) : IClassFixture<SessionFixture>
{
    /// <summary>A room token is somebody who signed in through Discord, not somebody a service vouches for.</summary>
    [Fact]
    public async Task A_room_token_cannot_spend_anything()
    {
        var client = fixture.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", fixture.TokenFor("room"));

        using var search = await client.GetAsync("/api/releases?q=inception");
        using var wishes = await client.GetAsync("/api/wishes");

        Assert.Equal(HttpStatusCode.Forbidden, search.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, wishes.StatusCode);
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
        var client = fixture.CreateServiceClient();

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
        using var response = await fixture.CreateServiceClient().GetAsync("/api/releases/987654");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task A_download_of_something_never_offered_is_refused_before_the_client_is_asked()
    {
        using var response = await fixture.CreateServiceClient().PostAsJsonAsync("/api/downloads",
            new StartDownloadRequest { TorrentId = 987654, RequestedBy = new Actor("10", "Alice") },
            ManifestJsonContext.Default.StartDownloadRequest);

        var reply = await response.Content.ReadFromJsonAsync(ManifestJsonContext.Default.StartDownloadReply);
        Assert.Equal("noLongerOffered", reply?.Outcome);
    }

    [Fact]
    public async Task A_torrent_client_that_is_not_there_is_said_rather_than_read_as_nothing()
    {
        using var response = await fixture.CreateServiceClient().GetAsync("/api/downloads");

        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
    }

    /// <summary>
    /// The round trip a Discord announcement makes: somebody waits on a film the tracker cannot answer,
    /// the list holds them, and being told in their channel is what takes them off it.
    /// </summary>
    [Fact]
    public async Task A_wish_is_held_until_its_channel_is_told()
    {
        var client = fixture.CreateServiceClient();
        const string film = "tt0449088";   // At World's End: in the title index, with no release on the tracker.

        using var asked = await client.PostAsJsonAsync("/api/wishes",
            new AddWishRequest { Chosen = film, RequestedBy = new Actor("77", "Bea"), ChannelId = "5" },
            ManifestJsonContext.Default.AddWishRequest);
        var reply = await asked.Content.ReadFromJsonAsync(ManifestJsonContext.Default.AddWishReply);
        Assert.Equal("subscribed", reply?.Outcome);

        var held = await client.GetFromJsonAsync("/api/wishes?userId=77", ManifestJsonContext.Default.IReadOnlyListWishView);
        Assert.Equal(film, Assert.Single(held!).ImdbId);

        using var told = await client.PostAsJsonAsync($"/api/wishes/{film}/told",
            new WishesTold(["5"]), ManifestJsonContext.Default.WishesTold);
        Assert.Equal(HttpStatusCode.NoContent, told.StatusCode);

        var after = await client.GetFromJsonAsync("/api/wishes?userId=77", ManifestJsonContext.Default.IReadOnlyListWishView);
        Assert.Empty(after!);
    }
}
