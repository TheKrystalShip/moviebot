using System.Text.Json.Serialization;

namespace TheKrystalShip.MovieBot.Core;

/// <summary>What one unit of transcode work makes.</summary>
public enum TranscodeUnitKind
{
    /// <summary>One quality of one title, from a segment to the end.</summary>
    [JsonStringEnumMemberName("video-rung")] VideoRung,

    /// <summary>Every audio rendition of one title.</summary>
    [JsonStringEnumMemberName("audio")] Audio,

    /// <summary>A quality a finished title is owed and does not have.</summary>
    [JsonStringEnumMemberName("rung-add")] RungAdd
}

/// <summary>What the executor should be doing with a unit.</summary>
public enum TranscodeDisposition
{
    /// <summary>Running, within the budget.</summary>
    [JsonStringEnumMemberName("run")] Run,

    /// <summary>Stopped in place with its state kept; for a wait measured in minutes.</summary>
    [JsonStringEnumMemberName("hold")] Hold,

    /// <summary>Not running and holding nothing; resumed later from its last whole segment.</summary>
    [JsonStringEnumMemberName("park")] Park
}

/// <summary>Where the executor has got a unit to.</summary>
public enum TranscodeUnitState
{
    [JsonStringEnumMemberName("running")] Running,
    [JsonStringEnumMemberName("held")] Held,
    [JsonStringEnumMemberName("parked")] Parked,
    [JsonStringEnumMemberName("done")] Done,
    [JsonStringEnumMemberName("failed")] Failed
}

/// <summary>A quality of a film: the directory it is written to and what it is encoded at.</summary>
public sealed record TranscodeRung(string Directory, int Width, int Height, int BitrateKbps);

/// <summary>
/// One piece of work the executor can do alone and repeat safely.
///
/// <see cref="Id"/> is made of what the unit is (its kind, its title and its rung) and nothing about
/// how far it has got, so it is the same string on every plan for as long as the work is owed.
/// </summary>
public sealed record TranscodePlanUnit
{
    public required string Id { get; init; }
    public required TranscodeUnitKind Kind { get; init; }
    public required string TitleId { get; init; }

    /// <summary>The quality made; absent on audio.</summary>
    public TranscodeRung? Rung { get; init; }

    /// <summary>The first segment still to be made, from what the outputs already hold.</summary>
    public required int FromSegment { get; init; }

    public required TranscodeDisposition Disposition { get; init; }

    /// <summary>Zero is the most urgent unit. Order in the plan is rank.</summary>
    public required int Rank { get; init; }

    /// <summary>The policy tier that placed the unit, one to six; the plan's reason in a form to compare.</summary>
    public required int Tier { get; init; }

    /// <summary>Why the unit stands where it does, in words for a log.</summary>
    public required string Reason { get; init; }

    /// <summary>Where new output is written; absent until placement decides.</summary>
    public string? Placement { get; init; }
}

/// <summary>Whether a finished title may be moved to the disk it is kept on.</summary>
public sealed record TitleDisposition(string TitleId, bool MaySettle);

/// <summary>What should be true of the card right now.</summary>
public sealed record TranscodePlan
{
    public required DateTimeOffset PlannedAt { get; init; }

    /// <summary>
    /// Whether the executor's last report was recent enough to plan from. False means the plan rests
    /// on manifests alone: nothing is known of what is running.
    /// </summary>
    public required bool ExecutorReporting { get; init; }

    /// <summary>Every unit, most urgent first.</summary>
    public required IReadOnlyList<TranscodePlanUnit> Units { get; init; }

    /// <summary>One entry per finished title.</summary>
    public required IReadOnlyList<TitleDisposition> Titles { get; init; }
}

/// <summary>One unit as the executor sees it.</summary>
public sealed record TranscodeUnitReport
{
    public required string Id { get; init; }
    public required TranscodeUnitState State { get; init; }

    /// <summary>Why the unit is held, parked or failed.</summary>
    public string? Reason { get; init; }

    /// <summary>Output seconds per wall second, from ffmpeg's own progress; absent when not running.</summary>
    public double? Speed { get; init; }

    /// <summary>Seconds of film each output holds, by output name: a rung's directory, or <c>audio</c>.</summary>
    public Dictionary<string, double>? Heads { get; init; }

    /// <summary>The disk the unit writes to.</summary>
    public string? Disk { get; init; }

    /// <summary>When the unit entered its current state, which is what a minimum run time is measured from.</summary>
    public DateTimeOffset? Since { get; init; }
}

/// <summary>What a disk holds, as the executor measured it.</summary>
public sealed record DiskReading(string Name, long FreeBytes, long TotalBytes);

/// <summary>The executor's report of every unit it knows and of its disks.</summary>
public sealed record TranscodeStatus
{
    public required IReadOnlyList<TranscodeUnitReport> Units { get; init; }
    public IReadOnlyList<DiskReading> Disks { get; init; } = [];
}
