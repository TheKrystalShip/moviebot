using Microsoft.Extensions.Logging.Abstractions;
using TheKrystalShip.MovieBot.Acquire.Imdb;
using TheKrystalShip.MovieBot.Bot.Notify;
using Xunit;

namespace TheKrystalShip.MovieBot.Tests;

/// <summary>
/// The wish list is the one thing the bot remembers, so what matters is that it comes back the
/// same from disk and that people are only ever dropped by being told.
/// </summary>
public sealed class WishListTests : IDisposable
{
    private readonly string _dir = Path.Combine(
        Path.GetTempPath(), "moviebot-tests", "wishes-" + Guid.NewGuid().ToString("N"));

    private string File => Path.Combine(_dir, "wishes.json");

    private static readonly ImdbTitle Prada = new()
    {
        ImdbId = "tt33612209",
        Title = "The Devil Wears Prada 2",
        Year = 2026,
        Starring = "Meryl Streep, Anne Hathaway",
        PosterUrl = new Uri("https://m.media-amazon.com/images/M/MV5BZmM3._V1_.jpg"),
    };

    private static WishSubscriber Person(ulong user, ulong channel = 1) => new()
    {
        UserId = user, ChannelId = channel, AskedAt = DateTimeOffset.UnixEpoch,
    };

    private WishList List() => new(File, NullLogger<WishList>.Instance);

    [Fact]
    public void Two_people_asking_for_one_film_share_one_wish()
    {
        var list = List();

        var (first, _) = list.Add(Prada, Person(10));
        var (second, wish) = list.Add(Prada, Person(20));
        var (again, _) = list.Add(Prada, Person(10));

        Assert.Equal(WishAdded.Subscribed, first);
        Assert.Equal(WishAdded.Subscribed, second);
        Assert.Equal(WishAdded.AlreadySubscribed, again);
        Assert.Single(list.All());
        Assert.Equal([10UL, 20UL], wish.Subscribers.Select(s => s.UserId));
    }

    [Fact]
    public void What_was_written_is_what_is_read_back()
    {
        List().Add(Prada, Person(10, channel: 77));

        var restored = List().All();

        var wish = Assert.Single(restored);
        Assert.Equal("tt33612209", wish.ImdbId);
        Assert.Equal("The Devil Wears Prada 2 (2026)", wish.Display);
        Assert.Equal("Meryl Streep, Anne Hathaway", wish.Starring);
        Assert.Equal(Prada.PosterUrl!.ToString(), wish.PosterUrl);
        var person = Assert.Single(wish.Subscribers);
        Assert.Equal(77UL, person.ChannelId);
    }

    [Fact]
    public void A_wish_goes_with_its_last_subscriber_and_not_before()
    {
        var list = List();
        list.Add(Prada, Person(10));
        list.Add(Prada, Person(20));

        Assert.NotNull(list.Remove(10, "tt33612209"));
        Assert.Single(list.All());
        Assert.Empty(list.For(10));
        Assert.Single(list.For(20));

        Assert.Null(list.Remove(10, "tt33612209"));

        Assert.NotNull(list.Remove(20, "TT33612209"));
        Assert.Empty(List().All());
    }

    /// <summary>
    /// Whoever asked in a channel the announcement could not reach stays on the list, so the next
    /// pass tries them again rather than losing them.
    /// </summary>
    [Fact]
    public void Forgetting_by_channel_keeps_whoever_was_not_told()
    {
        var list = List();
        list.Add(Prada, Person(10, channel: 1));
        list.Add(Prada, Person(20, channel: 2));
        list.Add(Prada, Person(30, channel: 1));

        list.Forget("tt33612209", new HashSet<ulong> { 1 });

        var wish = Assert.Single(List().All());
        Assert.Equal([20UL], wish.Subscribers.Select(s => s.UserId));

        list.Forget("tt33612209", new HashSet<ulong> { 2 });
        Assert.Empty(List().All());
    }

    [Fact]
    public void A_file_that_cannot_be_read_starts_the_list_empty()
    {
        Directory.CreateDirectory(_dir);
        System.IO.File.WriteAllText(File, "{ not json");

        Assert.Empty(List().All());
    }

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }
}
