using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using TheKrystalShip.MovieBot.Api.Auth;
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

    /// <summary>The disk finished films are kept on, standing in for a second volume.</summary>
    public string ColdRoot { get; } = Path.Combine(
        Path.GetTempPath(), "moviebot-tests", Guid.NewGuid().ToString("n"), "cold");

    /// <summary>
    /// Where this run's rooms are journalled. Its own, because the API restores every room it finds in
    /// its journal on start: a shared one carries one run's rooms into the next, and a test that
    /// asserts a room does not exist then fails on a room some earlier run made.
    /// </summary>
    public string StateRoot { get; } = Path.Combine(
        Path.GetTempPath(), "moviebot-tests", Guid.NewGuid().ToString("n"), "state");

    /// <summary>A title still being written: the head sits here, seeks past it are refused.</summary>
    public const string TranscodingTitle = "still-cooking";
    public const double TranscodingHead = 300;

    /// <summary>A finished title: the whole film is seekable.</summary>
    public const string ReadyTitle = "all-done";
    public const double TitleDuration = 6000;

    /// <summary>A finished title that has moved to the disk films are kept on.</summary>
    public const string SettledTitle = "put-away";

    /// <summary>Two finished films the catalogue has named, for anything that reads a library the way a person names a film.</summary>
    public const string Heat = "heat-1995";
    public const string Collateral = "collateral-2004";

    public SessionFixture()
    {
        WriteManifest(MediaRoot, TranscodingTitle, TitleStatus.Transcoding, TranscodingHead);
        WriteManifest(MediaRoot, ReadyTitle, TitleStatus.Ready, null);
        WriteManifest(MediaRoot, Heat, TitleStatus.Ready, null,
            new FilmIdentity { ImdbId = "tt0113277", Name = "Heat", Year = 1995, Starring = "Al Pacino, Robert De Niro" });
        WriteManifest(MediaRoot, Collateral, TitleStatus.Ready, null,
            new FilmIdentity { ImdbId = "tt0369339", Name = "Collateral", Year = 2004, Starring = "Tom Cruise, Jamie Foxx" });

        Directory.CreateDirectory(ColdRoot);
        File.WriteAllText(Path.Combine(ColdRoot, MediaRoots.ColdMarker), "");
        WriteManifest(ColdRoot, SettledTitle, TitleStatus.Ready, null);
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (!disposing) return;

        if (Directory.Exists(MediaRoot)) Directory.Delete(MediaRoot, recursive: true);
        if (Directory.Exists(ColdRoot)) Directory.Delete(ColdRoot, recursive: true);
        if (Directory.Exists(StateRoot)) Directory.Delete(StateRoot, recursive: true);
    }

    /// <summary>Discord is the only door, and these are the keys that door is locked with.</summary>
    public const string SigningKey = "a-test-signing-key-of-at-least-thirty-two-characters";
    public const string ServiceKey = "a-test-service-key";

    protected override void ConfigureWebHost(IWebHostBuilder builder) =>
        builder.ConfigureAppConfiguration((_, config) =>
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Media:Root"] = MediaRoot,
                ["Media:ColdRoot"] = ColdRoot,
                ["Rooms:Journal"] = Path.Combine(StateRoot, "rooms.json"),
                ["Auth:SigningKey"] = SigningKey,
                ["Auth:ServiceKey"] = ServiceKey
            }));

    /// <summary>What the Activity would have been given after signing in with Discord.</summary>
    public string TokenFor(string roomId, string userId = "u", string displayName = "Someone") =>
        RoomToken.Issue(new RoomTokenPayload
        {
            UserId = userId,
            DisplayName = displayName,
            RoomId = roomId,
            ExpiresAtUnix = DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeSeconds()
        }, new AuthOptions { SigningKey = SigningKey });

    /// <summary>A client that proves itself the way the bot does.</summary>
    public HttpClient CreateServiceClient()
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Add("X-MovieBot-Service", ServiceKey);
        return client;
    }

    private static void WriteManifest(string root, string id, TitleStatus status, double? head, FilmIdentity? film = null)
    {
        var manifest = new Manifest
        {
            Id = id,
            Title = id,
            Film = film,
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

        ManifestJson.WriteAtomic(Path.Combine(root, id, "manifest.json"), manifest);
    }

    /// <summary>A hub connection routed through the in-process test server.</summary>
    public async Task<HubConnection> ConnectAsync(string sessionId, string userId, string displayName)
    {
        var connection = new HubConnectionBuilder()
            .WithUrl(new Uri(Server.BaseAddress, "/hub/session"), o =>
            {
                o.HttpMessageHandlerFactory = _ => Server.CreateHandler();
                o.Transports = Microsoft.AspNetCore.Http.Connections.HttpTransportType.LongPolling;
                o.AccessTokenProvider = () => Task.FromResult<string?>(TokenFor(sessionId, userId, displayName));
            })
            // The player reads enums as the strings the server writes, so this client does too.
            .AddJsonProtocol(o => o.PayloadSerializerOptions.Converters.Add(
                new System.Text.Json.Serialization.JsonStringEnumConverter()))
            .Build();

        await connection.StartAsync();
        await connection.InvokeAsync<SessionStatePush>("Join", sessionId, userId, displayName);
        return connection;
    }
}
