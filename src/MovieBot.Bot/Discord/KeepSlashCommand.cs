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
            .WithDescription("Keep a film on disk instead of letting it be pruned")
            .AddOption(new SlashCommandOptionBuilder()
                .WithName(Add)
                .WithDescription("Keep a film. It will not be pruned until somebody removes the keep")
                .WithType(ApplicationCommandOptionType.SubCommand)
                .AddOption(
                    FilmOption,
                    ApplicationCommandOptionType.String,
                    "Which of the films on disk",
                    isRequired: true,
                    isAutocomplete: true))
            .AddOption(new SlashCommandOptionBuilder()
                .WithName(Remove)
                .WithDescription("Stop keeping a film, so it is pruned once it has seeded long enough")
                .WithType(ApplicationCommandOptionType.SubCommand)
                .AddOption(
                    FilmOption,
                    ApplicationCommandOptionType.String,
                    "Which of the kept films",
                    isRequired: true,
                    isAutocomplete: true))
            .AddOption(new SlashCommandOptionBuilder()
                .WithName(List)
                .WithDescription("Every film on disk, who keeps it, and when the rest leave")
                .WithType(ApplicationCommandOptionType.SubCommand))
            .Build();
}
