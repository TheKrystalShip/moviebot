namespace TheKrystalShip.MovieBot.Api.Offline;

/// <summary>What Save offline may hold and spend.</summary>
public sealed class OfflineOptions
{
    public const string Section = "Offline";

    /// <summary>
    /// How long after it was made a save still being made holds its title in the library and in place.
    /// A save past it is still listed and holds nothing.
    /// </summary>
    public double HoldHours { get; set; } = 48;

    /// <summary>Tracks one person may be fetching at once, across their devices.</summary>
    public int MaxTransfersPerPerson { get; set; } = 6;

    /// <summary>Tracks the server sends at once, for everybody together.</summary>
    public int MaxTransfers { get; set; } = 12;

    /// <summary>
    /// What one transfer may take while a room has somebody in it, in megabits a second; zero leaves it
    /// unlimited. With nobody watching a transfer runs as fast as the disk and the connection let it.
    /// </summary>
    public double MegabitsPerTransferWhileWatched { get; set; } = 20;

    /// <summary>
    /// How long a title asked for before the library held it is waited on. A season can take days to
    /// arrive; a device that never came back for what it asked for is not waited on for ever.
    /// </summary>
    public double WantDays { get; set; } = 14;

    public TimeSpan Hold => TimeSpan.FromHours(HoldHours);

    public TimeSpan WantFor => TimeSpan.FromDays(WantDays);
}
