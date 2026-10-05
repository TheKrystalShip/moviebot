using Microsoft.Extensions.Logging.Abstractions;
using TheKrystalShip.MovieBot.Acquire.Imdb;
using TheKrystalShip.MovieBot.Acquire.Search;
using TheKrystalShip.MovieBot.Api.Acquisition;
using Xunit;

namespace TheKrystalShip.MovieBot.Tests;

/// <summary>
/// A wish exists nowhere but the API's wish list, so what matters is that it comes back the
/// same from disk, that people are only ever dropped by stopping waiting, and that a film that came
/// out says so for as long as anybody waits on it.
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

    private static WishSubscriber Person(string user) => new() { UserId = user, AskedAt = DateTimeOffset.UnixEpoch };

    private WishList List() => new(File, NullLogger<WishList>.Instance);

    [Fact]
    public void Two_people_asking_for_one_film_share_one_wish()
    {
        var list = List();

        var (first, _) = list.Add(Prada, Person("local:usr_10"));
        var (second, wish) = list.Add(Prada, Person("local:usr_20"));
        var (again, _) = list.Add(Prada, Person("local:usr_10"));

        Assert.Equal(WishAdded.Subscribed, first);
        Assert.Equal(WishAdded.Subscribed, second);
        Assert.Equal(WishAdded.AlreadySubscribed, again);
        Assert.Single(list.All());
        Assert.Equal(["local:usr_10", "local:usr_20"], wish.Subscribers.Select(s => s.UserId));
    }

    [Fact]
    public void What_was_written_is_what_is_read_back()
    {
        List().Add(Prada, Person("local:usr_10"));

        var restored = List().All();

        var wish = Assert.Single(restored);
        Assert.Equal("tt33612209", wish.ImdbId);
        Assert.Equal("The Devil Wears Prada 2 (2026)", wish.Display);
        Assert.Equal("Meryl Streep, Anne Hathaway", wish.Starring);
        Assert.Equal(Prada.PosterUrl!.ToString(), wish.PosterUrl);
        Assert.Equal("local:usr_10", Assert.Single(wish.Subscribers).UserId);
    }

    [Fact]
    public void A_wish_goes_with_its_last_subscriber_and_not_before()
    {
        var list = List();
        list.Add(Prada, Person("10"));
        list.Add(Prada, Person("20"));

        Assert.NotNull(list.Remove("10", "tt33612209"));
        Assert.Single(list.All());
        Assert.Empty(list.For("10"));
        Assert.Single(list.For("20"));

        Assert.Null(list.Remove("10", "tt33612209"));

        Assert.NotNull(list.Remove("20", "TT33612209"));
        Assert.Empty(List().All());
    }

    [Fact]
    public void A_film_that_came_out_says_so_until_nobody_waits_on_it()
    {
        var list = List();
        list.Add(Prada, Person("10"));
        var release = new Release { TorrentId = 7, ReleaseName = "The.Devil.Wears.Prada.2.2026.1080p.WEB-GRP", Title = "The Devil Wears Prada 2" };

        Assert.Null(list.OutAs("tt33612209"));
        list.MarkOut("tt33612209", release);
        Assert.Same(release, list.OutAs("tt33612209"));

        list.Remove("10", "tt33612209");
        Assert.Null(list.OutAs("tt33612209"));
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
