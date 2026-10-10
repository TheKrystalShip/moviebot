using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;
using TheKrystalShip.MovieBot.Api.Sessions;
using TheKrystalShip.MovieBot.Core;
using Xunit;

namespace TheKrystalShip.MovieBot.Tests;

/// <summary>
/// A transcoding title whose rungs advance at their own pace is playable as far as its best rung,
/// and what a viewer says about their own quality stays with the server and the planner.
/// </summary>
public sealed class RungHeadsTests(SessionFixture fixture) : IClassFixture<SessionFixture>
{
    [Fact]
    public async Task A_seek_is_judged_against_the_best_rung_and_not_the_laggard()
    {
        var title = WriteTitle(overall: 100, rungs: [(1080, 100), (720, 900)], audio: 1000);
        await using var alice = await fixture.ConnectAsync(Room(), "u1", "Alice");
        await alice.InvokeAsync<SessionStatePush>("LoadTitle", title, null);

        var within = await alice.InvokeAsync<SessionStatePush>("Seek", 500.0);
        Assert.Equal(500.0, within.State.PositionSeconds, 3);

        var clamped = new TaskCompletionSource<SeekClamped>(TaskCreationOptions.RunContinuationsAsynchronously);
        alice.On<SeekClamped>("SeekClamped", clamp => clamped.TrySetResult(clamp));
        await alice.InvokeAsync<SessionStatePush>("Seek", 5000.0);

        var notice = await clamped.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(900.0, notice.HeadSeconds, 3);
        Assert.True(notice.GrantedSeconds < 900.0);
    }

    [Fact]
    public async Task The_audio_head_holds_the_best_rung_back()
    {
        var title = WriteTitle(overall: 100, rungs: [(1080, 100), (720, 900)], audio: 400);
        await using var alice = await fixture.ConnectAsync(Room(), "u1", "Alice");
        await alice.InvokeAsync<SessionStatePush>("LoadTitle", title, null);

        var clamped = new TaskCompletionSource<SeekClamped>(TaskCreationOptions.RunContinuationsAsynchronously);
        alice.On<SeekClamped>("SeekClamped", clamp => clamped.TrySetResult(clamp));
        await alice.InvokeAsync<SessionStatePush>("Seek", 5000.0);

        Assert.Equal(400.0, (await clamped.Task.WaitAsync(TimeSpan.FromSeconds(5))).HeadSeconds, 3);
    }

    [Fact]
    public async Task The_room_state_carries_each_rung_and_keeps_the_overall_head()
    {
        var title = WriteTitle(overall: 100, rungs: [(1080, 100), (720, 900)], audio: 1000);
        await using var alice = await fixture.ConnectAsync(Room(), "u1", "Alice");
        var state = (await alice.InvokeAsync<SessionStatePush>("LoadTitle", title, null)).State;

        Assert.Equal(100.0, state.TranscodeHead);
        Assert.Equal(1000.0, state.AudioHead);
        Assert.Equal([(1080, 100.0), (720, 900.0)], state.RungHeads!.Select(r => (r.Height, r.Seconds)));
    }

    [Fact]
    public async Task A_manifest_that_records_one_head_has_no_rung_heads()
    {
        await using var alice = await fixture.ConnectAsync(Room(), "u1", "Alice");
        var cooking = (await alice.InvokeAsync<SessionStatePush>("LoadTitle", SessionFixture.TranscodingTitle, null)).State;
        Assert.Equal(SessionFixture.TranscodingHead, cooking.TranscodeHead);
        Assert.Null(cooking.RungHeads);
        Assert.Null(cooking.AudioHead);

        var ready = (await alice.InvokeAsync<SessionStatePush>("LoadTitle", SessionFixture.ReadyTitle, null)).State;
        Assert.Null(ready.RungHeads);
    }

    [Fact]
    public async Task A_viewers_report_reaches_the_planner_and_no_other_client()
    {
        var title = WriteTitle(overall: 100, rungs: [(1080, 100), (720, 900)], audio: 1000);
        var room = Room();
        await using var alice = await fixture.ConnectAsync(room, "u1", "Alice");
        await using var bob = await fixture.ConnectAsync(room, "u2", "Bob");

        // Everything a room says to Bob is counted, from before the film goes in until it settles.
        var heard = 0;
        foreach (var method in new[] { "StateChanged", "ParticipantsChanged", "SeekClamped" })
            bob.On<object>(method, _ => Interlocked.Increment(ref heard));

        await alice.InvokeAsync<SessionStatePush>("LoadTitle", title, null);
        await Task.Delay(300);
        Interlocked.Exchange(ref heard, 0);

        await alice.InvokeAsync("ReportQuality", 720, 480);
        await Task.Delay(300);

        Assert.Equal(0, heard);

        // A viewer who has said nothing wants the tallest rung, and one who has said so wants theirs.
        var quality = fixture.Services.GetRequiredService<ViewerQuality>();
        Assert.Equal([480, 1080], quality.WantedHeights(room).Order());

        // The room's own state, as a viewer joining now is handed it, carries none of it.
        await using var carol = await fixture.ConnectAsync(room, "u3", "Carol");
        var joined = await carol.InvokeAsync<JoinReply>("Join", room);
        var wire = System.Text.Json.JsonSerializer.Serialize(joined, ManifestJsonContext.Default.JoinReply);
        Assert.DoesNotContain("480", wire);
        Assert.DoesNotContain("uality", wire);
    }

    [Fact]
    public async Task A_viewer_who_leaves_takes_their_report_with_them()
    {
        var title = WriteTitle(overall: 100, rungs: [(1080, 100), (720, 900)], audio: 1000);
        var room = Room();
        await using var bob = await fixture.ConnectAsync(room, "u2", "Bob");
        var alice = await fixture.ConnectAsync(room, "u1", "Alice");
        await alice.InvokeAsync<SessionStatePush>("LoadTitle", title, null);
        await alice.InvokeAsync("ReportQuality", 720, 720);

        var quality = fixture.Services.GetRequiredService<ViewerQuality>();
        Assert.Equal(2, quality.WantedHeights(room).Count);

        await alice.DisposeAsync();

        for (var i = 0; i < 50 && quality.WantedHeights(room).Count != 1; i++) await Task.Delay(100);
        Assert.Equal([1080], quality.WantedHeights(room));
    }

    private static string Room() => "rungs-" + Guid.NewGuid().ToString("n")[..8];

    /// <summary>A title mid-transcode with a record of each rung, written under the fixture's media root.</summary>
    private string WriteTitle(double overall, (int Height, double Head)[] rungs, double audio)
    {
        var id = "rungs-" + Guid.NewGuid().ToString("n")[..8];
        var manifest = new Manifest
        {
            Id = id,
            Title = id,
            DurationSeconds = SessionFixture.TitleDuration,
            Status = TitleStatus.Transcoding,
            HeadSeconds = overall,
            AudioHeadSeconds = audio,
            Video = new VideoInfo
            {
                Width = 1920,
                Height = 1080,
                SourceCodec = "hevc",
                SourceHdr = "sdr",
                Renditions =
                [
                    .. rungs.Select((r, i) => new Rendition
                    {
                        Name = $"{r.Height}p", BitrateKbps = 1000 * (i + 1), Width = r.Height * 16 / 9,
                        Height = r.Height, Uri = $"v{i}/index.m3u8", HeadSeconds = r.Head
                    })
                ]
            },
            Audio =
            [
                new AudioTrack
                {
                    Id = "a0", Kind = TrackKind.Feature, Language = "eng",
                    Label = "English", Channels = 2, Default = true, Uri = "a0/index.m3u8"
                }
            ],
            Subtitles = []
        };

        ManifestJson.WriteAtomic(Path.Combine(fixture.MediaRoot, id, "manifest.json"), manifest);
        return id;
    }
}
