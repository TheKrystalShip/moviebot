using System.Net.Http.Json;
using Xunit;
using Discord;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Logging.Abstractions;
using TheKrystalShip.MovieBot.Bot.Api;
using TheKrystalShip.MovieBot.Bot.Launch;
using TheKrystalShip.MovieBot.Bot.Watch;
using TheKrystalShip.MovieBot.Core;

namespace TheKrystalShip.MovieBot.Tests;

/// <summary>
/// The command, end to end against the real API and a presenter that records what it was handed.
///
/// Everything here happens without a gateway, because none of it is about Discord: which film,
/// which room, and what the person is told when it cannot be done.
/// </summary>
public sealed class WatchCommandTests(SessionFixture fixture) : IClassFixture<SessionFixture>
{
    private const ulong VoiceChannel = 918273645;

    private sealed class RecordingPresenter : ILaunchPresenter
    {
        public LaunchRequest? Seen { get; private set; }

        public Task<LaunchReply> PresentAsync(LaunchRequest request, CancellationToken ct)
        {
            Seen = request;
            return Task.FromResult(new LaunchReply(
                "launched", new EmbedBuilder().WithTitle(request.Title.Title).Build(), null));
        }
    }

    /// <summary>No download stands behind a title the fixture ingested, so the launch says nothing.</summary>
    private sealed class NoRetention : TheKrystalShip.MovieBot.Bot.Keep.IFilmRetention
    {
        public Task<string?> NoticeForAsync(string libraryId, CancellationToken ct) =>
            Task.FromResult<string?>(null);
    }

    private static WatchCommand Command(MovieBotApiClient api, ILaunchPresenter presenter) =>
        new(api, presenter, new NoRetention(), NullLogger<WatchCommand>.Instance);

    private WatchCommand Live(ILaunchPresenter presenter) =>
        Command(new MovieBotApiClient(fixture.CreateServiceClient()), presenter);

    private static WatchRequest Ask(string query, ulong? voiceChannel = VoiceChannel) => new()
    {
        VoiceChannelId = voiceChannel,
        VoiceChannelName = voiceChannel is null ? null : "Movie Night",
        Query = query,
        RequestedBy = "Alice"
    };

    [Fact]
    public async Task A_resolved_film_launches_into_the_session_the_voice_channel_names()
    {
        var presenter = new RecordingPresenter();

        var result = await Live(presenter).ExecuteAsync(
            Ask(SessionFixture.ReadyTitle), CancellationToken.None);

        Assert.Equal(WatchStatus.Launched, result.Status);
        Assert.NotNull(result.Reply);

        var launch = presenter.Seen!;
        Assert.Equal(VoiceChannel.ToString(), launch.SessionId);
        Assert.Equal(SessionFixture.ReadyTitle, launch.Title.Id);
        Assert.Equal("Movie Night", launch.VoiceChannelName);
        Assert.Equal("Alice", launch.RequestedBy);
        Assert.Null(launch.Replaced);
        Assert.False(launch.AlreadyWatching);
        Assert.StartsWith("Alice started ", launch.Headline);
    }

    [Fact]
    public async Task Without_a_voice_channel_there_is_no_room_to_launch_into()
    {
        var presenter = new RecordingPresenter();

        var result = await Live(presenter).ExecuteAsync(
            Ask(SessionFixture.ReadyTitle, voiceChannel: null), CancellationToken.None);

        Assert.Equal(WatchStatus.NotInVoiceChannel, result.Status);
        Assert.Null(result.Reply);
        Assert.Contains("Join a voice channel", result.Message);
        Assert.Null(presenter.Seen);
    }

    [Fact]
    public async Task A_film_nobody_has_says_what_the_library_does_have()
    {
        var result = await Live(new RecordingPresenter()).ExecuteAsync(
            Ask("casablanca"), CancellationToken.None);

        Assert.Equal(WatchStatus.TitleNotFound, result.Status);
        Assert.Null(result.Reply);
        Assert.Contains(SessionFixture.ReadyTitle, result.Message);
    }

    [Fact]
    public async Task An_ambiguous_request_is_refused_with_the_films_it_could_have_meant()
    {
        // Two cuts of one film, which is the ordinary way a library becomes ambiguous.
        AddTitle("the-duel-1977-theatrical", "The Duel 1977 Theatrical Cut");
        AddTitle("the-duel-1977-extended", "The Duel 1977 Extended Cut");

        var result = await Live(new RecordingPresenter()).ExecuteAsync(
            Ask("duel"), CancellationToken.None);

        Assert.Equal(WatchStatus.TitleAmbiguous, result.Status);
        Assert.Null(result.Reply);
        Assert.Contains("The Duel 1977 Theatrical Cut", result.Message);
        Assert.Contains("The Duel 1977 Extended Cut", result.Message);
    }

    /// <summary>
    /// Writes a manifest into the media root the API is serving. The library is read from the
    /// filesystem on every request, so a title added here is a title the bot can be asked for.
    /// </summary>
    private void AddTitle(string id, string title) =>
        ManifestJson.WriteAtomic(Path.Combine(fixture.MediaRoot, id, "manifest.json"), new Manifest
        {
            Id = id,
            Title = title,
            DurationSeconds = 6000,
            Status = TitleStatus.Ready,
            Video = new VideoInfo
            {
                Width = 1920,
                Height = 800,
                SourceCodec = "hevc",
                SourceHdr = "sdr",
                Renditions = [new Rendition { Name = "800p", BitrateKbps = 9000, Width = 1920, Height = 800, Uri = "v0/index.m3u8" }]
            },
            Audio = [],
            Subtitles = []
        });

    [Fact]
    public async Task A_transcoding_film_launches_because_it_is_watchable_already()
    {
        var presenter = new RecordingPresenter();

        var result = await Live(presenter).ExecuteAsync(
            Ask(SessionFixture.TranscodingTitle), CancellationToken.None);

        Assert.Equal(WatchStatus.Launched, result.Status);
        Assert.Equal(TitleStatus.Transcoding, presenter.Seen!.Title.Status);
        Assert.Equal(SessionFixture.TranscodingHead, presenter.Seen.Title.HeadSeconds);
    }

    [Fact]
    public async Task An_api_that_is_not_running_is_reported_as_that_and_nothing_else()
    {
        var presenter = new RecordingPresenter();
        var command = Command(
            new MovieBotApiClient(new HttpClient
            {
                BaseAddress = new Uri("http://127.0.0.1:1/"),
                Timeout = TimeSpan.FromSeconds(2)
            }),
            presenter);

        var result = await command.ExecuteAsync(Ask("anything"), CancellationToken.None);

        Assert.Equal(WatchStatus.BackendUnavailable, result.Status);
        Assert.Null(presenter.Seen);
    }

    [Fact]
    public async Task A_room_watching_something_else_is_switched_and_keeps_playing()
    {
        const ulong channel = 555000111;
        var sessionId = channel.ToString();

        await using var viewer = await fixture.ConnectAsync(sessionId, "u1", "Bob");
        await viewer.InvokeAsync<SessionStatePush>("LoadTitle", SessionFixture.ReadyTitle);
        await viewer.InvokeAsync<SessionStatePush>("Play", 1200.0);

        var presenter = new RecordingPresenter();
        var result = await Live(presenter).ExecuteAsync(
            Ask(SessionFixture.TranscodingTitle, channel), CancellationToken.None);

        Assert.Equal(WatchStatus.Launched, result.Status);
        Assert.Equal(SessionFixture.ReadyTitle, presenter.Seen!.Replaced?.Id);
        Assert.False(presenter.Seen.AlreadyWatching);
        Assert.Contains("switched Movie Night from", presenter.Seen.Headline);

        // The room was playing, so it goes on playing: the new film, from its start.
        var state = (await fixture.CreateServiceClient().GetFromJsonAsync(
            $"api/sessions/{sessionId}", ManifestJsonContext.Default.SessionStatePush))!.State;
        Assert.Equal(SessionFixture.TranscodingTitle, state.TitleId);
        Assert.False(state.Paused);
        Assert.InRange(state.PositionAt(DateTimeOffset.UtcNow), 0, 5);
    }

    [Fact]
    public async Task Asking_for_the_film_the_room_already_holds_leaves_it_where_it_is()
    {
        const ulong channel = 555000222;
        var sessionId = channel.ToString();

        await using var viewer = await fixture.ConnectAsync(sessionId, "u1", "Bob");
        await viewer.InvokeAsync<SessionStatePush>("LoadTitle", SessionFixture.ReadyTitle);
        var before = (await viewer.InvokeAsync<SessionStatePush>("Play", 1200.0)).State;

        var presenter = new RecordingPresenter();
        var result = await Live(presenter).ExecuteAsync(
            Ask(SessionFixture.ReadyTitle, channel), CancellationToken.None);

        Assert.Equal(WatchStatus.Launched, result.Status);
        Assert.True(presenter.Seen!.AlreadyWatching);
        Assert.Null(presenter.Seen.Replaced);
        Assert.StartsWith("Movie Night is already watching ", presenter.Seen.Headline);

        var after = (await fixture.CreateServiceClient().GetFromJsonAsync(
            $"api/sessions/{sessionId}", ManifestJsonContext.Default.SessionStatePush))!.State;
        Assert.Equal(before.Revision, after.Revision);
        Assert.False(after.Paused);
        Assert.InRange(after.PositionAt(DateTimeOffset.UtcNow), 1200, 1210);
    }
}
