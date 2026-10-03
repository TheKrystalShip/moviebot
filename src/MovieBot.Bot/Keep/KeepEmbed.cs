using Discord;

namespace TheKrystalShip.MovieBot.Bot.Keep;

/// <summary>
/// Every settled film and where each stands, in one place, for the reason every other embed
/// has one: two spellings of the same list would look like two programs.
/// </summary>
public static class KeepEmbed
{
    public static Embed List(IReadOnlyList<KeptFilm> films) =>
        new EmbedBuilder()
            .WithTitle("Films in the library")
            .WithDescription(films.Count == 0
                ? "None have settled yet. Fetch one with /watch."
                : string.Join("\n", films.Select(Line)))
            .WithFooter("A film leaves once nobody has played it for a while, unless somebody keeps it.")
            .WithColor(Color.Blue)
            .Build();

    private static string Line(KeptFilm film) =>
        film.IsKept
            ? $"**{film.Name}** — kept by {KeepCommand.Who(film)}"
            : film.DaysLeft is { } days and > 0
                ? $"**{film.Name}** — leaves in {days} {(days == 1 ? "day" : "days")}"
                : $"**{film.Name}** — leaves today";
}
