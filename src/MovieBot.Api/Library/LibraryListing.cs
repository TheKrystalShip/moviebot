namespace TheKrystalShip.MovieBot.Api.Library;

/// <summary>
/// The library as a page lists it, held in memory and rebuilt in the background every
/// <see cref="Every"/>, so a request for the shelf never waits on the disks.
///
/// Building the list asks the filesystem about every title: where it is, whether its manifest moved,
/// its fetched subtitles and pins. Each answer is a lookup the kernel normally holds, but on a host
/// short of memory those are evicted within seconds, and on the spinning disk films settle on each
/// one is a seek: a listing built per request then costs a second. Built here, that cost is paid off
/// the request path, and a page sees a title arrive, change state or move at most
/// <see cref="Every"/> after the disk does.
///
/// What a person needs exact reads <see cref="TitleLibrary"/> directly: a room's head, a title's own
/// manifest, and whether a film is here before something is fetched.
/// </summary>
public sealed class LibraryListing(TitleLibrary library, ILogger<LibraryListing> logger) : BackgroundService
{
    public static readonly TimeSpan Every = TimeSpan.FromSeconds(10);

    private volatile IReadOnlyList<TitleSummary>? _titles;
    private readonly Lock _gate = new();

    /// <summary>The last list built, building one first when none has been.</summary>
    public IReadOnlyList<TitleSummary> Titles() => _titles ?? Refresh();

    /// <summary>Builds the list from the disks now and holds it in place of the last.</summary>
    public IReadOnlyList<TitleSummary> Refresh()
    {
        lock (_gate)
        {
            var titles = library.List();
            _titles = titles;
            return titles;
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Every);
        try
        {
            do
            {
                try
                {
                    Refresh();
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    // The last list stands; the next tick asks again.
                    logger.LogWarning("The library could not be listed: {Reason}", ex.Message);
                }
            }
            while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
    }
}
