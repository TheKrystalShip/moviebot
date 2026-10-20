using Microsoft.Extensions.Logging.Abstractions;
using TheKrystalShip.MovieBot.Acquire.Imdb;
using TheKrystalShip.MovieBot.Acquire.Search;
using TheKrystalShip.MovieBot.Api.Data;
using TheKrystalShip.MovieBot.Api.Acquisition;
using TheKrystalShip.MovieBot.Api.Wants;
using Xunit;

namespace TheKrystalShip.MovieBot.Tests;

/// <summary>
/// A wish exists nowhere but the API's wish list, so what matters is that it comes back the
/// same once kept, that it goes with the last want that could ask the tracker for it, and that a film
/// that came out says so for as long as anybody wants it.
/// </summary>
public sealed class WishListTests : IDisposable
{
    private readonly string _dir = Path.Combine(
        Path.GetTempPath(), "moviebot-tests", "wishes-" + Guid.NewGuid().ToString("N"));

    private string Database => Path.Combine(_dir, "moviebot.db");

    private static readonly ImdbTitle Prada = new()
    {
        ImdbId = "tt33612209",
        Title = "The Devil Wears Prada 2",
        Year = 2026,
        Starring = "Meryl Streep, Anne Hathaway",
        PosterUrl = new Uri("https://m.media-amazon.com/images/M/MV5BZmM3._V1_.jpg"),
    };

    private WishList List() => new(new CinemaDatabase(Database));

    private WantStore Wants() => new(new CinemaDatabase(Database));

    private static void Want(WantStore wants, string user, bool mayWish = true, int minute = 0) =>
        wants.Set(user, Prada.ImdbId, WantKinds.Film, true, false, mayWish, DateTimeOffset.UnixEpoch.AddMinutes(minute));

    [Fact]
    public void Two_people_wanting_one_film_share_one_wish()
    {
        var list = List();
        var wants = Wants();

        list.Keep(Prada, DateTimeOffset.UnixEpoch);
        Want(wants, "local:usr_10", minute: 1);
        Want(wants, "local:usr_20", minute: 2);
        list.Keep(Prada, DateTimeOffset.UnixEpoch.AddMinutes(5));

        var wish = Assert.Single(list.All());
        Assert.Equal(["local:usr_10", "local:usr_20"], wish.Subscribers.Select(s => s.UserId));
        Assert.Equal(DateTimeOffset.UnixEpoch.AddMinutes(1), wish.Subscribers[0].AskedAt);
    }

    [Fact]
    public void Only_a_want_that_may_wish_is_a_subscriber()
    {
        var list = List();
        var wants = Wants();
        Want(wants, "local:usr_20");
        list.Keep(Prada, DateTimeOffset.UnixEpoch);
        Want(wants, "local:usr_10", mayWish: false);

        Assert.Equal(["local:usr_20"], list.Find("TT33612209")!.Subscribers.Select(s => s.UserId));
    }

    [Fact]
    public void What_was_kept_is_what_is_read_back()
    {
        List().Keep(Prada, DateTimeOffset.UnixEpoch);
        Want(Wants(), "local:usr_10");

        var restored = List().All();

        var wish = Assert.Single(restored);
        Assert.Equal("tt33612209", wish.ImdbId);
        Assert.Equal("The Devil Wears Prada 2 (2026)", wish.Display);
        Assert.Equal("Meryl Streep, Anne Hathaway", wish.Starring);
        Assert.Equal(Prada.PosterUrl!.ToString(), wish.PosterUrl);
        Assert.Equal("local:usr_10", Assert.Single(wish.Subscribers).UserId);
    }

    [Fact]
    public void A_wish_goes_with_the_last_want_that_may_wish_and_not_before()
    {
        var list = List();
        var wants = Wants();
        list.Keep(Prada, DateTimeOffset.UnixEpoch);
        Want(wants, "10");
        Want(wants, "20");
        Want(wants, "30", mayWish: false);

        Assert.True(wants.Remove("10", Prada.ImdbId));
        Assert.Single(list.All());
        Assert.False(wants.Remove("10", Prada.ImdbId));

        // The want that asks nothing of the tracker does not keep the wish alive.
        Assert.True(wants.Remove("20", Prada.ImdbId));
        Assert.Empty(List().All());
        Assert.NotNull(wants.Get("30", Prada.ImdbId));
    }

    [Fact]
    public void A_want_that_stops_wishing_takes_the_wish_with_it()
    {
        var list = List();
        var wants = Wants();
        list.Keep(Prada, DateTimeOffset.UnixEpoch);
        Want(wants, "10");

        Want(wants, "10", mayWish: false);

        Assert.Empty(list.All());
    }

    [Fact]
    public void A_film_that_came_out_says_so_until_nobody_wants_it()
    {
        var list = List();
        var wants = Wants();
        list.Keep(Prada, DateTimeOffset.UnixEpoch);
        Want(wants, "10");
        var release = new Release { TorrentId = 7, ReleaseName = "The.Devil.Wears.Prada.2.2026.1080p.WEB-GRP", Title = "The Devil Wears Prada 2" };

        Assert.Null(list.OutAs("tt33612209"));
        list.MarkOut("tt33612209", release);
        Assert.Same(release, list.OutAs("tt33612209"));

        wants.Remove("10", Prada.ImdbId);
        Assert.Null(list.OutAs("tt33612209"));
    }

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }
}
