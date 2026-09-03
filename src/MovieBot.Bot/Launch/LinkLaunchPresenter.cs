using Discord;
using Microsoft.Extensions.Options;
using TheKrystalShip.MovieBot.Bot.Api;
using TheKrystalShip.MovieBot.Bot.Configuration;
using TheKrystalShip.MovieBot.Core;

namespace TheKrystalShip.MovieBot.Bot.Launch;

/// <summary>
/// Opens the player in a browser, on a link carrying the room's session and the film.
///
/// The link is the whole handover: the player joins the session it names and loads the title it
/// names, so two people who click the same link are in the same room whichever machine they are
/// on, and a person who prefers a real browser window is not a second code path.
/// </summary>
public sealed class LinkLaunchPresenter(
    IOptions<PlayerOptions> player,
    IOptions<ApiOptions> api) : ILaunchPresenter
{
    private static readonly Color Accent = new(0x4C, 0xC9, 0xC0);

    public Task<LaunchReply> PresentAsync(LaunchRequest request, CancellationToken ct)
    {
        var url = PlayerUrl(request.SessionId, request.Title.Id);

        var embed = FilmCard.Build(request.Title, PosterUrl(request.Title))
            .WithUrl(url.ToString())
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
            .WithButton("Open the player", style: ButtonStyle.Link, url: url.ToString())
            .Build();

        return Task.FromResult(new LaunchReply(
            request.Headline,
            embed.Build(),
            components));
    }

    private Uri PlayerUrl(string sessionId, string titleId)
    {
        var builder = new UriBuilder(player.Value.BaseUrl);
        var query = $"session={Uri.EscapeDataString(sessionId)}&title={Uri.EscapeDataString(titleId)}";
        var existing = builder.Query.TrimStart('?');

        builder.Query = existing.Length == 0 ? query : $"{existing}&{query}";
        return builder.Uri;
    }

    /// <summary>
    /// Discord fetches an embed image from its own servers, so the poster is only offered when
    /// there is an address that reaches the API from outside this machine. An unreachable image
    /// url renders as a broken embed, which reads as a broken bot.
    /// </summary>
    private Uri? PosterUrl(LibraryTitle title)
    {
        if (title.Poster is not { Length: > 0 } poster) return null;
        if (api.Value.PublicBaseUrl is not { Length: > 0 } root) return null;

        return new Uri(
            $"{root.TrimEnd('/')}/media/{Uri.EscapeDataString(title.Id)}/{Uri.EscapeDataString(poster)}");
    }

    /// <summary>
    /// What can be watched right now. A film that is still transcoding is watchable from the
    /// start immediately, so the restriction worth naming is the seek limit, not the wait.
    /// </summary>
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
