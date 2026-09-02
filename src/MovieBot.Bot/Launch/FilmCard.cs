using Discord;
using TheKrystalShip.MovieBot.Bot.Api;

namespace TheKrystalShip.MovieBot.Bot.Launch;

/// <summary>
/// How a film is shown, wherever it is shown.
///
/// Every message about a film opens the same way — its name, its year, who is in it, its artwork
/// and somewhere to read more — and what differs between them is only what is being said about it.
/// Written once so two messages about the same film cannot describe it differently.
///
/// The poster is the small image rather than the large one. Posters are portrait and a large one
/// fills a message with artwork nobody asked to look at; beside the fields it reads as a card,
/// which is what this is.
/// </summary>
public static class FilmCard
{
    public static EmbedBuilder Build(LibraryTitle title, Uri? poster)
    {
        var embed = new EmbedBuilder().WithTitle(title.Name);

        if (poster is not null) embed.WithThumbnailUrl(poster.ToString());

        // Only what the catalogue actually answered. A field reading "unknown" is a field worth
        // leaving out.
        if (title.Film?.Starring is { Length: > 0 } starring)
            embed.AddField("Starring", starring, inline: true);

        if (title.Film?.Url is { Length: > 0 } page)
            embed.AddField("On IMDb", $"[{title.Film.ImdbId}]({page})", inline: true);

        return embed;
    }
}
