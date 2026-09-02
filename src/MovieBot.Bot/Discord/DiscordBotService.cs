using System.Net;
using Discord;
using Discord.WebSocket;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TheKrystalShip.MovieBot.Bot.Api;
using TheKrystalShip.MovieBot.Bot.Configuration;
using TheKrystalShip.MovieBot.Bot.Find;
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
    FindCommand find,
    TheKrystalShip.MovieBot.Acquire.Search.AutocompleteSearch suggestions,
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
                // CreateInstantInvite is what an Activity launch is made of, so it belongs in
                // the link that installs the bot rather than being discovered missing the first
                // time somebody asks for a film in a voice channel.
                (ulong)(GuildPermission.CreateInstantInvite
                        | GuildPermission.ViewChannel
                        | GuildPermission.SendMessages
                        | GuildPermission.EmbedLinks));

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

                // The overwrite is the whole command set, so every command has to be in this
                // one call: registering them separately leaves only the last one standing.
                await guild.BulkOverwriteApplicationCommandAsync(
                    [WatchSlashCommand.Build(), FindSlashCommand.Build()]);

                logger.LogInformation("Registered /{Watch} and /{Find} in {GuildName}",
                    WatchSlashCommand.Name, FindSlashCommand.Name, guild.Name);
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
        _ = command.CommandName switch
        {
            WatchSlashCommand.Name => HandleWatchAsync(command),
            FindSlashCommand.Name => HandleFindAsync(command),
            _ => Task.CompletedTask,
        };

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

    /// <summary>
    /// Answers the person who asked, then starts the download.
    ///
    /// The reply is deliberately not a live progress bar. A film takes minutes at best, an
    /// interaction token expires long before that, and an edited message nobody is looking at is
    /// worth less than a clear sentence saying to come back.
    /// </summary>
    private async Task HandleFindAsync(SocketSlashCommand command)
    {
        try
        {
            if (command.GuildId is not { } guildId || !_guilds.Contains(guildId))
            {
                await command.RespondAsync(
                    "This bot only answers in the server it is configured for.", ephemeral: true);
                return;
            }

            var chosen = command.Data.Options
                .FirstOrDefault(o => o.Name == FindSlashCommand.TitleOption)?.Value as string ?? "";

            await command.DeferAsync();

            var result = await find.ExecuteAsync(new FindRequest
            {
                Chosen = chosen,
                ChannelId = command.ChannelId ?? 0,
                RequesterId = command.User.Id,
                RequestedBy = (command.User as IGuildUser)?.DisplayName ?? command.User.Username,
            }, CancellationToken.None);

            if (result.Status != FindStatus.Started || result.Release is null)
            {
                await command.FollowupAsync(result.Message, allowedMentions: AllowedMentions.None);
                return;
            }

            var posted = await command.FollowupAsync(
                embed: DownloadEmbed.Starting(result.Release),
                allowedMentions: AllowedMentions.None);

            // Recorded after the message exists, because the message is what is being recorded.
            // The download is already running; this only decides whether it reports itself.
            if (result.Hash is { } hash)
                await find.RecordProgressMessageAsync(
                    hash, command.ChannelId ?? 0, posted.Id, CancellationToken.None);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "The /{Command} command failed", FindSlashCommand.Name);
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

            // Both commands autocomplete a title and they mean entirely different things by it:
            // one searches films already on disk, the other searches the tracker. Answering
            // without checking which was asked offers the library to somebody trying to
            // download something that is by definition not in it.
            if (interaction.Data.CommandName == FindSlashCommand.Name)
            {
                var found = await suggestions.SuggestAsync(
                    typed, interaction.User.Id.ToString(), CancellationToken.None);

                await interaction.RespondAsync(found
                    .Select(c => new AutocompleteResult(c.Label, c.TorrentId.ToString())));
                return;
            }

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
        title.Name.Length <= 100 ? title.Name : title.Name[..99] + "…";

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
