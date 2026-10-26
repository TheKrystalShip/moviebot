namespace TheKrystalShip.MovieBot.Core;

/// <summary>
/// Where a plan stands (<see cref="PlanView.State"/>). A plan exists only while it is going: cancelling it
/// or its room going after its time deletes it.
/// </summary>
public static class PlanStates
{
    /// <summary>Times are proposed and people are answering.</summary>
    public const string Proposed = "proposed";

    /// <summary>The maker picked a time and the room is not made yet.</summary>
    public const string Fixed = "fixed";

    /// <summary>The room exists, closed, with the people who said they come let in.</summary>
    public const string Open = "open";

    public static bool IsKnown(string? state) => state is Proposed or Fixed or Open;
}

/// <summary>What a person says of one proposed time (<see cref="PlanAnswerView.Answer"/>).</summary>
public static class PlanAnswers
{
    public const string In = "in";

    public const string Maybe = "maybe";

    public const string Cant = "cant";

    public static bool IsKnown(string? answer) => answer is In or Maybe or Cant;
}

/// <summary>What is measured of a plan's title (<see cref="PlanView.Readiness"/>).</summary>
public static class PlanReadiness
{
    /// <summary>The library holds it.</summary>
    public const string Ready = "ready";

    /// <summary>The torrent client has it and it is arriving.</summary>
    public const string Fetching = "fetching";

    /// <summary>The tracker offers nothing for it at the source floor.</summary>
    public const string Waiting = "waiting";

    /// <summary>Nothing is arriving yet and nothing says it cannot.</summary>
    public const string Queued = "queued";
}

/// <summary>One person's answer to one time.</summary>
public sealed record PlanAnswerView
{
    public required string UserId { get; init; }

    public string? Name { get; init; }

    /// <summary>One of <see cref="PlanAnswers"/>.</summary>
    public required string Answer { get; init; }
}

/// <summary>One proposed time, with how the people invited answered it.</summary>
public sealed record PlanTimeView
{
    public required DateTimeOffset At { get; init; }

    public required List<PlanAnswerView> Answers { get; init; }

    /// <summary>How many said <c>in</c>.</summary>
    public int In { get; init; }

    /// <summary>The asking person's answer, absent until they give one.</summary>
    public string? Mine { get; init; }

    /// <summary>Who put the time forward.</summary>
    public PlanPersonView? SuggestedBy { get; init; }
}

/// <summary>Somebody in a plan.</summary>
public sealed record PlanPersonView
{
    public required string UserId { get; init; }

    public string? Name { get; init; }

    /// <summary>Whether this person made the plan.</summary>
    public bool Maker { get; init; }
}

/// <summary>A plan as one of its people sees it.</summary>
public sealed record PlanView
{
    public required string Id { get; init; }

    public required string ImdbId { get; init; }

    /// <summary><c>film</c> or <c>show</c>.</summary>
    public required string Kind { get; init; }

    /// <summary>The title's name: its card's, else its IMDb id.</summary>
    public required string Title { get; init; }

    /// <summary>The title's card; absent for a title the catalogue does not list.</summary>
    public Catalogue.TitleCard? Card { get; init; }

    /// <summary>For a show, the episode the plan starts at.</summary>
    public int? Season { get; init; }

    public int? Number { get; init; }

    /// <summary>One of <see cref="PlanStates"/>.</summary>
    public required string State { get; init; }

    public required PlanPersonView MadeBy { get; init; }

    public required DateTimeOffset MadeAt { get; init; }

    /// <summary>Whether the asking person made the plan.</summary>
    public bool Mine { get; init; }

    /// <summary>The time settled on, once there is one.</summary>
    public DateTimeOffset? FixedTime { get; init; }

    /// <summary>The proposed times while the plan is proposed; the fixed time alone after.</summary>
    public required List<PlanTimeView> Times { get; init; }

    public required List<PlanPersonView> People { get; init; }

    /// <summary>One of <see cref="PlanReadiness"/>.</summary>
    public string? Readiness { get; init; }

    /// <summary>How much of the title has arrived, from 0 to 1, while it is <see cref="PlanReadiness.Fetching"/>.</summary>
    public double? Progress { get; init; }

    /// <summary>For a film the library holds, the title that plays it.</summary>
    public string? LibraryId { get; init; }

    /// <summary>The plan's room, once the plan is open.</summary>
    public string? RoomId { get; init; }
}

/// <summary>Plan watching a title: when, and with whom. A show starts at its season and episode, or at the maker's next.</summary>
public sealed record MakePlanRequest
{
    public string? ImdbId { get; init; }

    public string? Kind { get; init; }

    public int? Season { get; init; }

    public int? Number { get; init; }

    public List<DateTimeOffset>? Times { get; init; }

    public List<string>? People { get; init; }
}

/// <summary>A person's answer to one time of a plan.</summary>
public sealed record PlanAnswerRequest
{
    public string? Answer { get; init; }
}

/// <summary>A time of a plan: the one the maker picks, or one somebody in it suggests.</summary>
public sealed record FixPlanRequest
{
    public DateTimeOffset At { get; init; }
}

/// <summary>
/// The limits a plan's times and people keep, the same for the API that enforces them and for anybody
/// checking a request before making it (authority: <c>docs/api-contract.md</c>, Plans).
/// </summary>
public static class PlanLimits
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
}
