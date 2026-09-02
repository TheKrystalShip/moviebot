using Discord;
using Discord.WebSocket;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TheKrystalShip.MovieBot.Acquire.Download;

namespace TheKrystalShip.MovieBot.Bot.Download;

/// <summary>
/// Keeps each download's message showing where that download has got to.
///
/// A film takes minutes, and a message saying only that it started tells somebody nothing about
/// whether it is working. What they actually need to know is whether it is moving, and if it is
/// not, why — which is almost always that nobody is seeding it.
///
/// Editing a message sends no notification, so nothing here reaches anybody who is not already
/// looking. That is the point: this is for the person who checks back, and the ping when the film
/// is ready is a separate message because only a new message notifies.
/// </summary>
public sealed class DownloadProgressUpdater(
    DiscordSocketClient client,
    AcquisitionService acquisition,
    ILogger<DownloadProgressUpdater> logger) : BackgroundService
{
    /// <summary>How often each download's message is reconsidered.</summary>
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(10);

    /// <summary>
    /// The shortest gap between two edits, anywhere.
    ///
    /// Discord meters message edits per channel rather than per message, so several downloads in
    /// one channel share one allowance and their timers landing together is what would spend it.
    /// Every edit goes through here in turn, which spreads a burst into a queue. Discord asks that
    /// limits not be hard coded and be read from its response headers instead, so this is a floor
    /// well under the observed allowance rather than a copy of it — the library still holds the
    /// real one and will queue behind it.
    /// </summary>
    private static readonly TimeSpan MinimumGapBetweenEdits = TimeSpan.FromMilliseconds(1500);

    private readonly SemaphoreSlim _editGate = new(1, 1);
    private DateTimeOffset _lastEdit = DateTimeOffset.MinValue;

    /// <summary>
    /// What each message was last made to say. An edit that would change nothing is not sent:
    /// a download stalled at a third for ten minutes is otherwise sixty identical edits, all of
    /// them spending the channel's allowance to say the same thing.
    /// </summary>
    private readonly Dictionary<string, string> _lastRendered = [];

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
                logger.LogWarning(ex, "A progress sweep failed.");
            }
        }
    }

    private async Task SweepAsync(CancellationToken ct)
    {
        var downloads = await acquisition.ListAsync(ct);
        var live = new HashSet<string>();

        foreach (var download in downloads)
        {
            var tag = download.Tags.FirstOrDefault(
                t => t.StartsWith(TorrentTags.ProgressPrefix, StringComparison.Ordinal));

            if (tag is null) continue;
            if (TorrentTags.ReadProgress(tag) is not { } target)
            {
                logger.LogWarning("A download carries an unreadable progress tag: {Tag}", tag);
                await acquisition.ClearTagAsync(download.Hash, tag, ct);
                continue;
            }

            live.Add(download.Hash);

            var preparing = download.Tags.Contains(TorrentTags.NeedsIngest);
            var failed = download.Tags.Contains(TorrentTags.IngestFailed);
            var watchable = download.Tags.Contains(TorrentTags.Watchable);
            var rendered = Render(download, preparing, failed, watchable);

            if (_lastRendered.TryGetValue(download.Hash, out var previous) && previous == rendered)
                continue;

            if (!await TryEditAsync(
                    target.ChannelId, target.MessageId, download, preparing, failed, watchable, ct))
                continue;

            _lastRendered[download.Hash] = rendered;

            // Once the film is downloaded and prepared the message has nothing further to say,
            // so it stops being one of ours to keep up to date.
            if (download.IsFinished && !preparing)
            {
                await acquisition.ClearTagAsync(download.Hash, tag, ct);
                _lastRendered.Remove(download.Hash);
                live.Remove(download.Hash);
            }
        }

        foreach (var stale in _lastRendered.Keys.Where(h => !live.Contains(h)).ToList())
            _lastRendered.Remove(stale);
    }

    private async Task<bool> TryEditAsync(
        ulong channelId, ulong messageId, DownloadStatus download,
        bool preparing, bool failed, bool watchable, CancellationToken ct)
    {
        await PaceAsync(ct);

        try
        {
            if (client.GetChannel(channelId) is not IMessageChannel channel) return false;

            // Fetched through the channel rather than kept from the interaction that made it.
            // An interaction's token lasts fifteen minutes and a download does not, so editing
            // through the interaction would simply stop working partway through a long film.
            if (await channel.GetMessageAsync(messageId) is not IUserMessage message) return false;

            var embed = DownloadEmbed.Progress(download, preparing, failed, watchable);
            await message.ModifyAsync(m => m.Embed = embed);

            return true;
        }
        catch (global::Discord.Net.HttpException ex) when (ex.HttpCode == System.Net.HttpStatusCode.NotFound)
        {
            // Somebody deleted the message. Nothing to keep up to date any more.
            logger.LogDebug("The progress message {MessageId} is gone.", messageId);
            return true;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not update the progress message {MessageId}.", messageId);
            return false;
        }
    }

    /// <summary>Holds edits apart so a channel's allowance is spent in a queue, never in a burst.</summary>
    private async Task PaceAsync(CancellationToken ct)
    {
        await _editGate.WaitAsync(ct);
        try
        {
            var since = DateTimeOffset.UtcNow - _lastEdit;
            if (since < MinimumGapBetweenEdits)
                await Task.Delay(MinimumGapBetweenEdits - since, ct);

            _lastEdit = DateTimeOffset.UtcNow;
        }
        finally
        {
            _editGate.Release();
        }
    }

    private static string Render(DownloadStatus download, bool preparing, bool failed, bool watchable) =>
        DownloadEmbed.Signature(download, preparing, failed, watchable);
}
