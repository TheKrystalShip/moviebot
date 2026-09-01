using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Configuration;
using TheKrystalShip.MovieBot.Core;

namespace TheKrystalShip.MovieBot.Tests;

/// <summary>
/// Hosts the API in-process over a media root built for the test, so clamping can be exercised
/// against a title that is genuinely mid-transcode rather than a mocked head.
/// </summary>
public sealed class SessionFixture : WebApplicationFactory<Program>
{
    public string MediaRoot { get; } = Path.Combine(
        Path.GetTempPath(), "moviebot-tests", Guid.NewGuid().ToString("n"));

    /// <summary>A title still being written: the head sits here, seeks past it are refused.</summary>
    public const string TranscodingTitle = "still-cooking";
    public const double TranscodingHead = 300;

    /// <summary>A finished title: the whole film is seekable.</summary>
    public const string ReadyTitle = "all-done";
    public const double TitleDuration = 6000;

    public SessionFixture()
    {
        WriteManifest(TranscodingTitle, TitleStatus.Transcoding, TranscodingHead);
        WriteManifest(ReadyTitle, TitleStatus.Ready, null);
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing && Directory.Exists(MediaRoot))
            Directory.Delete(MediaRoot, recursive: true);
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder) =>
        builder.ConfigureAppConfiguration((_, config) =>
            config.AddInMemoryCollection(new Dictionary<string, string?> { ["Media:Root"] = MediaRoot }));

    private void WriteManifest(string id, TitleStatus status, double? head)
    {
        var manifest = new Manifest
        {
            Id = id,
            Title = id,
            DurationSeconds = TitleDuration,
            Status = status,
            HeadSeconds = head,
            Video = new VideoInfo
            {
                Width = 1920,
                Height = 800,
                SourceCodec = "hevc",
                SourceHdr = "hdr10",
                Renditions = [new Rendition { Name = "800p", BitrateKbps = 9000, Uri = "v0/index.m3u8" }]
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

        ManifestJson.WriteAtomic(Path.Combine(MediaRoot, id, "manifest.json"), manifest);
    }

    /// <summary>A hub connection routed through the in-process test server.</summary>
    public async Task<HubConnection> ConnectAsync(string sessionId, string userId, string displayName)
    {
        var connection = new HubConnectionBuilder()
            .WithUrl(new Uri(Server.BaseAddress, "/hub/session"), o =>
            {
                o.HttpMessageHandlerFactory = _ => Server.CreateHandler();
                o.Transports = Microsoft.AspNetCore.Http.Connections.HttpTransportType.LongPolling;
            })
            .Build();

        await connection.StartAsync();
        await connection.InvokeAsync<SessionStatePush>("Join", sessionId, userId, displayName);
        return connection;
    }
}
