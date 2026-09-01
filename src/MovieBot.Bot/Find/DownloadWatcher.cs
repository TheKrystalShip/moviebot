using Discord;
using Discord.WebSocket;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TheKrystalShip.MovieBot.Acquire.Download;

namespace TheKrystalShip.MovieBot.Bot.Find;

/// <summary>
/// Announces a film in the channel it was asked for, once there is something to watch.
///
/// Downloaded is not watchable. The file has to be transcoded before the player can open it, so
/// this waits for the hand-off to finish rather than for the download to: announcing at the end
/// of the download would tell a room a film is ready and have the command that plays it find
/// nothing.
///
/// It holds nothing. What to announce and where is read from the torrent's own tags on every
/// pass, so a bot restarted in the middle of a three-hour download still announces it — which is
/// the only case where remembering would have mattered.
///
/// The tag is removed once the message is sent, and that is what stops a film being announced on
/// every pass forever. A message that fails to send leaves the tag alone, so the next pass tries
/// again rather than losing it.
/// </summary>
public sealed class DownloadWatcher(
    DiscordSocketClient client,
    AcquisitionService acquisition,
    ILogger<DownloadWatcher> logger) : BackgroundService
{
    /// <summary>
    /// How often the torrent client is asked. A film takes minutes at best, so this is about
    /// answering within a reasonable window rather than promptly, and the person was told to
    /// check back rather than to wait.
    /// </summary>
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(30);

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

        foreach (var download in downloads.Where(d => d.IsFinished))
        {
            var tag = download.Tags.FirstOrDefault(
                t => t.StartsWith(TorrentTags.NotifyPrefix, StringComparison.Ordinal));

            if (tag is null) continue;

            // Still owed a transcode, so there is nothing to watch yet. Left alone rather than
            // announced, and found again on the next pass.
            if (download.Tags.Contains(TorrentTags.NeedsIngest)) continue;

            if (TorrentTags.ReadNotifyChannel(tag) is not { } channelId)
            {
                logger.LogWarning("A download carries an unreadable notify tag: {Tag}", tag);
                await acquisition.ClearTagAsync(download.Hash, tag, ct);
                continue;
            }

            var failed = download.Tags.Contains(TorrentTags.IngestFailed);

            if (await TryAnnounceAsync(channelId, download, failed))
                await acquisition.ClearTagAsync(download.Hash, tag, ct);
        }
    }

    private async Task<bool> TryAnnounceAsync(
        ulong channelId, DownloadStatus download, bool failed)
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

            // A film that arrived but could not be made watchable is reported as itself. Silence
            // would be indistinguishable from a transcode still running, and nobody would ever
            // find out.
            var embed = failed
                ? new EmbedBuilder()
                    .WithTitle("Downloaded, but it could not be prepared")
                    .WithDescription(download.Name)
                    .AddField("What happened",
                        "The film downloaded, but converting it for the player failed. "
                        + "It is on disk and can be retried.", inline: false)
                    .WithColor(Color.Red)
                    .WithCurrentTimestamp()
                    .Build()
                : new EmbedBuilder()
                    .WithTitle("Ready to watch")
                    .WithDescription(download.Name)
                    .AddField("Size", Describe(download.SizeBytes), inline: true)
                    .AddField("Watch it with", $"/{Discord.WatchSlashCommand.Name}", inline: true)
                    .WithColor(Color.Green)
                    .WithCurrentTimestamp()
                    .Build();

            await channel.SendMessageAsync(embed: embed, allowedMentions: AllowedMentions.None);

            logger.LogInformation("Announced {Name} in {ChannelId}.", download.Name, channelId);
            return true;
        }
        catch (Exception ex)
        {
            // Left untagged means untried: the next pass will attempt it again.
            logger.LogWarning(ex, "Could not announce {Name}.", download.Name);
            return false;
        }
    }

    private static string Describe(long bytes) => bytes >= 1L << 30
        ? $"{bytes / (double)(1L << 30):0.#} GiB"
        : $"{bytes / (double)(1L << 20):0.#} MiB";
}
