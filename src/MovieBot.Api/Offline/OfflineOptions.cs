namespace TheKrystalShip.MovieBot.Api.Offline;

/// <summary>What Save offline may hold and spend.</summary>
public sealed class OfflineOptions
{
    public const string Section = "Offline";

    /// <summary>The file the saves are kept in; empty for <c>offline.json</c> in the state directory.</summary>
    public string Path { get; set; } = "";

    /// <summary>
    /// How long after it was made a save still being made holds its title in the library and in place.
    /// A save past it is still listed and holds nothing.
    /// </summary>
    public double HoldHours { get; set; } = 48;

    /// <summary>Tracks one person may be fetching at once, across their devices.</summary>
    public int MaxTransfersPerPerson { get; set; } = 2;

    /// <summary>Tracks the server sends at once, for everybody together.</summary>
    public int MaxTransfers { get; set; } = 4;

    /// <summary>
    /// What one transfer may take while a room has somebody in it, in megabits a second; zero leaves it
    /// unlimited. With nobody watching a transfer runs as fast as the disk and the connection let it.
    /// </summary>
    public double MegabitsPerTransferWhileWatched { get; set; } = 20;

    public TimeSpan Hold => TimeSpan.FromHours(HoldHours);
}
