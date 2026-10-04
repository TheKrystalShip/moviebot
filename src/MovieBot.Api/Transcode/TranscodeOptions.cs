namespace TheKrystalShip.MovieBot.Api.Transcode;

/// <summary>What the planner decides by: the lead it keeps, the card's budgets and the ladder owed.</summary>
public sealed class TranscodeOptions
{
    public const string Section = "Transcode";

    /// <summary>Seconds a watched rung is kept ahead of its room's playhead.</summary>
    public double LeadSeconds { get; set; } = 120;

    /// <summary>
    /// Half the width of the band around the lead. A rung that is running stays short of lead until
    /// it is the lead plus the band ahead; one that is held is short of lead only below the lead
    /// minus the band. Two rungs near the lead do not trade places on every plan.
    /// </summary>
    public double BandSeconds { get; set; } = 30;

    /// <summary>Seconds a unit runs before a unit of the same urgency or less may hold it.</summary>
    public double MinRunSeconds { get; set; } = 60;

    /// <summary>Seconds a unit stays held before it is parked instead.</summary>
    public double ParkAfterSeconds { get; set; } = 120;

    /// <summary>How many stopped processes may be kept; the VRAM and RAM they hold is the limit.</summary>
    public int MaxHeld { get; set; } = 3;

    /// <summary>
    /// What the encoder can carry, in 1080p-equivalents of realtime. A unit costs its pixel area
    /// against 1920x1080.
    /// </summary>
    public double NvencBudget { get; set; } = 12;

    /// <summary>What the decoder can carry, in source decodes of realtime. A video unit costs one.</summary>
    public double NvdecBudget { get; set; } = 20;

    /// <summary>
    /// Whether each rung is its own unit of rank, or a film's rungs and audio run and hold together.
    /// A rung is only safe to hold on its own once the player caps its levels to the rungs that have
    /// caught up.
    /// </summary>
    public bool PerRung { get; set; }

    /// <summary>Seconds after which the executor's last report is no longer a fact about the card.</summary>
    public double StatusStaleSeconds { get; set; } = 30;

    /// <summary>Seconds the list of finished titles is reused before the library is asked again.</summary>
    public double CatalogRefreshSeconds { get; set; } = 10;

    public TimeSpan StatusStale => TimeSpan.FromSeconds(StatusStaleSeconds);
}
