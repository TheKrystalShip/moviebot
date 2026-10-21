using TheKrystalShip.MovieBot.Acquire.Download;
using TheKrystalShip.MovieBot.Api.Library;
using TheKrystalShip.MovieBot.Core;

namespace TheKrystalShip.MovieBot.Api.Plans;

/// <summary>
/// The rules of a plan, pure: each is settled by the facts handed in and nothing is assumed.
/// </summary>
public static class PlanRules
{
    /// <summary>The fewest proposed times a plan carries.</summary>
    public const int MinTimes = 1;

    /// <summary>The most times a plan holds still ahead: what it is made with, and what is suggested after.</summary>
    public const int MaxTimes = 6;

    /// <summary>How soon a time put forward may be, in minutes.</summary>
    public const int SoonestMinutes = 5;

    /// <summary>How far ahead a proposed time may be, in days.</summary>
    public const int FurthestDays = 30;

    /// <summary>The most people one plan invites.</summary>
    public const int MaxInvited = 20;

    /// <summary>An instant to the minute, in UTC: what a time is called.</summary>
    public static DateTimeOffset Minute(DateTimeOffset at)
    {
        var utc = at.UtcDateTime;
        return new DateTimeOffset(utc.Year, utc.Month, utc.Day, utc.Hour, utc.Minute, 0, TimeSpan.Zero);
    }

    /// <summary>
    /// The proposed times, to the minute and without repeats, or why they cannot be: one to
    /// <see cref="MaxTimes"/>, each <see cref="Ahead"/>.
    /// </summary>
    public static (List<DateTimeOffset> Times, string? Refusal) Times(IEnumerable<DateTimeOffset>? requested, DateTimeOffset now)
    {
        var times = (requested ?? []).Select(Minute).Distinct().Order().ToList();
        if (times.Count < MinTimes || times.Count > MaxTimes)
            return (times, $"A plan proposes {MinTimes} to {MaxTimes} times.");

        return times.Any(t => !Ahead(t, now)) ? (times, NotAhead) : (times, null);
    }

    /// <summary>Whether a time may be put forward: at least <see cref="SoonestMinutes"/> and at most <see cref="FurthestDays"/> ahead.</summary>
    public static bool Ahead(DateTimeOffset at, DateTimeOffset now) =>
        at >= Minute(now) + TimeSpan.FromMinutes(SoonestMinutes) && at <= now + TimeSpan.FromDays(FurthestDays);

    /// <summary>Why a time that is not <see cref="Ahead"/> is refused.</summary>
    public static readonly string NotAhead = $"A time is at least {SoonestMinutes} minutes and at most {FurthestDays} days ahead.";

    /// <summary>How many people said <c>in</c> to a time.</summary>
    public static int In(StoredPlan plan, DateTimeOffset at) =>
        plan.Answers.Count(a => a.At == at && a.Answer == PlanAnswers.In);

    /// <summary>The people who said <c>in</c> or <c>maybe</c> to the plan's picked time, which is who the room lets in.</summary>
    public static List<string> Coming(StoredPlan plan) => plan.FixedTime is not { } at ? []
        : [.. plan.People.Where(u => plan.Answers.Any(a => a.At == at && a.UserId == u && a.Answer is PlanAnswers.In or PlanAnswers.Maybe))];

    /// <summary>Whether the room is due: it is made <paramref name="openMinutes"/> ahead of the fixed time.</summary>
    public static bool OpenDue(StoredPlan plan, DateTimeOffset now, int openMinutes) =>
        plan.FixedTime is { } at && now >= at - TimeSpan.FromMinutes(openMinutes);

    /// <summary>Whether the reminder is due: from <paramref name="remindMinutes"/> before the fixed time until it.</summary>
    public static bool ReminderDue(StoredPlan plan, DateTimeOffset now, int remindMinutes) =>
        plan.FixedTime is { } at && now >= at - TimeSpan.FromMinutes(remindMinutes) && now <= at;

    /// <summary>The room a plan's people meet in. It is the plan's id: nobody else holds it.</summary>
    public static string RoomOf(string planId) => planId;

    /// <summary>
    /// The library title that plays the plan's film, or the plan's episode of its show; null while the
    /// library holds none.
    /// </summary>
    public static string? LibraryId(IReadOnlyList<TitleSummary> titles, StoredPlan plan) => plan.Kind == Wants.WantKinds.Show
        ? titles.FirstOrDefault(t => t is { Kind: TitleKind.Show, Episode: { } e } && t.Status != TitleStatus.Failed
            && string.Equals(t.Film?.ImdbId, plan.ImdbId, StringComparison.OrdinalIgnoreCase)
            && e.Season == plan.Season && e.Number == plan.Number)?.Id
        : titles.FirstOrDefault(t => t.Kind != TitleKind.Show && t.Status != TitleStatus.Failed
            && string.Equals(t.Film?.ImdbId, plan.ImdbId, StringComparison.OrdinalIgnoreCase))?.Id;

    /// <summary>
    /// What is measured of a plan's title: held by the library, arriving, known to have no release at
    /// the source floor, or none of those.
    /// </summary>
    public static (string Readiness, double? Progress) Readiness(bool inLibrary, DownloadStatus? download, bool askedEmpty) =>
        inLibrary ? (Core.PlanReadiness.Ready, null)
        : download is not null ? (Core.PlanReadiness.Fetching, download.Progress)
        : askedEmpty ? (Core.PlanReadiness.Waiting, null)
        : (Core.PlanReadiness.Queued, null);
}
