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
            .WithDescription("Watch a film in your voice channel, fetching it first if it is not here yet")
            .AddOption(
                TitleOption,
                ApplicationCommandOptionType.String,
                "Which film. Films already here are listed; anything else is searched for",
                isRequired: true,
                isAutocomplete: true)
            .Build();
}
