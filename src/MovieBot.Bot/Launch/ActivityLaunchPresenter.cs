using Discord;
using Discord.WebSocket;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TheKrystalShip.MovieBot.Bot.Api;
using TheKrystalShip.MovieBot.Bot.Configuration;
using TheKrystalShip.MovieBot.Core;

namespace TheKrystalShip.MovieBot.Bot.Launch;

/// <summary>
/// Opens the film as an Activity inside the voice channel people are already sitting in.
///
/// This is the whole of what phase five changes about the bot: resolving the film, opening the
/// session and every error path above this line are the link presenter's, unchanged. The only
/// difference is which door the reply opens.
/// </summary>
public sealed class ActivityLaunchPresenter(
    DiscordSocketClient client,
    IOptions<DiscordOptions> discord,
    IOptions<ApiOptions> api,
    IOptions<LaunchOptions> launch,
    ILaunchPresenter fallback,
    ILogger<ActivityLaunchPresenter> logger) : ILaunchPresenter
{
    private static readonly Color Accent = new(0x4C, 0xC9, 0xC0);

    public async Task<LaunchReply> PresentAsync(LaunchRequest request, CancellationToken ct)
    {
        if (discord.Value.ApplicationId is not { } applicationId)
        {
            logger.LogWarning("No application id configured, so the film opens in a browser instead.");
            return await fallback.PresentAsync(request, ct);
        }

        if (client.GetChannel(request.VoiceChannelId) is not IVoiceChannel channel)
        {
            logger.LogWarning("Voice channel {ChannelId} is not visible to the bot.", request.VoiceChannelId);
            return await fallback.PresentAsync(request, ct);
        }

        IInviteMetadata invite;
        try
        {
            // The ulong overload, not the DefaultApplications one — that names Discord's own
            // built-ins. This is what sets target_type 2 with this application's id.
            invite = await channel.CreateInviteToApplicationAsync(
                applicationId: applicationId,
                maxAge: launch.Value.InviteMaxAgeSeconds,
                maxUses: null,
                isTemporary: false,
                isUnique: true,
                options: new RequestOptions { CancelToken = ct });
        }
        catch (Exception ex)
        {
            // Almost always a missing Create Instant Invite in this channel. Saying so beats a
            // silent fall back to a browser link that looks like the Activity was never built.
            logger.LogError(ex, "Could not create an Activity invite for {ChannelName}. "
                + "The bot needs Create Instant Invite there.", request.VoiceChannelName);
            return await fallback.PresentAsync(request, ct);
        }

        var url = $"https://discord.gg/{invite.Code}";

        var embed = FilmCard.Build(request.Title, PosterUrl(request.Title))
            .WithUrl(url)
            .WithDescription(Availability(request.Title))
            .WithColor(Accent)
            .AddField("Voice channel", request.VoiceChannelName, inline: true)
            .AddField("Length", Humanize(request.Title.DurationSeconds), inline: true)
            .WithFooter($"Requested by {request.RequestedBy}");

        if (request.Replaced is { } replaced)
            embed.AddField("Replaced", replaced.Name);

        if (request.OnDisk is { } onDisk)
            embed.AddField("On disk", onDisk);

        var components = new ComponentBuilder()
            .WithButton("Watch together", style: ButtonStyle.Link, url: url)
            .Build();

        return new LaunchReply(
            request.Headline,
            embed.Build(),
            components);
    }

    private Uri? PosterUrl(LibraryTitle title)
    {
        if (title.Poster is not { Length: > 0 } poster) return null;
        if (api.Value.PublicBaseUrl is not { Length: > 0 } root) return null;

        return new Uri(
            $"{root.TrimEnd('/')}/media/{Uri.EscapeDataString(title.Id)}/{Uri.EscapeDataString(poster)}");
    }

    private static string Availability(LibraryTitle title) => title.Status switch
    {
        TitleStatus.Ready => "Ready. The whole film is seekable.",
        TitleStatus.Transcoding when title.HeadSeconds is { } head =>
            $"Still transcoding. Playback starts now; {Humanize(head)} is seekable so far, and the "
            + "limit lifts as the transcode runs.",
        TitleStatus.Transcoding => "Still transcoding. Playback starts now; seeking is limited to "
            + "what has been written so far.",
        _ => "The transcode for this film failed."
    };

    private static string Humanize(double seconds)
    {
        var span = TimeSpan.FromSeconds(Math.Max(0, seconds));
        if (span.TotalHours >= 1) return $"{(int)span.TotalHours}h {span.Minutes:00}m";
        if (span.TotalMinutes >= 1) return $"{span.Minutes}m {span.Seconds:00}s";
        return $"{span.Seconds}s";
    }
}
