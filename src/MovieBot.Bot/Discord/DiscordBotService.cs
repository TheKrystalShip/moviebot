using System.Net;
using Discord;
using Discord.WebSocket;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TheKrystalShip.MovieBot.Bot.Api;
using TheKrystalShip.MovieBot.Bot.Configuration;
using TheKrystalShip.MovieBot.Bot.Download;
using TheKrystalShip.MovieBot.Bot.Launch;
using TheKrystalShip.MovieBot.Bot.Notify;
using TheKrystalShip.MovieBot.Bot.Watch;
using TheKrystalShip.Discord.Voice;

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
    LaunchCards cards,
    TheKrystalShip.MovieBot.Acquire.Download.AcquisitionService acquisition,
    TheKrystalShip.MovieBot.Acquire.Imdb.ImdbClient catalogue,
    IVoiceSessions voiceSessions,
    VoiceDecryptHealth decryptHealth,
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
                // needs Set Voice Channel Status and Manage Channels: Discord asks for the second as
                // well from a bot that is not itself connected to the channel. Listening needs
                // Connect, and Speak carries the tone that tells somebody the bot is waiting for them.
                (ulong)(GuildPermission.CreateInstantInvite
                        | GuildPermission.ViewChannel
                        | GuildPermission.SendMessages
                        | GuildPermission.EmbedLinks
                        | GuildPermission.SetVoiceChannelStatus
                        | GuildPermission.ManageChannels
                        | GuildPermission.Connect
                        | GuildPermission.Speak));

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
                    [WatchSlashCommand.Build(), ShowSlashCommand.Build(), NotifySlashCommand.Build(),
                     VoiceSlashCommand.Build()]);

                logger.LogInformation("Registered /{Watch}, /{Show}, /{Notify} and /{Voice} in {GuildName}",
                    WatchSlashCommand.Name, ShowSlashCommand.Name, NotifySlashCommand.Name,
                    VoiceSlashCommand.Name, guild.Name);
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
        _ = AsPersonAsync(command.User.Id, () => command.CommandName switch
        {
            WatchSlashCommand.Name => HandleWatchAsync(command),
            ShowSlashCommand.Name => HandleShowAsync(command),
            NotifySlashCommand.Name => HandleNotifyAsync(command),
            VoiceSlashCommand.Name => HandleVoiceAsync(command),
            _ => Task.CompletedTask,
        });

        return Task.CompletedTask;
    }

    /// <summary>
    /// Runs one person's interaction as them: every call it makes to the API
    /// carries their token, so what they may do is decided where it is for everybody else.
    /// </summary>
    private static async Task AsPersonAsync(ulong discordUserId, Func<Task> work)
    {
        using var acting = ActingFor.Person(discordUserId);
        await work();
    }

    /// <summary>
    /// Brings the bot into a voice channel to listen, sends it away, or says what it is hearing.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Listening is never implicit.</b> The bot is in a voice channel because somebody asked it to
    /// be, and everyone in the channel is heard while it is there — not only whoever asked.
    /// </para>
    /// <para>
    /// <b>The room is told, or the bot does not stay.</b> The reply to whoever ran the command is only
    /// seen by them; the notice in the channel is the only way anyone else learns they are being
    /// listened to. A bot that joined and could not post that notice would be listening to people who
    /// were never told, so it leaves again and says why.
    /// </para>
    /// </remarks>
    private async Task HandleVoiceAsync(SocketSlashCommand command)
    {
        try
        {
            if (command.GuildId is not { } guildId || !_guilds.Contains(guildId))
            {
                await command.RespondAsync(
                    "This bot only answers in the server it is configured for.", ephemeral: true);
                return;
            }

            var subcommand = command.Data.Options.FirstOrDefault()?.Name;
            await command.DeferAsync(ephemeral: true);

            switch (subcommand)
            {
                case VoiceSlashCommand.Join:
                    await JoinVoiceAsync(command, guildId);
                    break;

                case VoiceSlashCommand.Leave:
                    var left = await voiceSessions.LeaveAsync(guildId);
                    await command.FollowupAsync(
                        left.IsSuccess ? "Stopped listening and left the voice channel." : left.Error,
                        ephemeral: true, allowedMentions: AllowedMentions.None);
                    break;

                case VoiceSlashCommand.Status:
                    await command.FollowupAsync(VoiceStatus(guildId), ephemeral: true, allowedMentions: AllowedMentions.None);
                    break;
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "The /{Command} command failed", VoiceSlashCommand.Name);
            await TryReportFailure(command, ex);
        }
    }

    private async Task JoinVoiceAsync(SocketSlashCommand command, ulong guildId)
    {
        if (!voiceSessions.IsEnabled)
        {
            await command.FollowupAsync("Listening is switched off on this bot.", ephemeral: true);
            return;
        }

        if ((command.User as SocketGuildUser)?.VoiceChannel is not { } channel)
        {
            await command.FollowupAsync(
                "Join a voice channel first, and I'll come to the one you're in.", ephemeral: true);
            return;
        }

        var joined = await voiceSessions.JoinAsync(guildId, channel.Id);
        if (joined.IsFailure)
        {
            await command.FollowupAsync(joined.Error, ephemeral: true, allowedMentions: AllowedMentions.None);
            return;
        }

        var name = (command.User as IGuildUser)?.DisplayName ?? command.User.Username;
        try
        {
            await command.Channel.SendMessageAsync(
                $"Listening in **{channel.Name}**, because {name} asked. Everyone in the channel is heard "
                + "while I'm there. Say \"okay computer, pause\" to stop the film"
                + ". `/voice leave` sends me away.",
                allowedMentions: AllowedMentions.None);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex,
                "Voice: could not tell {Channel} it is being listened to, so the bot is leaving again", channel.Name);
            await voiceSessions.LeaveAsync(guildId);
            await command.FollowupAsync(
                "I couldn't post a notice in this channel saying I'm listening, so I've left again. "
                + "Run this where I can send messages.", ephemeral: true);
            return;
        }

        logger.LogInformation("Voice: {User} brought the bot into {Channel}", name, channel.Name);
        await command.FollowupAsync($"Listening in **{channel.Name}**.", ephemeral: true, allowedMentions: AllowedMentions.None);
    }

    private string VoiceStatus(ulong guildId)
    {
        if (!voiceSessions.IsEnabled) return "Listening is switched off on this bot.";
        if (voiceSessions.Describe(guildId) is not { } session) return "Not listening in any voice channel.";

        var line = $"Listening in **{session.ChannelName}** for {Math.Floor(session.For.TotalMinutes)} min: "
            + $"{session.Captures} requests heard from {session.Speakers} "
            + (session.Speakers == 1 ? "person." : "people.");

        // Connected and sent nothing looks, from inside the channel, exactly like being heard and not
        // understood — so it is said outright rather than left for somebody to infer.
        return session.HearsNothing
            ? line + " No audio has arrived at all. Check the bot is not server-muted or deafened."
            : line;
    }

    private Task OnAutocomplete(SocketAutocompleteInteraction interaction)
    {
        _ = AsPersonAsync(interaction.User.Id, () => HandleAutocompleteAsync(interaction));
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
                await FetchThenWatchAsync(command, torrentId, voice);
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
            {
                var posted = await command.FollowupAsync(
                    reply.Text,
                    embed: reply.Embed,
                    components: reply.Components,
                    allowedMentions: AllowedMentions.None);

                // Recorded after the message exists, because the message is half of what is being
                // recorded. What it buys is the card saying so once the room behind it closes.
                if (reply.Invite is { } invite)
                    cards.Add(invite, command.ChannelId ?? 0, posted.Id);
            }
            else
                await command.FollowupAsync(result.Message, allowedMentions: AllowedMentions.None);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "The /{Command} command failed", WatchSlashCommand.Name);
            await TryReportFailure(command, ex);
        }
    }

    /// <summary>
    /// Starts the download for a room to watch in, and answers with the message that will show
    /// its progress.
    /// </summary>
    private async Task FetchThenWatchAsync(
        SocketSlashCommand command, long torrentId, SocketVoiceChannel? voice)
    {
        var result = await download.ExecuteAsync(new DownloadRequest
        {
            TorrentId = torrentId,
            ChannelId = command.ChannelId ?? 0,
            RequesterId = command.User.Id,
            RoomId = voice?.Id,
        }, CancellationToken.None);

        await PostStartedAsync(command, result, voice?.Name);
    }

    /// <summary>
    /// Fetches some or all of one season of a show: the tracker's packs and single episodes are
    /// planned by <c>ShowPlan</c>, at 720p unless 1080p is asked for, and
    /// only the picked episodes' files are fetched. Each download is announced here like a film's.
    /// </summary>
    private async Task HandleShowAsync(SocketSlashCommand command)
    {
        try
        {
            if (command.GuildId is not { } guildId || !_guilds.Contains(guildId))
            {
                await command.RespondAsync(
                    "This bot only answers in the server it is configured for.", ephemeral: true);
                return;
            }

            string? Option(string name) => command.Data.Options.FirstOrDefault(o => o.Name == name)?.Value?.ToString();
            var imdbId = Option(ShowSlashCommand.ShowOption) ?? "";
            var season = int.TryParse(Option(ShowSlashCommand.SeasonOption), out var s) ? s : 0;
            var quality = Option(ShowSlashCommand.QualityOption) ?? ShowPlan.DefaultQuality;

            await command.DeferAsync();

            if (!imdbId.StartsWith("tt", StringComparison.Ordinal))
            {
                await command.FollowupAsync("Pick a show from the list. The menu searches the catalogue as you type.",
                    allowedMentions: AllowedMentions.None);
                return;
            }

            var offers = await api.ShowReleasesAsync(imdbId, CancellationToken.None);
            if (ShowPlan.Episodes(offers, season, Option(ShowSlashCommand.EpisodesOption)) is not { } picked)
            {
                await command.FollowupAsync("Name the episodes as numbers and ranges, such as 1-3, 5, or leave them out for the whole season.",
                    allowedMentions: AllowedMentions.None);
                return;
            }

            var plan = ShowPlan.For(offers, picked, quality);
            if (plan.Downloads.Count == 0)
            {
                await command.FollowupAsync(
                    offers.Seasons.Count == 0
                        ? offers.EmptyExplanation ?? "The tracker has nothing of that show."
                        : $"The tracker has nothing of season {season}. It offers season{(offers.Seasons.Count == 1 ? "" : "s")} {string.Join(", ", offers.Seasons.Select(x => x.Season))}.",
                    allowedMentions: AllowedMentions.None);
                return;
            }

            var started = 0;
            foreach (var planned in plan.Downloads)
            {
                var reply = await api.StartDownloadAsync(new Core.StartDownloadRequest
                {
                    TorrentId = planned.Release.TorrentId,
                    NotifyChannelId = command.ChannelId?.ToString(),
                    Episodes = planned.Episodes,
                }, CancellationToken.None);
                if (reply is { Outcome: "started", Hash: { } hash })
                    await download.RecordMentionAsync(hash, command.User.Id, CancellationToken.None);

                var result = DownloadCommand.Describe(reply, intoRoom: false);
                if (result.Outcome == DownloadOutcome.Started) started++;
                await PostStartedAsync(command, result with
                {
                    Message = result.Outcome == DownloadOutcome.Started
                        ? $"Downloading {EpisodeWords(planned)} at {planned.Release.Resolution ?? quality}. "
                          + "The first is announced here as soon as it can be watched."
                        : result.Message,
                }, roomName: null);
            }

            if (started > 0 && (plan.OtherQuality > 0 || plan.NotOffered.Count > 0))
                await command.FollowupAsync(
                    string.Join(" ", new[]
                    {
                        plan.OtherQuality > 0 ? $"{plan.OtherQuality} of them {(plan.OtherQuality == 1 ? "is" : "are")} not offered at {quality} and {(plan.OtherQuality == 1 ? "comes" : "come")} at the quality the tracker has." : null,
                        plan.NotOffered.Count > 0 ? $"The tracker has nothing of episode{(plan.NotOffered.Count == 1 ? "" : "s")} {string.Join(", ", plan.NotOffered.Select(p => p.Episode))}." : null,
                    }.Where(x => x is not null)),
                    allowedMentions: AllowedMentions.None);
        }
        catch (MovieBotApiException ex)
        {
            logger.LogWarning(ex, "The /{Command} command could not reach the API", ShowSlashCommand.Name);
            await command.FollowupAsync("The tracker or the MovieBot API is not answering. Try again in a minute.",
                allowedMentions: AllowedMentions.None);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "The /{Command} command failed", ShowSlashCommand.Name);
            await TryReportFailure(command, ex);
        }
    }

    /// <summary>"3 episodes of Severance S02" for a pack picked from; the release's own name for a single episode.</summary>
    private static string EpisodeWords(PlannedDownload planned) =>
        planned.Episodes is { Count: > 0 } episodes
            ? $"{episodes.Count} {(episodes.Count == 1 ? "episode" : "episodes")} of {planned.Release.Display}"
            : planned.Release.Display;

    /// <summary>
    /// Answers a started download with the message that will show its progress, and records which
    /// message that is.
    ///
    /// The reply is the waiting state, never the launch. A film takes at least a minute to become
    /// watchable and an interaction token does not outlast a slow one, so whatever comes next — a
    /// launch for a film asked for in a room, an announcement for one fetched from outside a voice
    /// channel — is a separate message posted by the watcher, which is also the only kind of message
    /// that notifies the person who asked.
    ///
    /// <paramref name="roomName"/> is the whole difference: named, the film plays there as soon as it
    /// can be watched; null, it is fetched and announced.
    /// </summary>
    private async Task PostStartedAsync(SocketSlashCommand command, DownloadResult result, string? roomName)
    {
        if (result.Outcome != DownloadOutcome.Started || result.Release is null)
        {
            await command.FollowupAsync(result.Message, allowedMentions: AllowedMentions.None);
            return;
        }

        var posted = await command.FollowupAsync(
            result.Message,
            embed: DownloadEmbed.Starting(result.Release, roomName),
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
                    await command.DeferAsync(ephemeral: true);
                    await command.FollowupAsync(
                        embed: WishEmbed.List(await notify.ListAsync(CancellationToken.None)),
                        ephemeral: true);
                    return;

                case NotifySlashCommand.Cancel:
                    await command.DeferAsync(ephemeral: true);
                    var cancelled = await notify.CancelAsync(film, CancellationToken.None);
                    await command.FollowupAsync(
                        cancelled.Message, ephemeral: true, allowedMentions: AllowedMentions.None);
                    return;

                case NotifySlashCommand.Add:
                    break;

                default:
                    await command.RespondAsync("That is not one of the things /notify does.", ephemeral: true);
                    return;
            }

            await command.DeferAsync();

            var result = await notify.SubscribeAsync(new NotifyRequest { Chosen = film }, CancellationToken.None);

            await command.FollowupAsync(result.Message, embed: WishEmbed.For(result), allowedMentions: AllowedMentions.None);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "The /{Command} command failed", NotifySlashCommand.Name);
            await TryReportFailure(command, ex);
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

            // /show names a show from the catalogue; a show is fetched by its seasons, so the
            // tracker is asked only once one is picked.
            if (interaction.Data.CommandName == ShowSlashCommand.Name)
            {
                var shows = typed.Trim().Length < 2 ? [] : await api.FindShowsAsync(typed, CancellationToken.None);
                await interaction.RespondAsync(shows.Take(25).Select(s => new AutocompleteResult(Choice(s.Display), s.ImdbId)));
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
    ///
    /// A search that offers nothing says why on a row of its own. Picking that row submits the
    /// text as typed, which is what the menu would have left somebody to do anyway.
    /// </summary>
    private async Task<IEnumerable<AutocompleteResult>> WatchChoicesAsync(
        SocketAutocompleteInteraction interaction, string typed)
    {
        var library = await api.ListTitlesAsync(CancellationToken.None);
        var here = TitleMatcher.Suggest(library, typed);

        if (here.Count > 0 || typed.Trim().Length == 0)
            return here.Select(t => new AutocompleteResult(Choice(t), t.Id));

        var offered = await api.SuggestReleasesAsync(
            typed, interaction.User.Id.ToString(), CancellationToken.None);

        if (offered.Choices.Count > 0)
            return offered.Choices.Select(
                c => new AutocompleteResult(c.Label, TrackerPick.Value(c.TorrentId)));

        return offered.Explanation is { Length: > 0 } why
            ? [new AutocompleteResult(Choice(why), Choice(typed))]
            : [];
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
            return (await notify.ListAsync(CancellationToken.None))
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
                Choice(f.Starring is { Length: > 0 } cast ? $"{f.Display} — {cast}" : f.Display),
                f.ImdbId));
    }

    /// <summary>Discord shows at most this many autocomplete choices.</summary>
    private const int MaximumChoices = 25;

    /// <summary>Autocomplete labels are capped at 100 characters by Discord.</summary>
    private static string Choice(LibraryTitle title) => Choice(title.Name);

    private static string Choice(string label) =>
        label.Length <= 100 ? label : label[..99] + "…";

    /// <summary>
    /// Tells whoever ran a command that it did not work, in words they can act on where there are any:
    /// their account not being approved, or not allowing what they asked, is theirs to know.
    /// </summary>
    private async Task TryReportFailure(SocketSlashCommand command, Exception failure)
    {
        var sentence = failure switch
        {
            AccountRefusedException refused => refused.Sentence,
            MovieBotAccessDeniedException => "Your account does not let you do that in Krystal Cinema.",
            Core.TokenExchangeUnavailableException => "The sign-in service is not answering, so nothing happened. Try again in a minute.",
            _ => "That command did not work. The log has the reason.",
        };

        try
        {
            if (command.HasResponded)
                await command.FollowupAsync(sentence, ephemeral: true);
            else
                await command.RespondAsync(sentence, ephemeral: true);
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "The interaction was already gone");
        }
    }

    private Task OnLog(LogMessage message)
    {
        // A frame Discord.Net could not decrypt is reported as a log line and nowhere else, so this
        // handler — which every library message already passes through — is where it is counted.
        decryptHealth.Observe(message);

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
