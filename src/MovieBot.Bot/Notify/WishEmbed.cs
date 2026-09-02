using Discord;
using TheKrystalShip.MovieBot.Acquire.Imdb;
using TheKrystalShip.MovieBot.Acquire.Search;

namespace TheKrystalShip.MovieBot.Bot.Notify;

/// <summary>
/// Every way a wish is shown, in one place, for the same reason <c>DownloadEmbed</c> exists: a
/// second spelling would produce two messages about one film that look like they came from
/// different programs.
///
/// The poster is the small image. Posters are portrait, and a large one fills a message with
/// artwork nobody asked to look at.
/// </summary>
public static class WishEmbed
{
    /// <summary>The width the poster is asked for; a thumbnail is shown far smaller than this.</summary>
    private const int PosterWidth = 300;

    /// <summary>The reply to somebody who has just asked to be told.</summary>
    public static Embed Waiting(Wish wish)
    {
        var others = wish.Subscribers.Count - 1;

        return Film(wish)
            .WithDescription(
                "Not on the tracker yet. You will be told in this channel when it can be downloaded."
                + (others switch
                {
                    0 => "",
                    1 => " One other person is waiting on it too.",
                    _ => $" {others} other people are waiting on it too.",
                }))
            .WithColor(Color.Blue)
            .Build();
    }

    /// <summary>The announcement: the film can be downloaded, and this is the release that says so.</summary>
    public static Embed Available(Wish wish, Release release) =>
        Film(wish)
            .WithDescription($"{wish.Display} can be downloaded now.")
            .AddField("Release", release.ReleaseName, inline: false)
            .AddField("Quality", release.Summary, inline: true)
            .AddField("Watch it with", "/watch", inline: true)
            .WithColor(Color.Green)
            .WithCurrentTimestamp()
            .Build();

    /// <summary>What one person is waiting on.</summary>
    public static Embed List(IReadOnlyList<Wish> wishes) =>
        new EmbedBuilder()
            .WithTitle("Films you are waiting on")
            .WithDescription(wishes.Count == 0
                ? "None. Ask with /notify add and you will be told when one can be downloaded."
                : string.Join("\n", wishes.Select(w => $"[{w.Display}]({w.Url})")))
            .WithColor(Color.Blue)
            .Build();

    private static EmbedBuilder Film(Wish wish)
    {
        var builder = new EmbedBuilder()
            .WithTitle(wish.Display)
            .WithUrl(wish.Url);

        if (wish.Starring is { Length: > 0 } starring)
            builder.AddField("Starring", starring, inline: false);

        // Asked for at the size it will be seen, through the catalogue's own sizing rule.
        if (Uri.TryCreate(wish.PosterUrl, UriKind.Absolute, out var poster))
            builder.WithThumbnailUrl(new ImdbTitle
            {
                ImdbId = wish.ImdbId, Title = wish.Title, PosterUrl = poster
            }.PosterAt(PosterWidth)?.ToString());

        return builder;
    }
}
