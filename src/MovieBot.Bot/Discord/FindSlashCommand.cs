using Discord;

namespace TheKrystalShip.MovieBot.Bot.Discord;

/// <summary>
/// The shape of the command as Discord holds it. Kept apart from the handler so the registration
/// and the dispatch cannot drift on an option name.
/// </summary>
public static class FindSlashCommand
{
    public const string Name = "find";
    public const string TitleOption = "title";

    public static SlashCommandProperties Build() =>
        new SlashCommandBuilder()
            .WithName(Name)
            .WithDescription("Find a film that is not in the library yet, and start downloading it")
            .AddOption(
                TitleOption,
                ApplicationCommandOptionType.String,
                "The film, and its year if you know it",
                isRequired: true,
                isAutocomplete: true)
            .Build();
}
