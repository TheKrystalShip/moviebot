using Discord;
using Microsoft.Extensions.Logging.Abstractions;
using TheKrystalShip.MovieBot.Bot.Configuration;
using TheKrystalShip.MovieBot.Bot.Launch;
using TheKrystalShip.MovieBot.Core;
using Xunit;

namespace TheKrystalShip.MovieBot.Tests;

/// <summary>
/// A launch card is a door, and the promise is that it says so when it stops opening. What is
/// checked here is the whole of that decision — how long the invite behind it is good for, when
/// the card is over, and what it says once it is — since everything else about it is Discord's.
/// </summary>
public sealed class LaunchCardTests : IDisposable
{
    private readonly string _dir = Path.Combine(
        Path.GetTempPath(), "moviebot-tests", "cards-" + Guid.NewGuid().ToString("N"));

    private string File => Path.Combine(_dir, "launches.json");

    private LaunchCards Cards() => new(File, NullLogger<LaunchCards>.Instance);

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    private static LaunchCard Card(string session = "918273645", string title = "heat-1995") => new()
    {
        ChannelId = 42,
        MessageId = 99,
        SessionId = session,
        TitleId = title,
        InviteCode = "aBcD1234"
    };

    private static RoomSummary Room(string session = "918273645", string? title = "heat-1995",
        string? name = "Heat") => new()
    {
        SessionId = session,
        TitleId = title,
        Name = name,
        Paused = false,
        PositionSeconds = 120,
        Participants = 3
    };

    // ---- How long the invite lasts -------------------------------------------------------

    [Fact]
    public void An_invite_outlasts_the_film_it_was_made_for()
    {
        var launch = new LaunchOptions { InviteGraceSeconds = 1800 };

        // Heat runs two hours and fifty minutes. A flat half hour would have shut the door on it
        // with two hours of the film still to go.
        Assert.Equal(10218 + 1800, launch.InviteMaxAgeFor(10218));
    }

    [Fact]
    public void A_film_of_no_known_length_still_gets_an_invite_that_expires()
    {
        var launch = new LaunchOptions { InviteGraceSeconds = 1800 };

        Assert.Equal(1800, launch.InviteMaxAgeFor(0));
        Assert.Equal(1800, launch.InviteMaxAgeFor(double.NaN));
        Assert.Equal(1800, launch.InviteMaxAgeFor(-5));
    }

    [Fact]
    public void An_invite_is_never_asked_to_live_forever_or_longer_than_discord_allows()
    {
        // Discord reads an age of zero as never expiring, so a grace of none must not produce one.
        Assert.True(new LaunchOptions { InviteGraceSeconds = 0 }.InviteMaxAgeFor(0) > 0);

        Assert.Equal(604800, new LaunchOptions { InviteGraceSeconds = 1800 }.InviteMaxAgeFor(604800));
    }

    // ---- When a card is over -------------------------------------------------------------

    [Fact]
    public void A_card_whose_room_is_still_watching_the_film_stands()
    {
        Assert.Null(ExpiredCard.Reason(Card(), [Room()]));
    }

    [Fact]
    public void A_card_whose_room_was_forgotten_is_over()
    {
        var reason = ExpiredCard.Reason(Card(), []);

        Assert.NotNull(reason);
        Assert.Contains("closed", reason);
        Assert.Contains("/watch", reason);
    }

    [Fact]
    public void A_room_opened_again_with_nothing_in_it_is_not_the_room_the_card_opened()
    {
        var reason = ExpiredCard.Reason(Card(), [Room(title: null, name: null)]);

        Assert.NotNull(reason);
        Assert.Contains("closed", reason);
    }

    [Fact]
    public void A_card_the_room_has_moved_on_from_names_what_is_playing_instead()
    {
        var reason = ExpiredCard.Reason(Card(), [Room(title: "the-mask-1994", name: "The Mask")]);

        Assert.NotNull(reason);
        Assert.Contains("The Mask", reason);
        Assert.DoesNotContain("closed", reason);
    }

    [Fact]
    public void Another_rooms_business_leaves_a_card_alone()
    {
        Assert.Null(ExpiredCard.Reason(Card(), [Room(session: "111", title: "the-mask-1994"), Room()]));
    }

    // ---- What the card becomes -----------------------------------------------------------

    [Fact]
    public void An_expired_card_keeps_the_film_and_stops_being_a_link()
    {
        var posted = new EmbedBuilder()
            .WithTitle("Heat")
            .WithUrl("https://discord.gg/aBcD1234")
            .WithDescription("Ready. The whole film is seekable.")
            .WithThumbnailUrl("https://movies.example.com/media/heat-1995/poster.jpg")
            .WithColor(new Color(0x4C, 0xC9, 0xC0))
            .AddField("Voice channel", "Movie Night", inline: true)
            .WithFooter("Requested by Alice")
            .Build();

        var expired = ExpiredCard.Rebuild(posted, "This room has closed.");

        Assert.Equal("Heat", expired.Title);
        Assert.Null(expired.Url);
        Assert.Equal("This room has closed.", expired.Description);
        Assert.Equal("https://movies.example.com/media/heat-1995/poster.jpg", expired.Thumbnail?.Url);
        Assert.Equal("Movie Night", Assert.Single(expired.Fields).Value);
        Assert.Equal("Requested by Alice", expired.Footer?.Text);
        Assert.NotEqual(new Color(0x4C, 0xC9, 0xC0), expired.Color);
    }

    // ---- What is remembered about one --------------------------------------------------

    [Fact]
    public void A_card_comes_back_from_disk_as_it_went_in()
    {
        Cards().Add(
            new LaunchInvite { Code = "aBcD1234", SessionId = "918273645", TitleId = "heat-1995" },
            channelId: 42, messageId: 99);

        var card = Assert.Single(Cards().All());

        Assert.Equal(42ul, card.ChannelId);
        Assert.Equal(99ul, card.MessageId);
        Assert.Equal("918273645", card.SessionId);
        Assert.Equal("heat-1995", card.TitleId);
        Assert.Equal("aBcD1234", card.InviteCode);
    }

    [Fact]
    public void A_card_dealt_with_is_forgotten_and_a_second_room_is_left_alone()
    {
        var cards = Cards();
        cards.Add(new LaunchInvite { Code = "one", SessionId = "111", TitleId = "heat-1995" }, 42, 99);
        cards.Add(new LaunchInvite { Code = "two", SessionId = "222", TitleId = "the-mask-1994" }, 42, 100);

        cards.Forget(cards.All().First(c => c.MessageId == 99));

        Assert.Equal("two", Assert.Single(Cards().All()).InviteCode);
    }

    [Fact]
    public void A_message_that_cannot_be_named_is_not_written_down()
    {
        var cards = Cards();
        cards.Add(new LaunchInvite { Code = "one", SessionId = "111", TitleId = "heat-1995" }, 0, 99);
        cards.Add(new LaunchInvite { Code = "two", SessionId = "222", TitleId = "heat-1995" }, 42, 0);

        Assert.Empty(cards.All());
    }
}
