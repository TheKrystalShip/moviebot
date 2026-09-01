using Discord;

namespace TheKrystalShip.MovieBot.Bot.Discord;

/// <summary>
/// The shape of the command as Discord holds it. Kept apart from the handler so the registration
/// and the dispatch cannot drift on an option name.
/// </summary>
public static class WatchSlashCommand
{
    public const string Name = "watch";
    public const string TitleOption = "title";

    public static SlashCommandProperties Build() =>
        new SlashCommandBuilder()
            .WithName(Name)
            .WithDescription("Start a film in your voice channel")
            .AddOption(
                TitleOption,
                ApplicationCommandOptionType.String,
                "Which film, by name or id",
                isRequired: true,
                isAutocomplete: true)
            .Build();
}
