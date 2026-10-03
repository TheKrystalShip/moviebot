using Microsoft.Extensions.Logging.Abstractions;
using TheKrystalShip.MovieBot.Bot.Api;
using TheKrystalShip.MovieBot.Bot.Keep;
using Xunit;

namespace TheKrystalShip.MovieBot.Tests;

/// <summary>
/// <c>/keep</c> end to end through the client and the real API: the bot names the person, the API
/// records it, and what is said back is read from what the API answered.
/// </summary>
public sealed class KeepCommandTests(SessionFixture fixture) : IClassFixture<SessionFixture>
{
    private KeepCommand Command() => new(
        new MovieBotApiClient(fixture.CreateServiceClient()), NullLogger<KeepCommand>.Instance);

    [Fact]
    public async Task A_keep_is_recorded_for_the_person_and_said_to_the_next_to_ask()
    {
        var command = Command();

        var kept = await command.KeepAsync(SessionFixture.Heat, 4242, "Haru", CancellationToken.None);
        var again = await command.KeepAsync(SessionFixture.Heat, 77, "Bea", CancellationToken.None);

        Assert.Equal(KeepStatus.Kept, kept.Status);
        Assert.Equal("4242", kept.Film!.Keeper);
        Assert.Equal(KeepStatus.AlreadyKept, again.Status);
        Assert.Contains("<@4242>", again.Message);
        Assert.Equal("Heat (1995)", kept.Film.Name);

        var released = await command.ReleaseAsync(SessionFixture.Heat, 77, "Bea", CancellationToken.None);
        var nothing = await command.ReleaseAsync(SessionFixture.Heat, 77, "Bea", CancellationToken.None);

        Assert.Equal(KeepStatus.Released, released.Status);
        Assert.Equal(KeepStatus.NotKept, nothing.Status);
    }

    [Fact]
    public async Task A_film_the_library_does_not_hold_is_not_found()
    {
        var result = await Command().KeepAsync("nothing-here", 1, "A", CancellationToken.None);

        Assert.Equal(KeepStatus.NotFound, result.Status);
    }

    [Fact]
    public async Task The_notice_says_nothing_for_a_film_that_has_not_settled_and_a_number_for_one_that_has()
    {
        var command = Command();

        Assert.Null(await command.NoticeForAsync(SessionFixture.TranscodingTitle, CancellationToken.None));

        var settled = await command.NoticeForAsync(SessionFixture.SettledTitle, CancellationToken.None);
        Assert.NotNull(settled);
        Assert.Contains("/keep add", settled);
    }

    [Fact]
    public async Task The_list_is_the_settled_films()
    {
        var films = await Command().ListAsync(CancellationToken.None);

        Assert.Equal(SessionFixture.SettledTitle, Assert.Single(films).TitleId);
    }
}
