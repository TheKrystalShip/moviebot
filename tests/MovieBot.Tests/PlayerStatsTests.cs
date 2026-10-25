using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;
using TheKrystalShip.MovieBot.Api.Sessions;
using TheKrystalShip.MovieBot.Core;
using Xunit;

namespace TheKrystalShip.MovieBot.Tests;

/// <summary>
/// What a viewer's stats panel shows reaches the log when they send it, as one line under their name,
/// at most one every ten seconds a connection, and only as a JSON object no larger than the limit.
/// </summary>
public sealed class PlayerStatsTests(SessionFixture fixture) : IClassFixture<SessionFixture>
{
    private sealed class Lines : ILogger<PlayerStats>
    {
        public readonly List<string> Written = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter) => Written.Add(formatter(state, exception));
    }

    private readonly FakeTimeProvider _clock = new();
    private readonly Lines _log = new();
    private PlayerStats Stats() => new(_clock, _log);

    private const string Snapshot = """{"playback":"pb-1","sync":{"pictureOffsetMs":4.2,"outputLatencyMs":181.3}}""";

    [Fact]
    public void A_snapshot_is_one_line_naming_the_viewer_the_room_and_the_film()
    {
        Assert.True(Stats().Report("conn-1", "Devcas", "ca08d920", "the-imitation-game-2014", Snapshot));

        Assert.Equal($"Devcas's player in ca08d920 sent its playback stats for the-imitation-game-2014: {Snapshot}",
            Assert.Single(_log.Written));
    }

    [Fact]
    public void A_connection_sends_one_every_ten_seconds()
    {
        var stats = Stats();
        Assert.True(stats.Report("conn-1", "Devcas", "room", "film", Snapshot));
        Assert.False(stats.Report("conn-1", "Devcas", "room", "film", Snapshot));
        Assert.True(stats.Report("conn-2", "Ada", "room", "film", Snapshot));

        _clock.Advance(PlayerStats.Every);
        Assert.True(stats.Report("conn-1", "Devcas", "room", "film", Snapshot));
        Assert.Equal(3, _log.Written.Count);
    }

    [Fact]
    public void A_connection_that_left_is_forgotten()
    {
        var stats = Stats();
        Assert.True(stats.Report("conn-1", "Devcas", "room", "film", Snapshot));
        stats.Leave("conn-1");
        Assert.True(stats.Report("conn-1", "Devcas", "room", "film", Snapshot));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("[1,2,3]")]
    [InlineData("\"a string\"")]
    [InlineData("{\"cut\":")]
    public void Anything_but_a_json_object_is_not_written(string? snapshot)
    {
        Assert.False(Stats().Report("conn-1", "Devcas", "room", "film", snapshot));
        Assert.Empty(_log.Written);
    }

    [Fact]
    public void A_snapshot_past_the_limit_is_not_written()
    {
        var large = $$"""{"pad":"{{new string('x', PlayerStats.MaxChars)}}"}""";
        Assert.False(Stats().Report("conn-1", "Devcas", "room", "film", large));
        Assert.Empty(_log.Written);
    }

    [Fact]
    public async Task A_viewer_in_the_room_sends_and_a_connection_in_none_is_refused()
    {
        var room = "stats-" + Guid.NewGuid().ToString("n")[..8];
        await using var alice = await fixture.ConnectOutsideAsync("st-alice-1", "Alice");
        await using var bob = await fixture.ConnectOutsideAsync("st-bob-1", "Bob");
        await alice.InvokeAsync<JoinReply>("Join", room);

        Assert.True(await alice.InvokeAsync<bool>("ReportStats", Snapshot));
        Assert.False(await alice.InvokeAsync<bool>("ReportStats", Snapshot));
        await Assert.ThrowsAsync<HubException>(() => bob.InvokeAsync<bool>("ReportStats", Snapshot));
    }
}
