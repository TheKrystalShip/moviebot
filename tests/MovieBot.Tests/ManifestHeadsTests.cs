using TheKrystalShip.MovieBot.Api.Transcode;
using TheKrystalShip.MovieBot.Core;
using Xunit;

namespace TheKrystalShip.MovieBot.Tests;

/// <summary>The planner reads each output's head from the manifest the ingest writes.</summary>
public sealed class ManifestHeadsTests
{
    private readonly ManifestHeads _heads = new();

    [Fact]
    public void Each_rung_and_the_audio_are_read_at_their_own_head()
    {
        var heads = _heads.Of(Title(TitleStatus.Transcoding, overall: 100, rungHeads: [300, 100], audio: 400));

        Assert.Equal(300, heads.Rungs["v0"]);
        Assert.Equal(100, heads.Rungs["v1"]);
        Assert.Equal(400, heads.Audio);
    }

    [Fact]
    public void An_output_with_no_head_of_its_own_is_at_the_overall_head()
    {
        var heads = _heads.Of(Title(TitleStatus.Transcoding, overall: 100, rungHeads: [300, null], audio: null));

        Assert.Equal(300, heads.Rungs["v0"]);
        Assert.Equal(100, heads.Rungs["v1"]);
        Assert.Equal(100, heads.Audio);
    }

    [Fact]
    public void A_finished_title_is_whole_whatever_its_manifest_last_recorded()
    {
        var heads = _heads.Of(Title(TitleStatus.Ready, overall: 100, rungHeads: [300, 100], audio: 400));

        Assert.All(heads.Rungs.Values, head => Assert.Equal(Duration, head));
        Assert.Equal(Duration, heads.Audio);
    }

    private const double Duration = 6000;

    private static Manifest Title(TitleStatus status, double overall, double?[] rungHeads, double? audio) => new()
    {
        Id = "film",
        Title = "film",
        DurationSeconds = Duration,
        Status = status,
        HeadSeconds = overall,
        AudioHeadSeconds = audio,
        Video = new VideoInfo
        {
            Width = 1920,
            Height = 1080,
            SourceCodec = "h264",
            SourceHdr = "sdr",
            Renditions =
            [
                .. rungHeads.Select((head, i) => new Rendition
                {
                    Name = i == 0 ? "1080p" : "720p", BitrateKbps = i == 0 ? 9000 : 4000,
                    Width = i == 0 ? 1920 : 1280, Height = i == 0 ? 1080 : 720,
                    Uri = $"v{i}/index.m3u8", HeadSeconds = head
                })
            ]
        },
        Audio = [],
        Subtitles = []
    };
}
