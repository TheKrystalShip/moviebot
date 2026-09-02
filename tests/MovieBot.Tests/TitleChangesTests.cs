using Microsoft.AspNetCore.SignalR.Client;
using TheKrystalShip.MovieBot.Core;
using Xunit;

namespace TheKrystalShip.MovieBot.Tests;

/// <summary>
/// What a film gains while people are watching it reaches them down the connection they hold,
/// so a viewer who opened it early is never left with the copy they read.
/// </summary>
public sealed class TitleChangesTests(SessionFixture fixture) : IClassFixture<SessionFixture>
{
    [Fact]
    public async Task A_manifest_rewritten_on_disk_is_pushed_to_the_room_holding_it()
    {
        const string room = "title-changes-room";
        await using var viewer = await fixture.ConnectAsync(room, "u1", "Alice");

        var pushed = new TaskCompletionSource<Manifest>(TaskCreationOptions.RunContinuationsAsynchronously);
        viewer.On<Manifest>("TitleChanged", manifest =>
        {
            if (manifest.Thumbnails is not null) pushed.TrySetResult(manifest);
        });

        await viewer.InvokeAsync<SessionStatePush>("LoadTitle", SessionFixture.TranscodingTitle);

        // The transcode moving on: a further head, and the preview sheet written after the main
        // pass. Rewritten the way the ingest does it.
        var path = Path.Combine(fixture.MediaRoot, SessionFixture.TranscodingTitle, "manifest.json");
        var manifest = ManifestJson.Deserialize(await File.ReadAllTextAsync(path))!;
        manifest.HeadSeconds = SessionFixture.TranscodingHead + 120;
        manifest.Thumbnails = new ThumbnailStrip
        {
            Uri = "thumbs.jpg", IntervalSeconds = 10, Columns = 10, Rows = 10,
            Width = 320, Height = 180, Count = 100
        };
        await Task.Delay(50);
        ManifestJson.WriteAtomic(path, manifest);

        var fresh = await pushed.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(SessionFixture.TranscodingHead + 120, fresh.HeadSeconds);
        Assert.Equal("thumbs.jpg", fresh.Thumbnails?.Uri);
    }
}
