using TheKrystalShip.MovieBot.Core;
using Xunit;

namespace TheKrystalShip.MovieBot.Tests;

/// <summary>
/// A rung encoded at a constant quality weighs far less than the ceiling it was given, and a player
/// picks its rung and sizes its buffer by the rate the master states. The master states what the
/// segments measure once they are written.
/// </summary>
public sealed class MeasuredRatesTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "moviebot-rates", Guid.NewGuid().ToString("n"));

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    /// <summary>A media playlist under <paramref name="track"/> of two-second segments of these sizes.</summary>
    private void Track(string track, int[] bytes, bool ended = true)
    {
        Directory.CreateDirectory(Path.Combine(_dir, track));
        var lines = new List<string> { "#EXTM3U", "#EXT-X-TARGETDURATION:2", "#EXT-X-MAP:URI=\"init.mp4\"" };
        for (var i = 0; i < bytes.Length; i++)
        {
            var name = $"seg{i:D5}.m4s";
            File.WriteAllBytes(Path.Combine(_dir, track, name), new byte[bytes[i]]);
            lines.Add("#EXTINF:2.000000,");
            lines.Add(name);
        }
        if (ended) lines.Add("#EXT-X-ENDLIST");
        File.WriteAllLines(Path.Combine(_dir, track, "index.m3u8"), lines);
    }

    private static Manifest ManifestWith(params Rendition[] renditions) => new()
    {
        Id = "a-film",
        Title = "a-film",
        DurationSeconds = 6,
        Status = TitleStatus.Ready,
        Video = new VideoInfo { Width = 1280, Height = 720, SourceCodec = "h264", SourceHdr = "sdr", Renditions = renditions },
        Audio = [new AudioTrack { Id = "a0", Kind = TrackKind.Feature, Language = "eng", Label = "English", Channels = 2, Default = true, Uri = "a0/index.m3u8" }],
        Subtitles = []
    };

    private static Rendition Rung(string name, int ceilingKbps) =>
        new() { Name = name, BitrateKbps = ceilingKbps, Width = 1280, Height = 720, Uri = $"{name}/index.m3u8" };

    [Fact]
    public void A_track_is_measured_over_the_film_and_at_its_heaviest_segment()
    {
        // 250, 500 and 750 kB over two seconds each: 1000, 2000 and 3000 kbps.
        Track("v0", [250_000, 500_000, 750_000]);
        var rate = MeasuredRates.Of(Path.Combine(_dir, "v0", "index.m3u8"));
        Assert.Equal(new MeasuredRate(2000, 3000), rate);
    }

    [Fact]
    public void A_playlist_still_being_written_or_missing_a_segment_is_not_measured()
    {
        Track("v0", [250_000, 500_000], ended: false);
        Assert.Null(MeasuredRates.Of(Path.Combine(_dir, "v0", "index.m3u8")));

        Track("v1", [250_000, 500_000]);
        File.Delete(Path.Combine(_dir, "v1", "seg00001.m4s"));
        Assert.Null(MeasuredRates.Of(Path.Combine(_dir, "v1", "index.m3u8")));
        Assert.Null(MeasuredRates.Of(Path.Combine(_dir, "nowhere", "index.m3u8")));
    }

    [Fact]
    public void The_master_states_the_measured_peak_and_average_with_the_audio()
    {
        Track("v0", [250_000, 500_000, 750_000]);
        Track("a0", [32_000, 32_000, 32_000]);
        var manifest = ManifestWith(Rung("v0", 9000));

        Assert.True(MeasuredRates.Measure(_dir, manifest));
        Assert.False(MeasuredRates.Owed(manifest));

        var master = MasterPlaylist.Build(manifest);
        // 3000 + 128 at the peak, 2000 + 128 on average; never the encoder's 9000.
        Assert.Contains("BANDWIDTH=3128000,AVERAGE-BANDWIDTH=2128000,RESOLUTION=1280x720", master);
    }

    [Fact]
    public void A_rung_not_yet_measured_states_its_ceiling()
    {
        var manifest = ManifestWith(Rung("v0", 9000));
        Assert.True(MeasuredRates.Owed(manifest));

        var master = MasterPlaylist.Build(manifest);
        Assert.Contains("BANDWIDTH=9000000,RESOLUTION=1280x720", master);
        Assert.DoesNotContain("AVERAGE-BANDWIDTH", master);
    }

    [Fact]
    public void A_measured_rate_survives_the_manifest_round_trip()
    {
        var manifest = ManifestWith(Rung("v0", 9000));
        manifest.Video.Renditions[0].Rate = new MeasuredRate(2330, 5010);
        var read = ManifestJson.Deserialize(ManifestJson.Serialize(manifest))!;
        Assert.Equal(new MeasuredRate(2330, 5010), read.Video.Renditions[0].Rate);
        Assert.Null(read.Audio[0].Rate);
    }
}
