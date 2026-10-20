using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TheKrystalShip.MovieBot.Bot.Api;
using TheKrystalShip.MovieBot.Bot.Configuration;
using TheKrystalShip.MovieBot.Bot.Notify;
using Xunit;

namespace TheKrystalShip.MovieBot.Tests;

/// <summary>
/// <c>/notify</c> through the API as the Discord user it runs for: it makes and ends the person's
/// want of a film, and tells the channel what the API answered.
/// </summary>
public sealed class NotifyCommandTests(SessionFixture fixture) : IClassFixture<SessionFixture>
{
    private const string NotOut = "tt0325980";

    private NotifyCommand Command() => new(fixture.CreateBotApi(),
        Options.Create(new ApiOptions { PublicBaseUrl = "https://movies.example" }), NullLogger<NotifyCommand>.Instance);

    [Fact]
    public async Task A_film_nothing_has_is_waited_on_listed_and_let_go()
    {
        var command = Command();
        using var acting = ActingFor.Person(4242);

        var made = await command.SubscribeAsync(new NotifyRequest { Chosen = $"https://www.imdb.com/title/{NotOut}/" }, CancellationToken.None);
        Assert.Equal(NotifyStatus.Subscribed, made.Status);
        Assert.Equal(NotOut, made.Wish?.ImdbId);
        Assert.Contains("can be downloaded", made.Message);

        var again = await command.SubscribeAsync(new NotifyRequest { Chosen = NotOut }, CancellationToken.None);
        Assert.Equal(NotifyStatus.AlreadySubscribed, again.Status);

        Assert.Equal(NotOut, Assert.Single(await command.ListAsync(CancellationToken.None)).ImdbId);

        Assert.Equal(CancelStatus.Cancelled, (await command.CancelAsync(NotOut, CancellationToken.None)).Status);
        Assert.Equal(CancelStatus.NotWaiting, (await command.CancelAsync(NotOut, CancellationToken.None)).Status);
        Assert.Empty(await command.ListAsync(CancellationToken.None));
    }

    [Fact]
    public async Task A_film_the_library_holds_is_pointed_at_the_player_and_still_wanted()
    {
        var command = Command();
        using var acting = ActingFor.Person(77);

        var made = await command.SubscribeAsync(new NotifyRequest { Chosen = "tt0113277" }, CancellationToken.None);

        Assert.Equal(NotifyStatus.InLibrary, made.Status);
        Assert.Contains("already in the library", made.Message);
        Assert.Empty(await command.ListAsync(CancellationToken.None));
        Assert.Equal(CancelStatus.Cancelled, (await command.CancelAsync("tt0113277", CancellationToken.None)).Status);
    }

    [Fact]
    public async Task Text_that_names_no_film_and_an_id_the_catalogue_lacks_are_refused()
    {
        var command = Command();
        using var acting = ActingFor.Person(4242);

        Assert.Equal(NotifyStatus.NothingPicked,
            (await command.SubscribeAsync(new NotifyRequest { Chosen = "the devil wears prada" }, CancellationToken.None)).Status);
        Assert.Equal(NotifyStatus.NotFound,
            (await command.SubscribeAsync(new NotifyRequest { Chosen = "tt9999999" }, CancellationToken.None)).Status);
        Assert.Equal(CancelStatus.NothingPicked, (await command.CancelAsync("", CancellationToken.None)).Status);
    }
}
