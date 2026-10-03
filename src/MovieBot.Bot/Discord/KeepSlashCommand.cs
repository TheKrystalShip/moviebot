using Discord;

namespace TheKrystalShip.MovieBot.Bot.Discord;

/// <summary>
/// The shape of the command as Discord holds it. Kept apart from the handler so the registration
/// and the dispatch cannot drift on an option name.
/// </summary>
public static class KeepSlashCommand
{
    public const string Name = "keep";
    public const string Add = "add";
    public const string Remove = "remove";
    public const string List = "list";
    public const string FilmOption = "film";

    public static SlashCommandProperties Build() =>
        new SlashCommandBuilder()
            .WithName(Name)
            .WithDescription("Keep a film in the library instead of letting it leave")
            .AddOption(new SlashCommandOptionBuilder()
                .WithName(Add)
                .WithDescription("Keep a film. It will not leave until somebody removes the keep")
                .WithType(ApplicationCommandOptionType.SubCommand)
                .AddOption(
                    FilmOption,
                    ApplicationCommandOptionType.String,
                    "Which of the films in the library",
                    isRequired: true,
                    isAutocomplete: true))
            .AddOption(new SlashCommandOptionBuilder()
                .WithName(Remove)
                .WithDescription("Stop keeping a film, so it leaves once nobody has played it for a while")
                .WithType(ApplicationCommandOptionType.SubCommand)
                .AddOption(
                    FilmOption,
                    ApplicationCommandOptionType.String,
                    "Which of the kept films",
                    isRequired: true,
                    isAutocomplete: true))
            .AddOption(new SlashCommandOptionBuilder()
                .WithName(List)
                .WithDescription("Every settled film, who keeps it, and when the rest leave")
                .WithType(ApplicationCommandOptionType.SubCommand))
            .Build();
}
