using Discord;
using TheKrystalShip.MovieBot.Acquire.Download;
using TheKrystalShip.MovieBot.Acquire.Search;

namespace TheKrystalShip.MovieBot.Bot.Download;

/// <summary>
/// Every way a download is shown, in one place.
///
/// Three things render a download — the command that starts one, the updater that keeps its
/// message current, and the announcement when it is ready — and a second spelling of any of these
/// would not disagree loudly. It would just produce two messages about one film that look like
/// they came from different programs.
/// </summary>
public static class DownloadEmbed
{
    private const int BarWidth = 20;

    /// <summary>
    /// The message as it first appears, before there is any progress to report. It names the
    /// room the film will play in when there is one, because that is the whole of what the
    /// person is waiting for.
    /// </summary>
    public static Embed Starting(Release release, string? roomName = null)
    {
        var embed = new EmbedBuilder()
            .WithTitle(release.Display)
            .WithDescription(release.ReleaseName)
            .AddField("Quality", release.Summary, inline: false)
            .AddField("Progress", $"{Bar(0)}  starting", inline: false)
            .WithColor(Color.Blue)
            .WithCurrentTimestamp();

        if (roomName is not null)
            embed.AddField("Plays in", $"{roomName}, as soon as enough of it has arrived", inline: false);

        return embed.Build();
    }

    /// <summary>The message while the film is on its way, or once it has arrived.</summary>
    public static Embed Progress(DownloadStatus download, bool preparing, bool failed, bool watchable)
    {
        if (failed) return Failed(download);

        if (download.IsFinished && preparing)
            return new EmbedBuilder()
                .WithTitle(watchable
                    ? "Downloaded — playing while the rest is prepared"
                    : "Downloaded — preparing it for playback")
                .WithDescription(download.Name)
                .AddField("Progress", $"{Bar(1)}  100%", inline: false)
                .WithColor(Color.Gold)
                .Build();

        if (download.IsFinished) return Ready(download);

        // A film that can already be watched says so above its own bar: the bar is then about
        // how much of it is here, not about whether anybody can start.
        return new EmbedBuilder()
            .WithTitle(watchable ? "Downloading — already watchable" : "Downloading")
            .WithDescription(download.Name)
            .AddField("Progress", $"{Bar(download.Progress)}  {download.Progress:P0}", inline: false)
            .AddField("Speed", Rate(download), inline: true)

            // The seed count is the answer to why a download is slow, which is the only question
            // a slow one raises.
            .AddField("Seeds", download.Seeds == 0 ? "none connected" : $"{download.Seeds}",
                inline: true)
            .WithColor(Color.Blue)
            .Build();
    }

    /// <summary>
    /// A film that has arrived is a film, not a file. By the time this is sent the name has been
    /// read out of the release, so the message says what was watched for rather than what it was
    /// packed as — and keeps the release beside it, because which encode arrived is worth knowing.
    /// </summary>
    public static Embed Ready(DownloadStatus download, string? onDisk = null)
    {
        var embed = new EmbedBuilder()
            .WithTitle($"{Film(download)} is ready to watch")
            .WithDescription(download.Name)
            .AddField("Size", Size(download.SizeBytes), inline: true)
            .AddField("Watch it with", "/watch", inline: true)
            .WithColor(Color.Green)
            .WithCurrentTimestamp();

        if (onDisk is not null) embed.AddField("On disk", onDisk);

        return embed.Build();
    }

    public static Embed Failed(DownloadStatus download) =>
        new EmbedBuilder()
            .WithTitle($"{Film(download)} downloaded, but could not be prepared")
            .WithDescription(download.Name)
            .AddField("What happened",
                "The film downloaded, but converting it for the player failed. "
                + "It is on disk and can be retried.", inline: false)
            .WithColor(Color.Red)
            .WithCurrentTimestamp()
            .Build();

    /// <summary>
    /// What the message would say, as one string, for deciding whether an edit is worth sending.
    /// Derived from the same values the embed is, so the two cannot disagree about whether
    /// anything changed.
    /// </summary>
    public static string Signature(DownloadStatus download, bool preparing, bool failed, bool watchable) =>
        failed ? "failed"
        : download.IsFinished && preparing ? $"preparing|{watchable}"
        : download.IsFinished ? "ready"
        : $"{download.Progress:P0}|{Rate(download)}|{download.Seeds}|{download.State}|{watchable}";

    /// <summary>
    /// The film's name, read from the release the same way every other part of the pipeline reads
    /// it. This holds no manifest and cannot ask the catalogue, so it is the name a release name
    /// gives up and nothing more — which is always available and is what the library will be
    /// called until the catalogue improves on it.
    /// </summary>
    private static string Film(DownloadStatus download) =>
        ReleaseParser.ParseName(download.Name).Display;

    private static string Rate(DownloadStatus download)
    {
        if (download.State == DownloadState.Stalled || download.BytesPerSecond == 0)
            return "stalled";

        var speed = $"{download.BytesPerSecond / (double)(1 << 20):0.#} MiB/s";
        return download.Remaining is { } left ? $"{speed} · {Describe(left)} left" : speed;
    }

    private static string Describe(TimeSpan span) => span.TotalHours >= 1
        ? $"{(int)span.TotalHours}h {span.Minutes}m"
        : span.TotalMinutes >= 1
            ? $"{(int)span.TotalMinutes} min"
            : "under a minute";

    private static string Size(long bytes) => bytes >= 1L << 30
        ? $"{bytes / (double)(1L << 30):0.#} GiB"
        : $"{bytes / (double)(1L << 20):0.#} MiB";

    /// <summary>Block elements rather than a number alone, because a bar is read at a glance.</summary>
    private static string Bar(double progress)
    {
        var filled = (int)Math.Round(Math.Clamp(progress, 0, 1) * BarWidth);
        return new string('█', filled) + new string('░', BarWidth - filled);
    }
}
