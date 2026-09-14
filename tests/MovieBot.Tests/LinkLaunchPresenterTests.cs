using Xunit;
using Microsoft.Extensions.Options;
using TheKrystalShip.MovieBot.Bot.Api;
using TheKrystalShip.MovieBot.Bot.Configuration;
using TheKrystalShip.MovieBot.Bot.Launch;
using TheKrystalShip.MovieBot.Core;

namespace TheKrystalShip.MovieBot.Tests;

/// <summary>
/// The message the room actually sees. The link inside it is the entire handover, so it is
/// checked character by character rather than by whether a reply came back.
/// </summary>
public sealed class LinkLaunchPresenterTests
{
    private const string Player = "https://movies.example.com/watch";
    private const string PublicApi = "https://movies.example.com/api";

    private static LinkLaunchPresenter Presenter(string player = Player, string? publicApi = PublicApi) =>
        new(Options.Create(new PlayerOptions { BaseUrl = player }),
            Options.Create(new ApiOptions { BaseUrl = "http://127.0.0.1:8099", PublicBaseUrl = publicApi }));

    private static LaunchRequest Request(LibraryTitle? title = null, LibraryTitle? replaced = null) => new()
    {
        SessionId = "918273645",
        Title = title ?? Ready,
        VoiceChannelId = 918273645,
        VoiceChannelName = "Movie Night",
        RequestedBy = "Alice",
        Replaced = replaced
    };

    private static readonly LibraryTitle Heat = new()
    {
        Id = "heat-1995",
        Title = "Heat 1995",
        DurationSeconds = 10218,
        Status = TitleStatus.Ready,
        Film = new FilmIdentity { Name = "Heat", Year = 1995 }
    };

    private static readonly LibraryTitle Ready = new()
    {
        Id = "gladiator-2000-extended",
        Title = "Gladiator 2000 Extended Cut",
        DurationSeconds = 10260.5,
        Status = TitleStatus.Ready,
        Poster = "poster.jpg"
    };

    [Fact]
    public async Task The_link_carries_the_room_and_the_film()
    {
        var reply = await Presenter().PresentAsync(Request(), CancellationToken.None);

        Assert.Equal(
            "https://movies.example.com/watch?session=918273645&title=gladiator-2000-extended",
            reply.Embed.Url);
    }

    [Fact]
    public async Task A_player_address_that_already_carries_a_query_keeps_it()
    {
        var reply = await Presenter(player: "https://movies.example.com/watch?debug=1")
            .PresentAsync(Request(), CancellationToken.None);

        Assert.Equal(
            "https://movies.example.com/watch?debug=1&session=918273645&title=gladiator-2000-extended",
            reply.Embed.Url);
    }

    [Fact]
    public async Task The_poster_is_offered_only_when_an_address_Discord_can_reach_is_configured()
    {
        var withPoster = await Presenter().PresentAsync(Request(), CancellationToken.None);
        Assert.Equal(
            "https://movies.example.com/api/media/gladiator-2000-extended/poster.jpg",
            withPoster.Embed.Thumbnail?.Url);

        // Discord fetches embed images itself, so a loopback-only API means no poster at all
        // rather than an embed with a hole in it.
        var noPublicAddress = await Presenter(publicApi: null)
            .PresentAsync(Request(), CancellationToken.None);
        Assert.Null(noPublicAddress.Embed.Thumbnail);

        var noArtwork = await Presenter()
            .PresentAsync(Request(Ready with { Poster = null }), CancellationToken.None);
        Assert.Null(noArtwork.Embed.Thumbnail);
    }

    /// <summary>
    /// The film's name, not the release's. A release title keeps whichever edition words were
    /// typed into it and loses the punctuation a name has, and it is the only thing there is
    /// until the catalogue has been asked.
    /// </summary>
    [Fact]
    public async Task A_film_is_named_by_the_catalogue_where_the_catalogue_knows_it()
    {
        var unknown = await Presenter().PresentAsync(Request(), CancellationToken.None);
        Assert.Equal("Gladiator 2000 Extended Cut", unknown.Embed.Title);

        var known = await Presenter().PresentAsync(Request(Identified), CancellationToken.None);
        Assert.Equal("Gladiator (2000)", known.Embed.Title);
    }

    [Fact]
    public async Task What_the_catalogue_answered_is_shown_and_what_it_did_not_is_left_out()
    {
        var known = (await Presenter().PresentAsync(Request(Identified), CancellationToken.None)).Embed;

        Assert.Equal("Russell Crowe, Joaquin Phoenix",
            known.Fields.Single(f => f.Name == "Starring").Value);
        Assert.Equal("[tt0172495](https://www.imdb.com/title/tt0172495/)",
            known.Fields.Single(f => f.Name == "On IMDb").Value);

        var unknown = (await Presenter().PresentAsync(Request(), CancellationToken.None)).Embed;

        Assert.DoesNotContain(unknown.Fields, f => f.Name == "Starring");
        Assert.DoesNotContain(unknown.Fields, f => f.Name == "On IMDb");
    }

    private static readonly LibraryTitle Identified = Ready with
    {
        Film = new FilmIdentity
        {
            ImdbId = "tt0172495",
            Name = "Gladiator",
            Year = 2000,
            Starring = "Russell Crowe, Joaquin Phoenix"
        }
    };

    [Fact]
    public async Task A_ready_film_and_a_transcoding_one_promise_different_things()
    {
        var ready = await Presenter().PresentAsync(Request(), CancellationToken.None);
        Assert.Contains("seekable", ready.Embed.Description);
        Assert.DoesNotContain("transcoding", ready.Embed.Description);

        var cooking = await Presenter().PresentAsync(
            Request(Ready with { Status = TitleStatus.Transcoding, HeadSeconds = 1284 }),
            CancellationToken.None);

        Assert.Contains("Still transcoding", cooking.Embed.Description);
        Assert.Contains("Playback starts now", cooking.Embed.Description);
        Assert.Contains("21m 24s", cooking.Embed.Description);
    }

    [Fact]
    public async Task The_reply_names_the_film_the_room_and_who_asked()
    {
        var reply = await Presenter().PresentAsync(Request(), CancellationToken.None);

        Assert.Equal("Gladiator 2000 Extended Cut", reply.Embed.Title);
        Assert.Contains("Alice", reply.Text);
        Assert.Contains("Movie Night", reply.Text);
        Assert.Equal("Movie Night", Field(reply, "Voice channel"));
        Assert.Equal("2h 51m", Field(reply, "Length"));
        Assert.Contains("Alice", reply.Embed.Footer?.Text);
    }

    [Fact]
    public async Task A_switch_names_the_film_it_replaced_and_a_fresh_start_names_none()
    {
        var switching = await Presenter().PresentAsync(Request(replaced: Heat), CancellationToken.None);
        Assert.Equal("Heat (1995)", Field(switching, "Replaced"));
        Assert.Equal("Alice switched Movie Night from Heat (1995) to Gladiator 2000 Extended Cut.", switching.Text);

        var fresh = await Presenter().PresentAsync(Request(), CancellationToken.None);
        Assert.Null(Field(fresh, "Replaced"));
        Assert.Equal("Alice started Gladiator 2000 Extended Cut in Movie Night.", fresh.Text);

        var again = await Presenter().PresentAsync(
            Request() with { AlreadyWatching = true }, CancellationToken.None);
        Assert.Equal("Movie Night is already watching Gladiator 2000 Extended Cut. Alice asked for it again.", again.Text);
    }

    [Fact]
    public async Task The_button_opens_the_same_link_the_embed_does()
    {
        var reply = await Presenter().PresentAsync(Request(), CancellationToken.None);

        var row = Assert.IsType<global::Discord.ActionRowComponent>(Assert.Single(reply.Components!.Components));
        var button = Assert.IsType<global::Discord.ButtonComponent>(Assert.Single(row.Components));

        Assert.Equal(reply.Embed.Url, button.Url);
        Assert.Equal(global::Discord.ButtonStyle.Link, button.Style);
    }

    [Fact]
    public async Task Nothing_the_bot_writes_carries_an_emoji()
    {
        var reply = await Presenter().PresentAsync(
            Request(Ready with { Status = TitleStatus.Transcoding, HeadSeconds = 1284 }, replaced: Heat),
            CancellationToken.None);

        string?[] written =
        [
            reply.Text, reply.Embed.Title, reply.Embed.Description, reply.Embed.Footer?.Text,
            .. reply.Embed.Fields.SelectMany(f => new[] { f.Name, f.Value })
        ];

        foreach (var text in written)
            Assert.DoesNotContain(text ?? "", c => IsEmoji(c));
    }

    private static string? Field(LaunchReply reply, string name) =>
        reply.Embed.Fields.Where(f => f.Name == name).Select(f => f.Value).FirstOrDefault();

    /// <summary>
    /// Emoji live above the basic plane or in the symbol and dingbat blocks, and the variation
    /// selector is what promotes a symbol into one. The arrows, dashes, ellipses and pass/fail
    /// marks this ecosystem does use are typography and stay.
    /// </summary>
    private static bool IsEmoji(char c) =>
        !"\u2713\u2717".Contains(c)
        && (char.IsSurrogate(c)
            || c is >= '\u2600' and <= '\u27BF'
            || c is >= '\u2B00' and <= '\u2BFF'
            || c is '\uFE0F');
}
