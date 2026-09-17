using TheKrystalShip.MovieBot.Core;
using Xunit;

namespace TheKrystalShip.MovieBot.Tests;

/// <summary>
/// A film with one rung states no size on it, because that rung is the source's own size. The
/// whole library is written that way, so a manifest shaped like this has to load, describe itself
/// correctly, and survive being rewritten.
/// </summary>
public class SingleRungManifestTests
{
    private const string WithoutRenditionSize =
        """
        {
          "id": "the-matrix-1999",
          "title": "The Matrix",
          "durationSeconds": 8277.0,
          "status": "ready",
          "master": "master.m3u8",
          "video": {
            "width": 1280, "height": 532, "sourceCodec": "h264", "sourceHdr": "sdr",
            "renditions": [{ "name": "532p", "bitrateKbps": 9000, "uri": "v0/index.m3u8" }]
          },
          "audio": [],
          "subtitles": []
        }
        """;

    [Fact]
    public void A_manifest_whose_rung_states_no_size_loads()
    {
        var manifest = ManifestJson.Deserialize(WithoutRenditionSize);

        Assert.NotNull(manifest);
        Assert.Single(manifest.Video.Renditions);
    }

    /// <summary>
    /// A single rung is the source's size. Nothing has to be rewritten on disk for the master
    /// playlist to say so.
    /// </summary>
    [Fact]
    public void Its_one_rung_is_described_with_the_sources_size()
    {
        var manifest = ManifestJson.Deserialize(WithoutRenditionSize)!;

        Assert.Contains("RESOLUTION=1280x532", MasterPlaylist.Build(manifest));
    }

    /// <summary>
    /// A manifest is rewritten in place whenever a film gains a subtitle, by a pass that knows
    /// nothing about the film's dimensions. A rung that states no size has to come back out the
    /// same way: a zero on disk reads as a resolution the film has.
    /// </summary>
    [Fact]
    public void Rewriting_one_does_not_invent_a_size_for_its_rung()
    {
        var manifest = ManifestJson.Deserialize(WithoutRenditionSize)!;
        var written = ManifestJson.Serialize(manifest);

        Assert.DoesNotContain("\"width\": 0", written);
        Assert.DoesNotContain("\"height\": 0", written);

        // And it survives the round trip unchanged.
        var again = ManifestJson.Deserialize(written)!;
        Assert.Equal(0, again.Video.Renditions[0].Width);
        Assert.Contains("RESOLUTION=1280x532", MasterPlaylist.Build(again));
    }
}
