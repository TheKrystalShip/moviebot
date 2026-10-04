using TheKrystalShip.MovieBot.Core;

namespace TheKrystalShip.MovieBot.Api.Transcode;

/// <summary>
/// A room somebody is in with a film set, paused or playing, and the qualities its viewers would
/// choose. A room merely holding a film is not demand and is not passed in.
/// </summary>
public sealed record PlanRoom(
    string SessionId, string TitleId, double PlayheadSeconds, int Viewers, IReadOnlyList<int> WantedHeights)
{
    /// <summary>
    /// The one definition of a room being played: somebody connected to it, with its film set, paused
    /// or playing. Demand in every tier and whether a film may settle both read this.
    /// </summary>
    public bool IsPlaying => Viewers > 0;
}

/// <summary>Seconds of film each output of a title holds.</summary>
public sealed record TitleHeads(IReadOnlyDictionary<string, double> Rungs, double Audio);

/// <summary>One title as the planner needs it: what is on disk, what the source was, how far each output has got.</summary>
public sealed record PlanTitle(
    string Id,
    TitleStatus Status,
    double DurationSeconds,
    int SourceWidth,
    int SourceHeight,
    IReadOnlyList<TranscodeRung> Rungs,
    bool HasAudio,
    TitleHeads Heads);

/// <summary>Everything one plan is made from. The planner reads nothing else.</summary>
public sealed record PlanInput(
    DateTimeOffset Now,
    IReadOnlyList<PlanRoom> Rooms,
    IReadOnlyList<PlanTitle> Titles,
    TranscodeStatus? Status,
    TranscodeOptions Options);

/// <summary>
/// Ranks every unit of work and decides how much of it the card can carry. It is a function of its
/// input: a restart of either side computes the same answer from the same facts.
/// </summary>
public static class TranscodePlanner
{
    private const double ReferenceArea = 1920.0 * 1080.0;

    private static readonly string[] TierNames =
    [
        "",
        "watched, short of the lead",
        "asked for by a room, nothing made yet",
        "watched, comfortably ahead",
        "an unwatched quality of a film in a room",
        "a film nobody holds, owed its first pass",
        "backfill: a quality the film is owed"
    ];

    private sealed class Candidate
    {
        public required TranscodeUnitKind Kind;
        public required PlanTitle Title;
        public TranscodeRung? Rung;
        public required string Id;
        public required int FromSegment;
        public required int Tier;
        public double Ahead;
        public TranscodeUnitReport? Report;
        public bool Backfill => Kind == TranscodeUnitKind.RungAdd;
        public bool Failed => Report?.State == TranscodeUnitState.Failed;
        public double NvencCost => Kind == TranscodeUnitKind.Audio ? 0 : Area(Rung);
        public double NvdecCost => Kind == TranscodeUnitKind.Audio ? 0 : 1;
    }

    private sealed class Group
    {
        public required List<Candidate> Members;
        public int Tier;
        public int EffectiveTier;
        public bool Pinned;
        public double Ahead;
        public int TopHeight;
        public bool Running;
    }

    public static string UnitId(TranscodeUnitKind kind, string titleId, string? directory) => kind switch
    {
        TranscodeUnitKind.Audio => $"audio/{titleId}",
        TranscodeUnitKind.RungAdd => $"rung-add/{titleId}/{directory}",
        _ => $"video-rung/{titleId}/{directory}"
    };

    /// <summary>
    /// The rung a viewer who wants a height is served: the tallest that does not exceed it, or the
    /// lowest there is when every rung is taller.
    /// </summary>
    public static TranscodeRung? RungFor(IReadOnlyList<TranscodeRung> rungs, int wantedHeight)
    {
        TranscodeRung? best = null;
        TranscodeRung? lowest = null;
        foreach (var rung in rungs)
        {
            if (lowest is null || rung.Height < lowest.Height) lowest = rung;
            if (rung.Height <= wantedHeight && (best is null || rung.Height > best.Height)) best = rung;
        }

        return best ?? lowest;
    }

    public static TranscodePlan Plan(PlanInput input)
    {
        var options = input.Options;
        var reports = new Dictionary<string, TranscodeUnitReport>();
        foreach (var report in input.Status?.Units ?? [])
            reports[report.Id] = report;

        var roomsByTitle = input.Rooms
            .Where(r => r.IsPlaying)
            .GroupBy(r => r.TitleId)
            .ToDictionary(g => g.Key, g => g.ToList());
        var anyPlaying = roomsByTitle.Count > 0;

        var candidates = new List<Candidate>();
        foreach (var title in input.Titles)
        {
            roomsByTitle.TryGetValue(title.Id, out var rooms);
            if (title.Status == TitleStatus.Transcoding)
                Unfinished(title, rooms, reports, options, candidates);
            else if (title.Status == TitleStatus.Ready)
                Owed(title, reports, options, candidates);
        }

        var groups = Grouped(candidates, input.Now, options);
        groups.Sort(Compare);

        var units = Dispose(groups, anyPlaying, input.Now, options);
        var titles = input.Titles
            .Where(t => t.Status == TitleStatus.Ready)
            .Select(t => new TitleDisposition(t.Id, !roomsByTitle.ContainsKey(t.Id)))
            .ToList();

        return new TranscodePlan
        {
            PlannedAt = input.Now,
            ExecutorReporting = input.Status is not null,
            Units = units,
            Titles = titles
        };
    }

    /// <summary>The rungs and audio a title still being made has yet to finish.</summary>
    private static void Unfinished(
        PlanTitle title, List<PlanRoom>? rooms, Dictionary<string, TranscodeUnitReport> reports,
        TranscodeOptions options, List<Candidate> into)
    {
        foreach (var rung in title.Rungs)
        {
            var id = UnitId(TranscodeUnitKind.VideoRung, title.Id, rung.Directory);
            reports.TryGetValue(id, out var report);
            var head = HeadOf(report, rung.Directory, title.Heads.Rungs.GetValueOrDefault(rung.Directory));
            if (Finished(head, title)) continue;

            var (tier, ahead) = Rank(title, rooms, rung.Directory, head, report, options);
            into.Add(new Candidate
            {
                Kind = TranscodeUnitKind.VideoRung, Title = title, Rung = rung, Id = id,
                FromSegment = Segment(head), Tier = tier, Ahead = ahead, Report = report
            });
        }

        if (!title.HasAudio) return;

        var audioId = UnitId(TranscodeUnitKind.Audio, title.Id, null);
        reports.TryGetValue(audioId, out var audioReport);
        var audioHead = HeadOf(audioReport, "audio", title.Heads.Audio);
        if (Finished(audioHead, title)) return;

        var (audioTier, audioAhead) = Rank(title, rooms, null, audioHead, audioReport, options);
        into.Add(new Candidate
        {
            Kind = TranscodeUnitKind.Audio, Title = title, Id = audioId,
            FromSegment = Segment(audioHead), Tier = audioTier, Ahead = audioAhead, Report = audioReport
        });
    }

    /// <summary>
    /// The rungs a finished title's source should have made and the title lacks. Computed on every
    /// plan from the manifest's own list, so there is no record of owed work to drift from the disk.
    /// </summary>
    private static void Owed(
        PlanTitle title, Dictionary<string, TranscodeUnitReport> reports, TranscodeOptions options, List<Candidate> into)
    {
        if (title.SourceWidth <= 0 || title.SourceHeight <= 0) return;

        var ladder = Ladder.For(
            title.SourceWidth, title.SourceHeight, Ladder.TopKbps, Ladder.StepDownWidth, Ladder.StepDownKbps);
        var have = title.Rungs.Select(r => r.Directory).ToHashSet(StringComparer.Ordinal);

        foreach (var rung in ladder)
        {
            if (have.Contains(rung.Directory)) continue;

            var id = UnitId(TranscodeUnitKind.RungAdd, title.Id, rung.Directory);
            reports.TryGetValue(id, out var report);
            into.Add(new Candidate
            {
                Kind = TranscodeUnitKind.RungAdd, Title = title, Id = id, FromSegment = 0, Tier = 6,
                Rung = new TranscodeRung(rung.Directory, rung.Width, rung.Height, rung.BitrateKbps),
                Report = report
            });
        }
    }

    /// <summary>
    /// The tier a unit of a title still being made falls in, and how far ahead of its neediest
    /// watching room's playhead its output is.
    /// </summary>
    private static (int Tier, double Ahead) Rank(
        PlanTitle title, List<PlanRoom>? rooms, string? directory, double head,
        TranscodeUnitReport? report, TranscodeOptions options)
    {
        if (rooms is null) return (5, head);

        // Audio is wanted by every room holding the title: a viewer cannot watch a rung without it.
        var wanting = directory is null
            ? rooms
            : rooms.Where(r => r.WantedHeights.Any(h => RungFor(title.Rungs, h)?.Directory == directory)).ToList();
        if (wanting.Count == 0) return (4, head);

        if (head <= 0) return (2, head);

        var ahead = head - wanting.Max(r => r.PlayheadSeconds);
        var running = report?.State == TranscodeUnitState.Running;
        var shortOfLead = running ? ahead < options.LeadSeconds + options.BandSeconds
                                  : ahead < options.LeadSeconds - options.BandSeconds;
        return (shortOfLead ? 1 : 3, ahead);
    }

    private static List<Group> Grouped(List<Candidate> candidates, DateTimeOffset now, TranscodeOptions options)
    {
        IEnumerable<List<Candidate>> sets = options.PerRung
            ? candidates.Select(c => new List<Candidate> { c })
            : candidates
                .GroupBy(c => (c.Title.Id, c.Backfill))
                .Select(g => g.ToList());

        var groups = new List<Group>();
        foreach (var members in sets)
        {
            var tier = members.Min(c => c.Tier);
            var pinned = members.Any(c =>
                !c.Backfill
                && c.Report is { State: TranscodeUnitState.Running, Since: { } since }
                && (now - since).TotalSeconds < options.MinRunSeconds);

            groups.Add(new Group
            {
                Members = members,
                Tier = tier,
                // A unit that has only just started is not displaced by anything less urgent than a
                // watched film short of lead or one a room has just asked for.
                EffectiveTier = pinned ? Math.Min(tier, 3) : tier,
                Pinned = pinned && tier > 3,
                Ahead = members.Min(c => c.Ahead),
                TopHeight = members.Max(c => c.Rung?.Height ?? 0),
                Running = members.Any(c => c.Report?.State == TranscodeUnitState.Running)
            });
        }

        return groups;
    }

    private static int Compare(Group a, Group b)
    {
        var byTier = a.EffectiveTier.CompareTo(b.EffectiveTier);
        if (byTier != 0) return byTier;

        if (a.EffectiveTier == 1)
        {
            var byNeed = a.Ahead.CompareTo(b.Ahead);
            if (byNeed != 0) return byNeed;
        }

        var byHeight = b.TopHeight.CompareTo(a.TopHeight);
        if (byHeight != 0) return byHeight;

        var byRunning = b.Running.CompareTo(a.Running);
        if (byRunning != 0) return byRunning;

        return string.CompareOrdinal(a.Members[0].Id, b.Members[0].Id);
    }

    /// <summary>Walks the ranked groups from the top, spending the budgets, and says what each unit does.</summary>
    private static List<TranscodePlanUnit> Dispose(
        List<Group> groups, bool anyPlaying, DateTimeOffset now, TranscodeOptions options)
    {
        var units = new List<TranscodePlanUnit>();
        double nvenc = 0, nvdec = 0;
        var admittedAny = false;
        var held = 0;

        foreach (var group in groups)
        {
            var live = group.Members.Where(c => !c.Failed).ToList();
            var costEnc = live.Sum(c => c.NvencCost);
            var costDec = live.Sum(c => c.NvdecCost);
            var backfillWaits = anyPlaying && group.Tier == 6;

            string? over = null;
            var admitted = false;
            if (backfillWaits)
            {
                over = "a room is playing";
            }
            else if (live.Count > 0)
            {
                // The first group is always admitted, so a film dearer than the whole budget still runs.
                if (!admittedAny || (nvenc + costEnc <= options.NvencBudget && nvdec + costDec <= options.NvdecBudget))
                    admitted = true;
                else
                    over = nvenc + costEnc > options.NvencBudget ? "over the encoder budget" : "over the decoder budget";
            }

            if (admitted)
            {
                admittedAny = true;
                nvenc += costEnc;
                nvdec += costDec;
            }

            foreach (var c in group.Members)
            {
                var disposition = TranscodeDisposition.Park;
                var note = over;

                if (c.Failed)
                {
                    note = "the executor reports it failed";
                }
                else if (admitted)
                {
                    disposition = TranscodeDisposition.Run;
                    note = group.Pinned ? "kept running within its minimum run time" : null;
                }
                else if (!backfillWaits && StaysStopped(c, now, options, ref held))
                {
                    disposition = TranscodeDisposition.Hold;
                }

                units.Add(new TranscodePlanUnit
                {
                    Id = c.Id,
                    Kind = c.Kind,
                    TitleId = c.Title.Id,
                    Rung = c.Rung,
                    FromSegment = c.FromSegment,
                    Disposition = disposition,
                    Rank = units.Count,
                    Tier = group.EffectiveTier,
                    Reason = Reason(group, c, note)
                });
            }
        }

        return units;
    }

    /// <summary>
    /// Whether a unit that is not to run is stopped in place rather than parked: only a process that
    /// is already running or held, not yet held past the park threshold, and within how many stopped
    /// processes the card's memory can keep.
    /// </summary>
    private static bool StaysStopped(Candidate c, DateTimeOffset now, TranscodeOptions options, ref int held)
    {
        var state = c.Report?.State;
        if (state is not (TranscodeUnitState.Running or TranscodeUnitState.Held)) return false;

        if (state == TranscodeUnitState.Held && c.Report?.Since is { } since
            && (now - since).TotalSeconds >= options.ParkAfterSeconds)
            return false;

        if (held >= options.MaxHeld) return false;
        held++;
        return true;
    }

    private static string Reason(Group group, Candidate c, string? note)
    {
        var text = $"tier {group.EffectiveTier}: {TierNames[group.EffectiveTier]}";
        if (group.EffectiveTier is 1 or 3 && c.Tier == group.Tier)
            text += $" ({Math.Round(c.Ahead):0} s ahead)";
        return note is null ? text : $"{text}; {note}";
    }

    /// <summary>
    /// How far an output has got. The executor's measurement is newer than the manifest's, and is
    /// taken when there is one; otherwise the manifest's figure stands.
    /// </summary>
    private static double HeadOf(TranscodeUnitReport? report, string output, double manifestHead) =>
        report?.Heads is { } heads && heads.TryGetValue(output, out var reported)
            ? reported
            : manifestHead;

    /// <summary>An output is finished once it is within a segment of the film's end, the last one being a partial.</summary>
    private static bool Finished(double head, PlanTitle title) =>
        title.DurationSeconds > 0 && head >= title.DurationSeconds - Ladder.SegmentSeconds;

    private static int Segment(double head) =>
        (int)Math.Floor(Math.Max(0, head) / Ladder.SegmentSeconds);

    private static double Area(TranscodeRung? rung) =>
        rung is { Width: > 0, Height: > 0 } ? rung.Width * (double)rung.Height / ReferenceArea : 1;
}
