using Discord;

namespace TheKrystalShip.MovieBot.Bot.Discord;

/// <summary>
/// The shape of the command as Discord holds it. Kept apart from the handler so the registration
/// and the dispatch cannot drift on an option name.
/// </summary>
public static class DownloadSlashCommand
{
    public const string Name = "download";
    public const string FilmOption = "film";

    public static SlashCommandProperties Build() =>
        new SlashCommandBuilder()
            .WithName(Name)
            .WithDescription("Fetch a film for later, without changing what anyone is watching")
            .AddOption(
                FilmOption,
                ApplicationCommandOptionType.String,
                "Which film to fetch. The tracker is searched as you type",
                isRequired: true,
                isAutocomplete: true)
            .Build();
}
