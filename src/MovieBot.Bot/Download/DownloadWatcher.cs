using Discord;
using Discord.WebSocket;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TheKrystalShip.MovieBot.Acquire.Download;
using TheKrystalShip.MovieBot.Bot.Api;
using TheKrystalShip.MovieBot.Bot.Discord;
using TheKrystalShip.MovieBot.Bot.Keep;
using TheKrystalShip.MovieBot.Bot.Launch;
using TheKrystalShip.MovieBot.Bot.Sessions;

namespace TheKrystalShip.MovieBot.Bot.Download;

/// <summary>
/// Announces a film in the channel it was asked for, once there is something to watch — and
/// starts it in the room it was asked for.
///
/// Downloaded is not watchable, and watchable is not downloaded. The file has to be transcoding
/// before the player can open it, and the transcode starts while the file is still arriving, so
/// what is waited on is the mark the hand-off leaves the moment the film can be opened: seconds
/// into the transcode, however much of the download is still to come. Announcing at the end of
/// the download would be too late by a quarter of an hour on one side, and announcing at the
/// start of it would have the command that plays the film find nothing on the other.
///
/// A download that carries a room is one somebody asked to watch, not to file. The film is loaded
/// into that room exactly as the command would have loaded it had it been in the library, and the
/// announcement is the same launch the command would have posted, mentioning the person who
/// asked. Without a room it is an announcement and nothing more.
///
/// It holds nothing. What to announce and where is read from the torrent's own tags on every
/// pass, so a bot restarted in the middle of a three-hour download still announces it — which is
/// the only case where remembering would have mattered.
///
/// The tags are removed once the message is sent, and that is what stops a film being announced
/// on every pass forever. A message that fails to send leaves them alone, so the next pass tries
/// again rather than losing it — unless Discord refuses the channel itself, which is an answer no
/// later pass gets past, and the announcement is given up rather than asked for every ten seconds
/// until the film is deleted.
/// </summary>
public sealed class DownloadWatcher(
    DiscordSocketClient client,
    AcquisitionService acquisition,
    MovieBotApiClient api,
    ILaunchPresenter presenter,
    LaunchCards cards,
    IFilmRetention retention,
    ILogger<DownloadWatcher> logger) : BackgroundService
{
    /// <summary>
    /// How often the torrent client is asked. Somebody who picked a film that was not here yet
    /// is waiting in a voice channel for it to start, so this is the ceiling on how long a film
    /// that can be watched goes unannounced.
    /// </summary>
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(10);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (!await timer.WaitForNextTickAsync(stoppingToken)) return;
                await SweepAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                // A failing sweep must not end the watcher: the download it was going to
                // announce is still running, and the next pass will find it.
                logger.LogWarning(ex, "A download sweep failed.");
            }
        }
    }

    private async Task SweepAsync(CancellationToken ct)
    {
        var downloads = await acquisition.ListAsync(ct);

        // The library is read once per pass and only when a pass has something to load. Every
        // fact about a title comes from the listing at the moment it is used, never from before.
        IReadOnlyList<LibraryTitle>? library = null;

        foreach (var download in downloads)
        {
            var tag = download.Tags.FirstOrDefault(
                t => t.StartsWith(TorrentTags.NotifyPrefix, StringComparison.Ordinal));

            if (tag is null) continue;

            // Nothing to watch yet. A transcode owed, running, or interrupted all look the same
            // from here and none of them is worth telling anybody about; what is worth telling is
            // that the film can be opened, which is its own mark and arrives seconds into a
            // transcode rather than at the end of it.
            var failed = download.Tags.Contains(TorrentTags.IngestFailed);
            if (!download.Tags.Contains(TorrentTags.Watchable) && !failed) continue;

            if (TorrentTags.ReadNotifyChannel(tag) is not { } channelId)
            {
                logger.LogWarning("A download carries an unreadable notify tag: {Tag}", tag);
                await acquisition.ClearTagAsync(download.Hash, tag, ct);
                continue;
            }

            var requester = download.Tags
                .Select(TorrentTags.ReadRequester)
                .FirstOrDefault(id => id is not null);

            var roomTag = download.Tags.FirstOrDefault(
                t => t.StartsWith(TorrentTags.RoomPrefix, StringComparison.Ordinal));
            var room = roomTag is null ? null : TorrentTags.ReadRoom(roomTag);

            var id = download.Tags.Select(TorrentTags.ReadLibrary).FirstOrDefault(i => i is not null);
            var onDisk = id is null || failed ? null : await retention.NoticeForAsync(id, ct);

            LaunchReply? launch = null;
            if (!failed && room is { } roomId)
            {
                if (id is null)
                {
                    // Watchable without the hand-off having said what it called the film. There
                    // is nothing to load, so the announcement goes out as one and the room is
                    // left alone.
                    logger.LogWarning("{Name} is watchable but carries no library id.", download.Name);
                }
                else
                {
                    library ??= await api.ListTitlesAsync(ct);

                    launch = await TryLoadIntoRoomAsync(download, id, roomId, requester, onDisk, library, ct);

                    // The film is watchable and the room is still waiting for it. The listing
                    // lags the manifest by at most a moment, so the next pass is the answer
                    // rather than announcing a film without the way in.
                    if (launch is null) continue;
                }
            }

            if (!await TryAnnounceAsync(channelId, download, failed, requester, launch, onDisk))
                continue;

            await acquisition.ClearTagAsync(download.Hash, tag, ct);
            if (roomTag is not null) await acquisition.ClearTagAsync(download.Hash, roomTag, ct);
        }
    }

    /// <summary>
    /// Puts the film into the room and builds the launch, exactly as the command does for a film
    /// that was already in the library. Null when the library does not list the film yet, or
    /// when the API could not be reached; both are answered by the next pass.
    /// </summary>
    private async Task<LaunchReply?> TryLoadIntoRoomAsync(
        DownloadStatus download, string id, ulong roomId, ulong? requester, string? onDisk,
        IReadOnlyList<LibraryTitle> library, CancellationToken ct)
    {
        var title = library.FirstOrDefault(t => t.Id == id);
        if (title is null)
        {
            logger.LogDebug("{Id} is not in the library listing yet.", id);
            return null;
        }

        var voice = client.GetChannel(roomId) as IVoiceChannel;
        var requestedBy = await DisplayNameAsync(voice, requester);
        var sessionId = RoomSession.IdFor(roomId);

        try
        {
            var session = await api.OpenSessionAsync(sessionId, ct);

            // A room already holding this film is left exactly where it is, as the command
            // leaves it: somebody else may have started it there in the meantime.
            var alreadyWatching = session.TitleId == title.Id;
            var replaced = alreadyWatching ? null : library.FirstOrDefault(t => t.Id == session.TitleId);

            if (!alreadyWatching)
                session = await api.SetSessionTitleAsync(session.SessionId, title.Id, requestedBy, ct);

            var reply = await presenter.PresentAsync(new LaunchRequest
            {
                SessionId = session.SessionId,
                Title = title,
                VoiceChannelId = roomId,
                VoiceChannelName = voice?.Name ?? "the voice channel",
                RequestedBy = requestedBy,
                Replaced = replaced,
                AlreadyWatching = alreadyWatching,
                OnDisk = onDisk,
            }, ct);

            logger.LogInformation("{RequestedBy} {Verb} {TitleId} in session {SessionId}",
                requestedBy,
                alreadyWatching ? "rejoined" : replaced is null ? "started" : $"switched from {replaced.Id} to",
                title.Id, session.SessionId);

            return reply;
        }
        catch (MovieBotApiException ex)
        {
            logger.LogWarning(ex, "Could not load {TitleId} into session {SessionId}.", title.Id, sessionId);
            return null;
        }
    }

    /// <summary>
    /// The requester as the room sees them. The tag carries an id, and the name behind it is
    /// asked for when it is needed rather than written down, since a person renames themselves
    /// and a tag does not follow.
    /// </summary>
    private static async Task<string> DisplayNameAsync(IVoiceChannel? voice, ulong? requester)
    {
        if (requester is not { } id) return "Somebody";

        try
        {
            if (voice is not null
                && await voice.Guild.GetUserAsync(id, CacheMode.AllowDownload) is { } member)
                return member.DisplayName;
        }
        catch (Exception)
        {
            // A name is a courtesy. The launch is the point, and it goes out either way.
        }

        return "Somebody";
    }

    private async Task<bool> TryAnnounceAsync(
        ulong channelId, DownloadStatus download, bool failed, ulong? requester, LaunchReply? launch,
        string? onDisk)
    {
        try
        {
            if (client.GetChannel(channelId) is not IMessageChannel channel)
            {
                // The channel is gone or the bot can no longer see it. Clearing the tag is the
                // only way to stop retrying something that will never work.
                logger.LogWarning(
                    "Cannot announce {Name}: channel {ChannelId} is not reachable.",
                    download.Name, channelId);
                return true;
            }

            // Built from the one definition every other message about a download uses, unless
            // the film was started in a room, in which case the message is the launch itself.
            var embed = failed ? DownloadEmbed.Failed(download)
                : launch is { } ready ? ready.Embed
                : DownloadEmbed.Ready(download, onDisk);

            // The mention has to be in the message itself: text inside an embed renders as a
            // mention and notifies nobody.
            var mention = requester is { } accountId ? MentionUtils.MentionUser(accountId) : null;
            var content = launch is { } started
                ? mention is null ? started.Text : $"{mention} your film is ready. {started.Text}"
                : mention;

            // Exactly one account, named by id, rather than allowing mentions generally. The
            // launch's headline carries a display name, which is whatever a person set it to,
            // and it stays unable to notify anyone this did not choose.
            var mentions = requester is { } allowed
                ? new AllowedMentions { UserIds = [allowed] }
                : AllowedMentions.None;

            var posted = await channel.SendMessageAsync(
                text: content, embed: embed, components: launch?.Components, allowedMentions: mentions);

            // The announcement is the launch card when the film was asked for in a room, so it is
            // remembered the same way the command's is: it is a door, and it has to say so when it
            // stops opening.
            if (launch?.Invite is { } invite) cards.Add(invite, channelId, posted.Id);

            logger.LogInformation("Announced {Name} in {ChannelId}.", download.Name, channelId);
            return true;
        }
        catch (global::Discord.Net.HttpException ex) when (DiscordReach.IsOutOfReach(ex))
        {
            // The bot cannot post here, and no number of passes changes that. Giving up costs
            // the announcement; carrying on costs a refusal every ten seconds for as long as
            // the download is kept, so the tag goes and the reason is said once.
            logger.LogWarning(
                "Cannot announce {Name}: Discord refused channel {ChannelId} ({Reason}). "
                + "The bot needs View Channel and Send Messages there.",
                download.Name, channelId, DiscordReach.Explain(ex));
            return true;
        }
        catch (Exception ex)
        {
            // Left untagged means untried: the next pass will attempt it again.
            logger.LogWarning(ex, "Could not announce {Name}.", download.Name);
            return false;
        }
    }
}
