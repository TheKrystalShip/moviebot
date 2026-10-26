using TheKrystalShip.MovieBot.Api.Catalogue;
using System.Globalization;
using TheKrystalShip.MovieBot.Acquire.Tmdb;
using TheKrystalShip.MovieBot.Core;

namespace TheKrystalShip.MovieBot.Api.Shows;

/// <summary>What the follows read out of a show's kept description, and the one rule that turns an air date into "due".</summary>
public static class FollowViews
{
    /// <summary>
    /// A show can be followed while episodes may still come: the database's status is anything but
    /// <c>Ended</c> or <c>Canceled</c>. A show the database has not described cannot be followed.
    /// </summary>
    public static bool Followable(ShowDetails? details) =>
        details is not null
        && !string.Equals(details.Status, "Ended", StringComparison.OrdinalIgnoreCase)
        && !string.Equals(details.Status, "Canceled", StringComparison.OrdinalIgnoreCase);

    /// <summary>An air date as the database writes it, <c>2025-01-16</c>: a date, with no time or zone.</summary>
    public static DateOnly? AirDate(string? date) =>
        DateOnly.TryParseExact(date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed)
            ? parsed
            : null;

    /// <summary>The day it is in UTC, which is the zone an air date is read against.</summary>
    public static DateOnly Today(TimeProvider clock) => DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);

    /// <summary>
    /// Whether an episode is due on a day: from the day after its air date. An air date names the
    /// day in the network's own zone, which can lie a day behind this one, and a release follows the
    /// broadcast by hours, so the day after is when the tracker can be expected to hold it.
    /// </summary>
    public static bool IsDue(DateOnly airDate, DateOnly today) => airDate < today;

    public static AiringView? Airing(AiringEpisode? episode) =>
        episode is { AirDate: { Length: > 0 } date } && AirDate(date) is not null
            ? new AiringView(episode.Season, episode.Number, episode.Name, date)
            : null;

    /// <summary>
    /// The latest episode that is already due on a day, which is where a follow begins: only episodes
    /// after it are news. Null when none is due yet.
    /// </summary>
    public static EpisodeIdentity? Baseline(StoredShow? stored, DateOnly today)
    {
        var due = (stored?.Seasons ?? [])
            .Where(s => s.Number > 0)
            .SelectMany(s => s.Episodes)
            .Select(e => (e.Season, e.Number, Date: AirDate(e.AirDate)))
            .Concat(stored?.Details?.LastEpisodeToAir is { } last ? [(last.Season, last.Number, Date: AirDate(last.AirDate))] : [])
            .Where(e => e.Season > 0 && e.Date is { } date && IsDue(date, today))
            .OrderByDescending(e => e.Season).ThenByDescending(e => e.Number)
            .FirstOrDefault();

        return due == default ? null : new EpisodeIdentity { Season = due.Season, Number = due.Number };
    }

    /// <summary>One follow as the person's list shows it.</summary>
    public static FollowView View(this CatalogueTitles store, StoredFollow follow)
    {
        var details = store.Show(follow.ImdbId)?.Details;
        var card = store.Card(follow.ImdbId);
        return new FollowView
        {
            ImdbId = follow.ImdbId,
            Title = card?.Title ?? follow.ImdbId,
            Card = card,
            Since = follow.Since,
            AutoDownload = follow.AutoDownload,
            NextEpisode = Airing(details?.NextEpisodeToAir),
        };
    }
}
