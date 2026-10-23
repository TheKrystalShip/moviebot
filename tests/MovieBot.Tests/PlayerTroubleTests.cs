using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;
using TheKrystalShip.MovieBot.Api.Sessions;
using Xunit;

namespace TheKrystalShip.MovieBot.Tests;

/// <summary>
/// What a player says went wrong reaches the log in its own words, a few lines a minute per
/// connection at most, with what it said past that counted once the minute is over.
/// </summary>
public sealed class PlayerTroubleTests
{
    private sealed class Lines : ILogger<PlayerTrouble>
    {
        public readonly List<string> Written = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter) => Written.Add(formatter(state, exception));
    }

    private readonly FakeTimeProvider _clock = new();
    private readonly Lines _log = new();
    private PlayerTrouble Trouble() => new(_clock, _log);

    [Fact]
    public void A_report_names_the_viewer_the_room_the_film_and_the_playback()
    {
        Trouble().Report("conn-1", "Sushi", "ea64a65b", "pirates", "network", 63.02, "levelLoadError (404), again in 1s", "pb-1");

        Assert.Equal("Sushi's player in ea64a65b: network at 63.0s of pirates: levelLoadError (404), again in 1s (playback pb-1)",
            Assert.Single(_log.Written));
    }

    [Fact]
    public void A_kind_nobody_named_and_a_detail_with_control_characters_are_not_written_as_sent()
    {
        var trouble = Trouble();
        trouble.Report("conn-1", "Sushi", "room", null, "anything", 0, "made up", null);
        trouble.Report("conn-1", "Sushi", "room", null, "stuck", double.NaN, "line\nbreak" + new string('x', 400), null);

        var line = Assert.Single(_log.Written);
        Assert.DoesNotContain("\n", line);
        Assert.Contains("stuck at 0.0s of nothing: linebreak", line);
        Assert.True(line.Length < PlayerTrouble.DetailLength + 120);
    }

    [Fact]
    public void A_connection_writes_its_share_a_minute_and_the_rest_is_counted()
    {
        var trouble = Trouble();
        for (var i = 0; i < 50; i++) trouble.Report("conn-1", "Sushi", "room", "film", "network", i, null, "pb");
        trouble.Report("conn-2", "Ada", "room", "film", "stuck", 1, null, "pb-2");
        Assert.Equal(PlayerTrouble.PerMinute + 1, _log.Written.Count);

        _clock.Advance(TimeSpan.FromMinutes(1));
        trouble.Report("conn-1", "Sushi", "room", "film", "network", 60, null, "pb");
        Assert.Contains(_log.Written, l => l == $"Sushi's player in room reported {50 - PlayerTrouble.PerMinute} more problems last minute, not written");

        for (var i = 0; i < 20; i++) trouble.Report("conn-1", "Sushi", "room", "film", "network", i, null, "pb");
        trouble.Leave("conn-1", "Sushi", "room");
        Assert.Equal("Sushi's player in room reported 15 more problems before leaving, not written", _log.Written[^1]);
    }
}
