using System.Text.Json;
using TheKrystalShip.MovieBot.Core;
using Xunit;

namespace TheKrystalShip.MovieBot.Tests;

/// <summary>
/// What a title is, film or show, is the manifest's to say, and the listing carries it as the word
/// the site sorts its shelves by. A manifest that does not say is a film, which is every manifest
/// written before shows existed.
/// </summary>
public sealed class TitleKindTests(SessionFixture fixture) : IClassFixture<SessionFixture>
{
    private const string Unsaid =
        """
        {
          "id": "the-matrix-1999",
          "title": "The Matrix",
          "durationSeconds": 8277.0,
          "status": "ready",
          "master": "master.m3u8",
          "video": { "width": 1280, "height": 532, "sourceCodec": "h264", "sourceHdr": "sdr", "renditions": [] },
          "audio": [],
          "subtitles": []
        }
        """;

    [Fact]
    public void A_manifest_that_does_not_say_is_a_film()
    {
        Assert.Equal(TitleKind.Film, ManifestJson.Deserialize(Unsaid)!.Kind);
    }

    [Fact]
    public void A_manifest_that_says_show_is_a_show_and_stays_one_when_rewritten()
    {
        var show = ManifestJson.Deserialize(Unsaid.Replace("\"status\": \"ready\",", "\"status\": \"ready\", \"kind\": \"show\","))!;

        Assert.Equal(TitleKind.Show, show.Kind);
        Assert.Equal(TitleKind.Show, ManifestJson.Deserialize(ManifestJson.Serialize(show))!.Kind);
    }

    [Fact]
    public void An_episode_carries_its_place_in_the_show_through_a_rewrite()
    {
        var episode = ManifestJson.Deserialize(Unsaid.Replace(
            "\"status\": \"ready\",",
            "\"status\": \"ready\", \"kind\": \"show\", \"episode\": { \"season\": 2, \"number\": 4 },"))!;

        Assert.Equal(2, episode.Episode!.Season);
        Assert.Equal("S02E04", episode.Episode.Label);

        var again = ManifestJson.Deserialize(ManifestJson.Serialize(episode))!;
        Assert.Equal(4, again.Episode!.Number);
        Assert.Null(ManifestJson.Deserialize(Unsaid)!.Episode);
    }

    [Fact]
    public async Task The_listing_says_what_each_title_is()
    {
        var body = await fixture.CreateServiceClient().GetStringAsync("/api/titles");

        using var titles = JsonDocument.Parse(body);
        Assert.NotEmpty(titles.RootElement.EnumerateArray());
        Assert.All(titles.RootElement.EnumerateArray(), t => Assert.Equal("film", t.GetProperty("kind").GetString()));
    }
}
