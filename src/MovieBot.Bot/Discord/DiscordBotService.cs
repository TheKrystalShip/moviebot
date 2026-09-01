using System.Net;
using Discord;
using Discord.WebSocket;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TheKrystalShip.MovieBot.Bot.Api;
using TheKrystalShip.MovieBot.Bot.Configuration;
using TheKrystalShip.MovieBot.Bot.Library;
using TheKrystalShip.MovieBot.Bot.Watch;

namespace TheKrystalShip.MovieBot.Bot.Discord;

/// <summary>
/// The gateway connection and the whole of the bot's Discord surface.
///
/// Its only job is translation: a slash command becomes a <see cref="WatchRequest"/>, and the
/// answer becomes a message. Nothing decided here is remembered, and every fact in a reply was
/// read from the API while the command ran.
/// </summary>
public sealed class DiscordBotService(
    DiscordSocketClient client,
    MovieBotApiClient api,
    WatchCommand watch,
    IOptions<DiscordOptions> options,
    ILogger<DiscordBotService> logger) : BackgroundService
{
    private readonly HashSet<ulong> _guilds = [.. options.Value.GuildIds];
    private int _commandsRegistered;
    private int _tokenRejectionReported;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (string.IsNullOrWhiteSpace(options.Value.Token))
            throw new InvalidOperationException(
                "No Discord token. Set MOVIEBOT_TOKEN in the environment, or Discord:Token in user-secrets.");

        if (_guilds.Count == 0)
            throw new InvalidOperationException(
                "No guilds configured. Set Discord__GuildIds__0 to the id of the server this bot serves.");

        client.Log += OnLog;
        client.Ready += OnReadyAsync;
        client.SlashCommandExecuted += OnSlashCommand;
        client.AutocompleteExecuted += OnAutocomplete;

        // Reported once, on the log, so an unreachable API is not discovered for the first time
        // in front of a room.
        if (!await api.IsHealthyAsync(stoppingToken))
            logger.LogWarning("The API is not answering yet. Commands will refuse until it does.");

        // A bot that is running and not in the server looks exactly like one that is broken, so
        // the way to put it there is on the log next to everything else about starting up.
        if (options.Value.ApplicationId is { } applicationId)
            logger.LogInformation(
                "Invite this application with https://discord.com/oauth2/authorize"
                + "?client_id={ApplicationId}&scope=bot+applications.commands&permissions={Permissions}",
                applicationId,
                (ulong)(GuildPermission.SendMessages | GuildPermission.EmbedLinks));

        await client.LoginAsync(TokenType.Bot, options.Value.Token);
        await client.StartAsync();

        await Task.Delay(Timeout.Infinite, stoppingToken).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);

        await client.StopAsync();
    }

    /// <summary>
    /// Registers the command in each configured guild. Guild commands land immediately, where a
    /// global command takes an hour to propagate, and the overwrite is by definition idempotent.
    /// </summary>
    private async Task OnReadyAsync()
    {
        if (Interlocked.Exchange(ref _commandsRegistered, 1) == 1) return;

        foreach (var guildId in _guilds)
        {
            try
            {
                var guild = client.GetGuild(guildId);
                if (guild is null)
                {
                    logger.LogWarning("Guild {GuildId} is configured but the bot is not in it", guildId);
                    continue;
                }

                await guild.BulkOverwriteApplicationCommandAsync([WatchSlashCommand.Build()]);
                logger.LogInformation("Registered /{Command} in {GuildName}", WatchSlashCommand.Name, guild.Name);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Could not register commands in guild {GuildId}", guildId);
            }
        }
    }

    // Discord.Net awaits an event handler before dispatching the next event, so the work runs
    // off the gateway task. The handler swallows nothing: every path either answers the person
    // or logs why it could not.
    private Task OnSlashCommand(SocketSlashCommand command)
    {
        _ = HandleWatchAsync(command);
        return Task.CompletedTask;
    }

    private Task OnAutocomplete(SocketAutocompleteInteraction interaction)
    {
        _ = HandleAutocompleteAsync(interaction);
        return Task.CompletedTask;
    }

    private async Task HandleWatchAsync(SocketSlashCommand command)
    {
        try
        {
            if (command.CommandName != WatchSlashCommand.Name) return;

            // A verified application cannot be stopped from being added to a server, so the
            // guild it serves is enforced here rather than in the portal.
            if (command.GuildId is not { } guildId || !_guilds.Contains(guildId))
            {
                await command.RespondAsync(
                    "This bot only answers in the server it is configured for.", ephemeral: true);
                return;
            }

            var voice = (command.User as SocketGuildUser)?.VoiceChannel;
            var query = command.Data.Options
                .FirstOrDefault(o => o.Name == WatchSlashCommand.TitleOption)?.Value as string ?? "";

            await command.DeferAsync();

            var result = await watch.ExecuteAsync(new WatchRequest
            {
                VoiceChannelId = voice?.Id,
                VoiceChannelName = voice?.Name,
                Query = query,
                RequestedBy = (command.User as IGuildUser)?.DisplayName ?? command.User.Username
            }, CancellationToken.None);

            // Display names are whatever a person set them to, so nothing in a reply is allowed
            // to notify anyone.
            if (result.Reply is { } reply)
                await command.FollowupAsync(
                    reply.Text,
                    embed: reply.Embed,
                    components: reply.Components,
                    allowedMentions: AllowedMentions.None);
            else
                await command.FollowupAsync(result.Message, allowedMentions: AllowedMentions.None);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "The /{Command} command failed", WatchSlashCommand.Name);
            await TryReportFailure(command);
        }
    }

    private async Task HandleAutocompleteAsync(SocketAutocompleteInteraction interaction)
    {
        try
        {
            if (interaction.GuildId is not { } guildId || !_guilds.Contains(guildId))
            {
                await interaction.RespondAsync([]);
                return;
            }

            var typed = interaction.Data.Current.Value?.ToString() ?? "";
            var library = await api.ListTitlesAsync(CancellationToken.None);

            await interaction.RespondAsync(TitleMatcher.Suggest(library, typed)
                .Select(t => new AutocompleteResult(Choice(t), t.Id)));
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not offer title suggestions");

            // An empty list leaves the person free to type an id, where an unanswered
            // autocomplete leaves the menu spinning.
            try { await interaction.RespondAsync([]); } catch (Exception inner) { logger.LogDebug(inner, "The autocomplete interaction was already gone"); }
        }
    }

    /// <summary>Autocomplete labels are capped at 100 characters by Discord.</summary>
    private static string Choice(LibraryTitle title) =>
        title.Title.Length <= 100 ? title.Title : title.Title[..99] + "…";

    private async Task TryReportFailure(SocketSlashCommand command)
    {
        try
        {
            if (command.HasResponded)
                await command.FollowupAsync("That command did not work. The log has the reason.",
                    ephemeral: true);
            else
                await command.RespondAsync("That command did not work. The log has the reason.",
                    ephemeral: true);
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "The interaction was already gone");
        }
    }

    private Task OnLog(LogMessage message)
    {
        // Discord.Net treats a rejected token as a gateway error and reconnects forever, so the
        // reason scrolls past as one 401 among many. Said once, plainly, naming what to fix.
        if (message.Exception is global::Discord.Net.HttpException { HttpCode: HttpStatusCode.Unauthorized }
            && Interlocked.Exchange(ref _tokenRejectionReported, 1) == 0)
        {
            logger.LogError(
                "Discord rejected the token. MOVIEBOT_TOKEN is not this application's bot token.");
        }

        var level = message.Severity switch
        {
            LogSeverity.Critical => LogLevel.Critical,
            LogSeverity.Error => LogLevel.Error,
            LogSeverity.Warning => LogLevel.Warning,
            LogSeverity.Info => LogLevel.Information,
            LogSeverity.Verbose => LogLevel.Debug,
            _ => LogLevel.Trace
        };

        logger.Log(level, message.Exception, "{Source}: {Message}", message.Source, message.Message);
        return Task.CompletedTask;
    }
}
