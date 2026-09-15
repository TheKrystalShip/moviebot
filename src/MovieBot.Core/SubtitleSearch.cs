namespace TheKrystalShip.MovieBot.Core;

/// <summary>One thing that was compared, as the picker shows it.</summary>
public sealed record SubtitleCheckView(string Name, string Result, string Detail);

/// <summary>A candidate, with everything known about how well it fits and nothing that costs a download.</summary>
public sealed record SubtitleCandidateView
{
    public required long FileId { get; init; }
    public required string Release { get; init; }
    public required string Language { get; init; }
    public double? Fps { get; init; }
    public int DownloadCount { get; init; }
    public bool HearingImpaired { get; init; }
    public bool Trusted { get; init; }
    public required int Score { get; init; }
    public required IReadOnlyList<SubtitleCheckView> Checks { get; init; }
}

public sealed record SubtitleSearchView
{
    public required IReadOnlyList<SubtitleCandidateView> Candidates { get; init; }

    /// <summary>Downloads left today, as the index last stated it. Null before any were spent.</summary>
    public int? RemainingDownloads { get; init; }

    /// <summary>Why the list is empty, when it is. An empty menu otherwise reads as a broken search.</summary>
    public string? Explanation { get; init; }
}

public sealed record AddSubtitleRequest(long FileId, string? AddedBy);

/// <summary>
/// What adding a subtitle answers. A track somebody has already confirmed comes back as it stands,
/// marked so; a freshly fetched one says what the conversion did to it and what the day has left.
/// </summary>
public sealed record SubtitleAdded
{
    public required SubtitleTrack Track { get; init; }
    public bool? AlreadyPinned { get; init; }
    public int? Cues { get; init; }
    public int? Repaired { get; init; }
    public bool? LooksWrong { get; init; }
    public double? ShiftSeconds { get; init; }
    public double? AlignedFraction { get; init; }
    public int? RemainingDownloads { get; init; }
}

/// <summary>The allowance is spent, and when it comes back, which is what tells this refusal from the rest.</summary>
public sealed record QuotaReply(string Error, DateTimeOffset? ResetsAt);
