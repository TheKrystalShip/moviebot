using System.Buffers.Binary;
using System.Text;

using TheKrystalShip.MovieBot.Ingest.Ffmpeg;
using TheKrystalShip.MovieBot.Ingest.Pipeline;
using TheKrystalShip.MovieBot.Ingest.Probe;
using Xunit;

namespace TheKrystalShip.MovieBot.Tests;

/// <summary>
/// A transcode stopped part way is carried on from a little before where each of its playlists ends:
/// the rewind, what counts as a finished track, and the resumed segments put on the stopped
/// transcode's clock and in its sequence before they are listed. The files are fragmented MP4 cut
/// down to the boxes the resume reads, so nothing here needs a card.
/// </summary>
public sealed class TrackResumeTests : IDisposable
{
    private const uint Timescale = 24000;
    private const ulong SegmentTicks = 48048; // 2.002 s, 48 frames at 23.976 fps
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "moviebot-resume-" + Guid.NewGuid().ToString("N"));

    public TrackResumeTests() => Directory.CreateDirectory(_dir);

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private static byte[] Box(string type, params byte[][] content)
    {
        var body = content.SelectMany(b => b).ToArray();
        var box = new byte[8 + body.Length];
        BinaryPrimitives.WriteUInt32BigEndian(box, (uint)box.Length);
        Encoding.ASCII.GetBytes(type).CopyTo(box, 4);
        body.CopyTo(box, 8);
        return box;
    }

    private static byte[] U32(uint value) { var b = new byte[4]; BinaryPrimitives.WriteUInt32BigEndian(b, value); return b; }

    private static byte[] U64(ulong value) { var b = new byte[8]; BinaryPrimitives.WriteUInt64BigEndian(b, value); return b; }

    private static byte[] Init(string codec) =>
        Box("moov", Box("trak", Box("mdia",
            Box("mdhd", U32(0), U32(0), U32(0), U32(Timescale), U32(0), U32(0)),
            Box("minf", Box("stbl", Box("stsd", U32(0), U32(1), Box(codec, new byte[8])))))));

    private static byte[] Segment(uint sequence, ulong decodeTime) =>
        [.. Box("styp", new byte[8]),
         .. Box("moof", Box("mfhd", U32(0), U32(sequence)), Box("traf", Box("tfdt", [1, 0, 0, 0], U64(decodeTime)))),
         .. Box("mdat", new byte[16])];

    /// <summary>A track of <paramref name="count"/> two-second segments, the stopped transcode's, as ffmpeg writes it.</summary>
    private string Track(int count, bool ended = false)
    {
        var track = Path.Combine(_dir, "v0");
        Directory.CreateDirectory(track);
        File.WriteAllBytes(Path.Combine(track, "init.mp4"), Init("avc1"));
        var lines = new List<string> { "#EXTM3U", "#EXT-X-VERSION:7", "#EXT-X-TARGETDURATION:2", "#EXT-X-MEDIA-SEQUENCE:0",
            "#EXT-X-PLAYLIST-TYPE:EVENT", "#EXT-X-MAP:URI=\"init.mp4\"" };
        for (var n = 0; n < count; n++)
        {
            File.WriteAllBytes(Path.Combine(track, $"seg{n:D5}.m4s"), Segment((uint)n + 1, (ulong)n * SegmentTicks));
            lines.Add("#EXTINF:2.002000,");
            lines.Add($"seg{n:D5}.m4s");
        }
        if (ended) lines.Add("#EXT-X-ENDLIST");
        File.WriteAllText(Path.Combine(track, "index.m3u8"), string.Join('\n', lines) + "\n");
        return track;
    }

    [Fact]
    public void A_stopped_track_is_rewound_by_whole_segments_until_ten_seconds_are_gone()
    {
        var resume = TrackResume.Read(Track(12), "", durationSeconds: 150)!;

        Assert.False(resume.Complete);
        Assert.Equal(7, resume.Segments);
        Assert.Equal(7 * SegmentTicks, resume.Next!.Value.DecodeTime);
        Assert.Equal(8u, resume.Next!.Value.Sequence);
        Assert.Equal(7 * 2.002, resume.SourceSeconds, 6);
    }

    [Fact]
    public void An_ended_playlist_is_a_finished_track_only_when_it_runs_the_films_length()
    {
        Assert.True(TrackResume.Read(Track(75, ended: true), "", durationSeconds: 150)!.Complete);

        Directory.Delete(Path.Combine(_dir, "v0"), recursive: true);
        var stopped = TrackResume.Read(Track(30, ended: true), "", durationSeconds: 150)!;
        Assert.False(stopped.Complete);
        Assert.Equal(25, stopped.Segments);
    }

    [Fact]
    public void A_track_with_no_more_than_the_rewind_in_it_is_not_carried_on()
    {
        Assert.Null(TrackResume.Read(Track(5), "", durationSeconds: 150));
    }

    [Fact]
    public void The_resumed_segments_are_put_on_the_stopped_tracks_clock_and_listed_after_the_kept_ones()
    {
        var track = Track(12);
        var resume = TrackResume.Read(track, "", durationSeconds: 150)!;
        resume.Apply();

        Assert.False(File.Exists(Path.Combine(track, "seg00007.m4s")));
        Assert.DoesNotContain("seg00007", File.ReadAllText(Path.Combine(track, "index.m3u8")));

        // What a resumed ffmpeg writes: its own clock from zero, its own sequence from one.
        File.WriteAllBytes(Path.Combine(track, resume.ResumeInit), Init("avc1"));
        for (var n = 0; n < 3; n++)
            File.WriteAllBytes(Path.Combine(track, $"seg{7 + n:D5}.m4s"), Segment((uint)n + 1, (ulong)n * SegmentTicks));
        File.WriteAllText(Path.Combine(track, TrackResume.ResumePlaylist),
            "#EXTM3U\n#EXT-X-TARGETDURATION:2\n#EXT-X-MEDIA-SEQUENCE:7\n#EXT-X-MAP:URI=\"resume-init.mp4\"\n"
            + string.Concat(Enumerable.Range(7, 3).Select(n => $"#EXTINF:2.002000,\nseg{n:D5}.m4s\n")) + "#EXT-X-ENDLIST\n");

        resume.Finish();
        resume.Publish();

        for (var n = 0; n < 10; n++)
        {
            var start = Fmp4.ReadStart(Path.Combine(track, $"seg{n:D5}.m4s"));
            Assert.Equal((ulong)n * SegmentTicks, start.DecodeTime);
            Assert.Equal((uint)n + 1, start.Sequence);
        }

        var playlist = File.ReadAllText(Path.Combine(track, "index.m3u8"));
        Assert.Equal(Enumerable.Range(0, 10).Select(n => $"seg{n:D5}.m4s"),
            playlist.Split('\n').Where(l => l.StartsWith("seg", StringComparison.Ordinal)));
        Assert.Contains("#EXT-X-MAP:URI=\"init.mp4\"", playlist);
        Assert.Contains("#EXT-X-MEDIA-SEQUENCE:0", playlist);
        Assert.EndsWith("#EXT-X-ENDLIST\n", playlist);
        Assert.False(File.Exists(Path.Combine(track, TrackResume.ResumePlaylist)));
        Assert.False(File.Exists(Path.Combine(track, resume.ResumeInit)));
    }

    [Fact]
    public void A_resumed_track_encoded_differently_is_refused()
    {
        var track = Track(12);
        var resume = TrackResume.Read(track, "", durationSeconds: 150)!;
        resume.Apply();
        File.WriteAllBytes(Path.Combine(track, resume.ResumeInit), Init("hvc1"));
        File.WriteAllBytes(Path.Combine(track, "seg00007.m4s"), Segment(1, 0));
        File.WriteAllText(Path.Combine(track, TrackResume.ResumePlaylist), "#EXTM3U\n#EXTINF:2.002000,\nseg00007.m4s\n");

        Assert.Throws<InvalidDataException>(resume.Publish);
    }

    private static ProbeStream Video() => new()
    {
        Index = 0, CodecType = "video", CodecName = "h264", PixFmt = "yuv420p", Width = 1920, Height = 800, RFrameRate = "24000/1001"
    };

    [Fact]
    public void A_resumed_rung_seeks_a_whole_source_and_decodes_up_to_the_point_in_one_still_arriving()
    {
        var resume = TrackResume.Read(Track(12), "", durationSeconds: 150)!;
        var rung = VideoLadder.For(1920, 800, new IngestOptions { SourcePath = "/s.mkv", OutputRoot = _dir })[0];

        var whole = new MainPassArguments(new IngestOptions { SourcePath = "/s.mkv", OutputRoot = _dir })
            .Video(Video(), rung, _dir, toneMap: false, VideoDecode.Cuvid, resume);
        Assert.True(whole.IndexOf("-ss") < whole.IndexOf("-i"));
        Assert.Equal("14.013", whole[whole.IndexOf("-ss") + 1]);
        Assert.Equal("7", whole[whole.IndexOf("-start_number") + 1]);
        Assert.Equal("resume-init.mp4", whole[whole.IndexOf("-hls_fmp4_init_filename") + 1]);
        Assert.EndsWith(TrackResume.ResumePlaylist, whole[^1]);

        var arriving = new MainPassArguments(new IngestOptions
            {
                SourcePath = "/s.mkv", OutputRoot = _dir, Availability = new Arriving()
            })
            .Video(Video(), rung, _dir, toneMap: false, VideoDecode.Cuvid, resume);
        Assert.True(arriving.IndexOf("-ss") > arriving.IndexOf("-i"));
        Assert.Equal("14.013", arriving[arriving.IndexOf("-ss") + 1]);
    }

    private sealed class Arriving : ISourceAvailability
    {
        public Task<long> ReadableBytesAsync(CancellationToken ct) => Task.FromResult(0L);

        public Task<bool> IsCompleteAsync(CancellationToken ct) => Task.FromResult(false);
    }
}
