using TheKrystalShip.MovieBot.Api.Data;
using TheKrystalShip.MovieBot.Core;

namespace TheKrystalShip.MovieBot.Api.Plans;

/// <summary>One person's answer to one time of a plan.</summary>
public sealed record StoredAnswer(DateTimeOffset At, string UserId, string Answer);

/// <summary>A plan to watch a title together, with its times, its people and their answers.</summary>
public sealed record StoredPlan
{
    public required string Id { get; init; }

    /// <summary>The canonical <c>tt0458352</c> form.</summary>
    public required string ImdbId { get; init; }

    /// <summary>One of <see cref="Wants.WantKinds"/>.</summary>
    public required string Kind { get; init; }

    /// <summary>For a show, the episode the plan starts at.</summary>
    public int? Season { get; init; }

    public int? Number { get; init; }

    public required string MadeBy { get; init; }

    public required DateTimeOffset MadeAt { get; init; }

    /// <summary>Whether the maker's token held <c>cinema:download</c>, which lets the server fetch the title for the plan.</summary>
    public required bool MayFetch { get; init; }

    /// <summary>One of <see cref="PlanStates"/>.</summary>
    public required string State { get; init; }

    public DateTimeOffset? FixedTime { get; init; }

    public string? RoomId { get; init; }

    /// <summary>The proposed times, earliest first; the fixed time alone once there is one.</summary>
    public required IReadOnlyList<DateTimeOffset> Times { get; init; }

    /// <summary>Everybody in the plan, the maker first, then in the order they were invited.</summary>
    public required IReadOnlyList<string> People { get; init; }

    public required IReadOnlyList<StoredAnswer> Answers { get; init; }
}

/// <summary>A fixed plan's title, which the prefetcher fetches ahead of everything else.</summary>
public sealed record PlannedTitle(
    string ImdbId, string Kind, int? Season, int? Number, string MadeBy, DateTimeOffset MadeAt, DateTimeOffset At);

/// <summary>What answering a time came to.</summary>
public enum AnswerOutcome
{
    Stored,
    NotInPlan,
    NoSuchTime,
    Closed,
}

/// <summary>
/// The plans people make: a title, a few proposed times, the people invited and what each said to each
/// time. A plan creates no want; it is its own record, shown only to its maker and the people invited.
/// Every change is one transaction that checks the state it changes from, so two answers or a fix and a
/// cancel arriving together settle in an order and neither is lost.
/// </summary>
public sealed class PlanStore(CinemaDatabase database)
{
    private const string Columns = "id, imdb_id, kind, season, number, made_by, made_at, may_fetch, state, fixed_time, room_id";

    /// <summary>
    /// Makes a plan: the maker and the people invited are in it, and the maker is <c>in</c> for every
    /// time proposed.
    /// </summary>
    public StoredPlan Make(
        string id, string imdbId, string kind, int? season, int? number, string madeBy, bool mayFetch,
        IReadOnlyList<DateTimeOffset> times, IReadOnlyList<string> people, DateTimeOffset now) => database.Write(c =>
    {
        c.Execute(
            "INSERT INTO plans (id, imdb_id, kind, season, number, made_by, made_at, may_fetch, state) "
            + "VALUES ($id, $imdb, $kind, $season, $number, $by, $at, $fetch, $state)",
            ("$id", id), ("$imdb", imdbId), ("$kind", kind), ("$season", season), ("$number", number), ("$by", madeBy),
            ("$at", now), ("$fetch", mayFetch), ("$state", PlanStates.Proposed));
        c.Execute("INSERT INTO plan_people (plan_id, user_id, invited_at) VALUES ($id, $user, $at)",
            ("$id", id), ("$user", madeBy), ("$at", now));
        foreach (var user in people)
            c.Execute("INSERT INTO plan_people (plan_id, user_id, invited_at) VALUES ($id, $user, $at)",
                ("$id", id), ("$user", user), ("$at", now));
        foreach (var at in times)
        {
            c.Execute("INSERT INTO plan_times (plan_id, at) VALUES ($id, $at)", ("$id", id), ("$at", at));
            c.Execute("INSERT INTO plan_answers (plan_id, at, user_id, answer, answered_at) VALUES ($id, $at, $user, $answer, $now)",
                ("$id", id), ("$at", at), ("$user", madeBy), ("$answer", PlanAnswers.In), ("$now", now));
        }
        return Load(c, id)!;
    });

    public StoredPlan? Get(string id) => database.Read(c => Load(c, id));

    /// <summary>
    /// A person's plans: those still going, soonest first, then those that ended within the last week.
    /// </summary>
    public List<StoredPlan> For(string userId, DateTimeOffset now) => database.Read(c =>
    {
        var ids = c.Query(
            "SELECT p.id FROM plans p JOIN plan_people q ON q.plan_id = p.id WHERE q.user_id = $user "
            + "AND (p.state IN ('proposed', 'fixed', 'open') OR COALESCE(p.fixed_time, p.made_at) > $recent) "
            + "ORDER BY p.state IN ('proposed', 'fixed', 'open') DESC, COALESCE(p.fixed_time, "
            + "(SELECT MIN(at) FROM plan_times WHERE plan_id = p.id), p.made_at), p.id",
            r => r.GetString(0), ("$user", userId), ("$recent", now - TimeSpan.FromDays(7)));
        return ids.Select(id => Load(c, id)).OfType<StoredPlan>().ToList();
    });

    /// <summary>Every plan not yet over, which is what the keeper walks.</summary>
    public List<StoredPlan> Active() => database.Read(c => c
        .Query("SELECT id FROM plans WHERE state IN ('proposed', 'fixed', 'open') ORDER BY id", r => r.GetString(0))
        .Select(id => Load(c, id)).OfType<StoredPlan>().ToList());

    /// <summary>Stores a person's answer to one time while the plan has not opened.</summary>
    public AnswerOutcome Answer(string id, DateTimeOffset at, string userId, string answer, DateTimeOffset now) => database.Write(c =>
    {
        var state = c.First("SELECT state FROM plans WHERE id = $id", r => r.GetString(0), ("$id", id));
        if (state is null || c.Count("SELECT COUNT(*) FROM plan_people WHERE plan_id = $id AND user_id = $user",
                ("$id", id), ("$user", userId)) == 0) return AnswerOutcome.NotInPlan;
        if (state is not (PlanStates.Proposed or PlanStates.Fixed)) return AnswerOutcome.Closed;
        if (c.Count("SELECT COUNT(*) FROM plan_times WHERE plan_id = $id AND at = $at", ("$id", id), ("$at", at)) == 0)
            return AnswerOutcome.NoSuchTime;

        c.Execute(
            "INSERT INTO plan_answers (plan_id, at, user_id, answer, answered_at) VALUES ($id, $at, $user, $answer, $now) "
            + "ON CONFLICT (plan_id, at, user_id) DO UPDATE SET answer = excluded.answer, answered_at = excluded.answered_at",
            ("$id", id), ("$at", at), ("$user", userId), ("$answer", answer), ("$now", now));
        return AnswerOutcome.Stored;
    });

    /// <summary>
    /// Settles a proposed plan on one of its times; the other times and what was answered to them go.
    /// False when the plan is not proposed or has no such time.
    /// </summary>
    public bool Fix(string id, DateTimeOffset at) => database.Write(c =>
    {
        if (c.Count("SELECT COUNT(*) FROM plans WHERE id = $id AND state = 'proposed'", ("$id", id)) == 0
            || c.Count("SELECT COUNT(*) FROM plan_times WHERE plan_id = $id AND at = $at", ("$id", id), ("$at", at)) == 0)
            return false;

        c.Execute("DELETE FROM plan_times WHERE plan_id = $id AND at <> $at", ("$id", id), ("$at", at));
        c.Execute("UPDATE plans SET state = 'fixed', fixed_time = $at WHERE id = $id", ("$id", id), ("$at", at));
        return true;
    });

    /// <summary>Ends a plan that has not opened. False when it is not proposed or fixed.</summary>
    public bool Cancel(string id) => database.Write(c => c.Execute(
        "UPDATE plans SET state = 'cancelled' WHERE id = $id AND state IN ('proposed', 'fixed')", ("$id", id)) > 0);

    /// <summary>Records the room a fixed plan made. False when the plan is not fixed.</summary>
    public bool Open(string id, string roomId) => database.Write(c => c.Execute(
        "UPDATE plans SET state = 'open', room_id = $room WHERE id = $id AND state = 'fixed'", ("$id", id), ("$room", roomId)) > 0);

    /// <summary>Ends a plan whose room was reaped. False when the plan is not open.</summary>
    public bool Finish(string id) => database.Write(c => c.Execute(
        "UPDATE plans SET state = 'done' WHERE id = $id AND state = 'open'", ("$id", id)) > 0);

    /// <summary>
    /// The titles of fixed plans whose maker let the server fetch, with the earliest time first, for
    /// plans whose time is later than <paramref name="since"/>.
    /// </summary>
    public List<PlannedTitle> Planned(DateTimeOffset since) => database.Read(c => c.Query(
        "SELECT imdb_id, kind, season, number, made_by, made_at, fixed_time FROM plans "
        + "WHERE state IN ('fixed', 'open') AND may_fetch = 1 AND fixed_time > $since ORDER BY fixed_time, id",
        r => new PlannedTitle(r.GetString(0), r.GetString(1), r.IsDBNull(2) ? null : r.GetInt32(2),
            r.IsDBNull(3) ? null : r.GetInt32(3), r.GetString(4), r.GetInstant(5), r.GetInstant(6)),
        ("$since", since)));

    /// <summary>The IMDb ids of the titles of fixed plans later than <paramref name="since"/>, which the library keeps.</summary>
    public HashSet<string> Titles(DateTimeOffset since) => database.Read(c => c.Query(
        "SELECT DISTINCT imdb_id FROM plans WHERE state IN ('fixed', 'open') AND fixed_time > $since",
        r => r.GetString(0), ("$since", since))).ToHashSet(StringComparer.Ordinal);

    /// <summary>The rooms the reaper leaves alone: those of open plans whose time is later than <paramref name="since"/>.</summary>
    public HashSet<string> RoomsHeld(DateTimeOffset since) => database.Read(c => c.Query(
        "SELECT room_id FROM plans WHERE state = 'open' AND room_id IS NOT NULL AND fixed_time > $since",
        r => r.GetString(0), ("$since", since))).ToHashSet(StringComparer.Ordinal);

    private static StoredPlan? Load(Microsoft.Data.Sqlite.SqliteConnection c, string id)
    {
        var plan = c.First($"SELECT {Columns} FROM plans WHERE id = $id", r => new StoredPlan
        {
            Id = r.GetString(0), ImdbId = r.GetString(1), Kind = r.GetString(2),
            Season = r.IsDBNull(3) ? null : r.GetInt32(3), Number = r.IsDBNull(4) ? null : r.GetInt32(4),
            MadeBy = r.GetString(5), MadeAt = r.GetInstant(6), MayFetch = r.GetBool(7), State = r.GetString(8),
            FixedTime = r.IsDBNull(9) ? null : r.GetInstant(9), RoomId = r.IsDBNull(10) ? null : r.GetString(10),
            Times = [], People = [], Answers = [],
        }, ("$id", id));
        if (plan is null) return null;

        return plan with
        {
            Times = c.Query("SELECT at FROM plan_times WHERE plan_id = $id ORDER BY at", r => r.GetInstant(0), ("$id", id)),
            People = c.Query("SELECT user_id FROM plan_people WHERE plan_id = $id ORDER BY user_id = $by DESC, invited_at, rowid",
                r => r.GetString(0), ("$id", id), ("$by", plan.MadeBy)),
            Answers = c.Query("SELECT at, user_id, answer FROM plan_answers WHERE plan_id = $id ORDER BY at, answered_at, user_id",
                r => new StoredAnswer(r.GetInstant(0), r.GetString(1), r.GetString(2)), ("$id", id)),
        };
    }
}
