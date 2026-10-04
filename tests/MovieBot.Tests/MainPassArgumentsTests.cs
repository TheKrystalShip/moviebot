using TheKrystalShip.MovieBot.Ingest.Pipeline;
using TheKrystalShip.MovieBot.Ingest.Probe;
using Xunit;

namespace TheKrystalShip.MovieBot.Tests;

/// <summary>
/// The main pass is several processes, and what makes their outputs one film is that every one
/// of them is told the same things: the same keyframes, the same segment length, a size that is
/// the one the manifest advertises. None of that is observable until a player switches rungs, so
/// it is held here against the command lines themselves.
/// </summary>
public sealed class MainPassArgumentsTests
{
    private static readonly IngestOptions Options = new()
    {
        SourcePath = "/media/source.mkv",
        OutputRoot = "/tmp"
    };

    private static ProbeStream Stream(string codec, string pixelFormat, int width = 1920, int height = 1040) =>
        new()
        {
            Index = 0,
            CodecType = "video",
            CodecName = codec,
            PixFmt = pixelFormat,
            Width = width,
            Height = height,
            RFrameRate = "24000/1001"
        };

    private static string Value(IReadOnlyList<string> args, string flag)
    {
        var at = args.ToList().IndexOf(flag);
        Assert.True(at >= 0 && at + 1 < args.Count, $"{flag} is not in the arguments");
        return args[at + 1];
    }

    [Theory]
    [InlineData("h264", "yuv420p", "h264_cuvid")]
    [InlineData("hevc", "yuv420p10le", "hevc_cuvid")]
    [InlineData("hevc", "yuv420p", "hevc_cuvid")]
    [InlineData("h264", "yuv420p10le", null)]
    [InlineData("h264", "yuv422p", null)]
    [InlineData("hevc", "yuv444p", null)]
    [InlineData("av1", "yuv420p", null)]
    public void The_decoder_is_the_one_the_card_can_run(string codec, string pixelFormat, string? expected)
    {
        Assert.Equal(expected, MainPassArguments.CuvidDecoder(Stream(codec, pixelFormat)));
    }

    [Fact]
    public void High_10_h264_is_not_offered_to_the_card()
    {
        Assert.Equal(VideoDecode.Software, MainPassArguments.DecodeFor(Stream("h264", "yuv420p10le")));
        Assert.Equal(VideoDecode.Cuvid, MainPassArguments.DecodeFor(Stream("h264", "yuv420p")));
        Assert.Equal(VideoDecode.Hwaccel, MainPassArguments.DecodeFor(Stream("mpeg2video", "yuv420p")));
    }

    [Fact]
    public void The_step_down_is_scaled_by_the_decoder_to_the_size_the_ladder_advertises()
    {
        var video = Stream("h264", "yuv420p");
        var ladder = VideoLadder.For(1920, 1040, Options);
        var builder = new MainPassArguments(Options);

        var args = builder.Video(video, ladder[1], "/out", toneMap: false, VideoDecode.Cuvid);

        Assert.Equal("h264_cuvid", Value(args, "-c:v"));
        Assert.Equal($"{ladder[1].Width}x{ladder[1].Height}", Value(args, "-resize"));
        Assert.Equal("format=yuv420p", Value(args, "-vf"));
        Assert.DoesNotContain("-hwaccel", args);
    }

    [Fact]
    public void The_cuvid_decoder_holds_enough_surfaces_for_a_source_with_b_frames()
    {
        var video = Stream("h264", "yuv420p");
        var ladder = VideoLadder.For(1920, 1040, Options);
        var builder = new MainPassArguments(Options);

        foreach (var rung in ladder)
        {
            var args = builder.Video(video, rung, "/out", toneMap: false, VideoDecode.Cuvid);

            // The option belongs to the decoder, so it has to come before the input it opens.
            Assert.Equal("32", Value(args, "-surfaces"));
            Assert.True(args.IndexOf("-surfaces") < args.IndexOf("-i"));
        }
    }

    [Fact]
    public void The_top_rung_is_decoded_without_a_resize()
    {
        var video = Stream("h264", "yuv420p");
        var ladder = VideoLadder.For(1920, 1040, Options);

        var args = new MainPassArguments(Options).Video(video, ladder[0], "/out", false, VideoDecode.Cuvid);

        Assert.DoesNotContain("-resize", args);
        Assert.Equal("format=yuv420p", Value(args, "-vf"));
    }

    [Fact]
    public void The_fallback_scales_on_the_cpu_behind_the_generic_hardware_decode()
    {
        var video = Stream("h264", "yuv420p");
        var ladder = VideoLadder.For(1920, 1040, Options);

        var args = new MainPassArguments(Options).Video(video, ladder[1], "/out", false, VideoDecode.Hwaccel);

        Assert.Equal("cuda", Value(args, "-hwaccel"));
        Assert.DoesNotContain("-resize", args);
        Assert.DoesNotContain("-c:v h264_cuvid", string.Join(' ', args));
        Assert.Equal("format=yuv420p,scale=1280:-2", Value(args, "-vf"));
    }

    [Fact]
    public void A_source_the_card_cannot_decode_is_scaled_on_the_cpu_without_a_hardware_attempt()
    {
        var video = Stream("h264", "yuv420p10le");
        var ladder = VideoLadder.For(1920, 1040, Options);

        var args = new MainPassArguments(Options).Video(video, ladder[1], "/out", false, VideoDecode.Software);

        Assert.DoesNotContain("-hwaccel", args);
        Assert.Equal("format=yuv420p,scale=1280:-2", Value(args, "-vf"));
    }

    [Fact]
    public void Tone_mapping_runs_after_the_decoder_has_scaled_and_not_again_on_the_cpu()
    {
        var video = Stream("hevc", "yuv420p10le", 1920, 804);
        var ladder = VideoLadder.For(1920, 804, Options);

        var args = new MainPassArguments(Options).Video(video, ladder[1], "/out", toneMap: true, VideoDecode.Cuvid);

        Assert.Equal("opencl=ocl", Value(args, "-init_hw_device"));
        Assert.Equal("hevc_cuvid", Value(args, "-c:v"));
        Assert.Equal("1280x536", Value(args, "-resize"));

        var filter = Value(args, "-vf");
        Assert.StartsWith("format=p010le,hwupload,tonemap_opencl=", filter);
        Assert.DoesNotContain("scale=", filter);
    }

    [Fact]
    public void Every_rung_is_cut_on_the_same_keyframes_and_segments()
    {
        var video = Stream("h264", "yuv420p");
        var ladder = VideoLadder.For(1920, 1040, Options);
        var builder = new MainPassArguments(Options);

        var top = builder.Video(video, ladder[0], "/out", false, VideoDecode.Cuvid);
        var step = builder.Video(video, ladder[1], "/out", false, VideoDecode.Cuvid);

        foreach (var flag in new[] { "-force_key_frames", "-g", "-keyint_min", "-sc_threshold", "-hls_time" })
            Assert.Equal(Value(top, flag), Value(step, flag));

        Assert.Equal("expr:gte(t,n_forced*2)", Value(top, "-force_key_frames"));
        Assert.Equal("/out/v0/index.m3u8", top[^1]);
        Assert.Equal("/out/v1/index.m3u8", step[^1]);
    }

    [Fact]
    public void Audio_is_one_process_with_a_demuxer_for_each_rendition()
    {
        var audio = new List<ProbeStream>
        {
            Stream("dts", "yuv420p") with { Index = 1, CodecType = "audio" },
            Stream("ac3", "yuv420p") with
            {
                Index = 2, CodecType = "audio", Disposition = new() { ["comment"] = 1 }
            }
        };

        var args = new MainPassArguments(Options).Audio(audio, [0, 1], "/out");

        Assert.Equal(2, args.Count(a => a == "-i"));
        Assert.DoesNotContain("-hwaccel", args);
        Assert.DoesNotContain("h264_nvenc", args);
        Assert.Equal(["0:1", "1:2"], args.Where((_, i) => i > 0 && args[i - 1] == "-map"));
        Assert.Contains("/out/a0/index.m3u8", args);
        Assert.Contains("/out/a1/index.m3u8", args);
    }

    [Fact]
    public void An_audio_process_writes_the_renditions_it_was_given_under_their_own_names()
    {
        var audio = Enumerable.Range(1, 5)
            .Select(i => Stream("eac3", "yuv420p") with { Index = i, CodecType = "audio" })
            .ToList();

        var args = new MainPassArguments(Options).Audio(audio, [1, 3], "/out");

        Assert.Equal(2, args.Count(a => a == "-i"));
        Assert.Equal(["0:2", "1:4"], args.Where((_, i) => i > 0 && args[i - 1] == "-map"));
        Assert.Contains("/out/a1/index.m3u8", args);
        Assert.Contains("/out/a3/index.m3u8", args);
        Assert.DoesNotContain("/out/a0/index.m3u8", args);
    }

    [Theory]
    [InlineData(5, 1, "01234")]
    [InlineData(5, 2, "024|13")]
    [InlineData(5, 5, "0|1|2|3|4")]
    [InlineData(2, 5, "0|1")]
    [InlineData(1, 2, "0")]
    public void Every_rendition_is_in_exactly_one_audio_process(int tracks, int processes, string expected)
    {
        var groups = MainPassArguments.AudioGroups(tracks, processes);

        Assert.Equal(expected, string.Join('|', groups.Select(g => string.Concat(g))));
    }
}
