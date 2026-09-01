using Xunit;
using TheKrystalShip.MovieBot.Bot.Api;
using TheKrystalShip.MovieBot.Core;

namespace TheKrystalShip.MovieBot.Tests;

/// <summary>
/// The bot's only route to anything that persists, driven against the real API in-process.
///
/// Mocking the responses here would test a transcription of the contract rather than the
/// contract, and the failure this catches — a wire name or an enum spelling the two sides
/// disagree on — is invisible until it produces a null in front of a room.
/// </summary>
public sealed class MovieBotApiClientTests(SessionFixture fixture) : IClassFixture<SessionFixture>
{
    private MovieBotApiClient Client() => new(fixture.CreateClient());

    /// <summary>Nothing listens here, so it stands in for an API that is not running.</summary>
    private static MovieBotApiClient Unreachable() => new(new HttpClient
    {
        BaseAddress = new Uri("http://127.0.0.1:1/"),
        Timeout = TimeSpan.FromSeconds(2)
    });

    [Fact]
    public async Task The_library_arrives_with_its_statuses_and_heads_intact()
    {
        var titles = await Client().ListTitlesAsync(CancellationToken.None);

        var cooking = titles.Single(t => t.Id == SessionFixture.TranscodingTitle);
        Assert.Equal(TitleStatus.Transcoding, cooking.Status);
        Assert.Equal(SessionFixture.TranscodingHead, cooking.HeadSeconds);
        Assert.Equal(SessionFixture.TitleDuration, cooking.DurationSeconds);

        var ready = titles.Single(t => t.Id == SessionFixture.ReadyTitle);
        Assert.Equal(TitleStatus.Ready, ready.Status);
        Assert.Null(ready.HeadSeconds);
    }

    [Fact]
    public async Task Opening_a_session_creates_it_and_returns_a_room_nobody_has_touched()
    {
        var sessionId = Random.Shared.NextInt64(1, long.MaxValue).ToString();

        var state = await Client().OpenSessionAsync(sessionId, CancellationToken.None);

        Assert.Equal(sessionId, state.SessionId);
        Assert.True(state.Paused);
        Assert.Null(state.TitleId);
        Assert.Equal(0, state.Revision);
    }

    [Fact]
    public async Task An_api_that_is_not_running_is_reported_as_such_rather_than_thrown_raw()
    {
        var client = Unreachable();

        await Assert.ThrowsAsync<MovieBotApiException>(
            () => client.ListTitlesAsync(CancellationToken.None));
        await Assert.ThrowsAsync<MovieBotApiException>(
            () => client.OpenSessionAsync("1", CancellationToken.None));
    }

    [Fact]
    public async Task Health_says_what_it_measured()
    {
        Assert.True(await Client().IsHealthyAsync(CancellationToken.None));
        Assert.False(await Unreachable().IsHealthyAsync(CancellationToken.None));
    }
}
