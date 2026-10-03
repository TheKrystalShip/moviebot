using Discord;

namespace TheKrystalShip.MovieBot.Bot.Discord;

/// <summary>
/// The shape of /show as Discord holds it: fetching some or all of one season of a show. Kept apart
/// from the handler so the registration and the dispatch cannot drift on an option name.
/// </summary>
public static class ShowSlashCommand
{
    public const string Name = "show";
    public const string ShowOption = "show";
    public const string SeasonOption = "season";
    public const string EpisodesOption = "episodes";
    public const string QualityOption = "quality";

    public static SlashCommandProperties Build() =>
        new SlashCommandBuilder()
            .WithName(Name)
            .WithDescription("Fetch episodes of a show, without changing what anyone is watching")
            .AddOption(ShowOption, ApplicationCommandOptionType.String,
                "Which show. The catalogue is searched as you type", isRequired: true, isAutocomplete: true)
            .AddOption(SeasonOption, ApplicationCommandOptionType.Integer,
                "Which season", isRequired: true, minValue: 1)
            .AddOption(EpisodesOption, ApplicationCommandOptionType.String,
                "Which episodes, such as 1-3, 5. Leave it out for the whole season", isRequired: false)
            .AddOption(new SlashCommandOptionBuilder()
                .WithName(QualityOption)
                .WithDescription("720p takes a third of the space and is what shows are fetched at unless you say")
                .WithType(ApplicationCommandOptionType.String)
                .AddChoice("720p", "720p")
                .AddChoice("1080p", "1080p"))
            .Build();
}
