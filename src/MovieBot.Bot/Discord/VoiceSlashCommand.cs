using Discord;

namespace TheKrystalShip.MovieBot.Bot.Discord;

/// <summary>
/// The shape of the command as Discord holds it. Kept apart from the handler so the registration
/// and the dispatch cannot drift on a subcommand name.
/// </summary>
public static class VoiceSlashCommand
{
    public const string Name = "voice";
    public const string Join = "join";
    public const string Leave = "leave";
    public const string Status = "status";

    public static SlashCommandProperties Build() =>
        new SlashCommandBuilder()
            .WithName(Name)
            .WithDescription("Let the bot listen in a voice channel, so the film can be paused by saying so")
            .AddOption(new SlashCommandOptionBuilder()
                .WithName(Join)
                .WithDescription("Join the voice channel you are in and start listening")
                .WithType(ApplicationCommandOptionType.SubCommand))
            .AddOption(new SlashCommandOptionBuilder()
                .WithName(Leave)
                .WithDescription("Stop listening and leave the voice channel")
                .WithType(ApplicationCommandOptionType.SubCommand))
            .AddOption(new SlashCommandOptionBuilder()
                .WithName(Status)
                .WithDescription("Whether the bot is listening here, and whether it is hearing anything")
                .WithType(ApplicationCommandOptionType.SubCommand))
            .Build();
}
