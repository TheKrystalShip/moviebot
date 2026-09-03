using System.Net;
using Discord;
using Discord.WebSocket;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TheKrystalShip.MovieBot.Bot.Api;
using TheKrystalShip.MovieBot.Bot.Configuration;
using TheKrystalShip.MovieBot.Bot.Download;
using TheKrystalShip.MovieBot.Bot.Keep;
using TheKrystalShip.MovieBot.Bot.Library;
using TheKrystalShip.MovieBot.Bot.Notify;
using TheKrystalShip.MovieBot.Bot.Watch;

namespace TheKrystalShip.MovieBot.Bot.Discord;

/// <summary>
/// The gateway connection and the whole of the bot's Discord surface.
///
/// Its only job is translation: a slash command becomes a <see cref="WatchRequest"/> or a
/// <see cref="DownloadRequest"/>, and the answer becomes a message. Nothing decided here is
/// remembered, and every fact in a reply was read from the API while the command ran.
/// </summary>
public sealed class DiscordBotService(
    DiscordSocketClient client,
    MovieBotApiClient api,
    WatchCommand watch,
    DownloadCommand download,
    NotifyCommand notify,
    KeepCommand keep,
    TheKrystalShip.MovieBot.Acquire.Download.Retention retention,
    TheKrystalShip.MovieBot.Acquire.Search.AutocompleteSearch suggestions,
    TheKrystalShip.MovieBot.Acquire.Imdb.ImdbClient catalogue,
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
                // time somebody asks for a film in a voice channel. The line under a voice channel
                // needs both of the last two: Discord asks for Manage Channels as well from a bot
                // that is not itself connected to the channel.
                (ulong)(GuildPermission.CreateInstantInvite
                        | GuildPermission.ViewChannel
                        | GuildPermission.SendMessages
                        | GuildPermission.EmbedLinks
                        | GuildPermission.SetVoiceChannelStatus
                        | GuildPermission.ManageChannels));

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
                    [WatchSlashCommand.Build(), NotifySlashCommand.Build(), KeepSlashCommand.Build()]);

                logger.LogInformation("Registered /{Watch}, /{Notify} and /{Keep} in {GuildName}",
                    WatchSlashCommand.Name, NotifySlashCommand.Name, KeepSlashCommand.Name, guild.Name);
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
            NotifySlashCommand.Name => HandleNotifyAsync(command),
            KeepSlashCommand.Name => HandleKeepAsync(command),
            _ => Task.CompletedTask,
        };

        return Task.CompletedTask;
    }

    private Task OnAutocomplete(SocketAutocompleteInteraction interaction)
    {
        _ = HandleAutocompleteAsync(interaction);
        return Task.CompletedTask;
    }

    /// <summary>
    /// Plays the film, or fetches it and then plays it.
    ///
    /// One option carries two kinds of answer: a film already in the library, which is loaded into
    /// the room now, and a release on the tracker, which is started downloading and loaded into the
    /// room the moment it can be watched. Which one arrived is read from the value alone, because
    /// the value is all a picked row sends back.
    /// </summary>
    private async Task HandleWatchAsync(SocketSlashCommand command)
    {
        try
        {
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
            var requestedBy = (command.User as IGuildUser)?.DisplayName ?? command.User.Username;

            await command.DeferAsync();

            if (TrackerPick.Parse(query) is { } torrentId)
            {
                await FetchThenWatchAsync(command, torrentId, voice, requestedBy);
                return;
            }

            var result = await watch.ExecuteAsync(new WatchRequest
            {
                VoiceChannelId = voice?.Id,
                VoiceChannelName = voice?.Name,
                Query = query,
                RequestedBy = requestedBy
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
    /// Starts the download and answers with the message that will show its progress.
    ///
    /// The reply is the waiting state, not the launch. A film takes at least a minute to become
    /// watchable and an interaction token does not outlast a slow one, so the launch is a
    /// separate message, posted by the watcher when the film can be opened — which is also the
    /// only kind of message that notifies the person who asked.
    /// </summary>
    private async Task FetchThenWatchAsync(
        SocketSlashCommand command, long torrentId, SocketVoiceChannel? voice, string requestedBy)
    {
        var result = await download.ExecuteAsync(new DownloadRequest
        {
            TorrentId = torrentId,
            ChannelId = command.ChannelId ?? 0,
            RequesterId = command.User.Id,
            RequestedBy = requestedBy,
            RoomId = voice?.Id,
        }, CancellationToken.None);

        if (result.Outcome != DownloadOutcome.Started || result.Release is null)
        {
            await command.FollowupAsync(result.Message, allowedMentions: AllowedMentions.None);
            return;
        }

        var posted = await command.FollowupAsync(
            result.Message,
            embed: DownloadEmbed.Starting(result.Release, voice?.Name),
            allowedMentions: AllowedMentions.None);

        // Recorded after the message exists, because the message is what is being recorded.
        // The download is already running; this only decides whether it reports itself.
        if (result.Hash is { } hash)
            await download.RecordProgressMessageAsync(
                hash, command.ChannelId ?? 0, posted.Id, CancellationToken.None);
    }

    /// <summary>
    /// Puts the person on the list for a film, shows them the list, or takes them off it.
    ///
    /// The reply to asking is public, because somebody else in the channel wanting the same film
    /// is the ordinary case and the reply is how they find out they can ask too. The list is the
    /// person's own and goes to them alone.
    /// </summary>
    private async Task HandleNotifyAsync(SocketSlashCommand command)
    {
        try
        {
            if (command.GuildId is not { } guildId || !_guilds.Contains(guildId))
            {
                await command.RespondAsync(
                    "This bot only answers in the server it is configured for.", ephemeral: true);
                return;
            }

            var subcommand = command.Data.Options.FirstOrDefault();
            var film = subcommand?.Options?
                .FirstOrDefault(o => o.Name == NotifySlashCommand.FilmOption)?.Value as string ?? "";

            switch (subcommand?.Name)
            {
                case NotifySlashCommand.List:
                    await command.RespondAsync(
                        embed: WishEmbed.List(notify.List(command.User.Id)), ephemeral: true);
                    return;

                case NotifySlashCommand.Cancel:
                    var cancelled = notify.Cancel(command.User.Id, film);
                    await command.RespondAsync(
                        cancelled.Message, ephemeral: true, allowedMentions: AllowedMentions.None);
                    return;

                case NotifySlashCommand.Add:
                    break;

                default:
                    await command.RespondAsync("That is not one of the things /notify does.", ephemeral: true);
                    return;
            }

            await command.DeferAsync();

            var result = await notify.SubscribeAsync(new NotifyRequest
            {
                Chosen = film,
                ChannelId = command.ChannelId ?? 0,
                UserId = command.User.Id,
                RequestedBy = (command.User as IGuildUser)?.DisplayName ?? command.User.Username,
            }, CancellationToken.None);

            var embed = result switch
            {
                { Status: NotifyStatus.Subscribed or NotifyStatus.AlreadySubscribed, Wish: { } wish }
                    => WishEmbed.Waiting(wish),
                { Status: NotifyStatus.AlreadyAvailable, Wish: { } wish, Release: { } release }
                    => WishEmbed.Available(wish, release),
                _ => null,
            };

            await command.FollowupAsync(result.Message, embed: embed, allowedMentions: AllowedMentions.None);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "The /{Command} command failed", NotifySlashCommand.Name);
            await TryReportFailure(command);
        }
    }

    /// <summary>
    /// Keeps a film, lets one go, or lists where every film on disk stands.
    ///
    /// Keeping and releasing answer in public, because a film staying or going is the room's
    /// business and the reply is how the room finds out. The list is for whoever asked.
    /// </summary>
    private async Task HandleKeepAsync(SocketSlashCommand command)
    {
        try
        {
            if (command.GuildId is not { } guildId || !_guilds.Contains(guildId))
            {
                await command.RespondAsync(
                    "This bot only answers in the server it is configured for.", ephemeral: true);
                return;
            }

            var subcommand = command.Data.Options.FirstOrDefault();
            var film = subcommand?.Options?
                .FirstOrDefault(o => o.Name == KeepSlashCommand.FilmOption)?.Value as string ?? "";

            switch (subcommand?.Name)
            {
                case KeepSlashCommand.List:
                    await command.DeferAsync(ephemeral: true);
                    await command.FollowupAsync(
                        embed: KeepEmbed.List(await keep.ListAsync(CancellationToken.None), retention),
                        ephemeral: true, allowedMentions: AllowedMentions.None);
                    return;

                case KeepSlashCommand.Add:
                case KeepSlashCommand.Remove:
                    break;

                default:
                    await command.RespondAsync("That is not one of the things /keep does.", ephemeral: true);
                    return;
            }

            await command.DeferAsync();

            var result = subcommand.Name == KeepSlashCommand.Add
                ? await keep.KeepAsync(film, command.User.Id, CancellationToken.None)
                : await keep.ReleaseAsync(film, command.User.Id, CancellationToken.None);

            // The message names the keeper by mention so the name is always current, and the
            // mentions are forbidden so that naming them never pings them.
            await command.FollowupAsync(result.Message, allowedMentions: AllowedMentions.None);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "The /{Command} command failed", KeepSlashCommand.Name);
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

            // Both commands autocomplete a film and each means something different by it: one
            // the films that can be watched, one the catalogue of films that exist at all.
            // Answering without checking which was asked offers the library to somebody waiting
            // on a film that is by definition not in it.
            if (interaction.Data.CommandName == NotifySlashCommand.Name)
            {
                await interaction.RespondAsync(await NotifyChoicesAsync(interaction, typed));
                return;
            }

            if (interaction.Data.CommandName == KeepSlashCommand.Name)
            {
                await interaction.RespondAsync(await KeepChoicesAsync(interaction, typed));
                return;
            }

            await interaction.RespondAsync(await WatchChoicesAsync(interaction, typed));
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not offer title suggestions");

            // An empty list leaves the person free to type an id, where an unanswered
            // autocomplete leaves the menu spinning.
            try { await interaction.RespondAsync([]); } catch (Exception inner) { logger.LogDebug(inner, "The autocomplete interaction was already gone"); }
        }
    }

    /// <summary>
    /// What /watch offers as somebody types: the library while anything in it matches, and the
    /// tracker once nothing does.
    ///
    /// The library comes first and alone, because a film that is here is the answer and a row
    /// offering to fetch it again beside it is a way to end up with two. The tracker is asked only
    /// when the library has nothing, so somebody typing a film that is not here sees the search
    /// results appear in place of an empty menu — with what each release is beside its name, which
    /// is how a row that starts a download reads differently from one that starts a film.
    /// </summary>
    private async Task<IEnumerable<AutocompleteResult>> WatchChoicesAsync(
        SocketAutocompleteInteraction interaction, string typed)
    {
        var library = await api.ListTitlesAsync(CancellationToken.None);
        var here = TitleMatcher.Suggest(library, typed);

        if (here.Count > 0 || typed.Trim().Length == 0)
            return here.Select(t => new AutocompleteResult(Choice(t), t.Id));

        var offered = await suggestions.SuggestAsync(
            typed, interaction.User.Id.ToString(), CancellationToken.None);

        return offered.Select(c => new AutocompleteResult(c.Label, TrackerPick.Value(c.TorrentId)));
    }

    /// <summary>
    /// What /notify offers as somebody types: the catalogue's films for a wish being made, and
    /// the person's own wishes for one being cancelled. The options are flattened, so the
    /// subcommand is found by its type rather than its position.
    /// </summary>
    private async Task<IEnumerable<AutocompleteResult>> NotifyChoicesAsync(
        SocketAutocompleteInteraction interaction, string typed)
    {
        var subcommand = interaction.Data.Options
            .FirstOrDefault(o => o.Type == ApplicationCommandOptionType.SubCommand)?.Name;

        if (subcommand == NotifySlashCommand.Cancel)
            return notify.List(interaction.User.Id)
                .Where(w => w.Display.Contains(typed, StringComparison.OrdinalIgnoreCase))
                .Take(MaximumChoices)
                .Select(w => new AutocompleteResult(Choice(w.Display), w.ImdbId));

        // A single character matches half the catalogue, and the index answers per keystroke
        // anyway, so nothing is asked until there is something to ask about.
        if (typed.Trim().Length < 2) return [];

        var films = await catalogue.SuggestAsync(typed, CancellationToken.None);

        return films
            .Take(MaximumChoices)
            .Select(f => new AutocompleteResult(
                Choice(f.Starring is { Length: > 0 } cast ? $"{Display(f)} — {cast}" : Display(f)),
                f.ImdbId));
    }

    /// <summary>
    /// What /keep offers as somebody types: the films on disk that are not kept for a keep being
    /// added, and the kept ones for a keep being removed. The value behind a row is the
    /// download's hash, which is what the command acts on.
    /// </summary>
    private async Task<IEnumerable<AutocompleteResult>> KeepChoicesAsync(
        SocketAutocompleteInteraction interaction, string typed)
    {
        var subcommand = interaction.Data.Options
            .FirstOrDefault(o => o.Type == ApplicationCommandOptionType.SubCommand)?.Name;

        var removing = subcommand == KeepSlashCommand.Remove;
        var films = await keep.ListAsync(CancellationToken.None);

        return films
            .Where(f => f.IsKept == removing)
            .Where(f => typed.Trim().Length == 0
                        || f.Name.Contains(typed, StringComparison.OrdinalIgnoreCase)
                        || f.Release.Contains(typed, StringComparison.OrdinalIgnoreCase))
            .Take(MaximumChoices)
            .Select(f => new AutocompleteResult(
                Choice(removing || !f.IsFinished
                    ? f.Name
                    : $"{f.Name} — leaves after {TheKrystalShip.MovieBot.Acquire.Download.Retention.Describe(f.Remaining)}"),
                f.Hash));
    }

    /// <summary>Discord shows at most this many autocomplete choices.</summary>
    private const int MaximumChoices = 25;

    private static string Display(TheKrystalShip.MovieBot.Acquire.Imdb.ImdbTitle film) =>
        film.Year is { } year ? $"{film.Title} ({year})" : film.Title;

    /// <summary>Autocomplete labels are capped at 100 characters by Discord.</summary>
    private static string Choice(LibraryTitle title) => Choice(title.Name);

    private static string Choice(string label) =>
        label.Length <= 100 ? label : label[..99] + "…";

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
