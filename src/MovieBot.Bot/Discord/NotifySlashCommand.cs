using Discord;

namespace TheKrystalShip.MovieBot.Bot.Discord;

/// <summary>
/// The shape of the command as Discord holds it. Kept apart from the handler so the registration
/// and the dispatch cannot drift on an option name.
///
/// Three subcommands rather than three commands, because they are one thing seen from three
/// sides and the list of commands is what a person reads to find out what the bot does.
/// </summary>
public static class NotifySlashCommand
{
    public const string Name = "notify";
    public const string Add = "add";
    public const string List = "list";
    public const string Cancel = "cancel";
    public const string FilmOption = "film";

    public static SlashCommandProperties Build() =>
        new SlashCommandBuilder()
            .WithName(Name)
            .WithDescription("Be told when a film can be downloaded")
            .AddOption(new SlashCommandOptionBuilder()
                .WithName(Add)
                .WithDescription("Be told when a film that is not out yet can be downloaded")
                .WithType(ApplicationCommandOptionType.SubCommand)
                .AddOption(
                    FilmOption,
                    ApplicationCommandOptionType.String,
                    "The film's name, or a link to its IMDb page",
                    isRequired: true,
                    isAutocomplete: true))
            .AddOption(new SlashCommandOptionBuilder()
                .WithName(List)
                .WithDescription("The films you are waiting on")
                .WithType(ApplicationCommandOptionType.SubCommand))
            .AddOption(new SlashCommandOptionBuilder()
                .WithName(Cancel)
                .WithDescription("Stop waiting on a film")
                .WithType(ApplicationCommandOptionType.SubCommand)
                .AddOption(
                    FilmOption,
                    ApplicationCommandOptionType.String,
                    "Which of the films you are waiting on",
                    isRequired: true,
                    isAutocomplete: true))
            .Build();
}
