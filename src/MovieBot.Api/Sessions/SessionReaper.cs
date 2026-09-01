namespace TheKrystalShip.MovieBot.Api.Sessions;

public sealed class RoomOptions
{
    public const string Section = "Rooms";

    /// <summary>
    /// How long a room with nobody in it is kept before it is forgotten.
    ///
    /// A launch hands out a link that anybody who has it can open. Dropping the room means an old
    /// link finds nothing playing rather than picking up where the evening left off. It is
    /// measured from the moment the last person leaves, so a film playing to a full room is never
    /// at risk however long it runs.
    /// </summary>
    public TimeSpan IdleTimeout { get; set; } = TimeSpan.FromMinutes(30);

    /// <summary>How often to look. Finer than this buys nothing; a room is not urgent to forget.</summary>
    public TimeSpan SweepInterval { get; set; } = TimeSpan.FromMinutes(1);
}

/// <summary>Forgets rooms that nobody is watching.</summary>
public sealed class SessionReaper(
    SessionStore sessions,
    Microsoft.Extensions.Options.IOptions<RoomOptions> options,
    ILogger<SessionReaper> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var settings = options.Value;
        logger.LogInformation(
            "Rooms with nobody in them are forgotten after {IdleTimeout}", settings.IdleTimeout);

        using var timer = new PeriodicTimer(settings.SweepInterval);

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                var dropped = sessions.Sweep(settings.IdleTimeout);
                if (dropped.Count > 0)
                    logger.LogInformation("Forgot {Count} idle room(s): {Rooms}", dropped.Count, string.Join(", ", dropped));
            }
            catch (Exception ex)
            {
                // A sweep that throws must not take the API down with it; the next tick tries again.
                logger.LogError(ex, "A sweep failed");
            }
        }
    }
}
