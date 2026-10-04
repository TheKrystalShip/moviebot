using TheKrystalShip.MovieBot.Core;
using TheKrystalShip.MovieBot.Ingest.Pipeline;
using Xunit;

namespace TheKrystalShip.MovieBot.Tests;

/// <summary>
/// Which films are encoded again, and what the encode writes. A film goes over once: it is owed
/// while a rung records no quality or its ladder has a rung it lacks, and what the pass writes
/// says it is done. The segments of the pass are named apart, because a segment is cached as
/// immutable and the old ones are still in caches under their own names.
/// </summary>
public sealed class ReencoderTests
{
    private static Manifest Film(TitleStatus status, int width, int height, params Rendition[] renditions) => new()
    {
        Id = "film",
        Title = "Film",
        DurationSeconds = 600,
        Status = status,
        Video = new VideoInfo
        {
            Width = width, Height = height, SourceCodec = "h264", SourceHdr = "sdr", Renditions = renditions
        },
        Audio = [],
        Subtitles = []
    };

    private static Rendition Rung(string directory, int cq) =>
        new() { Name = directory, BitrateKbps = 9000, Uri = $"{directory}/index.m3u8", Cq = cq };

    [Fact]
    public void A_film_whose_rungs_record_no_quality_is_owed()
    {
        Assert.True(Reencoder.IsOwed(Film(TitleStatus.Ready, 1920, 1080, Rung("v0", 0), Rung("v1", 0))));
    }

    [Fact]
    public void A_film_with_the_top_rung_alone_is_owed_the_step_down()
    {
        Assert.True(Reencoder.IsOwed(Film(TitleStatus.Ready, 1920, 1080, Rung("v0", Reencoder.LibraryCq))));
    }

    [Fact]
    public void A_film_already_gone_over_or_made_at_the_new_quality_is_not_owed()
    {
        Assert.False(Reencoder.IsOwed(Film(TitleStatus.Ready, 1920, 1080,
            Rung("v0", Reencoder.LibraryCq), Rung("v1", Reencoder.LibraryCq))));
        Assert.False(Reencoder.IsOwed(Film(TitleStatus.Ready, 1920, 1080,
            Rung("v0", IngestOptions.DefaultCq), Rung("v1", IngestOptions.DefaultCq))));
    }

    [Fact]
    public void A_narrow_film_with_one_recorded_rung_is_not_owed_a_step_down()
    {
        Assert.False(Reencoder.IsOwed(Film(TitleStatus.Ready, 1280, 720, Rung("v0", IngestOptions.DefaultCq))));
    }

    [Fact]
    public void A_film_still_being_made_is_not_owed()
    {
        Assert.False(Reencoder.IsOwed(Film(TitleStatus.Transcoding, 1920, 1080, Rung("v0", 0))));
    }

    [Fact]
    public void A_prefix_renames_the_segments_and_the_initialisation_file()
    {
        var options = new IngestOptions { SourcePath = "/in.m3u8", OutputRoot = "/out", SegmentPrefix = "q23-" };
        var args = new MainPassArguments(options).HlsOutput("/out/v0").ToList();

        Assert.Equal("q23-init.mp4", args[args.IndexOf("-hls_fmp4_init_filename") + 1]);
        Assert.Equal("/out/v0/q23-seg%05d.m4s", args[args.IndexOf("-hls_segment_filename") + 1]);
    }

    [Fact]
    public void A_rung_without_a_recorded_quality_writes_none()
    {
        var json = ManifestJson.Serialize(Film(TitleStatus.Ready, 1920, 1080, Rung("v0", 0), Rung("v1", 23)));

        Assert.Single(System.Text.RegularExpressions.Regex.Matches(json, "\"cq\""));
        Assert.Equal(23, ManifestJson.Deserialize(json)!.Video.Renditions[1].Cq);
    }
}
