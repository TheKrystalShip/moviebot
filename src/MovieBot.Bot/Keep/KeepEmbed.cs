using Discord;
using TheKrystalShip.MovieBot.Acquire.Download;

namespace TheKrystalShip.MovieBot.Bot.Keep;

/// <summary>
/// Every film on disk and where each stands, in one place, for the reason every other embed
/// has one: two spellings of the same list would look like two programs.
/// </summary>
public static class KeepEmbed
{
    public static Embed List(IReadOnlyList<KeptFilm> films, Retention retention) =>
        new EmbedBuilder()
            .WithTitle("Films on disk")
            .WithDescription(films.Count == 0
                ? "None. Fetch one with /watch."
                : string.Join("\n", films.Select(Line)))
            .WithFooter(
                $"A film leaves after {Retention.Describe(retention.Window)} of seeding unless "
                + "somebody keeps it. Seeding only counts while this machine is on.")
            .WithColor(Color.Blue)
            .Build();

    private static string Line(KeptFilm film) =>
        film.Keeper is { } keeper
            ? $"**{film.Name}** — kept by {KeepCommand.Mention(keeper)}"
            : !film.IsFinished
                ? $"**{film.Name}** — still downloading"
                : film.Remaining == TimeSpan.Zero
                    ? $"**{film.Name}** — leaving"
                    : $"**{film.Name}** — leaves after {Retention.Describe(film.Remaining)} more of seeding";
}
