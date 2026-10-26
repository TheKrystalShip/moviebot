using System.Globalization;
using System.Text.RegularExpressions;
using TheKrystalShip.Agent.Plugin;
using TheKrystalShip.MovieBot.Core;

namespace TheKrystalShip.MovieBot.Assistant.Tools;

/// <summary>
/// Times as the person reads them: in the zone their browser names, else this host's. A time the model
/// writes is read in that zone, and a time a tool writes is written in it, so the person, the model and the
/// plan's page name one moment the same way.
/// </summary>
internal static partial class PersonClock
{
    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

    public static TimeZoneInfo Zone(Caller caller, TimeProvider clock) =>
        caller.TimeZone is { } id && TimeZoneInfo.TryFindSystemTimeZoneById(id, out var zone) ? zone : clock.LocalTimeZone;

    /// <summary>"Fri 2026-10-30 21:00", in <paramref name="zone"/>.</summary>
    public static string Write(DateTimeOffset at, TimeZoneInfo zone) =>
        TimeZoneInfo.ConvertTime(at, zone).ToString("ddd yyyy-MM-dd HH:mm", Invariant);

    [GeneratedRegex(@"(?<date>\d{4}-\d{2}-\d{2})[ T]+(?<hour>\d{1,2}):(?<minute>\d{2})")]
    private static partial Regex Written();

    /// <summary>The instant a time written in the person's clock names, to the minute; a refusal when it names none.</summary>
    public static DateTimeOffset Read(string said, TimeZoneInfo zone)
    {
        Match m = Written().Match(said);
        DateOnly day = default;
        int hour = m.Success ? int.Parse(m.Groups["hour"].Value, Invariant) : -1;
        int minute = m.Success ? int.Parse(m.Groups["minute"].Value, Invariant) : -1;
        if (!m.Success || hour > 23 || minute > 59
            || !DateOnly.TryParseExact(m.Groups["date"].Value, "yyyy-MM-dd", Invariant, DateTimeStyles.None, out day))
            throw new ToolRefusal($"'{said}' is not a time. Write it as YYYY-MM-DD HH:mm, as 2026-10-30 21:00.");

        var local = day.ToDateTime(new TimeOnly(hour, minute), DateTimeKind.Unspecified);
        if (zone.IsInvalidTime(local))
            throw new ToolRefusal($"{said} does not happen in the person's time zone: the clocks change then. Pick another time.");
        return new DateTimeOffset(local, zone.GetUtcOffset(local));
    }

    /// <summary>Why a time cannot be put forward now, in the person's clock; null when it can.</summary>
    public static string? NotAhead(DateTimeOffset at, DateTimeOffset now, TimeZoneInfo zone) =>
        PlanLimits.Ahead(PlanLimits.Minute(at), now) ? null
            : $"{Write(at, zone)} cannot be put forward: {PlanLimits.NotAhead[..1].ToLowerInvariant()}{PlanLimits.NotAhead[1..]} It is {Write(now, zone)} for the person.";
}
